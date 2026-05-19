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

    public (bool Success, string Output) Push(string dir, string message)
    {
        RunGit(dir, "add", "-A");
        RunGit(dir, "commit", "-m", message);
        return RunGitWithResult(dir, "push");
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
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode == 0, output.Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
