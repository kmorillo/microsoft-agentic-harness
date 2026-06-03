using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Presentation.LoggerUI;

/// <summary>
/// Renders parsed <see cref="LogEntry"/> instances to the console
/// with Spectre.Console markup, color-coded log levels, and variable highlighting.
/// </summary>
internal static class LogEntryFormatter
{
    private static readonly string[] SourceColors =
    {
        "deepskyblue1",
        "lightgoldenrod1",
        "palegreen1",
        "lightpink1",
        "plum2"
    };

    private static readonly object OutputLock = new();

    /// <summary>Severity rank per level name; higher is more severe. Unknown levels rank as info.</summary>
    private static readonly Dictionary<string, int> LevelRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trce"] = 0, ["trace"] = 0,
        ["dbug"] = 1, ["dbg"] = 1, ["debug"] = 1,
        ["info"] = 2, ["information"] = 2,
        ["warn"] = 3, ["warning"] = 3,
        ["error"] = 4, ["err"] = 4, ["fail"] = 4,
        ["crit"] = 5, ["critical"] = 5, ["fatal"] = 5,
    };

    /// <summary>Display names for each minimum-level filter step, indexed by rank.</summary>
    private static readonly string[] FilterNames = { "ALL", "DEBUG+", "INFO+", "WARN+", "ERROR+", "CRIT only" };

    /// <summary>
    /// Minimum severity rank to display; entries below it are suppressed. 0 shows everything.
    /// <c>volatile</c> because the keyboard listener thread mutates it while reader threads read it.
    /// </summary>
    private static volatile int _minLevelRank;

    /// <summary>
    /// Toggles the warning filter: jumps straight to WARN+ from any other state, or back to ALL
    /// when already at WARN+. Bound to the <c>W</c> key for one-press "show me the warnings".
    /// </summary>
    internal static void ToggleWarningFilter()
    {
        _minLevelRank = _minLevelRank == 3 ? 0 : 3;
        AnnounceFilter();
    }

    /// <summary>Advances the minimum-level filter one step, wrapping back to ALL. Bound to <c>L</c>.</summary>
    internal static void CycleLevelFilter()
    {
        _minLevelRank = (_minLevelRank + 1) % FilterNames.Length;
        AnnounceFilter();
    }

    private static void AnnounceFilter() =>
        WriteStatusLine($"[bold]Filter:[/] showing [cyan]{FilterNames[_minLevelRank]}[/]");

    private static bool PassesFilter(LogEntry entry)
    {
        if (_minLevelRank == 0)
            return true;

        var rank = entry.Level is not null && LevelRanks.TryGetValue(entry.Level, out var r) ? r : 2;
        return rank >= _minLevelRank;
    }

    /// <summary>
    /// Builds the per-source tag (e.g. "[ConsoleUI]") with a stable color from the palette.
    /// </summary>
    internal static string BuildSourceTag(string pipeName, int index)
    {
        var dot = pipeName.LastIndexOf('.');
        var label = (dot >= 0 && dot < pipeName.Length - 1) ? pipeName.Substring(dot + 1) : pipeName;
        var color = SourceColors[index % SourceColors.Length];
        return $"[{color}][[{label}]][/]";
    }

    /// <summary>
    /// Processes and displays a single log line tagged with its source.
    /// </summary>
    internal static void ProcessLogLine(string line, string sourceTag, bool parseJson)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        try
        {
            var logEntry = LogEntryParser.Parse(line, parseJson);
            DisplayLogEntry(logEntry, sourceTag);
        }
        catch
        {
            lock (OutputLock)
            {
                try { Console.WriteLine(line); }
                catch { /* swallow -- last-ditch fallback */ }
            }
        }
    }

    /// <summary>
    /// Writes a status message as a regular output line.
    /// </summary>
    internal static void WriteStatusLine(string message)
    {
        lock (OutputLock)
        {
            try { AnsiConsole.MarkupLine($"● {message}"); }
            catch { /* Ignore console errors */ }
        }
    }

    /// <summary>
    /// Prints the initial header banner listing all monitored pipes.
    /// </summary>
    internal static void PrintHeader(IReadOnlyList<string> pipeNames)
    {
        var heading = pipeNames.Count == 1
            ? $"[bold cornflowerblue]Log Viewer[/] - [cyan]{pipeNames[0]}[/]"
            : $"[bold cornflowerblue]Log Viewer[/] - [cyan]{pipeNames.Count} sources[/]";

        var rule = new Rule(heading);
        rule.RuleStyle("grey");
        AnsiConsole.Write(rule);

        AnsiConsole.WriteLine();
        for (int i = 0; i < pipeNames.Count; i++)
        {
            var tag = BuildSourceTag(pipeNames[i], i);
            AnsiConsole.MarkupLine($"  {tag} [grey]{pipeNames[i]}[/]");
        }
        AnsiConsole.MarkupLine("[grey]Press C to clear, W to toggle warnings-only, L to cycle level filter, Ctrl+C to exit[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays a log entry with formatting, colors, and a source tag.
    /// </summary>
    private static void DisplayLogEntry(LogEntry entry, string sourceTag)
    {
        if (!PassesFilter(entry))
            return;

        var config = LogEntryParser.GetLogLevelConfig(entry.Level);
        var color = config.Color;
        var icon = config.Icon;
        var displayLevel = config.Name;

        var timestamp = entry.Timestamp.HasValue
            ? $"[grey]{entry.Timestamp.Value:HH:mm:ss}[/]"
            : "[grey]         [/]";

        var levelBadge = $"[{color}]{displayLevel}[/]";

        var iconPart = !string.IsNullOrEmpty(icon) ? $"[{color}]{icon}[/] " : "";

        var message = FormatMessageWithHighlight(entry);

        if (entry.HasException)
        {
            iconPart = $"[{color}]✖[/] ";
        }

        lock (OutputLock)
        {
            AnsiConsole.MarkupLine($"{timestamp} {sourceTag} {levelBadge} {iconPart}{message}");

            if (entry.IsStructured && entry.HasException)
            {
                try
                {
                    var panel = new Panel(entry.Raw)
                        .BorderColor(Color.Red)
                        .Header("[red]Exception[/]")
                        .RoundedBorder()
                        .Collapse();
                    AnsiConsole.Write(panel);
                }
                catch
                {
                    AnsiConsole.MarkupLine($"[red]{EscapeMarkup(entry.Raw)}[/]");
                }
            }
        }
    }

    /// <summary>
    /// Formats a log message with variable values highlighted.
    /// </summary>
    private static string FormatMessageWithHighlight(LogEntry entry)
    {
        var message = entry.Message;

        if (entry.IsStructured && entry.Properties.Count > 0)
        {
            string? messageTemplate = null;
            if (entry.IsStructured)
            {
                try
                {
                    var json = JsonDocument.Parse(entry.Raw);
                    var root = json.RootElement;

                    string[] templateProps = { "MessageTemplate", "messageTemplate", "@t", "template" };
                    foreach (var prop in templateProps)
                    {
                        if (root.TryGetProperty(prop, out var templateElement) &&
                            templateElement.ValueKind == JsonValueKind.String)
                        {
                            messageTemplate = templateElement.GetString();
                            break;
                        }
                    }
                }
                catch { /* Ignore JSON parse errors */ }
            }

            if (!string.IsNullOrEmpty(messageTemplate))
            {
                return FormatTemplateWithHighlight(messageTemplate, entry.Properties);
            }

            return FormatMessageWithValueHighlighting(message, entry.Properties);
        }

        return HighlightGenericPatterns(message);
    }

    /// <summary>
    /// Formats a message template by replacing {placeholders} with highlighted property values.
    /// </summary>
    private static string FormatTemplateWithHighlight(string template, Dictionary<string, string> properties)
    {
        var result = new StringBuilder();
        var currentIndex = 0;

        var matches = Regex.Matches(template, @"\{(\w+)\}");

        foreach (Match match in matches.Cast<Match>().OrderBy(m => m.Index))
        {
            result.Append(EscapeMarkup(template.Substring(currentIndex, match.Index - currentIndex)));

            var propName = match.Groups[1].Value;

            var propValue = properties.FirstOrDefault(p =>
                string.Equals(p.Key, propName, StringComparison.OrdinalIgnoreCase)).Value;

            if (!string.IsNullOrEmpty(propValue))
            {
                result.Append($"[bold yellow on darkblue]{EscapeMarkup(propValue)}[/]");
            }
            else
            {
                result.Append(EscapeMarkup(match.Value));
            }

            currentIndex = match.Index + match.Length;
        }

        if (currentIndex < template.Length)
        {
            result.Append(EscapeMarkup(template.Substring(currentIndex)));
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats a message by finding and highlighting all occurrences of property values.
    /// </summary>
    private static string FormatMessageWithValueHighlighting(string message, Dictionary<string, string> properties)
    {
        var result = new StringBuilder();
        var lastIndex = 0;

        var allMatches = new List<(int Index, string Value)>();

        foreach (var (propName, value) in properties)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2)
                continue;

            var index = message.IndexOf(value, 0, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                allMatches.Add((index, value));
                index = message.IndexOf(value, index + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (allMatches.Count == 0)
        {
            return EscapeMarkup(message);
        }

        var sortedMatches = allMatches
            .OrderByDescending(m => m.Value.Length)
            .ThenBy(m => m.Index)
            .ToList();

        var nonOverlapping = new List<(int Index, int Length, string Value)>();
        foreach (var (index, value) in sortedMatches)
        {
            if (nonOverlapping.Any(m => index < m.Index + m.Length && index + value.Length > m.Index))
                continue;
            nonOverlapping.Add((index, value.Length, value));
        }

        nonOverlapping = nonOverlapping.OrderBy(m => m.Index).ToList();

        foreach (var (index, length, value) in nonOverlapping)
        {
            result.Append(EscapeMarkup(message.Substring(lastIndex, index - lastIndex)));

            var actualValue = message.Substring(index, length);
            result.Append($"[bold yellow on darkblue]{EscapeMarkup(actualValue)}[/]");

            lastIndex = index + length;
        }

        if (lastIndex < message.Length)
        {
            result.Append(EscapeMarkup(message.Substring(lastIndex)));
        }

        return result.ToString();
    }

    /// <summary>
    /// Applies generic highlighting patterns for non-structured logs.
    /// </summary>
    private static string HighlightGenericPatterns(string message)
    {
        var result = EscapeMarkup(message);

        result = Regex.Replace(
            result,
            @"&quot;([^&]*)&quot;",
            "[bold yellow on darkblue]&quot;$1[/][bold yellow on darkblue]&quot;[/]");

        result = Regex.Replace(
            result,
            @"([A-Z]:\\[^[\]]+|/[^[\]\s]+)",
            "[bold yellow on darkblue]$1[/]",
            RegexOptions.IgnoreCase);

        result = Regex.Replace(
            result,
            @"\b(\d{4,})\b(?!\])",
            "[bold cyan]$1[/]");

        return result;
    }

    /// <summary>
    /// Escapes Spectre.Console markup characters in text.
    /// </summary>
    private static string EscapeMarkup(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}
