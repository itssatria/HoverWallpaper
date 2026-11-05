using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;

namespace HoverWallpaper;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FitMode { Fill, Fit, Stretch, Tile, Center, Custom }

/// <summary>Crop rectangle in percent of the source image (used by FitMode.Custom).</summary>
public sealed class CropRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 100;
    public double H { get; set; } = 100;

    [JsonIgnore]
    public bool IsFull => W >= 99.9 && H >= 99.9;
}

public static class FitModes
{
    // Order must match the FitMode enum.
    public static readonly string[] Labels =
    {
        "Fill (cover, keeps proportions)",
        "Fit (whole image, black bars)",
        "Stretch (ignores proportions)",
        "Tile",
        "Center (original size)",
        "Custom crop…",
    };
}

public static class ImageRenderer
{
    /// <summary>Renders <paramref name="src"/> onto a new bitmap of <paramref name="target"/> size.</summary>
    public static Bitmap Render(Image src, Size target, FitMode mode, CropRect? crop)
    {
        var bmp = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        float sw = src.Width, sh = src.Height, tw = target.Width, th = target.Height;
        switch (mode)
        {
            case FitMode.Stretch:
                g.DrawImage(src, new RectangleF(0, 0, tw, th));
                break;

            case FitMode.Fill:
            case FitMode.Fit:
            {
                float scale = mode == FitMode.Fill ? Math.Max(tw / sw, th / sh) : Math.Min(tw / sw, th / sh);
                float dw = sw * scale, dh = sh * scale;
                g.DrawImage(src, new RectangleF((tw - dw) / 2, (th - dh) / 2, dw, dh));
                break;
            }

            case FitMode.Center:
                g.DrawImage(src, new RectangleF((tw - sw) / 2, (th - sh) / 2, sw, sh));
                break;

            case FitMode.Tile:
            {
                using var tex = new TextureBrush(src, WrapMode.Tile);
                g.FillRectangle(tex, 0, 0, tw, th);
                break;
            }

            case FitMode.Custom:
            {
                var c = crop ?? new CropRect();
                float cx = (float)Math.Clamp(c.X, 0, 99) / 100f * sw;
                float cy = (float)Math.Clamp(c.Y, 0, 99) / 100f * sh;
                float cw = Math.Clamp((float)c.W / 100f * sw, 1f, sw - cx);
                float ch = Math.Clamp((float)c.H / 100f * sh, 1f, sh - cy);
                g.DrawImage(src, new RectangleF(0, 0, tw, th), new RectangleF(cx, cy, cw, ch), GraphicsUnit.Pixel);
                break;
            }
        }
        return bmp;
    }
}
