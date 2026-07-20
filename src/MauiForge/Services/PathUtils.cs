namespace MauiForge.Services;

public static class PathUtils
{
    public static string NormalizeOrRepairPath(string dir, StateService state)
    {
        if (string.IsNullOrWhiteSpace(dir)) return dir;
        
        // Normalize slashes for comparison/usage
        var current = dir;
        if (Directory.Exists(current)) return current;

        // Try replacing forward slashes back to system directory separators if needed
        var sysPath = current.Replace('/', Path.DirectorySeparatorChar);
        if (Directory.Exists(sysPath)) return sysPath;

        // If path still doesn't exist on disk (e.g. slashes stripped by client JS attribute evaluation),
        // match against cached apps by comparing alphanumeric characters.
        var targetCompact = GetCompact(dir);
        try
        {
            var st = state.Load();
            if (st.CachedApps != null)
            {
                foreach (var app in st.CachedApps)
                {
                    if (GetCompact(app.Dir) == targetCompact && Directory.Exists(app.Dir))
                    {
                        return app.Dir;
                    }
                }
            }
        }
        catch { }

        return dir;
    }

    private static string GetCompact(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.Replace("\\", "").Replace("/", "").Replace(" ", "").ToLowerInvariant();
    }
}
