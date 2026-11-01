using System.Reflection;

namespace WoWCharacterTracker.Services;

// CHANGELOG.md, built into the exe, so What's new works offline and matches the running version.
static class Changelog
{
    public const string NewTitle = "New";
    public const string FixesTitle = "Fixes and tweaks";
    public const string PageUrl = "https://github.com/JDybeck/wow-character-tracker/blob/main/CHANGELOG.md";

    // The newest versions, newest first.
    public static IReadOnlyList<ChangelogVersion> Recent(int count)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Data/CHANGELOG.md");
        if (s is null) return [];
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd()).Take(count).ToList();
    }

    // "## v1.2.3" starts a version, "### New" a group; other text before the first group is its intro.
    public static List<ChangelogVersion> Parse(string markdown)
    {
        var versions = new List<ChangelogVersion>();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## "))
            {
                versions.Add(new ChangelogVersion(line[3..].Trim()));
                continue;
            }
            if (versions.Count == 0 || line.Length == 0) continue;

            var version = versions[^1];
            if (line.StartsWith("### ")) version.Groups.Add(new ChangelogGroup(line[4..].Trim()));
            else if (line.StartsWith("- ")) GroupFor(version).Items.Add(Item(line[2..], isSub: false));
            else if (line.StartsWith("  - ")) GroupFor(version).Items.Add(Item(line[4..], isSub: true));
            else version.Intro = (version.Intro + " " + Format.PlainNotes(line)).Trim();
        }
        return versions;
    }

    // Bullets outside a group (not how we write them, but they shouldn't vanish).
    static ChangelogGroup GroupFor(ChangelogVersion version)
    {
        if (version.Groups.Count == 0) version.Groups.Add(new ChangelogGroup(FixesTitle));
        return version.Groups[^1];
    }

    // A leading **name** becomes the item's head, so the view can make it stand out.
    static ChangelogItem Item(string text, bool isSub)
    {
        if (text.StartsWith("**") && text.IndexOf("**", 2, StringComparison.Ordinal) is > 2 and var end)
            return new ChangelogItem(Format.PlainNotes(text[2..end]), Format.PlainNotes(text[(end + 2)..]), isSub);
        return new ChangelogItem("", Format.PlainNotes(text), isSub);
    }
}

sealed class ChangelogVersion(string name)
{
    public string Name { get; } = name;
    public string Intro { get; set; } = "";
    public List<ChangelogGroup> Groups { get; } = [];
}

sealed class ChangelogGroup(string title)
{
    public string Title { get; } = title;
    public bool IsNew => Title == Changelog.NewTitle;
    public List<ChangelogItem> Items { get; } = [];
}

sealed record ChangelogItem(string Head, string Text, bool IsSub);
