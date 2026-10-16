using System.Reflection;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker;

// The addon files are embedded in the exe (see the .csproj), so we can install and update them.
static partial class AddonInstaller
{
    const string Prefix = "Addon/";

    public static string TargetDir(string flavorDir) => Path.Combine(Wow.AddOnsDir(flavorDir), "WoWCharacterTracker");

    public static string BundledVersion => ReadVersion(ReadBundled("WoWCharacterTracker.toc")) ?? "?";

    public static string? InstalledVersion(string flavorDir)
    {
        var toc = Path.Combine(TargetDir(flavorDir), "WoWCharacterTracker.toc");
        return File.Exists(toc) ? ReadVersion(File.ReadAllText(toc)) ?? "?" : null;
    }

    public static string? InstalledInterface(string flavorDir)
    {
        var toc = Path.Combine(TargetDir(flavorDir), "WoWCharacterTracker.toc");
        if (!File.Exists(toc)) return null;
        var m = InterfaceLine().Match(File.ReadAllText(toc));
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // Copies the bundled addon into the AddOns folder. The interface number is set to the
    // installed client's so the game doesn't flag the addon as out of date.
    public static void Install(string flavorDir, int? interfaceVersion)
    {
        var target = TargetDir(flavorDir);
        Directory.CreateDirectory(target);

        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix)))
        {
            var fileName = name[Prefix.Length..];
            var content = ReadBundled(fileName);
            if (fileName.EndsWith(".toc") && interfaceVersion is int v)
                content = InterfaceLine().Replace(content, $"## Interface: {v}", 1);
            File.WriteAllText(Path.Combine(target, fileName), content);
        }
    }

    // Settings.lua tells the addon which events get a screenshot. The game reads it on /reload or login.
    public static void WriteSettings(string flavorDir, FlavorSettings s)
    {
        var target = TargetDir(flavorDir);
        if (!Directory.Exists(target)) return;
        static string B(bool v) => v ? "true" : "false";
        var shots = string.Join(", ", Flavors.ShotKinds.Select(k => $"{k} = {B(s.Shoots(k))}"));
        // What gets posted, so the addon only sends those events live and screenshots only them.
        var post = $"level = {{ on = {B(s.PostLevels)}, split = {s.LevelSplit}, before = {s.LevelEveryBefore}, after = {s.LevelEveryAfter} }}, "
                   + $"rare = {B(s.PostRare)}, epic = {B(s.PostEpic)}, mount = {B(s.PostMounts)}, boss = {B(s.PostBosses)}, death = {B(s.PostDeaths)}";
        var lua = "-- Written by the Character Tracker app from its \"What gets posted\" page. Changes apply on /reload.\n"
                  + "WoWCharacterTrackerSettings = {\n"
                  + $"  screenshot = {{ {shots} }},\n"
                  + $"  post = {{ {post} }},\n"
                  + $"  hideChat = {B(s.HideChatInShots)},\n"
                  + "}\n";
        var file = Path.Combine(target, "Settings.lua");
        if (!File.Exists(file) || File.ReadAllText(file) != lua) File.WriteAllText(file, lua);
    }

    public static void SetInterface(string flavorDir, int interfaceVersion)
    {
        var toc = Path.Combine(TargetDir(flavorDir), "WoWCharacterTracker.toc");
        var text = File.ReadAllText(toc);
        File.WriteAllText(toc, InterfaceLine().Replace(text, $"## Interface: {interfaceVersion}", 1));
    }

    static string ReadBundled(string fileName)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + fileName)
                      ?? throw new InvalidOperationException($"Bundled addon file {fileName} is missing.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    static string? ReadVersion(string toc)
    {
        var m = VersionLine().Match(toc);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^##\s*Version:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^##\s*Interface:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex InterfaceLine();
}
