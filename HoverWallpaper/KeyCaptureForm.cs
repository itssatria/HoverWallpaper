using System.Windows.Forms;

namespace HoverWallpaper;

// Small dialog that reads one key press. KeyPreview handles keys that normally move
// focus (Tab, arrows) and KeyDown/KeyUp cover text input plus modifiers.
public sealed class KeyCaptureForm : Form
{
    readonly Label label;
    readonly Button ok;
    readonly Button cancel;
    readonly System.Windows.Forms.Timer poll = new() { Interval = 60 };

    public Keys Result { get; private set; } = Keys.None;

    public KeyCaptureForm(string zoneTitle)
    {
        Text = $"Hold key — {zoneTitle}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 158);
        KeyPreview = true;
        BackColor = Color.FromArgb(245, 245, 247);

        var heading = new Label
        {
            Text = "Press the key to hold",
            Font = new Font(FontFamily.GenericSansSerif, 11f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 18)
        };
        label = new Label
        {
            Text = "Waiting for a key…",
            AutoSize = false,
            Size = new Size(344, 42),
            Location = new Point(18, 48),
            Font = new Font(FontFamily.GenericSansSerif, 10f),
            ForeColor = Color.FromArgb(90, 90, 100)
        };
        ok = new Button { Text = "Use key", DialogResult = DialogResult.OK, Enabled = false, Size = new Size(110, 32), Location = new Point(146, 108) };
        cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(100, 32), Location = new Point(262, 108) };
        ok.Click += (_, _) => { if (Result == Keys.None) DialogResult = DialogResult.None; };

        Controls.AddRange([heading, label, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // The wallpaper is not focused, so the dialog gets the key events itself.
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        poll.Tick += OnPoll;
        Shown += (_, _) => { Activate(); poll.Start(); };
        FormClosed += (_, _) => poll.Stop();
    }

    // A lone Alt or Win key can be swallowed by the shell before KeyDown fires, so a
    // short poll picks up whichever modifier is actually held.
    void OnPoll(object? sender, EventArgs e)
    {
        if (Result != Keys.None) return;
        foreach (var key in new[] { Keys.ControlKey, Keys.ShiftKey, Keys.Menu, Keys.LWin, Keys.RWin })
        {
            if (!HoldKeys.IsDown(key)) continue;
            Show(key);
            return;
        }
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        // Lone modifier presses never raise KeyDown with a usable KeyCode on all
        // layouts, so read which modifier is down from the event itself.
        Keys key = e.KeyCode;
        if (key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            Show(key);
            return;
        }

        if (HoldKeys.IsAssignable(key)) Show(key);
    }

    void OnKeyUp(object? sender, KeyEventArgs e)
    {
        // A modifier released without a normal key press still counts as the chosen key.
        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
            Show(e.KeyCode);
    }

    void Show(Keys key)
    {
        Result = key;
        label.Text = $"Hold {HoldKeys.Describe(key)} while hovering the zone.";
        label.ForeColor = Color.FromArgb(0, 100, 180);
        ok.Enabled = true;
    }
}
