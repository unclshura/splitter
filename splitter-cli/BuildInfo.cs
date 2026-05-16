namespace splitter;

public static class BuildInfo
{
    public static string Version     { get; } = ThisAssembly.Version;
    public static string BuildNumber { get; } = ThisAssembly.BuildNumber;
    public static string Commit      { get; } = ThisAssembly.Commit;
}
