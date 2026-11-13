namespace ChestCounter;

/// <summary>
/// Release notes are written in four sections separated by "---", each starting with "**English**", "**Français**",
/// "**Darija**" or "**Español**", with a bold "What's new" line followed by a bullet list. This picks the list of one language.
/// </summary>
public static class ReleaseNotes
{
    private static readonly Dictionary<string, string> SectionTitle = new() { ["en"] = "English", ["fr"] = "Français", ["ary"] = "Darija", ["es"] = "Español" };

    /// <summary>RELEASE_NOTES.md of this version, built into the exe: shown once on the home tab after a new version.</summary>
    public static string? Embedded { get; } = LoadEmbedded();

    private static string? LoadEmbedded()
    {
        try
        {
            using var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("ReleaseNotes.md");
            return s == null ? null : new StreamReader(s).ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> WhatsNew(string? body, string language)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }
        var sections = System.Text.RegularExpressions.Regex.Split(body.Replace("\r\n", "\n"), @"^\s*-{3,}\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        string? Section(string code) => SectionTitle.TryGetValue(code, out var title)
            ? sections.FirstOrDefault(sec => sec.TrimStart().StartsWith($"**{title}**", StringComparison.OrdinalIgnoreCase))
            : null;
        var section = Section(language) ?? Section("en") ?? (sections.Length == 1 ? sections[0] : null);
        if (section == null)
        {
            return Array.Empty<string>();
        }

        // The bullet list that follows the bold "What's new" line (or, failing that, every bullet of the section).
        var lines = section.Split('\n').Select(l => l.TrimEnd()).ToList();
        var start = lines.FindIndex(l => l.StartsWith("**", StringComparison.Ordinal) && l.EndsWith("**", StringComparison.Ordinal) && l.Length > 4);
        var bullets = new List<string>();
        foreach (var line in start >= 0 ? lines.Skip(start + 1) : lines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            {
                bullets.Add(Plain(t[2..]));
            }
            else if (bullets.Count > 0 && t.Length == 0 && start >= 0)
            {
                break;
            }
        }
        return bullets;
    }

    /// <summary>Markdown to plain console text: **bold**, *italic*, `code` and [text](link) lose their marks.</summary>
    private static string Plain(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return text.Replace("**", "").Replace("`", "").Replace("*", "");
    }
}
