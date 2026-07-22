namespace MauiForge.Models;

public record GitStatus(int Ahead, int Behind, bool Dirty, DateTimeOffset? LastCommit = null, string? GitHubUrl = null);
