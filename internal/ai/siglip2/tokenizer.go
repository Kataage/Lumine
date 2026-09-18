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

	"golang.org/x/text/unicode/norm"
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

type tokenizerFile struct {
	Model struct {
		Type  string         `json:"type"`
		UnkID int            `json:"unk_id"`
		Vocab []unigramPiece `json:"vocab"`
	} `json:"model"`
}

type unigramTokenizer struct {
	byFirst [256][]unigramPiece
	unkID   int64
}

func loadTokenizer(path string) (*unigramTokenizer, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read SigLIP tokenizer: %w", err)
	}
	return parseTokenizer(data)
}

func parseTokenizer(data []byte) (*unigramTokenizer, error) {
	var file tokenizerFile
	if err := json.Unmarshal(data, &file); err != nil {
		return nil, fmt.Errorf("decode SigLIP tokenizer: %w", err)
	}
	if file.Model.Type != "Unigram" {
		return nil, fmt.Errorf("unsupported tokenizer model type %q", file.Model.Type)
	}
	if len(file.Model.Vocab) == 0 {
		return nil, errors.New("SigLIP tokenizer vocabulary is empty")
	}

	tokenizer := &unigramTokenizer{unkID: int64(file.Model.UnkID)}
	if tokenizer.unkID < 0 {
		tokenizer.unkID = siglipUNKID
	}
	for id := range file.Model.Vocab {
		piece := file.Model.Vocab[id]
		piece.ID = int64(id)
		if piece.Text == "" {
			continue
		}
		first := []byte(piece.Text)[0]
		tokenizer.byFirst[first] = append(tokenizer.byFirst[first], piece)
	}
	return tokenizer, nil
}

type unigramStep struct {
	score float64
	prev  int
	id    int64
	set   bool
}

func (t *unigramTokenizer) Encode64(text string) ([siglipTextLength]int64, error) {
	var output [siglipTextLength]int64
	normalized := normalizeSigLIPText(text)
	if normalized == "" {
		output[0] = siglipEOSID
		return output, nil
	}

	input := []byte(normalized)
	steps := make([]unigramStep, len(input)+1)
	steps[0] = unigramStep{score: 0, prev: -1, set: true}

	minPieceScore := 0.0
	haveScore := false
	for _, bucket := range t.byFirst {
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
		for _, piece := range t.byFirst[input[pos]] {
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
			steps[end] = unigramStep{score: score, prev: pos, id: t.unkID, set: true}
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
	ids = append(ids, siglipEOSID)
	copy(output[:], ids)
	for i := len(ids); i < len(output); i++ {
		output[i] = siglipPadID
	}
	return output, nil
}

func normalizeSigLIPText(text string) string {
	text = norm.NFKC.String(strings.ToLower(strings.TrimSpace(text)))
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
