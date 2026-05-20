using Avalonia;
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

        // Domain services (your pipeline)
        services.AddTransient<IFileProbeService, FileProbeService>();
        services.AddTransient<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IAutoDecisionService, AutoDecisionService>();
        services.AddSingleton<IProcessingService, ProcessingService>();
        services.AddSingleton<ILogService, LogService>();

        services.AddSingleton<IFileJobFactory, FileJobFactory>();

        return services;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp(ServiceProvider provider)
        => AppBuilder.Configure<App>(() => new App(provider))
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
