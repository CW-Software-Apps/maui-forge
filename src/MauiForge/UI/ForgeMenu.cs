using Spectre.Console;

namespace MauiForge.UI;

public static class ForgeMenu
{
    // ── Generic SelectionPrompt with optional "Back" ──────────────────────────

    public record ListItem<T>(string Label, T Value, bool IsSeparator = false);

    /// <summary>
    /// Arrow-key list. Returns null when the user picks "← Back".
    /// </summary>
    public static T? PromptList<T>(string title, List<ListItem<T>> items, int pageSize = 22) where T : class
    {
        if (items.Count == 0) return null;

        var selectables = items.Where(i => !i.IsSeparator).ToList();
        if (selectables.Count == 0) return null;

        const string BackLabel = "[black on grey70]  << Back  [/]";

        var prompt = new SelectionPrompt<string>()
            .Title($"\n  [cyan1]{Markup.Escape(title)}[/]")
            .PageSize(pageSize)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11));

        prompt.AddChoices(BackLabel);

        bool hasGroups = items.Any(i => i.IsSeparator);
        var orderedLabels = new List<string>(); // parallel to selectables

        if (hasGroups)
        {
            var groupItems  = new List<string>();
            string? groupHeader = null;

            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    if (groupHeader is not null && groupItems.Count > 0)
                        prompt.AddChoiceGroup($"[grey46]{Markup.Escape(groupHeader)}[/]", groupItems);
                    groupItems  = [];
                    groupHeader = item.Label;
                }
                else
                {
                    var lbl = "  " + item.Label;
                    groupItems.Add(lbl);
                    orderedLabels.Add(lbl);
                }
            }
            if (groupHeader is not null && groupItems.Count > 0)
                prompt.AddChoiceGroup($"[grey46]{Markup.Escape(groupHeader)}[/]", groupItems);
        }
        else
        {
            foreach (var item in selectables)
            {
                var lbl = "  " + item.Label;
                prompt.AddChoices(lbl);
                orderedLabels.Add(lbl);
            }
        }

        string? chosen;
        try { chosen = AnsiConsole.Prompt(prompt); }
        catch { return null; } // ESC or cancelled

        if (chosen == BackLabel) return null;

        var idx = orderedLabels.IndexOf(chosen);
        return idx >= 0 && idx < selectables.Count ? selectables[idx].Value : null;
    }

    /// <summary>
    /// Arrow-key list for value types. Returns (false, default) when the user picks "← Back".
    /// </summary>
    public static (bool Ok, T Value) PromptListValue<T>(string title, List<ListItem<T>> items, int pageSize = 22) where T : struct
    {
        if (items.Count == 0) return (false, default);

        const string BackLabel = "[black on grey70]  << Back  [/]";

        var selectables = items.Where(i => !i.IsSeparator).ToList();
        var labels      = selectables.Select(i => "  " + i.Label).ToList();

        var prompt = new SelectionPrompt<string>()
            .Title($"\n  [cyan1]{Markup.Escape(title)}[/]")
            .PageSize(pageSize)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
            .AddChoices([BackLabel, .. labels]);

        string? chosen;
        try { chosen = AnsiConsole.Prompt(prompt); }
        catch { return (false, default); } // ESC or cancelled

        if (chosen == BackLabel) return (false, default);

        var idx = labels.IndexOf(chosen);
        return idx >= 0 ? (true, selectables[idx].Value) : (false, default);
    }

    /// <summary>
    /// Wraps AnsiConsole.Prompt for a SelectionPrompt, returning null on ESC/cancel.
    /// </summary>
    public static string? PromptSelection(SelectionPrompt<string> prompt, string backLabel)
    {
        try
        {
            var chosen = AnsiConsole.Prompt(prompt);
            return chosen == backLabel ? null : chosen;
        }
        catch { return null; }
    }
}
