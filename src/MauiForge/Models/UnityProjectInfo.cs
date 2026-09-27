namespace MauiForge.Models;

public record UnityPublisherProfileInfo(
    string Id,
    string DisplayName,
    string Platform,
    string? Publisher = null,
    bool IsDemo = false,
    bool IsFullGame = true,
    string? OutputSubfolder = null
);

public record UnityProjectInfo(
    string? EditorVersion = null,
    string? EditorPath = null,
    bool HasBuildPipeline = false,
    string? BuildPipelineVersion = null,
    List<UnityPublisherProfileInfo>? Profiles = null,
    bool IsPackage = false,
    string? PackageName = null,
    string? TargetPlatform = null,
    bool GameConfigFound = false
)
{
    public bool IsUpmPackage => IsPackage;
};
