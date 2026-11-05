using System.Drawing.Drawing2D;

namespace HoverWallpaper;

public sealed class ZoneEditorForm : Form
{
    private readonly Image? image1;
    private readonly Image? image2;
    private readonly double desktopAspectRatio;

    private int zoneX;
    private int zoneY;
    private int zoneWidth;
    private int zoneHeight;

    private bool isDrawing;
    private Point dragStartScreenPt;
    private bool previewHoverActive;
    private bool cursorInsideZone;

    private readonly Panel canvas;
    private readonly Label lblCoords;
    private readonly CheckBox chkTestHover;

    public int ResultX => zoneX;
    public int ResultY => zoneY;
    public int ResultWidth => zoneWidth;
    public int ResultHeight => zoneHeight;

    public ZoneEditorForm(string? image1Path, string? image2Path, int initialX, int initialY, int initialW, int initialH)
    {
        Text = "Visual Hover-Zone Editor - Drag to Select Area";
        Width = 920;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        zoneX = Math.Clamp(initialX, 0, 100);
        zoneY = Math.Clamp(initialY, 0, 100);
        zoneWidth = Math.Clamp(initialW, 1, 100 - zoneX);
        zoneHeight = Math.Clamp(initialH, 1, 100 - zoneY);

        var vs = SystemInformation.VirtualScreen;
        desktopAspectRatio = (double)vs.Width / Math.Max(1, vs.Height);

        image1 = LoadSafeImage(image1Path);
        image2 = LoadSafeImage(image2Path);

        // Header toolbar
        var topBar = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 8, 12, 8), BackColor = Color.FromArgb(245, 245, 247) };
        var infoLabel = new Label
        {
            Text = "Click and drag on the image below to define the hover trigger area:",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Bold),
            Location = new Point(12, 14)
        };

        chkTestHover = new CheckBox
        {
            Text = "Test hover effect live",
            AutoSize = true,
            Location = new Point(560, 14),
            Font = new Font(FontFamily.GenericSansSerif, 9f)
        };
        chkTestHover.CheckedChanged += (_, _) =>
        {
            previewHoverActive = chkTestHover.Checked;
            canvas.Invalidate();
        };

        topBar.Controls.Add(infoLabel);
        if (image2 != null)
        {
            topBar.Controls.Add(chkTestHover);
        }

        // Bottom status & buttons bar
        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(14, 10, 14, 10), BackColor = Color.FromArgb(245, 245, 247) };
        lblCoords = new Label
        {
            Text = $"Hover Area: X = {zoneX}%, Y = {zoneY}%, Width = {zoneWidth}%, Height = {zoneHeight}%",
            AutoSize = true,
            Location = new Point(14, 20),
            Font = new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Regular)
        };

        var btnOk = new Button { Text = "Apply Selection", DialogResult = DialogResult.OK, Width = 130, Height = 36, Location = new Point(Width - 280, 12), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 95, Height = 36, Location = new Point(Width - 135, 12), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

        btnOk.Click += (_, _) =>
        {
            // Normalize minimum bounds
            if (zoneWidth < 1) zoneWidth = 1;
            if (zoneHeight < 1) zoneHeight = 1;
            Close();
        };
        btnCancel.Click += (_, _) => Close();

        // Preset buttons
        var btnPresetCenter = new Button { Text = "Center (50%)", AutoSize = true, Height = 30, Location = new Point(Width - 530, 15), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnPresetCenter.Click += (_, _) => SetZone(25, 25, 50, 50);

        var btnPresetFull = new Button { Text = "Full Screen", AutoSize = true, Height = 30, Location = new Point(Width - 435, 15), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnPresetFull.Click += (_, _) => SetZone(0, 0, 100, 100);

        bottomBar.Controls.AddRange(new Control[] { lblCoords, btnPresetCenter, btnPresetFull, btnOk, btnCancel });

        // Canvas for drawing
        canvas = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 32),
            Cursor = Cursors.Cross
        };
        canvas.Paint += OnCanvasPaint;
        canvas.MouseDown += OnCanvasMouseDown;
        canvas.MouseMove += OnCanvasMouseMove;
        canvas.MouseUp += OnCanvasMouseUp;
        canvas.MouseLeave += (_, _) =>
        {
            if (cursorInsideZone)
            {
                cursorInsideZone = false;
                if (previewHoverActive) canvas.Invalidate();
            }
        };

        Controls.Add(canvas);
        Controls.Add(topBar);
        Controls.Add(bottomBar);

        FormClosed += (_, _) =>
        {
            image1?.Dispose();
            image2?.Dispose();
        };
    }

    private static Image? LoadSafeImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(stream);
            return new Bitmap(temp);
        }
        catch
        {
            return null;
        }
    }

    private void SetZone(int x, int y, int w, int h)
    {
        zoneX = Math.Clamp(x, 0, 100);
        zoneY = Math.Clamp(y, 0, 100);
        zoneWidth = Math.Clamp(w, 1, 100 - zoneX);
        zoneHeight = Math.Clamp(h, 1, 100 - zoneY);
        UpdateCoordsLabel();
        canvas.Invalidate();
    }

    private Rectangle GetImageDisplayBounds()
    {
        int availableW = canvas.ClientSize.Width - 32;
        int availableH = canvas.ClientSize.Height - 32;
        if (availableW <= 0 || availableH <= 0) return Rectangle.Empty;

        int targetW = availableW;
        int targetH = (int)(targetW / desktopAspectRatio);

        if (targetH > availableH)
        {
            targetH = availableH;
            targetW = (int)(targetH * desktopAspectRatio);
        }

        int x = 16 + (availableW - targetW) / 2;
        int y = 16 + (availableH - targetH) / 2;
        return new Rectangle(x, y, Math.Max(1, targetW), Math.Max(1, targetH));
    }

    private void UpdateCoordsLabel()
    {
        lblCoords.Text = $"Hover Area: X = {zoneX}%, Y = {zoneY}%, Width = {zoneWidth}%, Height = {zoneHeight}%";
    }

    private void OnCanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var imgBounds = GetImageDisplayBounds();
        if (imgBounds.Width <= 0 || imgBounds.Height <= 0) return;

        // Draw active background image
        var currentImage = (previewHoverActive && cursorInsideZone && image2 != null) ? image2 : (image1 ?? image2);
        if (currentImage != null)
        {
            g.DrawImage(currentImage, imgBounds);
        }
        else
        {
            using var placeholderBrush = new SolidBrush(Color.FromArgb(50, 50, 55));
            g.FillRectangle(placeholderBrush, imgBounds);
            using var textBrush = new SolidBrush(Color.Gray);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("No image loaded.\nDrag to define trigger coordinates on the virtual desktop canvas.", Font, textBrush, imgBounds, sf);
        }

        // Calculate current screen rectangle for the hover zone
        int zx = imgBounds.Left + (int)Math.Round(imgBounds.Width * (zoneX / 100.0));
        int zy = imgBounds.Top + (int)Math.Round(imgBounds.Height * (zoneY / 100.0));
        int zw = Math.Max(1, (int)Math.Round(imgBounds.Width * (zoneWidth / 100.0)));
        int zh = Math.Max(1, (int)Math.Round(imgBounds.Height * (zoneHeight / 100.0)));
        var zoneRect = new Rectangle(zx, zy, zw, zh);

        if (!previewHoverActive)
        {
            // Dim outside area
            using var dimBrush = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
            using var path = new GraphicsPath();
            path.AddRectangle(imgBounds);
            path.AddRectangle(zoneRect);
            using var region = new Region(path);
            g.FillRegion(dimBrush, region);

            // Highlight border
            using var penBorder = new Pen(Color.FromArgb(0, 150, 255), 2f);
            penBorder.DashStyle = DashStyle.Dash;
            g.DrawRectangle(penBorder, zoneRect);

            // Corner handles
            int handleSize = 6;
            using var handleBrush = new SolidBrush(Color.White);
            using var handlePen = new Pen(Color.FromArgb(0, 120, 215), 1.5f);
            Point[] corners =
            [
                new(zoneRect.Left - handleSize / 2, zoneRect.Top - handleSize / 2),
                new(zoneRect.Right - handleSize / 2, zoneRect.Top - handleSize / 2),
                new(zoneRect.Left - handleSize / 2, zoneRect.Bottom - handleSize / 2),
                new(zoneRect.Right - handleSize / 2, zoneRect.Bottom - handleSize / 2)
            ];
            foreach (var pt in corners)
            {
                var r = new Rectangle(pt.X, pt.Y, handleSize, handleSize);
                g.FillRectangle(handleBrush, r);
                g.DrawRectangle(handlePen, r);
            }

            // Draw floating tag inside zone
            string tag = $"{zoneWidth}% x {zoneHeight}%";
            using var tagFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);
            var tagSize = g.MeasureString(tag, tagFont);
            int tagX = Math.Max(zoneRect.Left + 4, zoneRect.Left + (zoneRect.Width - (int)tagSize.Width) / 2);
            int tagY = Math.Max(zoneRect.Top + 4, zoneRect.Top + (zoneRect.Height - (int)tagSize.Height) / 2);
            var tagBgRect = new Rectangle(tagX - 3, tagY - 2, (int)tagSize.Width + 6, (int)tagSize.Height + 4);

            using var tagBgBrush = new SolidBrush(Color.FromArgb(200, 0, 120, 215));
            g.FillRectangle(tagBgBrush, tagBgRect);
            g.DrawString(tag, tagFont, Brushes.White, tagX, tagY);
        }
        else
        {
            // In live hover test mode, draw subtle border
            using var pen = new Pen(cursorInsideZone ? Color.FromArgb(100, 0, 255, 0) : Color.FromArgb(120, 255, 255, 255), 1.5f);
            pen.DashStyle = DashStyle.Dot;
            g.DrawRectangle(pen, zoneRect);
        }

        // Draw desktop screen boundary
        using var desktopPen = new Pen(Color.FromArgb(90, 90, 100), 1.5f);
        g.DrawRectangle(desktopPen, imgBounds);
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || previewHoverActive) return;

        var imgBounds = GetImageDisplayBounds();
        if (!imgBounds.Contains(e.Location)) return;

        isDrawing = true;
        dragStartScreenPt = e.Location;
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        var imgBounds = GetImageDisplayBounds();

        if (previewHoverActive)
        {
            int zx = imgBounds.Left + (int)Math.Round(imgBounds.Width * (zoneX / 100.0));
            int zy = imgBounds.Top + (int)Math.Round(imgBounds.Height * (zoneY / 100.0));
            int zw = Math.Max(1, (int)Math.Round(imgBounds.Width * (zoneWidth / 100.0)));
            int zh = Math.Max(1, (int)Math.Round(imgBounds.Height * (zoneHeight / 100.0)));
            var zoneRect = new Rectangle(zx, zy, zw, zh);

            bool inside = zoneRect.Contains(e.Location);
            if (inside != cursorInsideZone)
            {
                cursorInsideZone = inside;
                canvas.Invalidate();
            }
            return;
        }

        if (!isDrawing) return;

        int curX = Math.Clamp(e.X, imgBounds.Left, imgBounds.Right);
        int curY = Math.Clamp(e.Y, imgBounds.Top, imgBounds.Bottom);

        int minX = Math.Min(dragStartScreenPt.X, curX);
        int maxX = Math.Max(dragStartScreenPt.X, curX);
        int minY = Math.Min(dragStartScreenPt.Y, curY);
        int maxY = Math.Max(dragStartScreenPt.Y, curY);

        int newX = (int)Math.Round(100.0 * (minX - imgBounds.Left) / imgBounds.Width);
        int newY = (int)Math.Round(100.0 * (minY - imgBounds.Top) / imgBounds.Height);
        int newW = (int)Math.Round(100.0 * (maxX - minX) / imgBounds.Width);
        int newH = (int)Math.Round(100.0 * (maxY - minY) / imgBounds.Height);

        zoneX = Math.Clamp(newX, 0, 100);
        zoneY = Math.Clamp(newY, 0, 100);
        zoneWidth = Math.Clamp(Math.Max(1, newW), 1, 100 - zoneX);
        zoneHeight = Math.Clamp(Math.Max(1, newH), 1, 100 - zoneY);

        UpdateCoordsLabel();
        canvas.Invalidate();
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && isDrawing)
        {
            isDrawing = false;
            canvas.Invalidate();
        }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }
}

