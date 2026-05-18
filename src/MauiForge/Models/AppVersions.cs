namespace MauiForge.Models;

public record PlatformVersion(string Version, string Build);

public record AppVersions(
    PlatformVersion? iOS,
    PlatformVersion? Android,
    PlatformVersion? Csproj
)
{
    public PlatformVersion? Master => iOS ?? Android ?? Csproj;

    public bool InSync =>
        iOS == null || Android == null || iOS.Version == Android.Version;
}
