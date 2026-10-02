using System.IO;
using InfiniteSpace.Rendering;
using InfiniteSpace.Shell;

namespace InfiniteSpace.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AllThemesAreOffered_DreamyByDefault()
    {
        Assert.Equal(["Dreamy", "Claudish", "Glory", "Burn", "Sunday", "Parallax", "Ocean", "Void"], Themes.All);
        Assert.Equal("Dreamy", new AppSettings().Theme);
    }

    [Theory]
    [InlineData("Glory", typeof(GloryLook))]
    [InlineData("Burn", typeof(BurnLook))]
    [InlineData("Sunday", typeof(SundayLook))]
    [InlineData("Parallax", typeof(ParallaxLook))]
    [InlineData("Ocean", typeof(OceanLook))]
    [InlineData("Void", typeof(VoidLook))]
    public void PaintedThemes_HaveTheirOwnLook_AndAreRemembered(string theme, Type look)
    {
        using var created = Themes.CreateLook(theme);
        Assert.IsType(look, created);
        Assert.Equal(theme, Themes.Resolve(theme));

        new AppSettings { Theme = theme }.Save(_directory);
        Assert.Equal(theme, AppSettings.Load(_directory).Theme);
    }

    [Fact]
    public void Ink_ContrastsWithEachTheme()
    {
        // Light themes get dark ink; dark themes get light ink.
        Assert.True(Luminance(Themes.InkOf(Themes.Sunday)) < 0.3);
        Assert.True(Luminance(Themes.InkOf(Themes.Parallax)) < 0.3);
        foreach (var dark in new[] { Themes.Glory, Themes.Burn, Themes.Ocean, Themes.Void })
            Assert.True(Luminance(Themes.InkOf(dark)) > 0.8, dark);
    }

    private static double Luminance(System.Windows.Media.Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    [Fact]
    public void ChosenTheme_IsSaved()
    {
        var settings = new AppSettings { Theme = Themes.Claudish, StartWithWindows = false };
        settings.Save(_directory);

        var loaded = AppSettings.Load(_directory);

        Assert.Equal(Themes.Claudish, loaded.Theme);
        Assert.False(loaded.StartWithWindows);
    }

    [Fact]
    public void EachTheme_HasItsOwnLookAndInk()
    {
        using var dreamy = Themes.CreateLook(Themes.Dreamy);
        using var claudish = Themes.CreateLook(Themes.Claudish);

        Assert.IsType<DreamyLook>(dreamy);
        Assert.IsType<InkyLook>(claudish);
        Assert.Equal(System.Windows.Media.Colors.White, Themes.InkOf(Themes.Dreamy));
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x15, 0x15, 0x15), Themes.InkOf(Themes.Claudish));
    }

    [Fact]
    public void SettingsSavedBeforeThemesExisted_LoadAsDreamy()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"StartWithWindows":true}""");

        Assert.Equal(Themes.Dreamy, AppSettings.Load(_directory).Theme);
    }

    [Fact]
    public void SettingsSavedBeforeTools_ShowTheToolbar_WithDefaultHotkeyAndPlace()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"Theme":"Ocean"}""");

        var loaded = AppSettings.Load(_directory);

        Assert.True(loaded.ShowToolbar);
        Assert.Null(loaded.ToolWheelHotkey);
        Assert.Null(loaded.ToolbarLeft);
        Assert.Null(loaded.ToolbarTop);
    }

    [Fact]
    public void ToolSettings_AreSaved()
    {
        new AppSettings { ShowToolbar = false, ToolWheelHotkey = "Ctrl+Shift+Alt+T", ToolbarLeft = 120.5, ToolbarTop = 300 }.Save(_directory);

        var loaded = AppSettings.Load(_directory);

        Assert.False(loaded.ShowToolbar);
        Assert.Equal("Ctrl+Shift+Alt+T", loaded.ToolWheelHotkey);
        Assert.Equal(120.5, loaded.ToolbarLeft);
        Assert.Equal(300, loaded.ToolbarTop);
    }

    [Fact]
    public void TheInkPen_IsSaved()
    {
        new AppSettings { Pen = new Spaces.InkPen("#9B6DFF", 8, 32) }.Save(_directory);

        Assert.Equal(new Spaces.InkPen("#9B6DFF", 8, 32), AppSettings.Load(_directory).Pen);
    }

    [Fact]
    public void SettingsSavedBeforeInkChoicesExisted_UseTheThemesInkAndTheOldSizes()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"Theme":"Ocean"}""");

        Assert.Equal(Spaces.InkPen.Default, AppSettings.Load(_directory).Pen);
    }

    [Fact]
    public void UnreadableInkChoices_FallBackToSensibleOnes()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"InkColor":"glitter","PaintWidth":-4,"TextSize":5000}""");

        Assert.Equal(new Spaces.InkPen(null, Spaces.InkPen.MinThickness, Spaces.InkPen.MaxFontSize), AppSettings.Load(_directory).Pen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Neon")]
    public void UnknownTheme_FallsBackToDreamy(string? saved)
    {
        Assert.Equal(Themes.Dreamy, Themes.Resolve(saved));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
