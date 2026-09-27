using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MauiForge.Services;

public record UnityEditorInstallation(string Version, string ExecutablePath, bool IsDefault = false);

public class UnityLocatorService
{
    private List<UnityEditorInstallation>? _cachedEditors;
    private readonly object _lock = new();

    /// <summary>
    /// Scans default installation directories and returns all available Unity Editor executables.
    /// </summary>
    public List<UnityEditorInstallation> GetInstalledEditors(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (_cachedEditors != null && !forceRefresh) return _cachedEditors;

            var list = new List<UnityEditorInstallation>();
            var searchRoots = new List<string>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                searchRoots.Add(@"C:\Program Files\Unity\Hub\Editor");
                searchRoots.Add(@"C:\Program Files (x86)\Unity\Hub\Editor");
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localApp))
                    searchRoots.Add(Path.Combine(localApp, "Programs", "Unity", "Hub", "Editor"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                searchRoots.Add("/Applications/Unity/Hub/Editor");
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    searchRoots.Add(Path.Combine(home, "Applications", "Unity", "Hub", "Editor"));
            }
            else
            {
                searchRoots.Add("/opt/unity/hub/editor");
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    searchRoots.Add(Path.Combine(home, "Unity", "Hub", "Editor"));
            }

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var version = Path.GetFileName(dir);
                        var exe = GetExecutablePathForEditorDir(dir);
                        if (exe != null && File.Exists(exe))
                        {
                            if (!list.Any(e => string.Equals(e.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(new UnityEditorInstallation(version, exe));
                            }
                        }
                    }
                }
                catch { }
            }

            // Order descending by semver-like version
            _cachedEditors = list.OrderByDescending(e => e.Version, new UnityVersionComparer()).ToList();
            return _cachedEditors;
        }
    }

    /// <summary>
    /// Reads ProjectSettings/ProjectVersion.txt to determine which Unity version this project expects.
    /// </summary>
    public string? GetProjectEditorVersion(string projectDir)
    {
        var versionFile = Path.Combine(projectDir, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile)) return null;
        try
        {
            var text = File.ReadAllText(versionFile);
            var m = Regex.Match(text, @"m_EditorVersion:\s*([^\r\n]+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the best installed Unity Editor for the project.
    /// If an exact match is installed, uses it.
    /// Otherwise, selects the closest matching version in the same major/minor branch,
    /// or falls back to the highest available editor.
    /// </summary>
    public UnityEditorInstallation? ResolveEditorForProject(string projectDir)
    {
        var targetVersion = GetProjectEditorVersion(projectDir);
        var editors = GetInstalledEditors();
        if (editors.Count == 0) return null;

        if (string.IsNullOrEmpty(targetVersion))
        {
            return editors.FirstOrDefault();
        }

        // 1. Exact match
        var exact = editors.FirstOrDefault(e => string.Equals(e.Version, targetVersion, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Same stream (e.g. 6000.2)
        var parts = targetVersion.Split('.');
        if (parts.Length >= 2)
        {
            var streamPrefix = $"{parts[0]}.{parts[1]}.";
            var sameStream = editors.FirstOrDefault(e => e.Version.StartsWith(streamPrefix, StringComparison.OrdinalIgnoreCase));
            if (sameStream != null) return sameStream;

            // 3. Same major (e.g. 6000)
            var majorPrefix = $"{parts[0]}.";
            var sameMajor = editors.FirstOrDefault(e => e.Version.StartsWith(majorPrefix, StringComparison.OrdinalIgnoreCase));
            if (sameMajor != null) return sameMajor;
        }

        // 4. Highest installed editor
        return editors.FirstOrDefault();
    }

    /// <summary>
    /// Launches the project in the resolved Unity Editor.
    /// </summary>
    public bool OpenProjectInEditor(string projectDir, string? customEditorPath = null)
    {
        var editor = !string.IsNullOrEmpty(customEditorPath) && File.Exists(customEditorPath)
            ? new UnityEditorInstallation("Custom", customEditorPath)
            : ResolveEditorForProject(projectDir);

        if (editor == null || !File.Exists(editor.ExecutablePath)) return false;

        try
        {
            var psi = new ProcessStartInfo(editor.ExecutablePath)
            {
                Arguments = $"-projectPath \"{projectDir}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExecutablePathForEditorDir(string editorDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exe = Path.Combine(editorDir, "Editor", "Unity.exe");
            return File.Exists(exe) ? exe : null;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var app = Path.Combine(editorDir, "Unity.app", "Contents", "MacOS", "Unity");
            return File.Exists(app) ? app : null;
        }
        else
        {
            var bin = Path.Combine(editorDir, "Editor", "Unity");
            return File.Exists(bin) ? bin : null;
        }
    }

    private class UnityVersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var px = x.Split(new[] { '.', 'f', 'b', 'a', 'p', 'c' }, StringSplitOptions.RemoveEmptyEntries);
            var py = y.Split(new[] { '.', 'f', 'b', 'a', 'p', 'c' }, StringSplitOptions.RemoveEmptyEntries);

            var len = Math.Min(px.Length, py.Length);
            for (int i = 0; i < len; i++)
            {
                if (int.TryParse(px[i], out var nx) && int.TryParse(py[i], out var ny))
                {
                    if (nx != ny) return nx.CompareTo(ny);
                }
                else
                {
                    var cmp = string.Compare(px[i], py[i], StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }
            return px.Length.CompareTo(py.Length);
        }
    }
}
