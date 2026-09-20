package siglip2

import (
	"fmt"
	"image"
	_ "image/gif"
	_ "image/jpeg"
	_ "image/png"
	"math"
	"os"
)

const (
	siglipImageSize = 224
	siglipChannels  = 3
)

func preprocessImage(path string) ([]float32, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, fmt.Errorf("open image for SigLIP: %w", err)
	}
	defer file.Close()

	source, _, err := image.Decode(file)
	if err != nil {
		return nil, fmt.Errorf("decode image for SigLIP: %w", err)
	}
	bounds := source.Bounds()
	if bounds.Dx() <= 0 || bounds.Dy() <= 0 {
		return nil, fmt.Errorf("image has invalid dimensions")
	}

	output := make([]float32, siglipChannels*siglipImageSize*siglipImageSize)
	plane := siglipImageSize * siglipImageSize
	for y := 0; y < siglipImageSize; y++ {
		srcY := (float64(y)+0.5)*float64(bounds.Dy())/siglipImageSize - 0.5
		for x := 0; x < siglipImageSize; x++ {
			srcX := (float64(x)+0.5)*float64(bounds.Dx())/siglipImageSize - 0.5
			r, g, b := sampleBilinear(source, bounds, srcX, srcY)
			index := y*siglipImageSize + x
			output[index] = normalizePixel(r)
			output[plane+index] = normalizePixel(g)
			output[2*plane+index] = normalizePixel(b)
		}
	}
	return output, nil
}

func normalizePixel(value float64) float32 {
	if value < 0 {
		value = 0
	}
	if value > 255 {
		value = 255
	}
	return float32(value/127.5 - 1.0)
}

func sampleBilinear(source image.Image, bounds image.Rectangle, x, y float64) (float64, float64, float64) {
	x0 := int(math.Floor(x))
	y0 := int(math.Floor(y))
	x1 := x0 + 1
	y1 := y0 + 1
	fx := x - float64(x0)
	fy := y - float64(y0)

	x0 = clampInt(x0, 0, bounds.Dx()-1) + bounds.Min.X
	x1 = clampInt(x1, 0, bounds.Dx()-1) + bounds.Min.X
	y0 = clampInt(y0, 0, bounds.Dy()-1) + bounds.Min.Y
	y1 = clampInt(y1, 0, bounds.Dy()-1) + bounds.Min.Y

	r00, g00, b00, _ := source.At(x0, y0).RGBA()
	r10, g10, b10, _ := source.At(x1, y0).RGBA()
	r01, g01, b01, _ := source.At(x0, y1).RGBA()
	r11, g11, b11, _ := source.At(x1, y1).RGBA()

	mix := func(v00, v10, v01, v11 uint32) float64 {
		top := float64(v00)*(1-fx) + float64(v10)*fx
		bottom := float64(v01)*(1-fx) + float64(v11)*fx
		return (top*(1-fy) + bottom*fy) / 257.0
	}
	return mix(r00, r10, r01, r11), mix(g00, g10, g01, g11), mix(b00, b10, b01, b11)
}

func clampInt(value, minValue, maxValue int) int {
	if value < minValue {
		return minValue
	}
	if value > maxValue {
		return maxValue
	}
	return value
}
