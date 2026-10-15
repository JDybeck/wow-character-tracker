using System.Reflection;
using System.Text.RegularExpressions;

namespace LevelTimesCompanion;

// The addon files are embedded in the exe (see the .csproj), so we can install and update them.
static partial class AddonInstaller
{
    const string Prefix = "Addon/";

    public static string TargetDir(string wowPath) => Path.Combine(Wow.AddOnsDir(wowPath), "LevelTimes");

    public static string BundledVersion => ReadVersion(ReadBundled("LevelTimes.toc")) ?? "?";

    public static string? InstalledVersion(string wowPath)
    {
        var toc = Path.Combine(TargetDir(wowPath), "LevelTimes.toc");
        return File.Exists(toc) ? ReadVersion(File.ReadAllText(toc)) ?? "?" : null;
    }

    public static string? InstalledInterface(string wowPath)
    {
        var toc = Path.Combine(TargetDir(wowPath), "LevelTimes.toc");
        if (!File.Exists(toc)) return null;
        var m = InterfaceLine().Match(File.ReadAllText(toc));
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // Copies the bundled addon into the AddOns folder. The interface number is set to the
    // installed client's so the game doesn't flag the addon as out of date.
    public static void Install(string wowPath, int? interfaceVersion)
    {
        var target = TargetDir(wowPath);
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

    public static void SetInterface(string wowPath, int interfaceVersion)
    {
        var toc = Path.Combine(TargetDir(wowPath), "LevelTimes.toc");
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
