package domain

// SemanticEmbedding stores one normalized multimodal embedding for an asset.
// Vectors are persisted separately from AIAnalysis metadata so provenance and
// job state remain small while semantic search can scan compact binary values.
type SemanticEmbedding struct {
	AssetID      int64
	Engine       string
	ModelID      string
	ModelVersion string
	Dimensions   int
	Vector       []float32
}

type SemanticSearchHit struct {
	AssetID int64   `json:"assetId"`
	Score   float32 `json:"score"`
}
