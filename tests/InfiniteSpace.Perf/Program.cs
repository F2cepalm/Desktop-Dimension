using System.IO;
using System.Runtime;
using System.Windows;
using InfiniteSpace.Native;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Perf;

/// <summary>
/// Times every interactive action frame by frame against the 360 Hz budget (see <see cref="Result"/>)
/// and prints a table. Build in Release:
/// <code>dotnet run -c Release --project tests/InfiniteSpace.Perf -- [--themes Dreamy,Glory] [--actions paint,reshape] [--hz 360] [--out perf.md] [--capture folder]</code>
/// <c>--hz</c> is how often frames start (360 by default). Off a display that fast the compositor makes
/// frames wait now and then, which shows up in the slowest ones: give the display's own rate to see
/// the work alone.
/// Everything runs on the real GPU in windows far off screen, so the desktop is left alone.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        int exit = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            try
            {
                exit = Run(options);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                exit = 2;
            }
            app.Shutdown();
        };
        app.Run();
        return exit;
    }

    private static int Run(Options options)
    {
        Console.WriteLine($"Desktop_Dimension perf: {Dpi.Scale * 100:0}% scaling, GC {(GCSettings.IsServerGC ? "server" : "workstation")}, latency {GCSettings.LatencyMode}");
        var results = new List<Result>();
        foreach (var action in Scenarios.All)
        {
            if (!options.Wants(action.Name))
                continue;
            foreach (string theme in action.Themes ?? Themes.All)
            {
                if (!options.WantsTheme(theme))
                    continue;
                foreach (var result in action.Run(theme, options))
                {
                    results.Add(result);
                    Console.WriteLine(result.Row);
                }
            }
        }

        var table = Result.Header + "\n" + string.Join("\n", results.Select(r => r.Row));
        Console.WriteLine();
        Console.WriteLine(table);
        if (options.Out is { } path)
            File.WriteAllText(path, table + "\n");
        return 0;
    }
}

internal sealed class Options
{
    public HashSet<string>? Actions { get; private set; }

    public HashSet<string>? Themes { get; private set; }

    public string? Out { get; private set; }

    /// <summary>Where to save pictures of the windows as the compositor shows them (none when null).</summary>
    public string? Capture { get; private set; }

    public bool Wants(string action) => Actions is null || Actions.Contains(action);

    public bool WantsTheme(string theme) => Themes is null || Themes.Contains(theme);

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            string value = args[i + 1];
            switch (args[i])
            {
                case "--actions":
                    options.Actions = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                    break;
                case "--themes":
                    options.Themes = new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                    break;
                case "--out":
                    options.Out = value;
                    break;
                case "--dump":
                    FrameTimer.DumpFolder = value;
                    break;
                case "--hz":
                    FrameTimer.PeriodTicks = (long)(System.Diagnostics.Stopwatch.Frequency / double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--capture":
                    options.Capture = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {args[i]}");
            }
        }
        return options;
    }

    /// <summary>Saves what a window shows, if pictures were asked for.</summary>
    public void Picture(IntPtr window, string name)
    {
        if (Capture is { } folder)
            Ui.Capture(window, Path.Combine(folder, name + ".png"));
    }
}
