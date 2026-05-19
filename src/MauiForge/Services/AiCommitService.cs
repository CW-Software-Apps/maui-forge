namespace MauiForge.Services;

public class AiCommitService
{
    public record Provider(string Name, string Icon);

    public static readonly Provider[] AvailableProviders =
    [
        new("Claude (claude CLI)",  "✦"),
        new("Gemini (gemini CLI)",  "✦"),
        new("Ollama (local)",       "✦"),
        new("Smart suggestion",     "◈"),
    ];

    public static List<Provider> DetectAvailable(GitService git, string dir)
    {
        var list = new List<Provider>();

        if (IsInPath("claude"))  list.Add(AvailableProviders[0]);
        if (IsInPath("gemini"))  list.Add(AvailableProviders[1]);
        if (IsOllamaRunning())   list.Add(AvailableProviders[2]);

        list.Add(AvailableProviders[3]); // always available
        return list;
    }

    public string Generate(Provider provider, string diffContext, GitService git, string dir)
    {
        var prompt = BuildPrompt(diffContext);

        return provider.Name switch
        {
            var n when n.StartsWith("Claude")  => RunClaude(prompt)  ?? git.SuggestCommitMessage(dir),
            var n when n.StartsWith("Gemini")  => RunGemini(prompt)  ?? git.SuggestCommitMessage(dir),
            var n when n.StartsWith("Ollama")  => RunOllama(prompt)  ?? git.SuggestCommitMessage(dir),
            _                                   => git.SuggestCommitMessage(dir),
        };
    }

    private static string BuildPrompt(string diffContext) =>
        $"Write a concise git commit message (conventional commits format, one line, no quotes) " +
        $"for these changes:\n\n{diffContext}\n\nRespond with only the commit message.";

    private static string? RunClaude(string prompt)
    {
        try
        {
            return RunCli("claude", ["-p", prompt], timeoutMs: 20_000)?.Trim();
        }
        catch { return null; }
    }

    private static string? RunGemini(string prompt)
    {
        try
        {
            // gemini CLI: gemini -p "prompt" or gemini "prompt"
            var result = RunCli("gemini", ["-p", prompt], timeoutMs: 20_000)
                      ?? RunCli("gemini", [prompt], timeoutMs: 20_000);
            return result?.Trim();
        }
        catch { return null; }
    }

    private static string? RunOllama(string prompt)
    {
        try
        {
            // ollama run <model> with prompt piped via stdin is complex; use HTTP API on localhost:11434
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                model  = "llama3.2",   // most common small model
                prompt = prompt,
                stream = false,
            });
            var response = http.PostAsync("http://localhost:11434/api/generate",
                new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode) return null;
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString()?.Trim();
        }
        catch { return null; }
    }

    private static string? RunCli(string exe, string[] args, int timeoutMs = 15_000)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(timeoutMs);
            return proc.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    private static bool IsInPath(string exe)
    {
        var exeName = OperatingSystem.IsWindows() ? exe + ".exe" : exe;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(dir.Trim(), exeName))) return true;
            if (File.Exists(Path.Combine(dir.Trim(), exe)))     return true;
        }
        return false;
    }

    private static bool IsOllamaRunning()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var r = http.GetAsync("http://localhost:11434/api/tags").GetAwaiter().GetResult();
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
