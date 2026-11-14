namespace WoWCharacterTracker.ViewModels;

// The Race box on a friend's Leveling tab: their character's level times against one of yours.
sealed record LevelRaceRow(string Against, IReadOnlyList<string> Lines)
{
    // theirName: the friend's character; mine: your character's name and level.
    public static LevelRaceRow? From(LevelRace.Result? race, string theirName, string myName, int myLevel)
    {
        if (race is null) return null;
        var lines = new List<string>();
        if (race.Level > 0)
        {
            var gap = Math.Abs(race.Mine - race.Theirs);
            var who = race.Mine < race.Theirs ? "You" : theirName;
            lines.Add($"To level {race.Level}: {theirName} {Format.Played(race.Theirs)} played, you {Format.Played(race.Mine)}. "
                + (gap < 60 ? "Dead even." : $"{who} got there {Format.Played(gap)} sooner."));
        }
        if (race.Compared > 0)
            lines.Add($"Faster levels: {theirName} {race.TheirsFaster}, you {race.MineFaster}.");
        return lines.Count == 0 ? null : new LevelRaceRow($"Against your {myName}, level {myLevel}", lines);
    }
}
