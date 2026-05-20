using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MauiForge.Models;

namespace MauiForge.Services;

public partial class VersionService
{
    // ── iOS ─────────────────────────────────────────────────────────────────

    public PlatformVersion? ReadiOS(string dir)
    {
        var plist = FindPlist(dir);
        if (plist is null) return null;
        var content = File.ReadAllText(plist, Encoding.UTF8);
        var version = PlistValue(content, "CFBundleShortVersionString");
        var build   = PlistValue(content, "CFBundleVersion");
        return version is null || build is null ? null : new(version, build);
    }

    public void WriteiOS(string dir, string version, string build)
    {
        var plist = FindPlist(dir) ?? throw new FileNotFoundException("Info.plist not found");
        var content = File.ReadAllText(plist, Encoding.UTF8);
        content = SetPlistValue(content, "CFBundleShortVersionString", version);
        content = SetPlistValue(content, "CFBundleVersion", build);
        // NoNewline + UTF8 sem BOM — igual ao PS1: Set-Content -Encoding UTF8 -NoNewline
        File.WriteAllText(plist, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // ── Android ─────────────────────────────────────────────────────────────

    public PlatformVersion? ReadAndroid(string dir)
    {
        var manifest = FindFile(dir, "AndroidManifest.xml");
        if (manifest is null) return null;
        var doc  = XDocument.Load(manifest);
        var root = doc.Root;
        if (root is null) return null;
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var version = root.Attribute(android + "versionName")?.Value;
        var build   = root.Attribute(android + "versionCode")?.Value;
        return version is null || build is null ? null : new(version, build);
    }

    public void WriteAndroid(string dir, string version, string build)
    {
        var manifest = FindFile(dir, "AndroidManifest.xml") ?? throw new FileNotFoundException("AndroidManifest.xml not found");
        var doc  = XDocument.Load(manifest);
        var root = doc.Root ?? throw new InvalidOperationException("Invalid AndroidManifest.xml");
        XNamespace android = "http://schemas.android.com/apk/res/android";
        root.SetAttributeValue(android + "versionName", version);
        root.SetAttributeValue(android + "versionCode", build);
        doc.Save(manifest);
    }

    // ── .csproj ─────────────────────────────────────────────────────────────

    public PlatformVersion? ReadCsproj(string csprojPath)
    {
        var doc     = XDocument.Load(csprojPath);
        var version = doc.Descendants("Version").FirstOrDefault()?.Value
                   ?? doc.Descendants("ApplicationVersion").FirstOrDefault()?.Value;
        var build   = doc.Descendants("ApplicationDisplayVersion").FirstOrDefault()?.Value
                   ?? doc.Descendants("AssemblyVersion").FirstOrDefault()?.Value;
        return version is null ? null : new(version, build ?? "1");
    }

    public void WriteCsproj(string csprojPath, string version, string build)
    {
        var doc = XDocument.Load(csprojPath);
        SetOrCreate(doc, "Version", version);
        SetOrCreate(doc, "ApplicationDisplayVersion", build);
        doc.Save(csprojPath);
    }

    public string? GetIosTargetFramework(string csprojPath) =>
        GetTargetFrameworks(csprojPath)
            .FirstOrDefault(t => t.Contains("-ios", StringComparison.OrdinalIgnoreCase));

    public List<string> GetTargetFrameworks(string csprojPath)
    {
        try
        {
            var doc  = XDocument.Load(csprojPath);
            var tfms = doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value
                    ?? doc.Descendants("TargetFramework").FirstOrDefault()?.Value;
            if (tfms is null) return [];
            return [.. tfms.Split(';').Select(t => t.Trim()).Where(t => t.Length > 0)];
        }
        catch { return []; }
    }

    public List<string> GetTargetFrameworks(string csprojPath, string platformFilter) =>
        GetTargetFrameworks(csprojPath)
            .Where(t => t.Contains(platformFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public List<string> GetBuildConfigurations(string csprojPath)
    {
        try
        {
            var doc     = XDocument.Load(csprojPath);
            var configs = doc.Descendants("PropertyGroup")
                .Select(pg => pg.Attribute("Condition")?.Value ?? "")
                .Where(c => c.Contains("Configuration", StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    // Suporta tanto '$(Configuration)' == 'Release-X'
                    // quanto '$(Configuration)|$(Platform)' == 'Release-X|AnyCPU'
                    var m = Regex.Match(c, @"'([^']+)'\s*==\s*'([^']*)'");
                    if (!m.Success || !m.Groups[1].Value.Contains("Configuration")) return null;
                    var raw = m.Groups[2].Value;
                    // Remove sufixo |Platform se existir
                    var pipeIdx = raw.IndexOf('|');
                    return pipeIdx >= 0 ? raw[..pipeIdx] : raw;
                })
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            return configs.Count > 0 ? configs : ["Release", "Debug"];
        }
        catch { return ["Release", "Debug"]; }
    }

    // ── Increment ────────────────────────────────────────────────────────────

    public static string IncrementVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length == 0) return version;
        if (int.TryParse(parts[^1], out var last))
            parts[^1] = (last + 1).ToString();
        return string.Join('.', parts);
    }

    // ── Android ApplicationId ────────────────────────────────────────────────

    public string? ReadAndroidApplicationId(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            // ApplicationId in csproj
            var id = doc.Descendants("ApplicationId").FirstOrDefault()?.Value;
            if (id is { Length: > 0 }) return id;

            // Fallback: read from AndroidManifest.xml package attribute
            var manifest = FindFile(Path.GetDirectoryName(csproj)!, "AndroidManifest.xml");
            if (manifest is null) return null;
            var mdoc = XDocument.Load(manifest);
            return mdoc.Root?.Attribute("package")?.Value;
        }
        catch { return null; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Busca Info.plist no path fixo do MAUI primeiro; fallback recursivo excluindo bin/obj
    private static string? FindPlist(string dir)
    {
        var canonical = Path.Combine(dir, "Platforms", "iOS", "Info.plist");
        if (File.Exists(canonical)) return canonical;
        return Directory.EnumerateFiles(dir, "Info.plist", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    private static string? FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string? PlistValue(string content, string key)
    {
        var pattern = $"<key>{Regex.Escape(key)}</key>\\s*<string>(.*?)</string>";
        var m = Regex.Match(content, pattern, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string SetPlistValue(string content, string key, string value)
    {
        var pattern = $"(<key>{Regex.Escape(key)}</key>\\s*<string>)(.*?)(</string>)";
        return Regex.Replace(content, pattern, $"$1{value}$3", RegexOptions.Singleline);
    }

    private static void SetOrCreate(XDocument doc, string element, string value)
    {
        var el = doc.Descendants(element).FirstOrDefault();
        if (el is not null) { el.Value = value; return; }
        var pg = doc.Descendants("PropertyGroup").FirstOrDefault()
              ?? throw new InvalidOperationException("No PropertyGroup in csproj");
        pg.Add(new XElement(element, value));
    }
}
