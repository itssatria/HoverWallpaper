namespace HoverWallpaper;

// Owns the tray icon, the wallpaper window and the settings window, so the
// wallpaper keeps running when the settings window is closed.
public sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly SynchronizationContext ui;
    readonly RegisteredWaitHandle showWait;
    WallpaperWindow? wallpaper;
    SettingsForm? form;
    bool balloonShown;

    public bool IsExiting { get; private set; }

    public TrayApp(bool startHidden, EventWaitHandle showEvent)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Hover Wallpaper", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        tray = new NotifyIcon
        {
            Text = "Hover Wallpaper",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();

        // A second launch signals this event so the running instance opens its settings.
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => ui.Post(_ => ShowSettings(), null), null, Timeout.Infinite, false);

        var settings = AppSettings.Load();
        bool started = settings.HasImages && TryStart(settings, out _);
        if (!startHidden || !started) ShowSettings();
    }
    public bool IsRunning => wallpaper is { IsDisposed: false };

    public bool TryStart(AppSettings settings, out string? error)
    {
        error = null;
        Stop();
        try
        {
            var w = new WallpaperWindow(settings);
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

    public void ShowSettings()
    {
        if (form == null || form.IsDisposed) form = new SettingsForm(this);
        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
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

    static Icon LoadIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    protected override void ExitThreadCore()
    {
        IsExiting = true;
        showWait.Unregister(null);
        tray.Visible = false;
        tray.Dispose();
        Stop();
        if (form is { IsDisposed: false }) form.Close();
        base.ExitThreadCore();
    }
}
