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

            foreach (var csproj in FindCsprojs(rootDir, depth))
            {
                var dir  = Path.GetDirectoryName(csproj)!;
                if (entries.Any(e => e.Dir == dir)) continue;

                var name = Path.GetFileNameWithoutExtension(csproj);

                var ios     = versions.ReadiOS(dir);
                var android = versions.ReadAndroid(dir);
                var csprojV = versions.ReadCsproj(csproj);

                if (ios is null && android is null && csprojV is null) continue;

                var branch = git.GetBranch(dir);
                var status = git.GetStatus(dir);

                entries.Add(new AppEntry(
                    Name:     name,
                    Dir:      dir,
                    Branch:   branch,
                    Versions: new AppVersions(ios, android, csprojV),
                    Git:      status
                ));
            }
        }

        return entries;
    }

    public AppEntry? RefreshApp(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
        if (csproj is null) return null;

        var name = Path.GetFileNameWithoutExtension(csproj);

        var ios     = versions.ReadiOS(dir);
        var android = versions.ReadAndroid(dir);
        var csprojV = versions.ReadCsproj(csproj);

        if (ios is null && android is null && csprojV is null) return null;

        var branch = git.GetBranch(dir);
        var status = git.GetStatus(dir);

        return new AppEntry(
            Name:     name,
            Dir:      dir,
            Branch:   branch,
            Versions: new AppVersions(ios, android, csprojV),
            Git:      status
        );
    }

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", "node_modules", ".idea", "packages" };

    private static IEnumerable<string> FindCsprojs(string root, int depth)
    {
        if (depth < 0 || !Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*.csproj"))
            yield return f;
        if (depth == 0) yield break;
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            if (SkipDirs.Contains(Path.GetFileName(sub))) continue;
            foreach (var f in FindCsprojs(sub, depth - 1))
                yield return f;
        }
    }
}
