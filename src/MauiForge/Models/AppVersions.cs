namespace MauiForge.Models;

public record PlatformVersion(string Version, string Build);

public record AppVersions(
    PlatformVersion? iOS,
    PlatformVersion? Android,
    PlatformVersion? Csproj
)
{
    /// <summary>
    /// The authoritative version for this app. Csproj is the source of truth
    /// because it's the project file that defines the build — fall back to
    /// iOS or Android only when no .csproj version is available.
    /// </summary>
    public PlatformVersion? Master => Csproj ?? iOS ?? Android;

    /// <summary>
    /// All platforms that exist must have matching version strings.
    /// Checks iOS, Android, and Csproj — if any two differ, the app is out of sync.
    /// </summary>
    public bool InSync
    {
        get
        {
            var versions = new List<string>();
            if (iOS is not null) versions.Add(iOS.Version);
            if (Android is not null) versions.Add(Android.Version);
            if (Csproj is not null) versions.Add(Csproj.Version);

            if (versions.Count <= 1) return true;
            return versions.Distinct().Count() == 1;
        }
    }
}
