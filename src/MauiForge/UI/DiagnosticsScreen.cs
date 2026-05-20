using System.Diagnostics;
using MauiForge.Services;
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

        var adbPath      = DeviceService.FindAdb();
        var emulatorPath = DeviceService.FindEmulator();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold dim]Component[/]").Width(22))
            .AddColumn(new TableColumn("[bold dim]Status[/]").Width(46))
            .AddColumn(new TableColumn("[bold dim]Path[/]"));

        CheckExe(table, "dotnet",  "--version",  null);
        CheckExe(table, "git",     "--version",  null);
        CheckExe(table, "ssh",     "-V",         null);
        CheckExe(table, "xcrun",   "--version",  null);

        // adb — use discovered path
        if (adbPath is not null)
            CheckExe(table, "adb version", "version", adbPath, adbPath);
        else
            table.AddRow("[white]adb version[/]", "[red]not found[/]", "[grey46]—[/]");

        // emulator
        if (emulatorPath is not null)
            CheckExe(table, "emulator", "-version", emulatorPath, emulatorPath);
        else
            table.AddRow("[white]emulator[/]", "[grey46]not found[/]", "[grey46]—[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // adb devices
        if (adbPath is not null)
        {
            AnsiConsole.Write(new Rule("[dim]adb devices -l[/]").RuleStyle(Style.Parse("dim")));
            var devOutput = RunFull(adbPath, ["devices", "-l"]);
            foreach (var line in devOutput.Split('\n').Take(20).Where(l => l.TrimEnd().Length > 0))
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line.TrimEnd())}[/]");
            AnsiConsole.WriteLine();
        }

        // dotnet workloads
        AnsiConsole.Write(new Rule("[dim]dotnet workload list[/]").RuleStyle(Style.Parse("dim")));
        var wl = Run("dotnet", "workload list");
        foreach (var line in wl.Split('\n').Take(30).Where(l => l.TrimEnd().Length > 0))
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line.TrimEnd())}[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to go back...[/]").AllowEmpty());
    }

    private static void CheckExe(Table table, string label, string args, string? exePath, string? displayPath = null)
    {
        var exe = exePath ?? label.Split(' ')[0];
        try
        {
            var output = RunFull(exe, args.Split(' '))
                .Split('\n')
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
                ?.Trim() ?? "ok";

            var pathCol = displayPath is not null
                ? $"[grey46]{Markup.Escape(displayPath)}[/]"
                : "[grey46]in PATH[/]";

            table.AddRow(
                $"[white]{Markup.Escape(label)}[/]",
                $"[green]{Markup.Escape(output)}[/]",
                pathCol);
        }
        catch
        {
            table.AddRow($"[white]{Markup.Escape(label)}[/]", "[red]not found[/]", "[grey46]—[/]");
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
        ProcessEnvironment.UseEnglishCliOutput(psi);
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(8_000);
        return output;
    }

    private static string RunFull(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        ProcessEnvironment.UseEnglishCliOutput(psi);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(8_000);
        return output;
    }
}
