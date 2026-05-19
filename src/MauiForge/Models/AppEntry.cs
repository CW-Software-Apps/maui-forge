namespace MauiForge.Models;

public record AppEntry(
    string Name,
    string Dir,
    string Branch,
    AppVersions Versions,
    GitStatus Git
);
