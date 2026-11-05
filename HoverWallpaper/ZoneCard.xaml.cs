namespace HoverWallpaper;

// One hover zone card: its own hover image, fit/crop, and rectangle inside the
// shared Image 1 preview. SettingsWindow owns the dialogs and persistence.
public partial class ZoneCard : System.Windows.Controls.UserControl
{
    bool ready;
    bool normalizing;

    public HoverZone Zone { get; private set; } = new();
    public int Index { get; private set; }

    public event EventHandler? BrowseRequested;
    public event EventHandler? CropRequested;
    public event EventHandler? VisualEditorRequested;
    public event EventHandler? RemoveRequested;
    public event EventHandler? KeyRequested;
    public event EventHandler? ValuesChanged;

    public ZoneCard()
    {
        InitializeComponent();
        foreach (var label in FitModes.Labels) FitBox.Items.Add(label);
        PreviewCanvas.SizeChanged += (_, _) => UpdateRect();
        ready = true;
        UpdateCropButton();
    }

    // Binds the card to the model. Value edits are copied back immediately through
    // CopyValuesToModel, so the card never holds unsaved coordinate changes.
    public void Bind(HoverZone zone, int index, bool canRemove)
    {
        ready = false;
        Index = index;
        Zone = zone;
        HeaderText.Text = $"Hover zone {index + 1}";
        FitBox.SelectedIndex = Math.Max(0, (int)zone.Fit);
        NumX.Value = zone.X;
        NumY.Value = zone.Y;
        NumW.Value = Math.Clamp(zone.Width, 1, Math.Max(1, 100 - zone.X));
        NumH.Value = Math.Clamp(zone.Height, 1, Math.Max(1, 100 - zone.Y));
        NameText.Text = string.IsNullOrWhiteSpace(zone.Image) ? "No image selected" : System.IO.Path.GetFileName(zone.Image);
        NameText.ToolTip = string.IsNullOrWhiteSpace(zone.Image) ? null : zone.Image;
        RemoveBtn.Visibility = canRemove ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ready = true;
        UpdateCropButton();
        UpdateKey();
        UpdateRect();
    }

    public void UpdateKey()
    {
        string key = Zone.HoldKey ?? "";
        KeyText.Text = HoldKeys.Describe(key);
        ClearKeyBtn.Visibility = string.IsNullOrWhiteSpace(key)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        SetKeyBtn.Content = string.IsNullOrWhiteSpace(key) ? "Set key…" : "Change…";
        KeyHint.Text = string.IsNullOrWhiteSpace(key)
            ? "Hovering this zone reveals its image. Set a key to require it as well."
            : $"Reveals only while {HoldKeys.Describe(key)} is held and the cursor is inside the zone.";
        UpdateRect();
    }

    public void UpdateName()
    {
        NameText.Text = string.IsNullOrWhiteSpace(Zone.Image) ? "No image selected" : System.IO.Path.GetFileName(Zone.Image);
        NameText.ToolTip = string.IsNullOrWhiteSpace(Zone.Image) ? null : Zone.Image;
    }

    public void SetThumbnail(System.Windows.Media.ImageSource? source, string? error)
    {
        Thumb.Source = source;
        ThumbPlaceholder.Visibility = source == null
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error == null
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
        // An unreadable path still shows its file name; LoadZoneImage passes the error.
        UpdateName();
    }

    public void SetPreview(System.Windows.Media.ImageSource? source, string? error)
    {
        PreviewImage.Source = source;
        PreviewPlaceholder.Text = error != null ? $"Image 1 error:\n{error}" : "Choose Image 1 to preview";
        PreviewPlaceholder.Visibility = source == null
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        UpdateRect();
    }

    // Clamped values, so a zone can never extend past the bottom-right of the screen.
    public int X => Val(NumX, 0, 100, 60);
    public int Y => Val(NumY, 0, 100, 30);
    public int ZoneWidth => Math.Clamp(Val(NumW, 1, 100, 25), 1, Math.Max(1, 100 - X));
    public int ZoneHeight => Math.Clamp(Val(NumH, 1, 100, 35), 1, Math.Max(1, 100 - Y));

    public FitMode Fit => (FitMode)Math.Max(0, FitBox.SelectedIndex);

    public void CopyValuesToModel()
    {
        Zone.X = X;
        Zone.Y = Y;
        Zone.Width = ZoneWidth;
        Zone.Height = ZoneHeight;
        Zone.Fit = Fit;
        // The key is not a text field; it is written by SetKey/ClearKey already, but
        // this keeps a hand-broken value from reaching the wallpaper.
        if (!HoldKeys.TryParse(Zone.HoldKey, out var key)) Zone.HoldKey = "";
        else Zone.HoldKey = key.ToString();
    }

    static int Val(Wpf.Ui.Controls.NumberBox box, int min, int max, int fallback) =>
        box.Value is double d && !double.IsNaN(d) ? Math.Clamp((int)Math.Round(d), min, max) : fallback;

    void Browse_Click(object sender, System.Windows.RoutedEventArgs e) => BrowseRequested?.Invoke(this, EventArgs.Empty);
    void Crop_Click(object sender, System.Windows.RoutedEventArgs e) => CropRequested?.Invoke(this, EventArgs.Empty);
    void VisualEditor_Click(object sender, System.Windows.RoutedEventArgs e) => VisualEditorRequested?.Invoke(this, EventArgs.Empty);
    void Remove_Click(object sender, System.Windows.RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
    void SetKey_Click(object sender, System.Windows.RoutedEventArgs e) => KeyRequested?.Invoke(this, EventArgs.Empty);

    void ClearKey_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Zone.HoldKey = "";
        UpdateKey();
        ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    void Fit_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ready) return;
        UpdateCropButton();
        ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    void Values_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!ready || normalizing) return;
        NormalizeValues();
        UpdateRect();
        ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Shows the same clamped numbers the model will use, so a width that runs off
    // the screen is corrected in the field rather than only when applying.
    void NormalizeValues()
    {
        normalizing = true;
        try
        {
            if (NumX.Value != X) NumX.Value = X;
            if (NumY.Value != Y) NumY.Value = Y;
            if (NumW.Value != ZoneWidth) NumW.Value = ZoneWidth;
            if (NumH.Value != ZoneHeight) NumH.Value = ZoneHeight;
        }
        finally { normalizing = false; }
    }

    void UpdateCropButton()
    {
        CropBtn.Visibility = FitBox.SelectedIndex == (int)FitMode.Custom
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public void UpdateRect()
    {
        double cw = PreviewCanvas.ActualWidth;
        double ch = PreviewCanvas.ActualHeight;
        if (PreviewImage.Source == null || cw <= 0 || ch <= 0)
        {
            PreviewRect.Visibility = System.Windows.Visibility.Collapsed;
            PreviewKeyBadge.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }
        PreviewRect.Visibility = System.Windows.Visibility.Visible;
        double left = Math.Round(cw * X / 100.0);
        double top = Math.Round(ch * Y / 100.0);
        double w = Math.Max(1, Math.Round(cw * ZoneWidth / 100.0));
        double h = Math.Max(1, Math.Round(ch * ZoneHeight / 100.0));
        System.Windows.Controls.Canvas.SetLeft(PreviewRect, left);
        System.Windows.Controls.Canvas.SetTop(PreviewRect, top);
        PreviewRect.Width = w;
        PreviewRect.Height = h;

        // A key-gated zone gets its key printed inside the rectangle so the preview
        // shows why it behaves differently.
        bool hasKey = !string.IsNullOrWhiteSpace(Zone.HoldKey);
        PreviewKeyBadge.Visibility = hasKey
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        if (hasKey)
        {
            PreviewKeyText.Text = HoldKeys.Describe(Zone.HoldKey);
            System.Windows.Controls.Canvas.SetLeft(PreviewKeyBadge, left + 3);
            System.Windows.Controls.Canvas.SetTop(PreviewKeyBadge, top + 3);
        }
    }
}
