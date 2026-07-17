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
            if (!Directory.Exists(rootDir)) continue;

            foreach (var projectFile in FindCsprojs(rootDir, depth))
            {
                var dir = Path.GetDirectoryName(projectFile)!;
                
                if (projectFile.EndsWith("ProjectSettings.asset", StringComparison.OrdinalIgnoreCase))
                {
                    // Unity Project
                    dir = Path.GetDirectoryName(dir)!; // Parent of ProjectSettings is the root Unity folder
                    if (entries.Any(e => e.Dir == dir)) continue;
                    
                    var name = Path.GetFileName(dir);
                    var unityV = versions.ReadUnity(dir);
                    if (unityV is null) continue;
                    
                    var branchU = git.GetBranch(dir);
                    var statusU = git.FetchAndGetStatus(dir);
                    var iconU   = GetAppIconBase64(dir);
                    
                    entries.Add(new AppEntry(
                        Name:        name,
                        Dir:         dir,
                        Branch:      branchU,
                        Versions:    new AppVersions(null, null, unityV),
                        Git:         statusU,
                        ProjectType: "Unity",
                        IconBase64:  iconU
                    ));
                    continue;
                }

                // Csproj Project (MAUI, WPF, Blazor, standard .NET)
                if (entries.Any(e => e.Dir == dir)) continue;

                var nameC = Path.GetFileNameWithoutExtension(projectFile);
                var csprojContent = File.Exists(projectFile) ? File.ReadAllText(projectFile) : "";
                
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

                var ios     = versions.ReadiOS(dir);
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
                var icon   = GetAppIconBase64(dir);

                entries.Add(new AppEntry(
                    Name:        nameC,
                    Dir:         dir,
                    Branch:      branch,
                    Versions:    new AppVersions(ios, android, csprojV),
                    Git:         status,
                    ProjectType: projType,
                    IconBase64:  icon
                ));
            }
        }

        return entries;
    }

    public AppEntry? RefreshApp(string dir)
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
            var iconU   = GetAppIconBase64(dir);

            return new AppEntry(
                Name:        name,
                Dir:         dir,
                Branch:      branchU,
                Versions:    new AppVersions(null, null, unityV),
                Git:         statusU,
                ProjectType: "Unity",
                IconBase64:  iconU
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

        var ios     = versions.ReadiOS(dir);
        var android = versions.ReadAndroid(dir);
        var csprojV = versions.ReadCsproj(csproj);

        if (csprojV is null && projType == "WPF")
        {
            csprojV = versions.ReadAssemblyInfo(dir);
        }

        if (ios is null && android is null && csprojV is null) return null;

        var branch = git.GetBranch(dir);
        var status = git.FetchAndGetStatus(dir);
        var icon   = GetAppIconBase64(dir);

        return new AppEntry(
            Name:        nameC,
            Dir:         dir,
            Branch:      branch,
            Versions:    new AppVersions(ios, android, csprojV),
            Git:         status,
            ProjectType: projType,
            IconBase64:  icon
        );
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
        if (depth < 0 || !Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*.csproj"))
            yield return f;

        var unitySettings = Path.Combine(root, "ProjectSettings", "ProjectSettings.asset");
        if (File.Exists(unitySettings))
        {
            yield return unitySettings;
        }

        if (depth == 0) yield break;
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            if (SkipDirs.Contains(Path.GetFileName(sub))) continue;
            foreach (var f in FindCsprojs(sub, depth - 1))
                yield return f;
        }
    }
}
