package tagger

import (
	"encoding/csv"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"sort"
	"strconv"
	"strings"
)

type tagRow struct {
	Name     string
	Category int
}

var categoryNameToID = map[string]int{
	"general":   0,
	"artist":    1,
	"copyright": 3,
	"character": 4,
	"meta":      5,
	"year":      6,
	"rating":    9,
}

func loadTags(config modelConfig) ([]tagRow, error) {
	switch config.family {
	case familyCamieV2:
		return loadCamieTags(config.tagsPath)
	default:
		return loadCSVTags(config.tagsPath)
	}
}

func loadCSVTags(path string) ([]tagRow, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, fmt.Errorf("open Tagger tag metadata: %w", err)
	}
	defer file.Close()

	reader := csv.NewReader(file)
	header, err := reader.Read()
	if err != nil {
		return nil, fmt.Errorf("read Tagger tag metadata header: %w", err)
	}
	columns := map[string]int{}
	for index, name := range header {
		columns[strings.ToLower(strings.TrimSpace(name))] = index
	}
	nameIndex := firstColumn(columns, "name", "tag", "tag_name")
	categoryIndex := firstColumn(columns, "category", "type")
	if nameIndex < 0 {
		return nil, fmt.Errorf("Tagger metadata %s has no name column", path)
	}

	var rows []tagRow
	for {
		record, err := reader.Read()
		if err != nil {
			if errors.Is(err, io.EOF) {
				break
			}
			return nil, fmt.Errorf("read Tagger tag metadata: %w", err)
		}
		if nameIndex >= len(record) {
			continue
		}
		name := strings.TrimSpace(record[nameIndex])
		if name == "" {
			continue
		}
		category := 0
		if categoryIndex >= 0 && categoryIndex < len(record) {
			raw := strings.TrimSpace(record[categoryIndex])
			if value, parseErr := strconv.ParseFloat(raw, 64); parseErr == nil {
				category = int(value)
			} else if mapped, ok := categoryNameToID[strings.ToLower(raw)]; ok {
				category = mapped
			}
		}
		rows = append(rows, tagRow{Name: name, Category: category})
	}
	if len(rows) == 0 {
		return nil, fmt.Errorf("Tagger metadata %s contains no tags", path)
	}
	return rows, nil
}

func firstColumn(columns map[string]int, names ...string) int {
	for _, name := range names {
		if index, ok := columns[name]; ok {
			return index
		}
	}
	return -1
}

func loadCamieTags(path string) ([]tagRow, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read Camie tag metadata: %w", err)
	}
	var root map[string]any
	if err := json.Unmarshal(data, &root); err != nil {
		return nil, fmt.Errorf("decode Camie tag metadata: %w", err)
	}

	tagMapping := nestedMap(root, "dataset_info", "tag_mapping")
	if tagMapping == nil {
		tagMapping = nestedMap(root, "tag_mapping")
	}
	if tagMapping == nil {
		tagMapping = root
	}

	idxToTag := mapValue(tagMapping["idx_to_tag"])
	if idxToTag == nil {
		idxToTag = mapValue(root["idx_to_tag"])
	}
	tagToCategory := mapValue(tagMapping["tag_to_category"])
	if tagToCategory == nil {
		tagToCategory = mapValue(root["tag_to_category"])
	}
	if idxToTag == nil || tagToCategory == nil {
		return nil, fmt.Errorf("Camie metadata is missing idx_to_tag/tag_to_category")
	}

	type indexedTag struct {
		index int
		row   tagRow
	}
	indexed := make([]indexedTag, 0, len(idxToTag))
	seen := map[int]bool{}
	for rawIndex, rawName := range idxToTag {
		index, err := strconv.Atoi(rawIndex)
		if err != nil || index < 0 || seen[index] {
			return nil, fmt.Errorf("Camie metadata contains invalid output index %q", rawIndex)
		}
		seen[index] = true
		name := strings.TrimSpace(fmt.Sprint(rawName))
		if name == "" {
			return nil, fmt.Errorf("Camie metadata contains an empty tag at index %d", index)
		}
		category := categoryFromAny(tagToCategory[name])
		indexed = append(indexed, indexedTag{
			index: index,
			row:   tagRow{Name: name, Category: category},
		})
	}
	sort.Slice(indexed, func(i, j int) bool { return indexed[i].index < indexed[j].index })
	rows := make([]tagRow, len(indexed))
	for expected, item := range indexed {
		if item.index != expected {
			return nil, fmt.Errorf("Camie tag indices are not contiguous at %d", expected)
		}
		rows[expected] = item.row
	}
	if len(rows) == 0 {
		return nil, fmt.Errorf("Camie metadata contains no tags")
	}
	return rows, nil
}

func nestedMap(root map[string]any, path ...string) map[string]any {
	current := root
	for _, key := range path {
		next := mapValue(current[key])
		if next == nil {
			return nil
		}
		current = next
	}
	return current
}

func mapValue(value any) map[string]any {
	if value == nil {
		return nil
	}
	if typed, ok := value.(map[string]any); ok {
		return typed
	}
	return nil
}

func categoryFromAny(value any) int {
	switch typed := value.(type) {
	case float64:
		return int(typed)
	case string:
		if numeric, err := strconv.Atoi(strings.TrimSpace(typed)); err == nil {
			return numeric
		}
		return categoryNameToID[strings.ToLower(strings.TrimSpace(typed))]
	default:
		return 0
	}
}
