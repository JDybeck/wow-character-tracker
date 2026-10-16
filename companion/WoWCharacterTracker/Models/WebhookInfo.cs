namespace WoWCharacterTracker.Models;

class WebhookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    // Forever rulesets this webhook gets posts for. Null means all of them.
    public List<string>? ForeverRulesets { get; set; }

    public static readonly string[] AllRulesets = ["Normal", "Hardcore", "Self-found"];

    // Character tags this webhook posts for. Null or empty means every character ("standard").
    public List<string>? Tags { get; set; }

    public bool AcceptsTags(IReadOnlyCollection<string> characterTags) =>
        Tags is not { Count: > 0 } || Tags.Any(characterTags.Contains);

    // A character can be both hardcore and self-found ("Hardcore, Self-found"); either one counts.
    public bool AcceptsRuleset(Entry e)
    {
        if (ForeverRulesets is null) return true;
        var ruleset = e.Ruleset.Length > 0 ? e.Ruleset : e.Hardcore ? "Hardcore" : "Normal";
        return ruleset.Split(", ").Any(ForeverRulesets.Contains);
    }
}
