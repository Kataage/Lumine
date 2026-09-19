package commands

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"unsafe"

	"github.com/kataage/lumine/internal/infrastructure/db"
)

const (
	semanticSnapshotMagic      = "LUMSIDX1"
	semanticSnapshotVersion    = uint32(1)
	semanticSnapshotHeaderSize = 4096
	semanticSnapshotPrefix     = "semantic-exact-v1-"
	semanticSnapshotMetaOffset = 88
)

var (
	errSemanticSnapshotUnavailable       = errors.New("semantic persistent index is unavailable")
	errSemanticSnapshotStale             = errors.New("semantic persistent index is stale")
	errSemanticSnapshotCorrupt           = errors.New("semantic persistent index is corrupt")
	errSemanticSnapshotGenerationChanged = errors.New("semantic index generation changed during snapshot")
)

type semanticPersistentSnapshot struct {
	path       string
	key        semanticIndexKey
	generation uint64
	count      int
	dimensions int
	mapped     []byte
	unmap      func() error
	positions  map[int64]int
	data       []float32
}

type semanticSnapshotHeader struct {
	key           semanticIndexKey
	generation    uint64
	count         int
	dimensions    int
	vectorsOffset int
	checksum      [sha256.Size]byte
}

func semanticSnapshotKeyPrefix(key semanticIndexKey) string {
	keyHash := sha256.Sum256([]byte(key.engine + "\x00" + key.modelID + "\x00" + key.version))
	return fmt.Sprintf("%s%x-", semanticSnapshotPrefix, keyHash[:8])
}

func semanticSnapshotPath(root string, key semanticIndexKey, generation uint64) string {
	name := fmt.Sprintf("%s%016x.bin", semanticSnapshotKeyPrefix(key), generation)
	return filepath.Join(root, name)
}

func cleanupSemanticSnapshots(root string, key semanticIndexKey, keepPath string) {
	if root == "" {
		return
	}
	matches, err := filepath.Glob(filepath.Join(root, semanticSnapshotKeyPrefix(key)+"*.bin"))
	if err != nil {
		return
	}
	for _, path := range matches {
		if path == keepPath {
			continue
		}
		_ = os.Remove(path)
	}
}

func alignSemanticSnapshot(value int) int {
	const alignment = 4096
	return (value + alignment - 1) &^ (alignment - 1)
}

func encodeSemanticSnapshotHeader(header semanticSnapshotHeader) ([]byte, error) {
	if header.count < 0 || header.dimensions < 0 || header.vectorsOffset < semanticSnapshotHeaderSize {
		return nil, errSemanticSnapshotCorrupt
	}
	engine := []byte(header.key.engine)
	model := []byte(header.key.modelID)
	version := []byte(header.key.version)
	if len(engine) > math.MaxUint16 || len(model) > math.MaxUint16 || len(version) > math.MaxUint16 {
		return nil, errors.New("semantic snapshot provenance is too long")
	}
	if semanticSnapshotMetaOffset+len(engine)+len(model)+len(version) > semanticSnapshotHeaderSize {
		return nil, errors.New("semantic snapshot provenance exceeds header")
	}

	result := make([]byte, semanticSnapshotHeaderSize)
	copy(result[:8], semanticSnapshotMagic)
	binary.LittleEndian.PutUint32(result[8:12], semanticSnapshotVersion)
	binary.LittleEndian.PutUint32(result[12:16], semanticSnapshotHeaderSize)
	binary.LittleEndian.PutUint64(result[16:24], header.generation)
	binary.LittleEndian.PutUint64(result[24:32], uint64(header.count))
	binary.LittleEndian.PutUint32(result[32:36], uint32(header.dimensions))
	binary.LittleEndian.PutUint16(result[36:38], uint16(len(engine)))
	binary.LittleEndian.PutUint16(result[38:40], uint16(len(model)))
	binary.LittleEndian.PutUint16(result[40:42], uint16(len(version)))
	binary.LittleEndian.PutUint64(result[48:56], uint64(header.vectorsOffset))
	copy(result[56:88], header.checksum[:])

	cursor := semanticSnapshotMetaOffset
	copy(result[cursor:cursor+len(engine)], engine)
	cursor += len(engine)
	copy(result[cursor:cursor+len(model)], model)
	cursor += len(model)
	copy(result[cursor:cursor+len(version)], version)
	return result, nil
}

func decodeSemanticSnapshotHeader(data []byte) (semanticSnapshotHeader, error) {
	if len(data) < semanticSnapshotHeaderSize ||
		string(data[:8]) != semanticSnapshotMagic ||
		binary.LittleEndian.Uint32(data[8:12]) != semanticSnapshotVersion ||
		binary.LittleEndian.Uint32(data[12:16]) != semanticSnapshotHeaderSize {
		return semanticSnapshotHeader{}, errSemanticSnapshotCorrupt
	}

	count64 := binary.LittleEndian.Uint64(data[24:32])
	dimensions32 := binary.LittleEndian.Uint32(data[32:36])
	vectorsOffset64 := binary.LittleEndian.Uint64(data[48:56])
	maxInt := uint64(^uint(0) >> 1)
	if count64 > maxInt || uint64(dimensions32) > maxInt || vectorsOffset64 > maxInt {
		return semanticSnapshotHeader{}, errSemanticSnapshotCorrupt
	}

	engineLen := int(binary.LittleEndian.Uint16(data[36:38]))
	modelLen := int(binary.LittleEndian.Uint16(data[38:40]))
	versionLen := int(binary.LittleEndian.Uint16(data[40:42]))
	metaEnd := semanticSnapshotMetaOffset + engineLen + modelLen + versionLen
	if metaEnd > semanticSnapshotHeaderSize {
		return semanticSnapshotHeader{}, errSemanticSnapshotCorrupt
	}

	cursor := semanticSnapshotMetaOffset
	engine := string(data[cursor : cursor+engineLen])
	cursor += engineLen
	model := string(data[cursor : cursor+modelLen])
	cursor += modelLen
	version := string(data[cursor : cursor+versionLen])

	header := semanticSnapshotHeader{
		key: semanticKey(engine, model, version),
		generation: binary.LittleEndian.Uint64(data[16:24]),
		count: int(count64),
		dimensions: int(dimensions32),
		vectorsOffset: int(vectorsOffset64),
	}
	copy(header.checksum[:], data[56:88])
	if header.count > 0 && header.dimensions <= 0 {
		return semanticSnapshotHeader{}, errSemanticSnapshotCorrupt
	}
	return header, nil
}

func semanticSnapshotExpectedSize(header semanticSnapshotHeader) (int, error) {
	maxInt := int(^uint(0) >> 1)
	if header.count < 0 || header.dimensions < 0 ||
		(header.count > 0 && header.count > (maxInt-semanticSnapshotHeaderSize)/8) {
		return 0, errSemanticSnapshotCorrupt
	}
	minVectorsOffset := alignSemanticSnapshot(semanticSnapshotHeaderSize + header.count*8)
	if header.vectorsOffset != minVectorsOffset {
		return 0, errSemanticSnapshotCorrupt
	}
	if header.count == 0 {
		return semanticSnapshotHeaderSize, nil
	}
	if header.dimensions > maxInt/4 || header.count > maxInt/(header.dimensions*4) {
		return 0, errSemanticSnapshotCorrupt
	}
	vectorBytes := header.count * header.dimensions * 4
	if header.vectorsOffset > maxInt-vectorBytes {
		return 0, errSemanticSnapshotCorrupt
	}
	return header.vectorsOffset + vectorBytes, nil
}

func validateSemanticSnapshotBlob(blob []byte, dimensions int) error {
	if dimensions <= 0 || len(blob) != dimensions*4 {
		return errSemanticSnapshotCorrupt
	}
	for offset := 0; offset < len(blob); offset += 4 {
		value := math.Float32frombits(binary.LittleEndian.Uint32(blob[offset : offset+4]))
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return errSemanticSnapshotCorrupt
		}
	}
	return nil
}

func openSemanticPersistentSnapshot(
	root string,
	key semanticIndexKey,
	generation uint64,
) (*semanticPersistentSnapshot, error) {
	if root == "" {
		return nil, errSemanticSnapshotUnavailable
	}
	path := semanticSnapshotPath(root, key, generation)
	file, err := os.Open(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, errSemanticSnapshotUnavailable
		}
		return nil, err
	}
	stat, err := file.Stat()
	if err != nil {
		_ = file.Close()
		return nil, err
	}
	if stat.Size() < semanticSnapshotHeaderSize || stat.Size() > int64(^uint(0)>>1) {
		_ = file.Close()
		return nil, errSemanticSnapshotCorrupt
	}

	mapped, unmap, err := mapReadOnlyFile(file, int(stat.Size()))
	_ = file.Close()
	if err != nil {
		return nil, fmt.Errorf("mmap semantic snapshot: %w", err)
	}
	keep := false
	defer func() {
		if !keep {
			_ = unmap()
		}
	}()

	header, err := decodeSemanticSnapshotHeader(mapped)
	if err != nil {
		return nil, err
	}
	if header.key != key || header.generation != generation {
		return nil, errSemanticSnapshotStale
	}
	expectedSize, err := semanticSnapshotExpectedSize(header)
	if err != nil || expectedSize != len(mapped) {
		return nil, errSemanticSnapshotCorrupt
	}
	sum := sha256.Sum256(mapped[semanticSnapshotHeaderSize:])
	if !bytes.Equal(sum[:], header.checksum[:]) {
		return nil, errSemanticSnapshotCorrupt
	}

	positions := make(map[int64]int, header.count)
	for position := 0; position < header.count; position++ {
		offset := semanticSnapshotHeaderSize + position*8
		assetID := int64(binary.LittleEndian.Uint64(mapped[offset : offset+8]))
		if assetID <= 0 {
			return nil, errSemanticSnapshotCorrupt
		}
		if _, exists := positions[assetID]; exists {
			return nil, errSemanticSnapshotCorrupt
		}
		positions[assetID] = position
	}

	var vectors []float32
	if header.count > 0 {
		vectorCount := header.count * header.dimensions
		vectorBytes := mapped[header.vectorsOffset:]
		if len(vectorBytes) != vectorCount*4 || uintptr(unsafe.Pointer(&vectorBytes[0]))%unsafe.Alignof(float32(0)) != 0 {
			return nil, errSemanticSnapshotCorrupt
		}
		vectors = unsafe.Slice((*float32)(unsafe.Pointer(&vectorBytes[0])), vectorCount)
	}

	keep = true
	return &semanticPersistentSnapshot{
		path:       path,
		key:        key,
		generation: generation,
		count:      header.count,
		dimensions: header.dimensions,
		mapped:     mapped,
		unmap:      unmap,
		positions:  positions,
		data:       vectors,
	}, nil
}

func writeSemanticPersistentSnapshot(
	ctx context.Context,
	root string,
	repo *db.SemanticEmbeddingRepo,
	key semanticIndexKey,
) (*semanticPersistentSnapshot, error) {
	if root == "" || repo == nil {
		return nil, errSemanticSnapshotUnavailable
	}
	if ctx == nil {
		ctx = context.Background()
	}

	info, err := repo.ReadyEmbeddingSnapshotInfo(ctx, key.engine, key.modelID, key.version)
	if err != nil {
		return nil, err
	}
	count := info.Count
	dimensions := info.Dimensions
	vectorsOffset := alignSemanticSnapshot(semanticSnapshotHeaderSize + count*8)
	header := semanticSnapshotHeader{
		key:           key,
		generation:    info.Generation,
		count:         count,
		dimensions:    dimensions,
		vectorsOffset: vectorsOffset,
	}
	expectedSize, err := semanticSnapshotExpectedSize(header)
	if err != nil {
		return nil, err
	}

	// Snapshot files are immutable and generation-addressed. Reuse an already
	// valid file instead of trying to replace a file that may currently be
	// memory-mapped (Windows rejects deletion of mapped files).
	if existing, openErr := openSemanticPersistentSnapshot(root, key, info.Generation); openErr == nil {
		return existing, nil
	} else if errors.Is(openErr, errSemanticSnapshotCorrupt) || errors.Is(openErr, errSemanticSnapshotStale) {
		_ = os.Remove(semanticSnapshotPath(root, key, info.Generation))
	}

	if err := os.MkdirAll(root, 0755); err != nil {
		return nil, fmt.Errorf("create semantic index directory: %w", err)
	}
	temp, err := os.CreateTemp(root, ".semantic-exact-*.tmp")
	if err != nil {
		return nil, fmt.Errorf("create semantic snapshot temp: %w", err)
	}
	tempPath := temp.Name()
	closed := false
	defer func() {
		if !closed {
			_ = temp.Close()
		}
		_ = os.Remove(tempPath)
	}()

	if err := temp.Truncate(int64(expectedSize)); err != nil {
		return nil, fmt.Errorf("size semantic snapshot: %w", err)
	}

	position := 0
	var previousID int64
	if count > 0 {
		err = repo.WalkReadyEmbeddingBlobs(
			ctx,
			key.engine,
			key.modelID,
			key.version,
			func(assetID int64, rowDimensions int, blob []byte) error {
				if err := ctx.Err(); err != nil {
					return err
				}
				if position >= count || rowDimensions != dimensions || assetID <= previousID {
					return errSemanticSnapshotGenerationChanged
				}
				if err := validateSemanticSnapshotBlob(blob, dimensions); err != nil {
					return fmt.Errorf("validate semantic snapshot asset %d: %w", assetID, err)
				}

				var idBytes [8]byte
				binary.LittleEndian.PutUint64(idBytes[:], uint64(assetID))
				if _, err := temp.WriteAt(idBytes[:], int64(semanticSnapshotHeaderSize+position*8)); err != nil {
					return err
				}
				vectorOffset := int64(vectorsOffset + position*dimensions*4)
				if _, err := temp.WriteAt(blob, vectorOffset); err != nil {
					return err
				}
				previousID = assetID
				position++
				return nil
			},
		)
		if err != nil {
			return nil, err
		}
	}
	if position != count {
		return nil, errSemanticSnapshotGenerationChanged
	}

	endGeneration, err := repo.SemanticIndexGeneration(ctx)
	if err != nil {
		return nil, err
	}
	if endGeneration != info.Generation {
		return nil, errSemanticSnapshotGenerationChanged
	}

	if _, err := temp.Seek(semanticSnapshotHeaderSize, io.SeekStart); err != nil {
		return nil, err
	}
	hasher := sha256.New()
	if _, err := io.Copy(hasher, temp); err != nil {
		return nil, fmt.Errorf("hash semantic snapshot: %w", err)
	}
	copy(header.checksum[:], hasher.Sum(nil))
	headerBytes, err := encodeSemanticSnapshotHeader(header)
	if err != nil {
		return nil, err
	}
	if _, err := temp.WriteAt(headerBytes, 0); err != nil {
		return nil, fmt.Errorf("write semantic snapshot header: %w", err)
	}
	if err := temp.Sync(); err != nil {
		return nil, fmt.Errorf("sync semantic snapshot: %w", err)
	}
	if err := temp.Close(); err != nil {
		return nil, fmt.Errorf("close semantic snapshot: %w", err)
	}
	closed = true

	target := semanticSnapshotPath(root, key, endGeneration)
	if err := os.Rename(tempPath, target); err != nil {
		return nil, fmt.Errorf("publish semantic snapshot: %w", err)
	}

	return openSemanticPersistentSnapshot(root, key, endGeneration)
}
