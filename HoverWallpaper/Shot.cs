using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HoverWallpaper;

// Dev-only helper (DEBUG builds): renders the settings window to a PNG so the layout
// can be checked without a human looking at the screen. Triggered by HW_SHOT=<path>.
public static class Shot
{
    public static void Run(TrayApp tray, string outPath)
    {
        tray.ShowSettings();
        Pump();

        var window = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (window == null) { Console.Error.WriteLine("settings window not found"); return; }

        window.UpdateLayout();
        Pump();

        int w = (int)Math.Ceiling(window.ActualWidth);
        int h = (int)Math.Ceiling(window.ActualHeight);
        if (w <= 0 || h <= 0) { Console.Error.WriteLine($"bad window size {w}x{h}"); return; }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(outPath)) encoder.Save(fs);
        Console.WriteLine($"window {w}x{h} -> {outPath}");
    }

    // The WPF dispatcher runs on the WinForms message loop, so pump it between steps.
    static void Pump()
    {
        for (int i = 0; i < 20; i++)
        {
            System.Windows.Forms.Application.DoEvents();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(50);
            System.Windows.Forms.Application.DoEvents();
        }
    }
}
