using MauiForge.Models;
using MauiForge.Services;
using Spectre.Console;

namespace MauiForge.UI;

public static class RemoteConnectScreen
{
    public static RemoteClientService? Show(StateService stateService)
    {
        var st = stateService.Load();

        // ── Discover servers ────────────────────────────────────
        List<RemoteServerInfo> servers = [];
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start("[dim]Scanning network for MAUI Forge servers...[/]", _ =>
            {
                servers = RemoteDiscoveryService.Discover(timeoutMs: 4000);
            });

        // ── Build choice list ───────────────────────────────────
        var choices = new List<string>();

        if (servers.Count > 0)
        {
            foreach (var s in servers)
            {
                var label = $"{s.Hostname} ({s.Host}:{s.Port})";
                if (s.TokenRequired) label += " [dim][lock][/]";
                choices.Add(label);
            }
        }
        else
        {
            AnsiConsole.MarkupLine("  [yellow](!) No servers found on the network.[/]");
        }

        choices.Add("[cyan1]Enter IP manually...[/]");
        choices.Add("Back");

        var picked = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold cyan1]Connect to Remote Server[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .PageSize(12)
                .AddChoices(choices));

        if (picked == "Back") return null;
        if (picked == "[cyan1]Enter IP manually...[/]")
        {
            var host = AnsiConsole.Ask<string>("[cyan1]Server IP or hostname:[/]");
            var port = AnsiConsole.Ask<int>("[cyan1]Port[/]", 5123);
            servers = [new RemoteServerInfo { Host = host, Port = port, TokenRequired = true }];
            picked = servers[0].ToString();
        }

        // ── Resolve selected server ─────────────────────────────
        var idx = choices.IndexOf(picked);
        if (idx < 0 || idx >= servers.Count) return null;
        var server = servers[idx];

        // ── Check info ─────────────────────────────────────────
        var client = new RemoteClientService();
        try
        {
            var info = client.GetInfoAsync().GetAwaiter().GetResult();
            var needsToken = info.GetProperty("requiresToken").GetBoolean();
            var hostname = info.GetProperty("hostname").GetString();
            server.Hostname = hostname ?? server.Hostname;
            server.TokenRequired = needsToken;
        }
        catch
        {
            AnsiConsole.MarkupLine($"[red]✗ Could not connect to {server.Host}:{server.Port}.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to continue...[/]").AllowEmpty());
            return null;
        }

        // ── Token ───────────────────────────────────────────────
        string? token = null;
        if (server.TokenRequired)
        {
            // Check saved tokens
            var saved = st.KnownRemotes.Find(r => r.Host == server.Host && r.Port == server.Port);
            if (saved?.Token != null && AnsiConsole.Confirm("Use saved token?", true))
            {
                token = saved.Token;
            }
            else
            {
                token = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan1]Enter access token:[/]")
                        .Secret());
            }

            // Save token if wanted
            if (AnsiConsole.Confirm("Remember this token for next time?", true))
            {
                var existing = st.KnownRemotes.Find(r => r.Host == server.Host && r.Port == server.Port);
                if (existing != null)
                {
                    existing.Token = token;
                    existing.LastUsed = DateTime.UtcNow;
                }
                else
                {
                    st.KnownRemotes.Add(new SavedRemote
                    {
                        Host = server.Host,
                        Port = server.Port,
                        Hostname = server.Hostname,
                        Token = token,
                        LastUsed = DateTime.UtcNow
                    });
                }
                stateService.Save(st);
            }
        }

        // ── Connect ─────────────────────────────────────────────
        client.Connect(server, token);

        try
        {
            var apps = client.GetAppsAsync().GetAwaiter().GetResult();
            if (apps == null)
            {
                AnsiConsole.MarkupLine("[red]✗ Could not fetch app list from server.[/]");
                AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to continue...[/]").AllowEmpty());
                return null;
            }
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]✗ Authentication failed. Check your token.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to continue...[/]").AllowEmpty());
            return null;
        }

        AnsiConsole.MarkupLine($"[green]✓ Connected to [white]{server}[/][/]");

        if (AnsiConsole.Confirm("Open web dashboard in browser?", true))
        {
            try
            {
                var url = $"http://{server.Host}:{server.Port}";
                AnsiConsole.MarkupLine($"[dim]Opening [cyan1]{url}[/] in your browser...[/]");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        return client;
    }
}
