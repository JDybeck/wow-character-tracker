namespace WoWCharacterTracker.Services;

// What a highlight says, the same everywhere: the character's details in the app, the stream
// overlay's highlights box and the stream text files. A verb and a subject, so the app can color
// the subject ("Won a duel against " + "Grimtusk").
static class HighlightText
{
    public static (string Verb, string Subject) Of(Entry e) => e.Kind switch
    {
        "boss" => ("Killed ", e.Boss + (e.Instance.Length > 0 ? $" ({e.Instance})" : "")),
        "keyrun" => (e.Timed ? "Timed " : "Finished ", Format.KeyRun(e) + (e.Timed ? $" (+{e.Upgrades})" : " (over time)")),
        "loot" => ("Looted ", e.Item),
        "mount" => (e.Bought ? "Bought " : "Got ", e.Mount),
        "duel" when e.Lost => (e.Fled ? "Ran from a duel with " : "Lost a duel to ", e.Opponent),
        "duel" => ("Won a duel against ", e.Opponent),
        "makgora" when e.Lost => ("Lost a Mak'gora to ", e.Opponent),
        "makgora" => ("Won a Mak'gora against ", e.Opponent),
        "bg" => (e.Lost ? "Lost " : "Won ", e.Instance.Length > 0 ? e.Instance : "a battleground"),
        "mplus" => ("Reached ", $"{e.Step} Mythic+ rating" + (e.Rating > 0 ? $" ({e.Rating})" : "")),
        "pvp" => ("Reached ", $"{e.Step} in {e.Bracket}" + (e.Rating > 0 ? $" ({e.Rating})" : "")),
        "death" => ("", $"Died at level {e.Level}" + (e.Killer.Length > 0 ? $", killed by {e.Killer}" : "")),
        _ => ("", ""),
    };

    // The whole line, "" when the event has nothing to say (an item or opponent we don't know).
    public static string Text(Entry e)
    {
        var (verb, subject) = Of(e);
        return subject.Length == 0 ? "" : verb + subject;
    }
}
