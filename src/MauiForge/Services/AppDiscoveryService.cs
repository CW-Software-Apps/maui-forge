using MauiForge.Models;

namespace MauiForge.Services;

public class AppDiscoveryService(VersionService versions, GitService git)
{
    public List<AppEntry> FindApps(string rootDir, int depth = 2)
    {
        return FindApps([rootDir], depth);
    }

    public List<AppEntry> FindApps(IEnumerable<string> rootDirs, int depth = 2)
    {
        var entries = new List<AppEntry>();

        foreach (var rootDir in rootDirs)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) continue;
            try
            {
                if (!Directory.Exists(rootDir)) continue;
            }
            catch
            {
                continue;
            }

            foreach (var projectFile in FindCsprojs(rootDir, depth))
            {
                try
                {
                    var dir = Path.GetDirectoryName(projectFile)!;

                    if (projectFile.EndsWith("ProjectSettings.asset", StringComparison.OrdinalIgnoreCase))
                    {
                        // Unity Project
                        dir = Path.GetDirectoryName(dir)!; // Parent of ProjectSettings is the root Unity folder
                        if (entries.Any(e => string.Equals(e.Dir, dir, StringComparison.OrdinalIgnoreCase))) continue;

                        var name = Path.GetFileName(dir);
                        var unityV = versions.ReadUnity(dir);
                        if (unityV is null) continue;

                        var branchU = git.GetBranch(dir);
                        var statusU = git.FetchAndGetStatus(dir);
                        var iconU = GetAppIconBase64(dir);
                        var lastActivityU = MaxDate(GetLastWriteTimeUtc(projectFile), GetLastBuildOutputTime(dir));

                        entries.Add(new AppEntry(
                            Name: name,
                            Dir: dir,
                            Branch: branchU,
                            Versions: new AppVersions(null, null, unityV),
                            Git: statusU,
                            ProjectType: "Unity",
                            IconBase64: iconU,
                            LastActivityAt: lastActivityU
                        ));
                        continue;
                    }

                    // Csproj Project (MAUI, WPF, Blazor, standard .NET)
                    if (entries.Any(e => string.Equals(e.Dir, dir, StringComparison.OrdinalIgnoreCase))) continue;

                    var nameC = Path.GetFileNameWithoutExtension(projectFile);
                    var csprojContent = "";
                    try
                    {
                        if (File.Exists(projectFile)) csprojContent = File.ReadAllText(projectFile);
                    }
                    catch { }

                    string projType = "ClassLibrary";
                    if (csprojContent.Contains("<UseMaui>true</UseMaui>"))
                    {
                        projType = "MAUI";
                    }
                    else if (csprojContent.Contains("<UseWPF>true</UseWPF>") || csprojContent.Contains("Microsoft.NET.Sdk.WindowsDesktop") || csprojContent.Contains("wpf") || csprojContent.Contains("WPF"))
                    {
                        projType = "WPF";
                    }
                    else if (csprojContent.Contains("Microsoft.NET.Sdk.Razor") || csprojContent.Contains("Microsoft.AspNetCore.Components"))
                    {
                        projType = "Blazor";
                    }

                    var ios = versions.ReadiOS(dir);
                    var android = versions.ReadAndroid(dir);
                    var csprojV = versions.ReadCsproj(projectFile);

                    // Fallback for WPF to check AssemblyInfo
                    if (csprojV is null && projType == "WPF")
                    {
                        csprojV = versions.ReadAssemblyInfo(dir);
                    }

                    if (ios is null && android is null && csprojV is null) continue;

                    var branch = git.GetBranch(dir);
                    var status = git.FetchAndGetStatus(dir);
                    var icon = GetAppIconBase64(dir);
                    var lastActivity = MaxDate(
                        versions.GetVersionFilesLastWriteTime(dir, projectFile),
                        GetLastBuildOutputTime(dir));

                    entries.Add(new AppEntry(
                        Name: nameC,
                        Dir: dir,
                        Branch: branch,
                        Versions: new AppVersions(ios, android, csprojV),
                        Git: status,
                        ProjectType: projType,
                        IconBase64: icon,
                        LastActivityAt: lastActivity
                    ));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error scanning project file '{projectFile}': {ex.Message}");
                }
            }
        }

        return entries;
    }

    public AppEntry? RefreshApp(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;

            var unitySettings = Path.Combine(dir, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(unitySettings))
            {
                var name = Path.GetFileName(dir);
                var unityV = versions.ReadUnity(dir);
                if (unityV is null) return null;

                var branchU = git.GetBranch(dir);
                var statusU = git.FetchAndGetStatus(dir);
                var iconU = GetAppIconBase64(dir);
                var lastActivityU = MaxDate(GetLastWriteTimeUtc(unitySettings), GetLastBuildOutputTime(dir));

                return new AppEntry(
                    Name: name,
                    Dir: dir,
                    Branch: branchU,
                    Versions: new AppVersions(null, null, unityV),
                    Git: statusU,
                    ProjectType: "Unity",
                    IconBase64: iconU,
                    LastActivityAt: lastActivityU
                );
            }

            var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
            if (csproj is null) return null;

            var nameC = Path.GetFileNameWithoutExtension(csproj);
            var csprojContent = File.Exists(csproj) ? File.ReadAllText(csproj) : "";

            string projType = "ClassLibrary";
            if (csprojContent.Contains("<UseMaui>true</UseMaui>"))
            {
                projType = "MAUI";
            }
            else if (csprojContent.Contains("<UseWPF>true</UseWPF>") || csprojContent.Contains("Microsoft.NET.Sdk.WindowsDesktop") || csprojContent.Contains("wpf") || csprojContent.Contains("WPF"))
            {
                projType = "WPF";
            }
            else if (csprojContent.Contains("Microsoft.NET.Sdk.Razor") || csprojContent.Contains("Microsoft.AspNetCore.Components"))
            {
                projType = "Blazor";
            }

            var ios = versions.ReadiOS(dir);
            var android = versions.ReadAndroid(dir);
            var csprojV = versions.ReadCsproj(csproj);

            if (csprojV is null && projType == "WPF")
            {
                csprojV = versions.ReadAssemblyInfo(dir);
            }

            if (ios is null && android is null && csprojV is null) return null;

            var branch = git.GetBranch(dir);
            var status = git.FetchAndGetStatus(dir);
            var icon = GetAppIconBase64(dir);
            var lastActivity = MaxDate(
                versions.GetVersionFilesLastWriteTime(dir, csproj),
                GetLastBuildOutputTime(dir));

            return new AppEntry(
                Name: nameC,
                Dir: dir,
                Branch: branch,
                Versions: new AppVersions(ios, android, csprojV),
                Git: status,
                ProjectType: projType,
                IconBase64: icon,
                LastActivityAt: lastActivity
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing app in dir '{dir}': {ex.Message}");
            return null;
        }
    }

    private string? GetAppIconBase64(string dir)
    {
        try
        {
            var appIconDirs = new[]
            {
                Path.Combine(dir, "Resources", "AppIcon"),
                Path.Combine(dir, "Resources", "Images"),
                Path.Combine(dir, "Resources", "Media"),
                Path.Combine(dir, "Assets")
            };

            foreach (var iconDir in appIconDirs)
            {
                if (!Directory.Exists(iconDir)) continue;

                // 1. Search for foreground files (svg or png)
                var fgFile = Directory.EnumerateFiles(iconDir, "*fg.*")
                    .Concat(Directory.EnumerateFiles(iconDir, "*foreground*.*"))
                    .Concat(Directory.EnumerateFiles(iconDir, "*toolbar*.*"))
                    .FirstOrDefault(f => f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

                if (fgFile != null)
                {
                    var mime = fgFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
                    return ToBase64(fgFile, mime);
                }

                // 2. Search for any standard appicon (svg or png)
                var stdFile = Directory.EnumerateFiles(iconDir, "appicon.*")
                    .FirstOrDefault(f => f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

                if (stdFile != null)
                {
                    var mime = stdFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
                    return ToBase64(stdFile, mime);
                }
            }

            // 3. wwwroot for Blazor
            var wwwroot = Path.Combine(dir, "wwwroot");
            if (Directory.Exists(wwwroot))
            {
                var icon192 = Path.Combine(wwwroot, "icon-192.png");
                if (File.Exists(icon192)) return ToBase64(icon192, "image/png");

                var favPng = Path.Combine(wwwroot, "favicon.png");
                if (File.Exists(favPng)) return ToBase64(favPng, "image/png");

                var favIco = Path.Combine(wwwroot, "favicon.ico");
                if (File.Exists(favIco)) return ToBase64(favIco, "image/x-icon");
            }

            // 4. Generic logo/icon in root directory
            var genericIcon = Directory.EnumerateFiles(dir, "*icon*.png")
                .Concat(Directory.EnumerateFiles(dir, "*logo*.png"))
                .FirstOrDefault();
            if (genericIcon != null) return ToBase64(genericIcon, "image/png");

            var genericIco = Directory.EnumerateFiles(dir, "*.ico").FirstOrDefault();
            if (genericIco != null) return ToBase64(genericIco, "image/x-icon");
        }
        catch { }
        return null;
    }

    // Most recent write time under bin/ (build output), used as the "last build generated"
    // half of the LastActivityAt signal. Bounded by try/catch since bin/ can contain
    // locked or permission-denied files without that being worth failing the whole scan.
    private static DateTimeOffset? GetLastBuildOutputTime(string dir)
    {
        try
        {
            var bin = Path.Combine(dir, "bin");
            if (!Directory.Exists(bin)) return null;
            DateTime? latest = null;
            foreach (var file in Directory.EnumerateFiles(bin, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var written = File.GetLastWriteTimeUtc(file);
                    if (latest is null || written > latest) latest = written;
                }
                catch { }
            }
            return latest is null ? null : new DateTimeOffset(latest.Value, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? GetLastWriteTimeUtc(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero); }
        catch { return null; }
    }

    private static DateTimeOffset? MaxDate(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }

    private static string ToBase64(string path, string mimeType)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return ""; }
    }

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "node_modules", ".idea", "packages" };

    private static IEnumerable<string> FindCsprojs(string root, int depth)
    {
        if (depth < 0) yield break;

        try
        {
            if (!Directory.Exists(root)) yield break;
        }
        catch
        {
            yield break;
        }

        string[] files = Array.Empty<string>();
        try
        {
            files = Directory.GetFiles(root, "*.csproj");
        }
        catch { }

        foreach (var f in files)
            yield return f;

        bool hasUnity = false;
        var unitySettings = Path.Combine(root, "ProjectSettings", "ProjectSettings.asset");
        try
        {
            hasUnity = File.Exists(unitySettings);
        }
        catch { }

        if (hasUnity)
        {
            yield return unitySettings;
        }

        if (depth == 0) yield break;

        string[] subDirs = Array.Empty<string>();
        try
        {
            subDirs = Directory.GetDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var sub in subDirs)
        {
            string folderName = "";
            try
            {
                folderName = Path.GetFileName(sub);
            }
            catch { continue; }

            if (SkipDirs.Contains(folderName)) continue;

            IEnumerable<string> childCsprojs = Enumerable.Empty<string>();
            try
            {
                childCsprojs = FindCsprojs(sub, depth - 1);
            }
            catch { continue; }

            foreach (var f in childCsprojs)
                yield return f;
        }
    }
}

