using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using SkiaSharp;

namespace DriveAlt.Api.Processing;

/// <summary>
/// Produces the images students see from a page: a thumbnail and one crop per question and answer region.
/// Pages are shown as they are; answers are screenshots like everything else (the bot just never types them).
/// </summary>
public sealed class PageRenderer(Storage storage)
{
    public const float CropPadding = 0.012f;
    public const int ThumbWidth = 420;

    public static bool HasCrop(string label) => label is RegionLabels.Question or RegionLabels.Answer;

    public Task RenderAsync(Guid fileId, Page page, CancellationToken ct) => Task.Run(() =>
    {
        using var img = ImageOps.LoadUpright(storage.PageImage(fileId, page.Number));
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(storage.ThumbDir(fileId));
        using (var thumb = ImageOps.FitWithin(img, ThumbWidth, ThumbWidth * 2))
            ImageOps.SaveJpeg(thumb, storage.Thumb(fileId, page.Number), 80);

        foreach (var r in page.Regions.Where(r => HasCrop(r.Label)))
        {
            var rect = ToRect(r, img.Width, img.Height, CropPadding);
            if (rect.Width < 8 || rect.Height < 8) continue;
            using var crop = ImageOps.Crop(img, rect);
            ImageOps.SaveJpeg(crop, storage.Crop(r.Id), 88);
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
}
