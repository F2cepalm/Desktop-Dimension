using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using InfiniteSpace.Native;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace InfiniteSpace.Shell;

internal sealed record TrayActions(
    Action Draw,
    Action Block,
    Action Move,
    Action Remove,
    Action Ink,
    Action EraseWithBrush,
    Action EraseAll,
    IReadOnlyList<string> Themes,
    Func<string> CurrentTheme,
    Action<string> SetTheme,
    Func<bool> IsAutoStartEnabled,
    Action<bool> SetAutoStart,
    Action OpenDataFolder,
    Action Exit);

/// <summary>Notification-area icon: the app's only permanent UI besides the spaces themselves.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private readonly IntPtr _imageHandle;

    public TrayIcon(TrayActions actions)
    {
        var autostart = new WinForms.ToolStripMenuItem("Start with Windows");
        autostart.Click += (_, _) => actions.SetAutoStart(!actions.IsAutoStartEnabled());

        var theme = new WinForms.ToolStripMenuItem("Theme");
        foreach (string name in actions.Themes)
        {
            var option = new WinForms.ToolStripMenuItem(name);
            option.Click += (_, _) => actions.SetTheme(name);
            theme.DropDownItems.Add(option);
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Item("Draw a space", "Ctrl+Alt+S", actions.Draw));
        menu.Items.Add(Item("Spawn a block", "Ctrl+Alt+B", actions.Block));
        menu.Items.Add(Item("Move a space", "Ctrl+Alt+M", actions.Move));
        // From the menu the cursor is on the tray, so this removes the newest space.
        menu.Items.Add(Item("Remove newest space", "Ctrl+Alt+E", actions.Remove));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("Ink colour and size…", "Right-click a space", actions.Ink));
        menu.Items.Add(Item("Erase with brush…", null, actions.EraseWithBrush));
        menu.Items.Add(Item("Erase all spaces…", null, actions.EraseAll));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(theme);
        menu.Items.Add(autostart);
        menu.Items.Add(Item("Open data folder", null, actions.OpenDataFolder));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("Exit", null, actions.Exit));
        menu.Opening += (_, _) =>
        {
            autostart.Checked = actions.IsAutoStartEnabled();
            string current = actions.CurrentTheme();
            foreach (WinForms.ToolStripMenuItem option in theme.DropDownItems)
                option.Checked = option.Text == current;
        };

        _imageHandle = CreateIconHandle();
        _image = Drawing.Icon.FromHandle(_imageHandle);
        _icon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = "Infinite Space",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                actions.Draw();
        };
    }

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
        Win32.DestroyIcon(_imageHandle);
    }

    private static WinForms.ToolStripMenuItem Item(string text, string? shortcut, Action action)
    {
        var item = new WinForms.ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>A tiny dream block: black blob, white rim, a few coloured pixel stars.</summary>
    private static IntPtr CreateIconHandle()
    {
        using var bitmap = new Drawing.Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var blob = new GraphicsPath())
            {
                blob.AddClosedCurve(
                [
                    new Drawing.Point(3, 15), new Drawing.Point(8, 4), new Drawing.Point(20, 3),
                    new Drawing.Point(29, 9), new Drawing.Point(28, 22), new Drawing.Point(18, 29),
                    new Drawing.Point(6, 26),
                ], 0.5f);
                g.FillPath(Drawing.Brushes.Black, blob);
                using var rim = new Drawing.Pen(Drawing.Color.White, 2.5f);
                g.DrawPath(rim, blob);
            }

            g.SmoothingMode = SmoothingMode.None;
            Plus(g, Drawing.Color.FromArgb(0xFF, 0xEF, 0x11), 11, 12);
            Plus(g, Drawing.Color.FromArgb(0x5F, 0xCD, 0xE4), 20, 19);
            Dot(g, Drawing.Color.FromArgb(0xFF, 0x00, 0xD0), 20, 9);
            Dot(g, Drawing.Color.FromArgb(0x08, 0xA3, 0x10), 11, 21);
        }
        return bitmap.GetHicon();

        static void Dot(Drawing.Graphics g, Drawing.Color colour, int x, int y)
        {
            using var brush = new Drawing.SolidBrush(colour);
            g.FillRectangle(brush, x - 1, y - 1, 2, 2);
        }

        static void Plus(Drawing.Graphics g, Drawing.Color colour, int x, int y)
        {
            using var brush = new Drawing.SolidBrush(colour);
            g.FillRectangle(brush, x - 1, y - 3, 2, 6);
            g.FillRectangle(brush, x - 3, y - 1, 6, 2);
        }
    }
}
