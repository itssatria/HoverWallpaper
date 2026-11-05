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

        // HW_SHOT_SAVE_PRESET=<name> exercises the preset save path (including copying
        // the images) before capturing, so the result can be checked from a build.
        var savePresetName = Environment.GetEnvironmentVariable("HW_SHOT_SAVE_PRESET");
        if (!string.IsNullOrEmpty(savePresetName))
        {
          //  Console.WriteLine(window.DebugSavePreset(savePresetName));
          //  window.UpdateLayout();
          //  Pump();
        }

        // HW_SHOT_ZONES=1 captures just the hover-zone cards at their own size.
        if (Environment.GetEnvironmentVariable("HW_SHOT_ZONES") == "1")
        {
            var zones = window.ZonesPanel;
            zones.UpdateLayout();
            Pump();
            var zbmp = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(zones.ActualWidth)),
                Math.Max(1, (int)Math.Ceiling(zones.ActualHeight)),
                96, 96, PixelFormats.Pbgra32);
            zbmp.Render(zones);
            Save(zbmp, outPath);
            Console.WriteLine($"zones {(int)zones.ActualWidth}x{(int)zones.ActualHeight} -> {outPath}");
            return;
        }

        // HW_SHOT_PRESETS=1 captures just the presets card.
        if (Environment.GetEnvironmentVariable("HW_SHOT_PRESETS") == "1")
        {
            var card = window.PresetsCard;
            card.UpdateLayout();
            Pump();
            var pbmp = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(card.ActualWidth)),
                Math.Max(1, (int)Math.Ceiling(card.ActualHeight)),
                96, 96, PixelFormats.Pbgra32);
            pbmp.Render(card);
            Save(pbmp, outPath);
            Console.WriteLine($"presets {(int)card.ActualWidth}x{(int)card.ActualHeight} -> {outPath}");
            return;
        }

        int w = (int)Math.Ceiling(window.ActualWidth);
        int h = (int)Math.Ceiling(window.ActualHeight);
        if (w <= 0 || h <= 0) { Console.Error.WriteLine($"bad window size {w}x{h}"); return; }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        Save(rtb, outPath);
        Console.WriteLine($"window {w}x{h} -> {outPath}");
    }

    static void Save(BitmapSource source, string outPath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = File.Create(outPath);
        encoder.Save(fs);
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
