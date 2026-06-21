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
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        var logPaveVM = new LogPaneViewModel();
        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<FileListViewModel>();
        services.AddTransient<PreviewPaneViewModel>();
        services.AddTransient<InspectorPaneViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<ProgressViewModel>();
        services.AddSingleton<LogPaneViewModel>(logPaveVM);
        services.AddSingleton<ILogService>(logPaveVM);

        // splitter services
        services.AddSingleton<UltraFaceDetector>();
        services.AddSingleton<YoloV10ObjectDetector>();
        services.AddSingleton<OSNetEmbeddingExtractor>();
        services.AddSingleton<IObjectTracker, ObjectTracker>();
        services.AddSingleton<IBufferPool, BufferPool>();
        services.AddSingleton<IMatToBitmapConverter, MatToBitmapConverter>();
        services.AddKeyedSingleton<IObjectDetector>("face", (x,_) => new SingleThreadedDetector<UltraFaceDetector>(x.GetRequiredService<UltraFaceDetector>()));
        services.AddKeyedSingleton<IObjectDetector>("body", (x,_) => new SingleThreadedDetector<YoloV10ObjectDetector>(x.GetRequiredService<YoloV10ObjectDetector>()));
        services.AddKeyedSingleton<IObjectDetector>("none", (x,_) => new SingleThreadedDetector<DummyDetector>(x.GetRequiredService<DummyDetector>()));
        services.AddSingleton<IEmbeddingExtractor>(x => new SingleThreadedEmbeddingExtractor<OSNetEmbeddingExtractor>(x.GetRequiredService<OSNetEmbeddingExtractor>()));
        services.AddSingleton<Func<string, IObjectDetector>>(x => detectorName => x.GetKeyedService<IObjectDetector>(detectorName) ?? new DummyDetector());
        services.AddSingleton<Func<string, IObjectTracker>>(x => detectorName =>
        {
            var detectorFactory = x.GetRequiredService<Func<string, IObjectDetector>>();
            var extractor       = x.GetRequiredService<IEmbeddingExtractor>();
            return new ObjectTracker(detectorFactory(detectorName), extractor);
        });
        services.AddSingleton<ILogger, GlobalLogger>();
        services.AddSingleton<IJobProcessor, JobProcessor>();

        // Domain services (your pipeline)
        services.AddTransient<IFileProbeService,    FileProbeService>();
        services.AddTransient<IThumbnailService,    ThumbnailService>();
        services.AddSingleton<IAutoDecisionService, AutoDecisionService>();

        services.AddSingleton<IFileJobFactory, FileJobFactory>();

        return services;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var services = ConfigureServices();
        var provider = services.BuildServiceProvider();

        return AppBuilder.Configure<App>(() => new App(provider))
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
}
