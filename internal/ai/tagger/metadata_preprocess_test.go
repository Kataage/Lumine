package tagger

import (
	"image"
	"image/color"
	"math"
	"os"
	"path/filepath"
	"testing"
)

func TestLoadCSVTags(t *testing.T) {
	path := filepath.Join(t.TempDir(), "tags.csv")
	if err := os.WriteFile(path, []byte(
		"name,category\n1girl,0\ncharacter_x,4\nexplicit,9\n",
	), 0o644); err != nil {
		t.Fatal(err)
	}
	rows, err := loadCSVTags(path)
	if err != nil {
		t.Fatal(err)
	}
	if len(rows) != 3 || rows[1].Category != 4 || rows[2].Category != 9 {
		t.Fatalf("unexpected rows: %+v", rows)
	}
}

func TestLoadCamieTags(t *testing.T) {
	path := filepath.Join(t.TempDir(), "metadata.json")
	content := `{
	  "dataset_info": {
	    "tag_mapping": {
	      "idx_to_tag": {"0":"1girl","1":"hero_x","2":"explicit"},
	      "tag_to_category": {"1girl":"general","hero_x":"character","explicit":"rating"}
	    }
	  }
	}`
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
	rows, err := loadCamieTags(path)
	if err != nil {
		t.Fatal(err)
	}
	if len(rows) != 3 {
		t.Fatalf("rows = %d", len(rows))
	}
	if rows[0].Name != "1girl" || rows[0].Category != 0 {
		t.Fatalf("row0 = %+v", rows[0])
	}
	if rows[1].Category != 4 || rows[2].Category != 9 {
		t.Fatalf("unexpected categories: %+v", rows)
	}
}

func TestCandidatePreprocessShapesAndRanges(t *testing.T) {
	img := image.NewNRGBA(image.Rect(0, 0, 1, 1))
	img.SetNRGBA(0, 0, color.NRGBA{R: 255, G: 0, B: 0, A: 255})

	wd := preprocessWD(img, 1)
	if got, want := wd.Shape, []int64{1, 1, 1, 3}; !sameShape(got, want) {
		t.Fatalf("WD shape = %v want %v", got, want)
	}
	if len(wd.Data) != 3 || wd.Data[0] != 0 || wd.Data[1] != 0 || wd.Data[2] != 255 {
		t.Fatalf("WD BGR = %v", wd.Data)
	}

	pixai := preprocessPixAIV09(img, 1)
	if got, want := pixai.Shape, []int64{1, 3, 1, 1}; !sameShape(got, want) {
		t.Fatalf("PixAI shape = %v want %v", got, want)
	}
	if !near(float64(pixai.Data[0]), 1, 1e-6) ||
		!near(float64(pixai.Data[1]), -1, 1e-6) ||
		!near(float64(pixai.Data[2]), -1, 1e-6) {
		t.Fatalf("PixAI normalized RGB = %v", pixai.Data)
	}

	camie := preprocessCamieV2(img, 1)
	if got, want := camie.Shape, []int64{1, 3, 1, 1}; !sameShape(got, want) {
		t.Fatalf("Camie shape = %v want %v", got, want)
	}
	if !near(float64(camie.Data[0]), (1.0-0.485)/0.229, 1e-5) {
		t.Fatalf("Camie red channel = %v", camie.Data[0])
	}
}

func TestWDAlphaCompositesOnWhite(t *testing.T) {
	img := image.NewNRGBA(image.Rect(0, 0, 1, 1))
	img.SetNRGBA(0, 0, color.NRGBA{R: 255, A: 0})
	wd := preprocessWD(img, 1)
	if len(wd.Data) != 3 || wd.Data[0] != 255 || wd.Data[1] != 255 || wd.Data[2] != 255 {
		t.Fatalf("transparent pixel should become white BGR, got %v", wd.Data)
	}
}

func sameShape(a, b []int64) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

func near(a, b, tolerance float64) bool {
	return math.Abs(a-b) <= tolerance
}
