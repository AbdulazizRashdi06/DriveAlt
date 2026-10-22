using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using SkiaSharp;

namespace DriveAlt.Api.Processing;

/// <summary>
/// Produces everything students see from a page: the masked page (answer regions burned out), a thumbnail and
/// one crop per question region. Crops are cut from the masked page, so an answer that overlaps a question box
/// is hidden there too.
/// </summary>
public sealed class PageRenderer(Storage storage)
{
    private static readonly SKColor Ink = SKColor.Parse("#2A1B0F");
    private static readonly SKColor InkStripe = SKColor.Parse("#3D2A19");
    private static readonly SKColor Gold = SKColor.Parse("#FFD34D");

    public const float CropPadding = 0.012f;
    public const int ThumbWidth = 420;

    public Task RenderAsync(Guid fileId, Page page, CancellationToken ct) => Task.Run(() =>
    {
        using var img = ImageOps.LoadUpright(storage.PageImage(fileId, page.Number));
        var answers = page.Regions.Where(r => r.Label == RegionLabels.Answer).ToList();
        if (answers.Count > 0)
        {
            using var canvas = new SKCanvas(img);
            foreach (var a in answers) Hatch(canvas, ToRect(a, img.Width, img.Height, 0));
        }
        ct.ThrowIfCancellationRequested();
        ImageOps.SaveJpeg(img, storage.MaskedPage(fileId, page.Number), 88);

        Directory.CreateDirectory(storage.ThumbDir(fileId));
        using (var thumb = ImageOps.FitWithin(img, ThumbWidth, ThumbWidth * 2))
            ImageOps.SaveJpeg(thumb, storage.Thumb(fileId, page.Number), 80);

        foreach (var q in page.Regions.Where(r => r.Label == RegionLabels.Question))
        {
            var rect = ToRect(q, img.Width, img.Height, CropPadding);
            if (rect.Width < 8 || rect.Height < 8) continue;
            using var crop = ImageOps.Crop(img, rect);
            ImageOps.SaveJpeg(crop, storage.Crop(q.Id), 88);
        }
    }, ct);

    public static SKRectI ToRect(Region r, int width, int height, float pad)
    {
        var x0 = Math.Clamp(r.X - pad, 0, 1);
        var y0 = Math.Clamp(r.Y - pad, 0, 1);
        var x1 = Math.Clamp(r.X + r.W + pad, 0, 1);
        var y1 = Math.Clamp(r.Y + r.H + pad, 0, 1);
        var left = (int)MathF.Floor(x0 * width);
        var top = (int)MathF.Floor(y0 * height);
        var right = Math.Min(width, (int)MathF.Ceiling(x1 * width));
        var bottom = Math.Min(height, (int)MathF.Ceiling(y1 * height));
        return new SKRectI(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    /// <summary>Fully opaque ink fill with diagonal stripes and a gold pill: nothing underneath survives.</summary>
    private static void Hatch(SKCanvas canvas, SKRectI r)
    {
        var rect = new SKRect(r.Left, r.Top, r.Right, r.Bottom);
        using var fill = new SKPaint { Color = Ink, IsAntialias = false, Style = SKPaintStyle.Fill };
        canvas.DrawRect(rect, fill);

        canvas.Save();
        canvas.ClipRect(rect);
        var stripe = Math.Max(8, Math.Min(rect.Width, rect.Height) / 12);
        using var stripePaint = new SKPaint { Color = InkStripe, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stripe };
        for (var x = rect.Left - rect.Height; x < rect.Right + stripe; x += stripe * 2)
            canvas.DrawLine(x, rect.Bottom + stripe, x + rect.Height + 2 * stripe, rect.Top - stripe, stripePaint);

        // Gold pill in the middle, so it reads as "hidden on purpose" rather than a broken image.
        if (rect.Height >= 18 && rect.Width >= 40)
        {
            var pillH = Math.Clamp(rect.Height / 3f, 6, 34);
            var pillW = Math.Min(rect.Width * 0.5f, pillH * 4.2f);
            using var gold = new SKPaint { Color = Gold, IsAntialias = true, Style = SKPaintStyle.Fill };
            var pill = SKRect.Create(rect.MidX - pillW / 2, rect.MidY - pillH / 2, pillW, pillH);
            canvas.DrawRoundRect(pill, pillH / 2, pillH / 2, gold);
        }
        canvas.Restore();
    }
}
