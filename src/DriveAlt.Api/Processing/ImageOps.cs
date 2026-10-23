using SkiaSharp;

namespace DriveAlt.Api.Processing;

/// <summary>Small SkiaSharp helpers: decode with EXIF orientation, resize, crop, JPEG encode.</summary>
public static class ImageOps
{
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public static (int Width, int Height) Identify(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException($"Unreadable image: {Path.GetFileName(path)}");
        return Upright(codec);
    }

    public static (int Width, int Height) Identify(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Unreadable image");
        return Upright(codec);
    }

    private static (int Width, int Height) Upright(SKCodec codec)
    {
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        return codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (h, w)
            : (w, h);
    }

    /// <summary>Decodes an image and applies its EXIF orientation, so phone photos come out upright.</summary>
    public static SKBitmap LoadUpright(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException($"Unreadable image: {Path.GetFileName(path)}");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"Could not decode {Path.GetFileName(path)}: {result}");
        }
        var origin = codec.EncodedOrigin;
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap;
        using (bitmap) return Orient(bitmap, origin);
    }

    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (src.Height, src.Width) : (src.Width, src.Height);
        var dst = new SKBitmap(new SKImageInfo(w, h, src.ColorType, src.AlphaType));
        using var canvas = new SKCanvas(dst);
        float sw = src.Width, sh = src.Height;
        // Maps stored pixel (x, y) to upright (x', y'): x' = ScaleX·x + SkewX·y + TransX, y' = SkewY·x + ScaleY·y + TransY.
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, sw, 0, 1, 0, 0, 0, 1),     // mirror
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, sw, 0, -1, sh, 0, 0, 1), // 180°
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, sh, 0, 0, 1),    // flip
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),         // transpose
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, sh, 1, 0, 0, 0, 0, 1),      // 90° clockwise
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, sh, -1, 0, sw, 0, 0, 1), // transverse
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, sw, 0, 0, 1),    // 90° counter-clockwise
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(m);
        canvas.DrawBitmap(src, 0, 0, Sampling, null);
        return dst;
    }

    public static SKBitmap FitWithin(SKBitmap src, int maxW, int maxH)
    {
        var scale = Math.Min(1f, Math.Min(maxW / (float)src.Width, maxH / (float)src.Height));
        if (scale >= 1f) return src.Copy();
        var info = new SKImageInfo(Math.Max(1, (int)(src.Width * scale)), Math.Max(1, (int)(src.Height * scale)), src.ColorType, src.AlphaType);
        return src.Resize(info, Sampling) ?? throw new InvalidOperationException("Resize failed");
    }

    public static SKBitmap Crop(SKBitmap src, SKRectI rect)
    {
        var dst = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, src.ColorType, src.AlphaType));
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(src, rect, new SKRect(0, 0, rect.Width, rect.Height), Sampling, null);
        return dst;
    }

    public static void SaveJpeg(SKBitmap bitmap, string path, int quality)
    {
        // JPEG has no alpha: flatten onto white first so transparent PNGs don't turn black.
        using var flat = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0, Sampling, null);
        }
        using var data = flat.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidOperationException("JPEG encode failed");
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) data.SaveTo(fs);
        File.Move(tmp, path, overwrite: true);
    }
}
