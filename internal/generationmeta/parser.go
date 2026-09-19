package generationmeta

import (
	"bytes"
	"compress/zlib"
	"encoding/binary"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

const ParserVersion = 1
const maxMetadataChunk = 16 * 1024 * 1024

var loraSyntaxPattern = regexp.MustCompile(`(?i)<lora:([^:>]+)(?::([0-9.+-]+))?>`)

type comfyNode struct {
	ClassType string         `json:"class_type"`
	Inputs    map[string]any `json:"inputs"`
}

func Read(path string) (domain.GenerationMetadata, map[string]string, error) {
	raw := make(map[string]string)
	ext := strings.ToLower(filepath.Ext(path))
	var err error
	switch ext {
	case ".png", ".apng":
		err = readPNG(path, raw)
	case ".webp":
		err = readWebP(path, raw)
	case ".jpg", ".jpeg":
		err = readJPEG(path, raw)
	default:
		return domain.GenerationMetadata{
			SchemaVersion: 1,
			SourceFormat: strings.TrimPrefix(ext, "."),
			LoRAs: []domain.GenerationLoRA{},
			Extra: map[string]any{},
		}, raw, nil
	}
	if err != nil {
		return domain.GenerationMetadata{}, nil, err
	}
	return Normalize(raw, strings.TrimPrefix(ext, ".")), raw, nil
}

func readPNG(path string, raw map[string]string) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()

	signature := make([]byte, 8)
	if _, err := io.ReadFull(file, signature); err != nil {
		return err
	}
	if !bytes.Equal(signature, []byte{137, 80, 78, 71, 13, 10, 26, 10}) {
		return fmt.Errorf("invalid PNG signature")
	}

	for {
		var length uint32
		if err := binary.Read(file, binary.BigEndian, &length); err != nil {
			if err == io.EOF {
				break
			}
			return err
		}
		chunkTypeBytes := make([]byte, 4)
		if _, err := io.ReadFull(file, chunkTypeBytes); err != nil {
			return err
		}
		chunkType := string(chunkTypeBytes)
		relevant := chunkType == "tEXt" || chunkType == "zTXt" || chunkType == "iTXt" || chunkType == "comf"
		if relevant && length <= maxMetadataChunk {
			data := make([]byte, int(length))
			if _, err := io.ReadFull(file, data); err != nil {
				return err
			}
			switch chunkType {
			case "tEXt":
				parsePNGText(data, raw)
			case "zTXt":
				parsePNGCompressedText(data, raw)
			case "iTXt":
				parsePNGInternationalText(data, raw)
			case "comf":
				parseComfyChunk(data, raw)
			}
		} else {
			if _, err := io.CopyN(io.Discard, file, int64(length)); err != nil {
				return err
			}
		}
		if _, err := io.CopyN(io.Discard, file, 4); err != nil { // CRC
			return err
		}
		if chunkType == "IEND" {
			break
		}
	}
	return nil
}

func parsePNGText(data []byte, raw map[string]string) {
	if index := bytes.IndexByte(data, 0); index > 0 {
		raw[string(data[:index])] = string(data[index+1:])
	}
}

func parsePNGCompressedText(data []byte, raw map[string]string) {
	index := bytes.IndexByte(data, 0)
	if index <= 0 || index+2 > len(data) || data[index+1] != 0 {
		return
	}
	reader, err := zlib.NewReader(bytes.NewReader(data[index+2:]))
	if err != nil {
		return
	}
	defer reader.Close()
	decoded, err := io.ReadAll(io.LimitReader(reader, maxMetadataChunk))
	if err == nil {
		raw[string(data[:index])] = string(decoded)
	}
}

func parsePNGInternationalText(data []byte, raw map[string]string) {
	keywordEnd := bytes.IndexByte(data, 0)
	if keywordEnd <= 0 || keywordEnd+3 > len(data) {
		return
	}
	keyword := string(data[:keywordEnd])
	rest := data[keywordEnd+1:]
	compressed := rest[0] == 1
	method := rest[1]
	rest = rest[2:]
	languageEnd := bytes.IndexByte(rest, 0)
	if languageEnd < 0 {
		return
	}
	rest = rest[languageEnd+1:]
	translatedEnd := bytes.IndexByte(rest, 0)
	if translatedEnd < 0 {
		return
	}
	text := rest[translatedEnd+1:]
	if compressed {
		if method != 0 {
			return
		}
		reader, err := zlib.NewReader(bytes.NewReader(text))
		if err != nil {
			return
		}
		defer reader.Close()
		decoded, err := io.ReadAll(io.LimitReader(reader, maxMetadataChunk))
		if err != nil {
			return
		}
		text = decoded
	}
	raw[keyword] = string(text)
}

func parseComfyChunk(data []byte, raw map[string]string) {
	if index := bytes.IndexByte(data, 0); index > 0 {
		raw[string(data[:index])] = string(data[index+1:])
	}
}

func readWebP(path string, raw map[string]string) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	header := make([]byte, 12)
	if _, err := io.ReadFull(file, header); err != nil {
		return err
	}
	if string(header[:4]) != "RIFF" || string(header[8:12]) != "WEBP" {
		return fmt.Errorf("invalid WebP header")
	}
	for {
		chunkHeader := make([]byte, 8)
		if _, err := io.ReadFull(file, chunkHeader); err != nil {
			if err == io.EOF || err == io.ErrUnexpectedEOF {
				break
			}
			return err
		}
		size := binary.LittleEndian.Uint32(chunkHeader[4:8])
		if string(chunkHeader[:4]) == "EXIF" && size <= maxMetadataChunk {
			data := make([]byte, int(size))
			if _, err := io.ReadFull(file, data); err != nil {
				return err
			}
			parseExifPayload(data, raw)
		} else {
			if _, err := io.CopyN(io.Discard, file, int64(size)); err != nil {
				return err
			}
		}
		if size%2 == 1 {
			if _, err := io.CopyN(io.Discard, file, 1); err != nil {
				return err
			}
		}
	}
	return nil
}

func readJPEG(path string, raw map[string]string) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	header := make([]byte, 2)
	if _, err := io.ReadFull(file, header); err != nil || header[0] != 0xff || header[1] != 0xd8 {
		return fmt.Errorf("invalid JPEG header")
	}
	for {
		var prefix [1]byte
		if _, err := io.ReadFull(file, prefix[:]); err != nil {
			break
		}
		if prefix[0] != 0xff {
			continue
		}
		var marker [1]byte
		if _, err := io.ReadFull(file, marker[:]); err != nil {
			break
		}
		for marker[0] == 0xff {
			if _, err := io.ReadFull(file, marker[:]); err != nil {
				return nil
			}
		}
		if marker[0] == 0xda || marker[0] == 0xd9 {
			break
		}
		if marker[0] >= 0xd0 && marker[0] <= 0xd7 {
			continue
		}
		var sizeBytes [2]byte
		if _, err := io.ReadFull(file, sizeBytes[:]); err != nil {
			break
		}
		size := int(binary.BigEndian.Uint16(sizeBytes[:]))
		if size < 2 {
			return fmt.Errorf("invalid JPEG segment size")
		}
		payloadSize := size - 2
		if marker[0] == 0xe1 && payloadSize <= maxMetadataChunk {
			payload := make([]byte, payloadSize)
			if _, err := io.ReadFull(file, payload); err != nil {
				return err
			}
			parseExifPayload(payload, raw)
		} else {
			if _, err := io.CopyN(io.Discard, file, int64(payloadSize)); err != nil {
				return err
			}
		}
	}
	return nil
}

func parseExifPayload(data []byte, raw map[string]string) {
	if bytes.HasPrefix(data, []byte("Exif\x00\x00")) {
		data = data[6:]
	}
	for key, value := range parseTIFFASCII(data) {
		if strings.EqualFold(key, "UserComment") {
			value = strings.TrimPrefix(value, "ASCII\x00\x00\x00")
			mergeWrappedMetadata(value, raw)
		}
		if splitMetadataPrefix(value, raw) {
			continue
		}
		if key == "ImageDescription" && strings.Contains(value, "Negative prompt:") {
			raw["parameters"] = value
		}
	}
}

func parseTIFFASCII(data []byte) map[string]string {
	result := make(map[string]string)
	if len(data) < 8 {
		return result
	}
	var order binary.ByteOrder
	switch string(data[:2]) {
	case "II":
		order = binary.LittleEndian
	case "MM":
		order = binary.BigEndian
	default:
		return result
	}
	if order.Uint16(data[2:4]) != 42 {
		return result
	}
	offset := int(order.Uint32(data[4:8]))
	if offset < 0 || offset+2 > len(data) {
		return result
	}
	count := int(order.Uint16(data[offset : offset+2]))
	base := offset + 2
	for i := 0; i < count; i++ {
		entryOffset := base + i*12
		if entryOffset+12 > len(data) {
			break
		}
		entry := data[entryOffset : entryOffset+12]
		tag := order.Uint16(entry[0:2])
		typ := order.Uint16(entry[2:4])
		itemCount := order.Uint32(entry[4:8])
		if typ != 2 && typ != 7 {
			continue
		}
		byteCount := int(itemCount)
		if byteCount <= 0 || byteCount > maxMetadataChunk {
			continue
		}
		var valueBytes []byte
		if byteCount <= 4 {
			valueBytes = append([]byte{}, entry[8:8+byteCount]...)
		} else {
			valueOffset := int(order.Uint32(entry[8:12]))
			if valueOffset < 0 || valueOffset+byteCount > len(data) {
				continue
			}
			valueBytes = append([]byte{}, data[valueOffset:valueOffset+byteCount]...)
		}
		valueBytes = bytes.TrimRight(valueBytes, "\x00")
		value := strings.TrimSpace(string(valueBytes))
		if value == "" {
			continue
		}
		key := fmt.Sprintf("EXIF_%04X", tag)
		switch tag {
		case 0x010E:
			key = "ImageDescription"
		case 0x010F:
			key = "Make"
		case 0x0110:
			key = "Model"
		case 0x9286:
			key = "UserComment"
		}
		result[key] = value
	}
	return result
}

func splitMetadataPrefix(value string, raw map[string]string) bool {
	index := strings.Index(value, ":")
	if index <= 0 {
		return false
	}
	key := strings.TrimSpace(value[:index])
	payload := strings.TrimSpace(value[index+1:])
	if key == "" || payload == "" {
		return false
	}
	var parsed any
	if json.Unmarshal([]byte(payload), &parsed) != nil {
		return false
	}
	raw[key] = payload
	return true
}

func mergeWrappedMetadata(value string, raw map[string]string) {
	var wrapped map[string]any
	if json.Unmarshal([]byte(strings.TrimSpace(value)), &wrapped) != nil {
		return
	}
	for key, item := range wrapped {
		encoded, err := json.Marshal(item)
		if err == nil {
			raw[key] = string(encoded)
		}
	}
}

func decodeJSON(raw string, target any) error {
	decoder := json.NewDecoder(strings.NewReader(raw))
	decoder.UseNumber()
	return decoder.Decode(target)
}

func Normalize(raw map[string]string, sourceFormat string) domain.GenerationMetadata {
	result := domain.GenerationMetadata{
		SchemaVersion: 1,
		SourceFormat:  sourceFormat,
		LoRAs:         []domain.GenerationLoRA{},
		Extra:         map[string]any{},
	}
	if promptJSON := strings.TrimSpace(raw["prompt"]); promptJSON != "" {
		result.RawPromptJSON = promptJSON
		parseComfyPrompt(promptJSON, &result)
	}
	if workflowJSON := strings.TrimSpace(raw["workflow"]); workflowJSON != "" {
		result.RawWorkflowJSON = workflowJSON
	}
	if parameters := firstRaw(raw, "parameters", "Parameters"); parameters != "" {
		result.Parameters = parameters
		parseParameters(parameters, &result)
	}
	for key, value := range raw {
		if key == "prompt" || key == "workflow" || strings.EqualFold(key, "parameters") {
			continue
		}
		var parsed any
		if decodeJSON(value, &parsed) == nil {
			result.Extra[key] = parsed
		} else {
			result.Extra[key] = value
		}
	}
	result.LoRAs = mergeLoRAs(result.LoRAs, parseLoRASyntax(result.Positive))
	return result
}

func firstRaw(raw map[string]string, keys ...string) string {
	for _, wanted := range keys {
		for key, value := range raw {
			if strings.EqualFold(key, wanted) && strings.TrimSpace(value) != "" {
				return strings.TrimSpace(value)
			}
		}
	}
	return ""
}

func parseComfyPrompt(raw string, result *domain.GenerationMetadata) {
	var nodes map[string]comfyNode
	if decodeJSON(raw, &nodes) != nil || len(nodes) == 0 {
		return
	}

	var samplerID string
	for id, node := range nodes {
		lower := strings.ToLower(node.ClassType)
		if strings.Contains(lower, "ksampler") ||
			(strings.Contains(lower, "sampler") && (node.Inputs["steps"] != nil || node.Inputs["sampler_name"] != nil)) {
			samplerID = id
			break
		}
	}
	if samplerID != "" {
		sampler := nodes[samplerID]
		result.Steps = firstInt(result.Steps, sampler.Inputs["steps"])
		result.CFG = firstFloat(result.CFG, sampler.Inputs["cfg"])
		result.Seed = firstInt64(result.Seed, sampler.Inputs["seed"], sampler.Inputs["noise_seed"])
		result.Sampler = firstString(result.Sampler, sampler.Inputs["sampler_name"], sampler.Inputs["sampler"])
		result.Scheduler = firstString(result.Scheduler, sampler.Inputs["scheduler"])
		result.Positive = firstString(result.Positive, collectTextFromRef(sampler.Inputs["positive"], nodes, map[string]bool{}))
		result.Negative = firstString(result.Negative, collectTextFromRef(sampler.Inputs["negative"], nodes, map[string]bool{}))
		if latentID := referenceID(sampler.Inputs["latent_image"]); latentID != "" {
			if latent, ok := nodes[latentID]; ok {
				result.Width = firstInt(result.Width, latent.Inputs["width"])
				result.Height = firstInt(result.Height, latent.Inputs["height"])
			}
		}
	}

	for _, node := range nodes {
		lowerClass := strings.ToLower(node.ClassType)
		if result.Positive == "" && (strings.Contains(lowerClass, "guider") || strings.Contains(lowerClass, "conditioning")) {
			result.Positive = firstString(result.Positive, collectTextFromRef(node.Inputs["positive"], nodes, map[string]bool{}))
			result.Negative = firstString(result.Negative, collectTextFromRef(node.Inputs["negative"], nodes, map[string]bool{}))
		}
		if result.Checkpoint == "" {
			result.Checkpoint = firstString(result.Checkpoint, node.Inputs["ckpt_name"], node.Inputs["checkpoint"], node.Inputs["unet_name"])
		}
		if result.Seed == 0 {
			result.Seed = firstInt64(result.Seed, node.Inputs["seed"], node.Inputs["noise_seed"])
		}
		if result.Steps == 0 {
			result.Steps = firstInt(result.Steps, node.Inputs["steps"])
		}
		if result.CFG == 0 {
			result.CFG = firstFloat(result.CFG, node.Inputs["cfg"])
		}
		if result.Sampler == "" {
			result.Sampler = firstString(result.Sampler, node.Inputs["sampler_name"])
		}
		if result.Scheduler == "" {
			result.Scheduler = firstString(result.Scheduler, node.Inputs["scheduler"])
		}
		if result.Width == 0 {
			result.Width = firstInt(result.Width, node.Inputs["width"])
		}
		if result.Height == 0 {
			result.Height = firstInt(result.Height, node.Inputs["height"])
		}
		if strings.Contains(lowerClass, "lora") || node.Inputs["lora_name"] != nil {
			name := firstString("", node.Inputs["lora_name"], node.Inputs["lora"])
			if name != "" {
				weight := firstFloat(0, node.Inputs["strength_model"], node.Inputs["strength"], node.Inputs["weight"])
				if weight == 0 {
					weight = 1
				}
				var triggers []string
				for key, value := range node.Inputs {
					if strings.Contains(strings.ToLower(key), "trigger") || strings.Contains(strings.ToLower(key), "trained_word") {
						for _, candidate := range stringsFromAny(value) {
							triggers = append(triggers, strings.FieldsFunc(candidate, func(r rune) bool {
								return r == ',' || r == '\n' || r == ';'
							})...)
						}
					}
				}
				result.LoRAs = append(result.LoRAs, domain.GenerationLoRA{
					Name: name, Weight: weight, TriggerWords: uniqueStrings(triggers),
				})
			}
		}
	}
	result.LoRAs = mergeLoRAs(result.LoRAs)
}

func collectTextFromRef(value any, nodes map[string]comfyNode, seen map[string]bool) string {
	if direct, ok := value.(string); ok {
		return strings.TrimSpace(direct)
	}
	id := referenceID(value)
	if id == "" || seen[id] {
		return ""
	}
	node, ok := nodes[id]
	if !ok {
		return ""
	}
	seen[id] = true
	var texts []string
	for key, input := range node.Inputs {
		lower := strings.ToLower(key)
		if (strings.Contains(lower, "text") || lower == "prompt") && isStringish(input) {
			texts = append(texts, stringsFromAny(input)...)
		}
	}
	if len(texts) == 0 {
		for _, input := range node.Inputs {
			if referenceID(input) != "" {
				if text := collectTextFromRef(input, nodes, seen); text != "" {
					texts = append(texts, text)
				}
			}
		}
	}
	return strings.Join(uniqueStrings(texts), ", ")
}

func referenceID(value any) string {
	array, ok := value.([]any)
	if !ok || len(array) < 1 {
		return ""
	}
	switch id := array[0].(type) {
	case string:
		return id
	case json.Number:
		return id.String()
	case float64:
		return strconv.FormatInt(int64(id), 10)
	default:
		return ""
	}
}

func isStringish(value any) bool {
	switch value.(type) {
	case string, []string:
		return true
	case []any:
		return referenceID(value) == ""
	default:
		return false
	}
}

func stringsFromAny(value any) []string {
	switch raw := value.(type) {
	case string:
		if strings.TrimSpace(raw) != "" {
			return []string{strings.TrimSpace(raw)}
		}
	case []string:
		return uniqueStrings(raw)
	case []any:
		var result []string
		for _, item := range raw {
			if value, ok := item.(string); ok && strings.TrimSpace(value) != "" {
				result = append(result, strings.TrimSpace(value))
			}
		}
		return uniqueStrings(result)
	}
	return nil
}

func firstString(existing string, values ...any) string {
	if strings.TrimSpace(existing) != "" {
		return strings.TrimSpace(existing)
	}
	for _, value := range values {
		if stringValue, ok := value.(string); ok && strings.TrimSpace(stringValue) != "" {
			return strings.TrimSpace(stringValue)
		}
	}
	return ""
}

func firstFloat(existing float64, values ...any) float64 {
	if existing != 0 {
		return existing
	}
	for _, value := range values {
		switch number := value.(type) {
		case json.Number:
			if result, err := number.Float64(); err == nil {
				return result
			}
		case float64:
			return number
		case float32:
			return float64(number)
		case int:
			return float64(number)
		case string:
			if result, err := strconv.ParseFloat(number, 64); err == nil {
				return result
			}
		}
	}
	return 0
}

func firstInt(existing int, values ...any) int {
	if existing != 0 {
		return existing
	}
	return int(firstInt64(0, values...))
}

func firstInt64(existing int64, values ...any) int64 {
	if existing != 0 {
		return existing
	}
	for _, value := range values {
		switch number := value.(type) {
		case json.Number:
			if result, err := number.Int64(); err == nil {
				return result
			}
			if result, err := strconv.ParseFloat(number.String(), 64); err == nil {
				return int64(result)
			}
		case float64:
			return int64(number)
		case int64:
			return number
		case int:
			return int64(number)
		case string:
			if result, err := strconv.ParseInt(number, 10, 64); err == nil {
				return result
			}
		}
	}
	return 0
}

func parseParameters(value string, result *domain.GenerationMetadata) {
	text := strings.TrimSpace(value)
	if text == "" {
		return
	}
	settingsIndex := strings.LastIndex(text, "\nSteps:")
	body := text
	settings := ""
	if settingsIndex >= 0 {
		body = text[:settingsIndex]
		settings = strings.TrimSpace(text[settingsIndex+1:])
	}
	negativeMarker := "\nNegative prompt:"
	if index := strings.Index(body, negativeMarker); index >= 0 {
		result.Positive = firstString(result.Positive, strings.TrimSpace(body[:index]))
		result.Negative = firstString(result.Negative, strings.TrimSpace(body[index+len(negativeMarker):]))
	} else {
		result.Positive = firstString(result.Positive, body)
	}
	for _, item := range splitSettings(settings) {
		parts := strings.SplitN(item, ":", 2)
		if len(parts) != 2 {
			continue
		}
		key := strings.ToLower(strings.TrimSpace(parts[0]))
		raw := strings.TrimSpace(parts[1])
		switch key {
		case "steps":
			result.Steps = firstInt(result.Steps, raw)
		case "sampler":
			result.Sampler = firstString(result.Sampler, raw)
		case "scheduler", "schedule type":
			result.Scheduler = firstString(result.Scheduler, raw)
		case "cfg scale", "cfg":
			result.CFG = firstFloat(result.CFG, raw)
		case "seed":
			result.Seed = firstInt64(result.Seed, raw)
		case "model", "checkpoint":
			result.Checkpoint = firstString(result.Checkpoint, raw)
		case "size":
			dimensions := strings.FieldsFunc(raw, func(r rune) bool { return r == 'x' || r == 'X' || r == '×' })
			if len(dimensions) == 2 {
				result.Width = firstInt(result.Width, dimensions[0])
				result.Height = firstInt(result.Height, dimensions[1])
			}
		}
	}
	result.LoRAs = mergeLoRAs(result.LoRAs, parseLoRASyntax(result.Positive))
}

func splitSettings(value string) []string {
	var result []string
	var current strings.Builder
	inQuote := false
	for _, r := range value {
		if r == '"' {
			inQuote = !inQuote
		}
		if r == ',' && !inQuote {
			result = append(result, strings.TrimSpace(current.String()))
			current.Reset()
			continue
		}
		current.WriteRune(r)
	}
	if strings.TrimSpace(current.String()) != "" {
		result = append(result, strings.TrimSpace(current.String()))
	}
	return result
}

func parseLoRASyntax(prompt string) []domain.GenerationLoRA {
	matches := loraSyntaxPattern.FindAllStringSubmatch(prompt, -1)
	result := make([]domain.GenerationLoRA, 0, len(matches))
	for _, match := range matches {
		weight := 1.0
		if len(match) > 2 && match[2] != "" {
			if parsed, err := strconv.ParseFloat(match[2], 64); err == nil {
				weight = parsed
			}
		}
		result = append(result, domain.GenerationLoRA{
			Name: strings.TrimSpace(match[1]), Weight: weight, TriggerWords: []string{},
		})
	}
	return mergeLoRAs(result)
}

func mergeLoRAs(groups ...[]domain.GenerationLoRA) []domain.GenerationLoRA {
	result := make([]domain.GenerationLoRA, 0)
	index := make(map[string]int)
	for _, group := range groups {
		for _, item := range group {
			name := strings.TrimSpace(item.Name)
			if name == "" {
				continue
			}
			key := strings.ToLower(filepath.Base(name))
			if existingIndex, exists := index[key]; exists {
				existing := &result[existingIndex]
				if existing.Weight == 0 && item.Weight != 0 {
					existing.Weight = item.Weight
				}
				existing.TriggerWords = uniqueStrings(existing.TriggerWords, item.TriggerWords)
				continue
			}
			if item.Weight == 0 {
				item.Weight = 1
			}
			item.Name = name
			item.TriggerWords = uniqueStrings(item.TriggerWords)
			index[key] = len(result)
			result = append(result, item)
		}
	}
	return result
}

func uniqueStrings(groups ...[]string) []string {
	seen := make(map[string]struct{})
	result := make([]string, 0)
	for _, group := range groups {
		for _, value := range group {
			value = strings.TrimSpace(value)
			if value == "" {
				continue
			}
			key := strings.ToLower(value)
			if _, exists := seen[key]; exists {
				continue
			}
			seen[key] = struct{}{}
			result = append(result, value)
		}
	}
	return result
}
