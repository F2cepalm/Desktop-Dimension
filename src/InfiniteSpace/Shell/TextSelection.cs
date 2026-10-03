using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Media;
using InfiniteSpace.Native;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace InfiniteSpace.Shell;

/// <summary>Text selected in another app, as text wrap needs it.</summary>
/// <param name="Bounds">Where it is on screen (DIPs); null when the app doesn't say.</param>
/// <param name="LineHeight">The height of its first line on screen (DIPs), which gives its size as shown.</param>
/// <param name="PointSize">Its font size as the app reports it, in points.</param>
/// <param name="Color">Its colour, if the app says.</param>
internal sealed record SelectedText(string Text, Rect? Bounds, double? LineHeight, double? PointSize, bool Bold, bool Italic, Color? Color);

/// <summary>
/// Reads what is selected in the app in front: through UI Automation where the app supports it (browsers,
/// Office, most editors: text, place and style), else by copying it (text only).
/// </summary>
internal static class TextSelection
{
    /// <summary>Longest selection taken, in characters.</summary>
    public const int MaxChars = 100_000;

    /// <summary>
    /// The focused element's selected text, its line rectangles and its first character's style, through
    /// UI Automation. Slow on some apps (a few hundred ms): call it off the UI thread.
    /// </summary>
    /// <returns>null when nothing is selected or the app doesn't expose its text.</returns>
    public static SelectedText? FromAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused is null || focused.Current.ProcessId == Environment.ProcessId
                ? null // focus is on this app: nothing of another app's is selected
                : Read(focused);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The selection in <paramref name="element"/>, or in the nearest of its parents that has text.</summary>
    internal static SelectedText? Read(AutomationElement element)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            for (int depth = 0; element is not null && depth < 10; depth++, element = walker.GetParent(element))
            {
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                    continue;
                var ranges = ((TextPattern)pattern).GetSelection();
                string text = string.Join("\n", ranges.Select(r => r.GetText(MaxChars))).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
                if (text.Trim().Length == 0)
                    return null;
                // WPF reports colours as 0xRRGGBB; the UI Automation spec (browsers, Office) as COLORREF, 0xBBGGRR.
                return Describe(text, ranges, rgbColors: element.Current.FrameworkId == "WPF");
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            // The app went away or doesn't support it after all.
        }
        return null;
    }

    private static SelectedText Describe(string text, TextPatternRange[] ranges, bool rgbColors)
    {
        Rect? bounds = null;
        double? lineHeight = null;
        foreach (var range in ranges)
        {
            foreach (var r in range.GetBoundingRectangles())
            {
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0)
                    continue;
                var dips = new Rect(r.X / Dpi.Scale, r.Y / Dpi.Scale, r.Width / Dpi.Scale, r.Height / Dpi.Scale);
                lineHeight ??= dips.Height;
                bounds = bounds is { } b ? Rect.Union(b, dips) : dips;
            }
        }

        // The style of its first character (a selection of mixed styles reports no single value).
        var first = ranges[0].Clone();
        first.MoveEndpointByRange(TextPatternRangeEndpoint.End, first, TextPatternRangeEndpoint.Start);
        first.ExpandToEnclosingUnit(TextUnit.Character);
        object? Attribute(AutomationTextAttribute attribute)
        {
            var value = first.GetAttributeValue(attribute);
            return value == AutomationElement.NotSupported || value == TextPattern.MixedAttributeValue ? null : value;
        }
        Color? color = null;
        if (Attribute(TextPattern.ForegroundColorAttribute) is int value)
        {
            byte low = (byte)(value & 0xFF), middle = (byte)((value >> 8) & 0xFF), high = (byte)((value >> 16) & 0xFF);
            color = rgbColors ? Color.FromRgb(high, middle, low) : Color.FromRgb(low, middle, high);
        }
        return new SelectedText(
            text,
            bounds,
            lineHeight,
            Attribute(TextPattern.FontSizeAttribute) is double points && points > 0 ? points : null,
            Attribute(TextPattern.FontWeightAttribute) is int weight && weight >= 600,
            Attribute(TextPattern.IsItalicAttribute) is true,
            color);
    }

    /// <summary>
    /// For apps without UI Automation text: copies the selection (Ctrl+C) and reads it, putting what was on
    /// the clipboard back afterwards. The keys the user holds are let go of first, so the copy can't become
    /// another shortcut (Ctrl+W would close a tab).
    /// </summary>
    /// <remarks>
    /// The clipboard is worked on a thread of its own: copying what is on it (a big picture) or waiting on
    /// an app that renders its data late (or hangs) would otherwise hold up every frame meanwhile.
    /// </remarks>
    /// <returns>null when nothing was copied.</returns>
    public static async Task<string?> CopyAsync()
    {
        for (int i = 0; AnyKeyDown(); i++)
        {
            if (i >= 100)
                return null; // keys held for over 1.5 s: better not to copy at all
            await Task.Delay(15);
        }
        return await OnClipboardThread(Copy);
    }

    /// <summary>Runs clipboard work on a thread of its own (the clipboard needs a single-threaded apartment).</summary>
    private static Task<T> OnClipboardThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        { IsBackground = true, Name = "Infinite Space clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>The copy itself (see <see cref="CopyAsync"/>), on the clipboard's thread.</summary>
    private static string? Copy()
    {
        var saved = SaveClipboard();
        uint before = GetClipboardSequenceNumber();
        Press(VK_CONTROL, VK_C);
        for (int i = 0; i < 25 && GetClipboardSequenceNumber() == before; i++)
            Thread.Sleep(20);
        string? text = null;
        if (GetClipboardSequenceNumber() != before)
        {
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                // Another app holds the clipboard.
            }
        }
        RestoreClipboard(saved);
        text = text?.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return string.IsNullOrWhiteSpace(text) ? null : text.Length > MaxChars ? text[..MaxChars] : text;
    }

    /// <summary>Types a key into the app in front (a key that turned out not to be meant for this app).</summary>
    public static void SendKey(ushort vk) => Press(vk);

    /// <summary>The W key's virtual-key code.</summary>
    public const ushort VK_W = 0x57;

    private const ushort VK_CONTROL = 0x11, VK_C = 0x43;

    /// <summary>W or a modifier still held down.</summary>
    private static bool AnyKeyDown() =>
        new[] { VK_W, 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(vk => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>Every format on the clipboard that can be read, copied, so it can be put back.</summary>
    private static DataObject? SaveClipboard()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } current)
                return null;
            var copy = new DataObject();
            foreach (string format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    if (current.GetData(format, autoConvert: false) is { } data)
                        copy.SetData(format, data);
                }
                catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException or InvalidOperationException)
                {
                    // A format that can't be read here (a delayed render that failed): left out.
                }
            }
            return copy;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static void RestoreClipboard(DataObject? saved)
    {
        try
        {
            if (saved is null || saved.GetFormats().Length == 0)
                Clipboard.Clear();
            else
                Clipboard.SetDataObject(saved, copy: true);
        }
        catch (ExternalException)
        {
            // Another app holds the clipboard: what was copied stays on it.
        }
    }

    /// <summary>Presses the keys in order and lets go of them in reverse.</summary>
    private static void Press(params ushort[] keys)
    {
        var inputs = new INPUT[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], up: false);
            inputs[^(i + 1)] = Key(keys[i], up: true);
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static INPUT Key(ushort vk, bool up) => new()
        {
            Type = INPUT_KEYBOARD,
            Union = new InputUnion { Keyboard = new KEYBDINPUT { Vk = vk, Flags = up ? KEYEVENTF_KEYUP : 0 } },
        };
    }

    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    /// <summary>Sized as its largest member (the mouse's), as SendInput expects.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
