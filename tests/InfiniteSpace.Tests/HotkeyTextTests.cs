using System.Windows.Input;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public class HotkeyTextTests
{
    [Theory]
    [InlineData("Ctrl+Alt+`", ModifierKeys.Control | ModifierKeys.Alt, Key.Oem3)]
    [InlineData("win + alt + w", ModifierKeys.Windows | ModifierKeys.Alt, Key.W)]
    [InlineData("Ctrl+Shift+F9", ModifierKeys.Control | ModifierKeys.Shift, Key.F9)]
    [InlineData("Ctrl+Shift+Alt+T", ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt, Key.T)]
    [InlineData("Control+Alt+5", ModifierKeys.Control | ModifierKeys.Alt, Key.D5)]
    public void Hotkeys_AreRead_AndWrittenBack(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(HotkeyText.TryParse(text, out var readModifiers, out var readKey));
        Assert.Equal((modifiers, key), (readModifiers, readKey));

        string formatted = HotkeyText.Format(readModifiers, readKey);
        Assert.True(HotkeyText.TryParse(formatted, out var again, out var againKey));
        Assert.Equal((modifiers, key), (again, againKey));
    }

    [Fact]
    public void Format_WritesTheBacktickAndDigits_AsTyped()
    {
        Assert.Equal("Ctrl+Alt+`", HotkeyText.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.Oem3));
        Assert.Equal("Ctrl+Alt+Shift+Win+7", HotkeyText.Format(ModifierKeys.Windows | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control, Key.D7));
    }

    [Theory]
    [InlineData("W")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+W+Q")]
    [InlineData("")]
    [InlineData(null)]
    public void Unreadable_OrBareKeys_Fail(string? text)
    {
        Assert.False(HotkeyText.TryParse(text, out _, out _));
    }
}
