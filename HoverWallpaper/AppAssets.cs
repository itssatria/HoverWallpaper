using System.Reflection;

namespace HoverWallpaper;

// Icon and logo are embedded in the exe, so they also work in the single-file publish.
internal static class AppAssets
{
    static Icon? icon;

    public static Icon AppIcon => icon ??= LoadIcon();

    static Icon LoadIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HoverWallpaper.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    /// <summary>The 512px logo used in the settings header, or null if it is missing.</summary>
    public static Image? LoadLogo()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HoverWallpaper.png");
            if (s == null) return null;
            using var img = Image.FromStream(s);
            return new Bitmap(img);
        }
        catch { return null; }
    }
}
