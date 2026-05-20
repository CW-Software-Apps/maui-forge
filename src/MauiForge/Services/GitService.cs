using MauiForge.Models;

namespace MauiForge.Services;

public class GitService
{
    public GitStatus GetStatus(string dir)
    {
        // Single process: porcelain v2 with branch gives ahead/behind/dirty in one call
        var output = RunGit(dir, "status", "--porcelain=v2", "--branch");
        var ahead  = 0;
        var behind = 0;
        var dirty  = false;

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("# branch.ab "))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[2].TrimStart('+'), out ahead);
                    int.TryParse(parts[3].TrimStart('-'), out behind);
                }
            }
            else if (line.Length > 0 && line[0] is '1' or '2' or '?' or 'u')
            {
                dirty = true;
            }
        }

        return new GitStatus(Ahead: ahead, Behind: behind, Dirty: dirty);
    }

    public GitStatus FetchAndGetStatus(string dir)
    {
        RunGit(dir, "fetch", "--quiet");
        return GetStatus(dir);
    }

    public string GetBranch(string dir) =>
        RunGit(dir, "rev-parse", "--abbrev-ref", "HEAD").Trim();

    public (bool Success, string Output) Pull(string dir) =>
        RunGitWithResult(dir, "pull");

    public (bool Success, string Output) Commit(string dir, string message)
    {
        RunGit(dir, "add", "-A");
        return RunGitWithResult(dir, "commit", "-m", message);
    }

    public (bool Success, string Output) Push(string dir, string message)
    {
        RunGit(dir, "add", "-A");
        RunGit(dir, "commit", "-m", message);
        return RunGitWithResult(dir, "push");
    }

    public (bool Success, string Output) PushOnly(string dir) =>
        RunGitWithResult(dir, "push");

    public string GetDiffStat(string dir) =>
        RunGit(dir, "diff", "--staged", "--stat");

    public string GetUnstagedDiffStat(string dir) =>
        RunGit(dir, "diff", "--stat");

    public string GetChangedFilesSummary(string dir)
    {
        var staged   = RunGit(dir, "diff", "--cached", "--name-status");
        var unstaged = RunGit(dir, "diff", "--name-status");
        var untracked = RunGit(dir, "ls-files", "--others", "--exclude-standard");
        return string.Join('\n', new[] { staged, unstaged, untracked }.Where(s => s.Length > 0));
    }

    public string SuggestCommitMessage(string dir)
    {
        var summary = GetChangedFilesSummary(dir);
        if (string.IsNullOrWhiteSpace(summary)) return "chore: update files";

        var lines  = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var added  = lines.Count(l => l.StartsWith('A') || l.StartsWith('?'));
        var modified = lines.Count(l => l.StartsWith('M'));
        var deleted = lines.Count(l => l.StartsWith('D'));

        // Detect common patterns
        var files = lines.Select(l => l.Split('\t').Last().Trim()).ToList();
        var hasVersion = files.Any(f => f.Contains("Info.plist") || f.Contains("AndroidManifest") || f.EndsWith(".csproj"));
        var hasCs      = files.Any(f => f.EndsWith(".cs"));
        var hasXaml    = files.Any(f => f.EndsWith(".xaml"));

        if (hasVersion && modified > 0 && added == 0)
            return "chore: bump version";

        var parts = new List<string>();
        if (added > 0)    parts.Add($"add {added} file(s)");
        if (modified > 0) parts.Add($"update {modified} file(s)");
        if (deleted > 0)  parts.Add($"remove {deleted} file(s)");

        var type = hasCs || hasXaml ? "feat" : "chore";
        return $"{type}: {string.Join(", ", parts)}";
    }

    private static string RunGit(string dir, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory       = dir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            ProcessEnvironment.UseEnglishCliOutput(psi);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output.Trim();
        }
        catch { return ""; }
    }

    private static (bool, string) RunGitWithResult(string dir, params string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory       = dir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            ProcessEnvironment.UseEnglishCliOutput(psi);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode == 0, output.Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
