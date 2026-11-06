namespace HoverWallpaper;

// Owns the tray icon, the wallpaper window and the settings window, so the
// wallpaper keeps running when the settings window is closed.
public sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly SynchronizationContext ui;
    readonly RegisteredWaitHandle showWait;
    WallpaperWindow? wallpaper;
    SettingsWindow? form;
    bool balloonShown;

    public bool IsExiting { get; private set; }

    public TrayApp(bool startHidden, EventWaitHandle showEvent, bool startWallpaper = true)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Hover Wallpaper", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        tray = new NotifyIcon
        {
            Text = "Hover Wallpaper",
            Icon = AppAssets.AppIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();

        // A second launch signals this event so the running instance opens its settings.
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => ui.Post(_ => ShowSettings(), null), null, Timeout.Infinite, false);

        var settings = AppSettings.Load();
        bool started = startWallpaper && settings.HasImages && TryStart(settings, out _, waitForDesktop: startHidden);
        if (!startHidden || !started) ShowSettings();
    }
    public bool IsRunning => wallpaper is { IsDisposed: false };

    public bool TryStart(AppSettings settings, out string? error, bool waitForDesktop = false)
    {
        error = null;
        Stop();
        try
        {
            var w = new WallpaperWindow(settings, waitForDesktop);
            w.Show();
            wallpaper = w;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Stop()
    {
        wallpaper?.Close();
        wallpaper = null;
    }

    // The settings window is WPF, so it lives on the WPF dispatcher. This app runs on the
    // WinForms loop; both use the same STA thread, so the dispatcher runs when it is created.
    public void ShowSettings()
    {
        if (System.Windows.Application.Current == null) return;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (form == null || form.IsClosed)
            {
                form = new SettingsWindow(this);
                // Lets WinForms dialogs (zone/crop editors) and their shortcuts work
                // while a WPF window is open.
                System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(form);
            }
            if (!form.IsVisible) form.Show();
            if (form.WindowState == System.Windows.WindowState.Minimized)
                form.WindowState = System.Windows.WindowState.Normal;
            form.Activate();
        });
    }

    public void NotifyHiddenToTray()
    {
        if (balloonShown) return;
        balloonShown = true;
        tray.ShowBalloonTip(3000, "Hover Wallpaper", "Still running in the tray. Right-click the icon to exit.", ToolTipIcon.Info);
    }

    public void ExitApp()
    {
        IsExiting = true;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        IsExiting = true;
        showWait.Unregister(null);
        tray.Visible = false;
        tray.Dispose();
        Stop();
        if (System.Windows.Application.Current != null && form is { IsClosed: false })
            System.Windows.Application.Current.Dispatcher.Invoke(() => form.Close());
        base.ExitThreadCore();
    }
}
