package tagger

import (
	"fmt"
	"image"
	"image/color"
	"image/draw"
	_ "image/gif"
	_ "image/jpeg"
	_ "image/png"
	"math"
	"os"
)

type imageTensor struct {
	Data  []float32
	Shape []int64
}

func preprocessImage(path string, config modelConfig) (imageTensor, error) {
	file, err := os.Open(path)
	if err != nil {
		return imageTensor{}, fmt.Errorf("open image for Tagger: %w", err)
	}
	defer file.Close()

	source, _, err := image.Decode(file)
	if err != nil {
		return imageTensor{}, fmt.Errorf("decode image for Tagger: %w", err)
	}
	if source.Bounds().Dx() <= 0 || source.Bounds().Dy() <= 0 {
		return imageTensor{}, fmt.Errorf("image has invalid dimensions")
	}

	switch config.family {
	case familyWDV3:
		return preprocessWD(source, config.imageSize), nil
	case familyPixAIV09:
		return preprocessPixAIV09(source, config.imageSize), nil
	case familyCamieV2:
		return preprocessCamieV2(source, config.imageSize), nil
	default:
		return imageTensor{}, fmt.Errorf("unsupported Tagger family %q", config.family)
	}
}

func preprocessWD(source image.Image, size int) imageTensor {
	opaque := alphaComposite(source, color.RGBA{255, 255, 255, 255})
	bounds := opaque.Bounds()
	side := maxInt(bounds.Dx(), bounds.Dy())
	square := image.NewRGBA(image.Rect(0, 0, side, side))
	draw.Draw(square, square.Bounds(), &image.Uniform{C: color.White}, image.Point{}, draw.Src)
	offset := image.Pt((side-bounds.Dx())/2, (side-bounds.Dy())/2)
	draw.Draw(square, image.Rectangle{Min: offset, Max: offset.Add(bounds.Size())}, opaque, bounds.Min, draw.Src)

	data := make([]float32, size*size*3)
	for y := 0; y < size; y++ {
		srcY := scaleCoordinate(y, size, side)
		for x := 0; x < size; x++ {
			srcX := scaleCoordinate(x, size, side)
			r, g, b := sampleBicubicRGB(square, srcX, srcY)
			index := (y*size + x) * 3
			// Official WD ONNX examples use OpenCV BGR NHWC float32 input.
			data[index] = float32(clamp255(b))
			data[index+1] = float32(clamp255(g))
			data[index+2] = float32(clamp255(r))
		}
	}
	return imageTensor{
		Data:  data,
		Shape: []int64{1, int64(size), int64(size), 3},
	}
}

func preprocessPixAIV09(source image.Image, size int) imageTensor {
	opaque := alphaComposite(source, color.RGBA{255, 255, 255, 255})
	bounds := opaque.Bounds()
	plane := size * size
	data := make([]float32, 3*plane)
	for y := 0; y < size; y++ {
		srcY := scaleCoordinate(y, size, bounds.Dy()) + float64(bounds.Min.Y)
		for x := 0; x < size; x++ {
			srcX := scaleCoordinate(x, size, bounds.Dx()) + float64(bounds.Min.X)
			r, g, b := sampleBicubicRGB(opaque, srcX, srcY)
			index := y*size + x
			data[index] = normalizeMinusOneToOne(r)
			data[plane+index] = normalizeMinusOneToOne(g)
			data[2*plane+index] = normalizeMinusOneToOne(b)
		}
	}
	return imageTensor{
		Data:  data,
		Shape: []int64{1, 3, int64(size), int64(size)},
	}
}

func preprocessCamieV2(source image.Image, size int) imageTensor {
	opaque := alphaDiscard(source)
	bounds := opaque.Bounds()
	scale := float64(size) / float64(maxInt(bounds.Dx(), bounds.Dy()))
	newWidth := maxInt(1, int(float64(bounds.Dx())*scale))
	newHeight := maxInt(1, int(float64(bounds.Dy())*scale))
	pad := color.RGBA{R: 124, G: 116, B: 104, A: 255}
	canvas := image.NewRGBA(image.Rect(0, 0, size, size))
	draw.Draw(canvas, canvas.Bounds(), &image.Uniform{C: pad}, image.Point{}, draw.Src)

	offsetX := (size - newWidth) / 2
	offsetY := (size - newHeight) / 2
	for y := 0; y < newHeight; y++ {
		srcY := scaleCoordinate(y, newHeight, bounds.Dy()) + float64(bounds.Min.Y)
		for x := 0; x < newWidth; x++ {
			srcX := scaleCoordinate(x, newWidth, bounds.Dx()) + float64(bounds.Min.X)
			r, g, b := sampleLanczosRGB(opaque, srcX, srcY, 3)
			canvas.SetRGBA(offsetX+x, offsetY+y, color.RGBA{
				R: uint8(math.Round(clamp255(r))),
				G: uint8(math.Round(clamp255(g))),
				B: uint8(math.Round(clamp255(b))),
				A: 255,
			})
		}
	}

	const (
		meanR = 0.485
		meanG = 0.456
		meanB = 0.406
		stdR  = 0.229
		stdG  = 0.224
		stdB  = 0.225
	)
	plane := size * size
	data := make([]float32, 3*plane)
	for y := 0; y < size; y++ {
		for x := 0; x < size; x++ {
			pixel := canvas.RGBAAt(x, y)
			index := y*size + x
			data[index] = float32((float64(pixel.R)/255.0 - meanR) / stdR)
			data[plane+index] = float32((float64(pixel.G)/255.0 - meanG) / stdG)
			data[2*plane+index] = float32((float64(pixel.B)/255.0 - meanB) / stdB)
		}
	}
	return imageTensor{
		Data:  data,
		Shape: []int64{1, 3, int64(size), int64(size)},
	}
}

func alphaComposite(source image.Image, background color.Color) *image.RGBA {
	bounds := source.Bounds()
	output := image.NewRGBA(image.Rect(0, 0, bounds.Dx(), bounds.Dy()))
	draw.Draw(output, output.Bounds(), &image.Uniform{C: background}, image.Point{}, draw.Src)
	draw.Draw(output, output.Bounds(), source, bounds.Min, draw.Over)
	return output
}

func alphaDiscard(source image.Image) *image.RGBA {
	bounds := source.Bounds()
	output := image.NewRGBA(image.Rect(0, 0, bounds.Dx(), bounds.Dy()))
	for y := 0; y < bounds.Dy(); y++ {
		for x := 0; x < bounds.Dx(); x++ {
			pixel := color.NRGBAModel.Convert(source.At(bounds.Min.X+x, bounds.Min.Y+y)).(color.NRGBA)
			output.SetRGBA(x, y, color.RGBA{R: pixel.R, G: pixel.G, B: pixel.B, A: 255})
		}
	}
	return output
}

func scaleCoordinate(index, outputSize, inputSize int) float64 {
	return (float64(index)+0.5)*float64(inputSize)/float64(outputSize) - 0.5
}

func normalizeMinusOneToOne(value float64) float32 {
	return float32(clamp255(value)/127.5 - 1.0)
}

func sampleBicubicRGB(source image.Image, x, y float64) (float64, float64, float64) {
	x0 := int(math.Floor(x))
	y0 := int(math.Floor(y))
	bounds := source.Bounds()
	var red, green, blue, weightSum float64
	for j := -1; j <= 2; j++ {
		sy := clampInt(y0+j, bounds.Min.Y, bounds.Max.Y-1)
		wy := cubicWeight(y - float64(y0+j))
		for i := -1; i <= 2; i++ {
			sx := clampInt(x0+i, bounds.Min.X, bounds.Max.X-1)
			wx := cubicWeight(x - float64(x0+i))
			weight := wx * wy
			r, g, b := rgb255(source.At(sx, sy))
			red += r * weight
			green += g * weight
			blue += b * weight
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

func sampleLanczosRGB(source image.Image, x, y float64, radius int) (float64, float64, float64) {
	x0 := int(math.Floor(x))
	y0 := int(math.Floor(y))
	bounds := source.Bounds()
	var red, green, blue, weightSum float64
	for j := y0 - radius + 1; j <= y0+radius; j++ {
		sy := clampInt(j, bounds.Min.Y, bounds.Max.Y-1)
		wy := lanczosWeight(y-float64(j), radius)
		if wy == 0 {
			continue
		}
		for i := x0 - radius + 1; i <= x0+radius; i++ {
			sx := clampInt(i, bounds.Min.X, bounds.Max.X-1)
			wx := lanczosWeight(x-float64(i), radius)
			weight := wx * wy
			if weight == 0 {
				continue
			}
			r, g, b := rgb255(source.At(sx, sy))
			red += r * weight
			green += g * weight
			blue += b * weight
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

func lanczosWeight(x float64, radius int) float64 {
	x = math.Abs(x)
	if x == 0 {
		return 1
	}
	if x >= float64(radius) {
		return 0
	}
	return sinc(x) * sinc(x/float64(radius))
}

func sinc(x float64) float64 {
	value := math.Pi * x
	return math.Sin(value) / value
}

func rgb255(value color.Color) (float64, float64, float64) {
	r, g, b, _ := value.RGBA()
	return float64(r) / 257.0, float64(g) / 257.0, float64(b) / 257.0
}

func clamp255(value float64) float64 {
	if value < 0 {
		return 0
	}
	if value > 255 {
		return 255
	}
	return value
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

func maxInt(a, b int) int {
	if a > b {
		return a
	}
	return b
}
