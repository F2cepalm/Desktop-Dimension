using System.IO;
using System.Runtime;
using System.Windows;
using InfiniteSpace.Rendering;
using InfiniteSpace.Shell;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfiniteSpace;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(initiallyOwned: true, @"Local\InfiniteSpace.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
            return;

        // No blocking full collections while there is memory to spare: they would stall a frame.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        var builder = Host.CreateApplicationBuilder(args);
        string dataDirectory = ResolveDataDirectory(builder.Configuration);
        builder.Services.AddSingleton(sp => new SpaceStore(dataDirectory, sp.GetRequiredService<ILogger<SpaceStore>>()));
        builder.Services.AddSingleton<GpuCompositor>();
        builder.Services.AddSingleton<Universe>();
        builder.Services.AddSingleton<SpaceManager>();
        builder.Services.AddSingleton<ShellController>();

        using var host = builder.Build();
        host.Start();

        var logger = host.Services.GetRequiredService<ILogger<Application>>();
        var shell = host.Services.GetRequiredService<ShellController>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => shell.Start();
        app.Exit += (_, _) => shell.Dispose();
        var gpu = host.Services.GetRequiredService<GpuCompositor>();
        app.DispatcherUnhandledException += (_, e) =>
        {
            // A background app should outlive a bad frame; log and keep the spaces alive. If the GPU
            // went away mid-drawing, the next frame makes everything again on a new one.
            if (e.Exception is SharpGen.Runtime.SharpGenException gpuError && GpuCompositor.IsDeviceLoss(gpuError.HResult))
            {
                logger.LogWarning(e.Exception, "The GPU went away; making everything again");
                gpu.MarkLost();
            }
            else
            {
                logger.LogError(e.Exception, "Unhandled UI exception");
            }
            e.Handled = true;
        };
        app.Run();

        host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// %AppData%\InfiniteSpace by default; override with the InfiniteSpace:DataDirectory setting
    /// (e.g. --InfiniteSpace:DataDirectory=D:\spaces or env InfiniteSpace__DataDirectory).
    /// </summary>
    private static string ResolveDataDirectory(IConfiguration configuration) =>
        configuration["InfiniteSpace:DataDirectory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InfiniteSpace");
}
