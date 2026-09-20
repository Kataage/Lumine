package siglip2

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"os"
	"strings"
	"unicode/utf8"
)

const (
	siglipTextLength = 64
	siglipPadID      = int64(0)
	siglipEOSID      = int64(1)
	siglipUNKID      = int64(3)
)

type unigramPiece struct {
	Text  string
	Score float64
	ID    int64
}

func (p *unigramPiece) UnmarshalJSON(data []byte) error {
	var raw []json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	if len(raw) != 2 {
		return fmt.Errorf("invalid unigram vocabulary entry")
	}
	if err := json.Unmarshal(raw[0], &p.Text); err != nil {
		return err
	}
	if err := json.Unmarshal(raw[1], &p.Score); err != nil {
		return err
	}
	return nil
}

type bpePair struct {
	left  string
	right string
}

type siglipTokenizer struct {
	kind string

	// Legacy Unigram support is kept for compatibility with older fixtures.
	unigramByFirst [256][]unigramPiece
	unigramUnkID   int64

	// SigLIP2 currently ships the multilingual Gemma tokenizer as BPE.
	bpeVocab        map[string]int64
	bpeMergeRank    map[bpePair]int
	bpeUnkID        int64
	bpeFuseUnk      bool
	bpeByteFallback bool
}

func loadTokenizer(path string) (*siglipTokenizer, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read SigLIP tokenizer: %w", err)
	}
	return parseTokenizer(data)
}

func parseTokenizer(data []byte) (*siglipTokenizer, error) {
	var envelope struct {
		Model json.RawMessage
	}
	if err := json.Unmarshal(data, &envelope); err != nil {
		return nil, fmt.Errorf("decode SigLIP tokenizer: %w", err)
	}
	if len(envelope.Model) == 0 {
		return nil, errors.New("SigLIP tokenizer model is missing")
	}

	var base struct {
		Type string
	}
	if err := json.Unmarshal(envelope.Model, &base); err != nil {
		return nil, fmt.Errorf("decode SigLIP tokenizer model: %w", err)
	}

	switch base.Type {
	case "BPE":
		return parseBPETokenizer(envelope.Model)
	case "Unigram":
		return parseUnigramTokenizer(envelope.Model)
	default:
		return nil, fmt.Errorf("unsupported tokenizer model type %q", base.Type)
	}
}

func parseBPETokenizer(data []byte) (*siglipTokenizer, error) {
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("decode SigLIP BPE model: %w", err)
	}

	var vocab map[string]int64
	if err := json.Unmarshal(raw["vocab"], &vocab); err != nil {
		return nil, fmt.Errorf("decode SigLIP BPE vocabulary: %w", err)
	}
	if len(vocab) == 0 {
		return nil, errors.New("SigLIP BPE vocabulary is empty")
	}

	mergeRank, err := parseBPEMerges(raw["merges"])
	if err != nil {
		return nil, err
	}

	var unkToken string
	_ = json.Unmarshal(raw["unk_token"], &unkToken)
	if strings.TrimSpace(unkToken) == "" {
		unkToken = "<unk>"
	}
	unkID, ok := vocab[unkToken]
	if !ok {
		unkID = siglipUNKID
	}

	var fuseUnk bool
	_ = json.Unmarshal(raw["fuse_unk"], &fuseUnk)
	var byteFallback bool
	_ = json.Unmarshal(raw["byte_fallback"], &byteFallback)

	return &siglipTokenizer{
		kind:            "BPE",
		bpeVocab:        vocab,
		bpeMergeRank:    mergeRank,
		bpeUnkID:        unkID,
		bpeFuseUnk:      fuseUnk,
		bpeByteFallback: byteFallback,
	}, nil
}

func parseBPEMerges(data json.RawMessage) (map[bpePair]int, error) {
	result := make(map[bpePair]int)
	if len(data) == 0 || bytes.Equal(bytes.TrimSpace(data), []byte("null")) {
		return result, nil
	}

	var entries []json.RawMessage
	if err := json.Unmarshal(data, &entries); err != nil {
		return nil, fmt.Errorf("decode SigLIP BPE merges: %w", err)
	}

	for rank, entry := range entries {
		var line string
		if err := json.Unmarshal(entry, &line); err == nil {
			parts := strings.SplitN(line, " ", 2)
			if len(parts) != 2 || parts[0] == "" || parts[1] == "" {
				return nil, fmt.Errorf("invalid SigLIP BPE merge %q", line)
			}
			result[bpePair{left: parts[0], right: parts[1]}] = rank
			continue
		}

		var pair []string
		if err := json.Unmarshal(entry, &pair); err != nil || len(pair) != 2 || pair[0] == "" || pair[1] == "" {
			return nil, fmt.Errorf("invalid SigLIP BPE merge at rank %d", rank)
		}
		result[bpePair{left: pair[0], right: pair[1]}] = rank
	}
	return result, nil
}

func parseUnigramTokenizer(data []byte) (*siglipTokenizer, error) {
	var model struct {
		Type  string
		UnkID int
		Vocab []unigramPiece
	}
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(data, &raw); err != nil {
		return nil, fmt.Errorf("decode SigLIP Unigram model: %w", err)
	}
	if err := json.Unmarshal(raw["type"], &model.Type); err != nil {
		return nil, err
	}
	if value, ok := raw["unk_id"]; ok {
		if err := json.Unmarshal(value, &model.UnkID); err != nil {
			return nil, err
		}
	}
	if err := json.Unmarshal(raw["vocab"], &model.Vocab); err != nil {
		return nil, fmt.Errorf("decode SigLIP Unigram vocabulary: %w", err)
	}
	if len(model.Vocab) == 0 {
		return nil, errors.New("SigLIP tokenizer vocabulary is empty")
	}

	tokenizer := &siglipTokenizer{kind: "Unigram", unigramUnkID: int64(model.UnkID)}
	if tokenizer.unigramUnkID < 0 {
		tokenizer.unigramUnkID = siglipUNKID
	}
	for id := range model.Vocab {
		piece := model.Vocab[id]
		piece.ID = int64(id)
		if piece.Text == "" {
			continue
		}
		first := []byte(piece.Text)[0]
		tokenizer.unigramByFirst[first] = append(tokenizer.unigramByFirst[first], piece)
	}
	return tokenizer, nil
}

func (t *siglipTokenizer) Encode64(text string) ([siglipTextLength]int64, error) {
	switch t.kind {
	case "BPE":
		return t.encodeBPE64(text)
	case "Unigram":
		return t.encodeUnigram64(text)
	default:
		return [siglipTextLength]int64{}, errors.New("SigLIP tokenizer is not initialized")
	}
}

func (t *siglipTokenizer) encodeBPE64(text string) ([siglipTextLength]int64, error) {
	normalized := normalizeSigLIP2Text(text)
	if normalized == "" {
		return packSigLIPTextTokens(nil), nil
	}

	tokens := t.bpeInitialSymbols(normalized)
	if len(tokens) == 0 {
		return [siglipTextLength]int64{}, errors.New("SigLIP BPE tokenizer produced no symbols")
	}

	for {
		bestRank := int(^uint(0) >> 1)
		best := bpePair{}
		found := false
		for i := 0; i+1 < len(tokens); i++ {
			pair := bpePair{left: tokens[i], right: tokens[i+1]}
			if rank, ok := t.bpeMergeRank[pair]; ok && rank < bestRank {
				bestRank = rank
				best = pair
				found = true
			}
		}
		if !found {
			break
		}

		merged := make([]string, 0, len(tokens))
		for i := 0; i < len(tokens); {
			if i+1 < len(tokens) && tokens[i] == best.left && tokens[i+1] == best.right {
				merged = append(merged, tokens[i]+tokens[i+1])
				i += 2
				continue
			}
			merged = append(merged, tokens[i])
			i++
		}
		tokens = merged
	}

	ids := make([]int64, 0, min(len(tokens)+1, siglipTextLength))
	lastWasUnk := false
	for _, token := range tokens {
		id, ok := t.bpeVocab[token]
		if !ok {
			id = t.bpeUnkID
		}
		if t.bpeFuseUnk && id == t.bpeUnkID && lastWasUnk {
			continue
		}
		ids = append(ids, id)
		lastWasUnk = id == t.bpeUnkID
		if len(ids) >= siglipTextLength-1 {
			break
		}
	}
	return packSigLIPTextTokens(ids), nil
}

func (t *siglipTokenizer) bpeInitialSymbols(text string) []string {
	result := make([]string, 0, utf8.RuneCountInString(text))
	for _, r := range text {
		token := string(r)
		if _, ok := t.bpeVocab[token]; ok {
			result = append(result, token)
			continue
		}

		if t.bpeByteFallback {
			for _, value := range []byte(token) {
				byteToken := fmt.Sprintf("<0x%02X>", value)
				if _, ok := t.bpeVocab[byteToken]; ok {
					result = append(result, byteToken)
				} else {
					result = append(result, "<unk>")
				}
			}
			continue
		}
		result = append(result, "<unk>")
	}
	return result
}

type unigramStep struct {
	score float64
	prev  int
	id    int64
	set   bool
}

func (t *siglipTokenizer) encodeUnigram64(text string) ([siglipTextLength]int64, error) {
	var output [siglipTextLength]int64
	normalized := normalizeLegacyUnigramText(text)
	if normalized == "" {
		return packSigLIPTextTokens(nil), nil
	}

	input := []byte(normalized)
	steps := make([]unigramStep, len(input)+1)
	steps[0] = unigramStep{score: 0, prev: -1, set: true}

	minPieceScore := 0.0
	haveScore := false
	for _, bucket := range t.unigramByFirst {
		for _, piece := range bucket {
			if !haveScore || piece.Score < minPieceScore {
				minPieceScore = piece.Score
				haveScore = true
			}
		}
	}
	unkPenalty := minPieceScore - 10

	for pos := 0; pos < len(input); pos++ {
		if !steps[pos].set {
			continue
		}
		for _, piece := range t.unigramByFirst[input[pos]] {
			token := []byte(piece.Text)
			end := pos + len(token)
			if end > len(input) || !bytes.Equal(input[pos:end], token) {
				continue
			}
			score := steps[pos].score + piece.Score
			if !steps[end].set || score > steps[end].score {
				steps[end] = unigramStep{score: score, prev: pos, id: piece.ID, set: true}
			}
		}

		_, runeSize := utf8.DecodeRune(input[pos:])
		if runeSize <= 0 {
			runeSize = 1
		}
		end := pos + runeSize
		score := steps[pos].score + unkPenalty
		if end <= len(input) && (!steps[end].set || score > steps[end].score) {
			steps[end] = unigramStep{score: score, prev: pos, id: t.unigramUnkID, set: true}
		}
	}

	if !steps[len(input)].set {
		return output, errors.New("SigLIP tokenizer could not encode input")
	}

	ids := make([]int64, 0, siglipTextLength)
	for pos := len(input); pos > 0; {
		step := steps[pos]
		if !step.set || step.prev < 0 || step.prev >= pos {
			return output, errors.New("invalid SigLIP tokenizer path")
		}
		ids = append(ids, step.id)
		pos = step.prev
	}
	for left, right := 0, len(ids)-1; left < right; left, right = left+1, right-1 {
		ids[left], ids[right] = ids[right], ids[left]
	}

	if len(ids) >= siglipTextLength {
		ids = ids[:siglipTextLength-1]
	}
	return packSigLIPTextTokens(ids), nil
}

func packSigLIPTextTokens(content []int64) [siglipTextLength]int64 {
	var output [siglipTextLength]int64
	if len(content) > siglipTextLength-1 {
		content = content[:siglipTextLength-1]
	}
	sequenceLength := len(content) + 1 // sticky EOS
	start := siglipTextLength - sequenceLength
	copy(output[start:start+len(content)], content)
	output[siglipTextLength-1] = siglipEOSID
	return output
}

// SigLIP2 training uses the multilingual Gemma tokenizer with lowercase text.
// The serialized BPE tokenizer replaces literal spaces with the metaspace
// marker before applying merges.
func normalizeSigLIP2Text(text string) string {
	text = strings.ToLower(strings.TrimSpace(text))
	if text == "" {
		return ""
	}
	// Match SigLIP2's tokenizer backend: lowercase first, then replace literal
	// spaces with the Gemma metaspace marker. Fixed-length packing is handled
	// separately and left-pads so the sticky EOS token remains at position 63,
	// matching the reference SigLIP2 text pooling contract.
	return strings.ReplaceAll(text, " ", "▁")
}

func normalizeLegacyUnigramText(text string) string {
	text = strings.ToLower(strings.TrimSpace(text))
	if text == "" {
		return ""
	}
	fields := strings.Fields(text)
	if len(fields) == 0 {
		return ""
	}
	return "▁" + strings.Join(fields, "▁")
}

func cosineUnit(vector []float32) ([]float32, error) {
	if len(vector) == 0 {
		return nil, errors.New("empty embedding")
	}
	var sum float64
	for _, value := range vector {
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return nil, errors.New("embedding contains non-finite values")
		}
		sum += float64(value) * float64(value)
	}
	if sum <= 0 {
		return nil, errors.New("embedding has zero norm")
	}
	scale := float32(1 / math.Sqrt(sum))
	result := make([]float32, len(vector))
	for i, value := range vector {
		result[i] = value * scale
	}
	return result, nil
}
