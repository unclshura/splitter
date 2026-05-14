namespace splitter;

public static class FileMaskExpander
{
    public static string[] Expand(string input)
    {
        // If no mask, return the single full path
        if (!HasMask(input))
            return [Path.GetFullPath(input)];

        string directory = Path.GetDirectoryName(input) ?? Directory.GetCurrentDirectory();
        string pattern   = Path.GetFileName(input);

        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
    }

    private static bool HasMask(string path)
        => path.IndexOfAny(['*', '?']) >= 0;
}