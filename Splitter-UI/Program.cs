using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Splitter_UI;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var services = ConfigureServices();
        var provider = services.BuildServiceProvider();

        BuildAvaloniaApp(provider)
            .StartWithClassicDesktopLifetime(args);
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<FileListViewModel>();
        services.AddTransient<PreviewPaneViewModel>();
        services.AddTransient<InspectorPaneViewModel>();
        services.AddTransient<StatusBarViewModel>();
        services.AddTransient<LogPaneViewModel>();

        // splitter services
        services.AddSingleton<UltraFaceDetector>();
        services.AddSingleton<YoloOnnxObjectDetector>();
        services.AddSingleton( x => new SingleThreadedDetector<UltraFaceDetector>(x.GetRequiredService<UltraFaceDetector>()) );
        services.AddSingleton( x => new SingleThreadedDetector<YoloOnnxObjectDetector>(x.GetRequiredService<YoloOnnxObjectDetector>()));
        services.AddSingleton<Func<string, IObjectDetector>>( x => detectorName =>
        {
            return detectorName switch
            {
                "face" => x.GetRequiredService<SingleThreadedDetector<UltraFaceDetector>>(),
                "body" => x.GetRequiredService<SingleThreadedDetector<YoloOnnxObjectDetector>>(),
                _      => new DummyDetector()
            };
        });
        services.AddSingleton<splitter.ILogger, GlobalLogger>();

        // Domain services (your pipeline)
        services.AddTransient<IFileProbeService,    FileProbeService>();
        services.AddTransient<IThumbnailService,    ThumbnailService>();
        services.AddSingleton<IAutoDecisionService, AutoDecisionService>();
        services.AddSingleton<IProcessingService,   ProcessingService>();
        services.AddSingleton<ILogService,          LogService>();

        services.AddSingleton<IFileJobFactory, FileJobFactory>();

        return services;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp(ServiceProvider provider)
        => AppBuilder.Configure<App>(() => new App(provider))
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("Font Awesome 7 Free") },
                    new FontFallback { FontFamily = new FontFamily("Font Awesome 7 Free Solid") }
                }
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
