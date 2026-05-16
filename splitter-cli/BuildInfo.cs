using System.Reflection;

namespace splitter;

public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

    public static string Version         => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static string FileVersion     => Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
    public static string AssemblyVersion => Assembly.GetName().Version?.ToString() ?? "unknown";
}
