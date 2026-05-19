using Spectre.Console;

namespace MauiForge.UI;

/// <summary>
/// Single-keypress action menu and ESC-aware list selector.
/// </summary>
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
        AnsiConsole.MarkupLine($"[cyan1]{Markup.Escape(title)}[/]");
        AnsiConsole.WriteLine();

        foreach (var group in groups)
        {
            AnsiConsole.Write(new Rule($"[dim]{Markup.Escape(group.Header)}[/]").RuleStyle(Style.Parse("grey23 dim")));
            foreach (var item in group.Items)
            {
                var hint = item.Hint is { Length: > 0 } ? $"  [grey46]{Markup.Escape(item.Hint)}[/]" : "";
                AnsiConsole.MarkupLine($"  [bold cyan1][[{item.Key}]][/]  {item.Label}{hint}");
            }
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Markup("[dim]Key > [/]");
        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.Escape) { AnsiConsole.WriteLine(); return '\0'; }

                var ch = char.ToLower(k.KeyChar);
                var all = groups.SelectMany(g => g.Items);
                if (all.Any(i => i.Key == ch)) { AnsiConsole.WriteLine(); return ch; }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    // ── Arrow-key list with ESC support ──────────────────────────────────────

    public record ListItem<T>(string Label, T Value, bool IsSeparator = false);

    /// <summary>
    /// Arrow-key selection list. Returns null if ESC is pressed.
    /// </summary>
    public static T? PromptList<T>(string title, List<ListItem<T>> items, int pageSize = 20) where T : class
    {
        var result = PromptListStruct<T?>(title,
            items.Select(i => new ListItem<T?>(i.Label, i.Value, i.IsSeparator)).ToList(),
            pageSize, hasCancel: true);
        return result;
    }

    /// <summary>
    /// Arrow-key selection list for value types. Returns (false, default) if ESC is pressed.
    /// </summary>
    public static (bool Ok, T Value) PromptListValue<T>(string title, List<ListItem<T>> items, int pageSize = 20) where T : struct
    {
        var selectable = items.Where(i => !i.IsSeparator).ToList();
        if (selectable.Count == 0) return (false, default);

        var cursor = 0;
        Render(title, items, selectable, cursor, pageSize);

        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);

                if (k.Key == ConsoleKey.Escape)
                {
                    ClearMenu(title, items, pageSize);
                    return (false, default);
                }
                if (k.Key == ConsoleKey.Enter)
                {
                    ClearMenu(title, items, pageSize);
                    return (true, selectable[cursor].Value);
                }

                var prev = cursor;
                cursor = Navigate(k, cursor, selectable.Count, pageSize);
                if (cursor != prev) Rerender(title, items, selectable, cursor, pageSize);
            }
        }
        finally { Console.CursorVisible = true; }
    }

    private static T? PromptListStruct<T>(string title, List<ListItem<T>> items, int pageSize, bool hasCancel)
    {
        var selectable = items.Where(i => !i.IsSeparator).ToList();
        if (selectable.Count == 0) return default;

        var cursor = 0;
        Render(title, items, selectable, cursor, pageSize);

        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                var k = Console.ReadKey(intercept: true);

                if (k.Key == ConsoleKey.Escape)
                {
                    ClearMenu(title, items, pageSize);
                    return default;
                }
                if (k.Key == ConsoleKey.Enter)
                {
                    ClearMenu(title, items, pageSize);
                    return selectable[cursor].Value;
                }

                var prev = cursor;
                cursor = Navigate(k, cursor, selectable.Count, pageSize);
                if (cursor != prev) Rerender(title, items, selectable, cursor, pageSize);
            }
        }
        finally { Console.CursorVisible = true; }
    }

    private static int Navigate(ConsoleKeyInfo k, int cursor, int count, int pageSize) => k.Key switch
    {
        ConsoleKey.UpArrow   => Math.Max(0, cursor - 1),
        ConsoleKey.DownArrow => Math.Min(count - 1, cursor + 1),
        ConsoleKey.PageUp    => Math.Max(0, cursor - pageSize),
        ConsoleKey.PageDown  => Math.Min(count - 1, cursor + pageSize),
        ConsoleKey.Home      => 0,
        ConsoleKey.End       => count - 1,
        _ => cursor,
    };

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static int _lastLineCount;

    private static void Render<T>(string title, List<ListItem<T>> items, List<ListItem<T>> selectable, int cursor, int pageSize)
    {
        _lastLineCount = 0;

        // Title
        AnsiConsole.MarkupLine($"[cyan1]{Markup.Escape(title)}[/]  [dim](↑↓ navigate · Enter select · ESC cancel)[/]");
        _lastLineCount++;

        // Paging
        var visibleStart = Math.Max(0, cursor - pageSize / 2);
        var visibleEnd   = Math.Min(items.Count, visibleStart + pageSize + 5);

        foreach (var item in items.Skip(visibleStart).Take(visibleEnd - visibleStart))
        {
            if (item.IsSeparator)
            {
                AnsiConsole.Write(new Rule($"[dim]{StripMarkup(item.Label)}[/]").RuleStyle(Style.Parse("grey23 dim")));
            }
            else
            {
                var isSelected = selectable.IndexOf(item) == cursor;
                if (isSelected)
                    AnsiConsole.MarkupLine($"[bold cyan1 on grey11]▶ {item.Label}[/]");
                else
                    AnsiConsole.MarkupLine($"  {item.Label}");
            }
            _lastLineCount++;
        }
    }

    private static void Rerender<T>(string title, List<ListItem<T>> items, List<ListItem<T>> selectable, int cursor, int pageSize)
    {
        // Move cursor up
        Console.Write($"\x1b[{_lastLineCount}A");
        // Clear each line down
        for (var i = 0; i < _lastLineCount; i++)
            Console.Write($"\x1b[2K\n");
        Console.Write($"\x1b[{_lastLineCount}A");

        Render(title, items, selectable, cursor, pageSize);
    }

    private static void ClearMenu<T>(string title, List<ListItem<T>> items, int pageSize)
    {
        Console.Write($"\x1b[{_lastLineCount}A");
        for (var i = 0; i < _lastLineCount; i++)
            Console.Write($"\x1b[2K\n");
        Console.Write($"\x1b[{_lastLineCount}A");
    }

    private static string StripMarkup(string s)
    {
        // Remove [tag] and [/tag] patterns
        return System.Text.RegularExpressions.Regex.Replace(s, @"\[/?[^\]]*\]", "");
    }
}
