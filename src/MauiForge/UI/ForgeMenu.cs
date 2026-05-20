using Spectre.Console;

namespace MauiForge.UI;

public static class ForgeMenu
{
    // ── Single-key action menu ────────────────────────────────────────────────

    public record KeyItem(char Key, string Label, string? Hint = null);
    public record KeyGroup(string Header, List<KeyItem> Items);

    /// <summary>
    /// Renders a grouped key-shortcut menu and waits for a single keypress.
    /// Returns the char pressed, or '\0' for ESC.
    /// </summary>
    public static char PromptKey(string title, List<KeyGroup> groups)
    {
        AnsiConsole.MarkupLine($"  [bold cyan1]{Markup.Escape(title)}[/]");
        AnsiConsole.WriteLine();

        foreach (var group in groups)
        {
            AnsiConsole.Write(
                new Rule($"[grey46]{Markup.Escape(group.Header)}[/]")
                    .RuleStyle(Style.Parse("grey23"))
                    .LeftJustified());

            foreach (var item in group.Items)
            {
                var hint = item.Hint is { Length: > 0 }
                    ? $"  [grey46]{Markup.Escape(item.Hint)}[/]"
                    : "";
                AnsiConsole.MarkupLine($"  [bold cyan1 on grey7] {item.Key} [/]  {item.Label}{hint}");
            }
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine("[grey46]  Press a key — ESC to go back[/]");
        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Escape) return '\0';

                var ch  = char.ToLower(k.KeyChar);
                var all = groups.SelectMany(g => g.Items);
                if (all.Any(i => i.Key == ch)) return ch;
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    // ── Arrow-key list backed by Spectre SelectionPrompt ─────────────────────

    public record ListItem<T>(string Label, T Value, bool IsSeparator = false);

    private const string BackSentinel = "\x00__BACK__";

    /// <summary>
    /// Arrow-key list using Spectre SelectionPrompt. Returns null if the user
    /// picks "← Back" (or if the list is empty).
    /// </summary>
    public static T? PromptList<T>(string title, List<ListItem<T>> items, int pageSize = 20) where T : class
    {
        if (items.Count == 0) return null;

        // Build Spectre prompt. We use the Label as the display string and
        // recover the value by index after selection.
        var selectables = items.Where(i => !i.IsSeparator).ToList();
        if (selectables.Count == 0) return null;

        // Map label → index (labels might not be unique, so we track order)
        var labelList = new List<string>();
        var backLabel = $"  [grey46]← Back  [dim](ESC)[/][/]";

        labelList.Add(backLabel);

        // Build the prompt with groups
        var prompt = new SelectionPrompt<string>()
            .Title($"  [cyan1]{Markup.Escape(title)}[/]")
            .PageSize(pageSize)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11));

        // Add "back" as first group
        prompt.AddChoices(backLabel);

        bool hasGroups = items.Any(i => i.IsSeparator);
        if (hasGroups)
        {
            var currentGroupItems = new List<string>();
            string? currentHeader = null;

            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    if (currentHeader is not null && currentGroupItems.Count > 0)
                        prompt.AddChoiceGroup(currentHeader, currentGroupItems);
                    currentGroupItems = [];
                    currentHeader = $"[grey46]{Markup.Escape(item.Label)}[/]";
                }
                else
                {
                    var lbl = "  " + item.Label;
                    currentGroupItems.Add(lbl);
                    labelList.Add(lbl);
                }
            }
            if (currentHeader is not null && currentGroupItems.Count > 0)
                prompt.AddChoiceGroup(currentHeader, currentGroupItems);
        }
        else
        {
            var flat = selectables.Select(i => "  " + i.Label).ToList();
            prompt.AddChoices(flat);
            labelList.AddRange(flat);
        }

        var chosen = AnsiConsole.Prompt(prompt);
        if (chosen == backLabel) return null;

        // Recover value by matching label index
        var chosenIdx = labelList.IndexOf(chosen);
        if (chosenIdx <= 0) return null; // 0 = backLabel, -1 = not found

        // labelList[0] = back, labelList[1..] = selectables in order
        var valueIdx = chosenIdx - 1;
        return valueIdx < selectables.Count ? selectables[valueIdx].Value : null;
    }
}
