using System.Drawing.Drawing2D;

namespace HoverWallpaper;

// Drag a crop rectangle over an image. The rectangle keeps the desktop's aspect
// ratio, so the cropped area fills the wallpaper without distortion.
public sealed class CropEditorForm : Form
{
    enum DragMode { None, Draw, Move }

    readonly Image? image;
    readonly string? error;
    readonly double k;              // normalized crop height = normalized crop width * k
    readonly Panel canvas;
    readonly Label info;
    readonly Size desktop = SystemInformation.VirtualScreen.Size;
    double nx, ny, nw, nh;          // crop rectangle as 0..1 of the image
    DragMode drag;
    PointF anchor, grabOffset;

    public CropRect Result => new() { X = nx * 100, Y = ny * 100, W = nw * 100, H = nh * 100 };

    double MaxW => Math.Min(1.0, 1.0 / k);

    public CropEditorForm(string path, CropRect initial)
    {
        Text = "Custom crop - drag to draw a new area, drag inside to move it";
        Width = 900; Height = 640;
        StartPosition = FormStartPosition.CenterParent;

        image = ImageHelper.LoadSafeImage(path, out error);
        double aspect = (double)desktop.Width / Math.Max(1, desktop.Height);
        k = image == null ? 1 : (double)image.Width / image.Height / aspect;

        canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 32), Cursor = Cursors.Cross };
        canvas.Paint += OnCanvasPaint;
        canvas.MouseDown += OnDown;
        canvas.MouseMove += OnMove;
        canvas.MouseUp += (_, _) => drag = DragMode.None;

        info = new Label { AutoSize = true, Location = new Point(14, 20) };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.FromArgb(245, 245, 247) };
        const AnchorStyles br = AnchorStyles.Bottom | AnchorStyles.Right;
        var reset = new Button { Text = "Reset", Width = 80, Height = 36, Location = new Point(Width - 375, 12), Anchor = br };
        var ok = new Button { Text = "Apply crop", DialogResult = DialogResult.OK, Width = 120, Height = 36, Location = new Point(Width - 285, 12), Anchor = br, Enabled = image != null };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 95, Height = 36, Location = new Point(Width - 150, 12), Anchor = br };
        reset.Click += (_, _) => ResetRect();
        bottom.Controls.AddRange(new Control[] { info, reset, ok, cancel });
        Controls.Add(canvas);
        Controls.Add(bottom);
        CancelButton = cancel;

        // Start from the saved crop if it is valid, otherwise the largest centered area.
        nw = Math.Min(initial.W / 100.0, MaxW);
        nh = nw * k;
        nx = initial.X / 100.0;
        ny = initial.Y / 100.0;
        if (initial.IsFull || nw < 0.02) ResetRect();
        ClampRect();
        UpdateInfo();
        FormClosed += (_, _) => image?.Dispose();
    }

    void ResetRect()
    {
        nw = MaxW; nh = nw * k;
        nx = (1 - nw) / 2; ny = (1 - nh) / 2;
        UpdateInfo();
        canvas.Invalidate();
    }

    void ClampRect()
    {
        nw = Math.Clamp(nw, 0.02, MaxW);
        nh = nw * k;
        nx = Math.Clamp(nx, 0, 1 - nw);
        ny = Math.Clamp(ny, 0, 1 - nh);
    }

    void UpdateInfo() =>
        info.Text = $"Crop: X {nx * 100:0.#}%  Y {ny * 100:0.#}%  W {nw * 100:0.#}%  H {nh * 100:0.#}%   (matches your screen shape)";

    Rectangle ImageBounds()
    {
        if (image == null) return Rectangle.Empty;
        int aw = canvas.ClientSize.Width - 32, ah = canvas.ClientSize.Height - 32;
        if (aw <= 0 || ah <= 0) return Rectangle.Empty;
        double s = Math.Min((double)aw / image.Width, (double)ah / image.Height);
        int w = Math.Max(1, (int)(image.Width * s)), h = Math.Max(1, (int)(image.Height * s));
        return new Rectangle(16 + (aw - w) / 2, 16 + (ah - h) / 2, w, h);
    }

    RectangleF CropOnScreen(Rectangle b) =>
        new(b.Left + (float)(nx * b.Width), b.Top + (float)(ny * b.Height), (float)(nw * b.Width), (float)(nh * b.Height));

    PointF ToNorm(Point p, Rectangle b) =>
        new((float)Math.Clamp((p.X - b.Left) / (double)b.Width, 0, 1), (float)Math.Clamp((p.Y - b.Top) / (double)b.Height, 0, 1));

    void OnDown(object? s, MouseEventArgs e)
    {
        var b = ImageBounds();
        if (e.Button != MouseButtons.Left || image == null || !b.Contains(e.Location)) return;
        var n = ToNorm(e.Location, b);
        if (CropOnScreen(b).Contains(e.Location))
        {
            drag = DragMode.Move;
            grabOffset = new PointF(n.X - (float)nx, n.Y - (float)ny);
        }
        else
        {
            drag = DragMode.Draw;
            anchor = n;
        }
    }

    void OnMove(object? s, MouseEventArgs e)
    {
        var b = ImageBounds();
        if (drag == DragMode.None || b.IsEmpty) return;
        var n = ToNorm(e.Location, b);
        if (drag == DragMode.Move)
        {
            nx = n.X - grabOffset.X;
            ny = n.Y - grabOffset.Y;
        }
        else
        {
            // Width follows the mouse; height is forced by the screen aspect ratio.
            double w = Math.Abs(n.X - anchor.X);
            double hLimit = (n.Y >= anchor.Y ? 1 - anchor.Y : anchor.Y) / k;
            w = Math.Min(Math.Max(w, Math.Abs(n.Y - anchor.Y) / k), Math.Min(MaxW, hLimit));
            nw = w; nh = w * k;
            nx = n.X >= anchor.X ? anchor.X : anchor.X - nw;
            ny = n.Y >= anchor.Y ? anchor.Y : anchor.Y - nh;
        }
        ClampRect();
        UpdateInfo();
        canvas.Invalidate();
    }

    void OnCanvasPaint(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        var b = ImageBounds();
        if (image == null || b.IsEmpty)
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(error ?? "No image loaded.", Font, Brushes.LightGray, canvas.ClientRectangle, sf);
            return;
        }
        g.DrawImage(image, b);
        var r = CropOnScreen(b);
        using var path = new GraphicsPath();
        path.AddRectangle(b);
        path.AddRectangle(r);
        using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var region = new Region(path);
        g.FillRegion(dim, region);
        using var pen = new Pen(Color.FromArgb(0, 150, 255), 2f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }
}
