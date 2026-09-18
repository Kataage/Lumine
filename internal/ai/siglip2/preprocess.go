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
			r, g, b := sampleBicubic(source, bounds, srcX, srcY)
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

func sampleBicubic(source image.Image, bounds image.Rectangle, x, y float64) (float64, float64, float64) {
	x0 := int(math.Floor(x))
	y0 := int(math.Floor(y))
	var red, green, blue, weightSum float64
	for j := -1; j <= 2; j++ {
		sy := clampInt(y0+j, 0, bounds.Dy()-1) + bounds.Min.Y
		wy := cubicWeight(y - float64(y0+j))
		for i := -1; i <= 2; i++ {
			sx := clampInt(x0+i, 0, bounds.Dx()-1) + bounds.Min.X
			wx := cubicWeight(x - float64(x0+i))
			weight := wx * wy
			r, g, b, _ := source.At(sx, sy).RGBA()
			red += (float64(r) / 257.0) * weight
			green += (float64(g) / 257.0) * weight
			blue += (float64(b) / 257.0) * weight
			weightSum += weight
		}
	}
	if weightSum != 0 {
		red /= weightSum
		green /= weightSum
		blue /= weightSum
	}
	return red, green, blue
}

func cubicWeight(x float64) float64 {
	x = math.Abs(x)
	const a = -0.5
	switch {
	case x <= 1:
		return (a+2)*x*x*x - (a+3)*x*x + 1
	case x < 2:
		return a*x*x*x - 5*a*x*x + 8*a*x - 4*a
	default:
		return 0
	}
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
