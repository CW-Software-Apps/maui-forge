using System.Diagnostics;
using Spectre.Console;

namespace MauiForge.UI;

public static class DiagnosticsScreen
{
    public static void Show()
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan1]  Diagnostics  [/]").RuleStyle(Style.Parse("cyan1 dim")));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold dim]Component[/]").Width(22))
            .AddColumn(new TableColumn("[bold dim]Status[/]"));

        Check(table, "dotnet --version",  "dotnet", "--version");
        Check(table, "dotnet workloads",  "dotnet", "workload list");
        Check(table, "adb version",       "adb",    "version");
        Check(table, "ssh -V",            "ssh",    "-V");
        Check(table, "xcrun --version",   "xcrun",  "--version");
        Check(table, "git --version",     "git",    "--version");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[dim]dotnet workload list[/]").RuleStyle(Style.Parse("dim")));
        var wl = Run("dotnet", "workload list");
        foreach (var line in wl.Split('\n').Take(30))
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line.TrimEnd())}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to go back...[/]").AllowEmpty());
    }

    private static void Check(Table table, string label, string exe, string args)
    {
        try
        {
            var output = Run(exe, args).Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "ok";
            table.AddRow($"[white]{Markup.Escape(label)}[/]", $"[green]{Markup.Escape(output)}[/]");
        }
        catch
        {
            table.AddRow($"[white]{Markup.Escape(label)}[/]", "[red]not found[/]");
        }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(8_000);
        return output;
    }
}
