namespace WoWCharacterTracker.Services.Discord;

// What each kind of event looks like in Discord: title, text, color, links. The level-up text is
// the owner's format; max level and legendary loot get extra flair.
sealed class EventEmbeds(Flavor flavor)
{
    public string ItemUrl(int itemId) => WowheadUrl("item", itemId);
    string WowheadUrl(string type, int id) => $"https://www.wowhead.com/{flavor.WowheadPath}{type}={id}";

    // What Wowhead knows an event's item by: loot's item, a mount's item or (learned without one) its
    // spell. Null when there's none. The post's picture and links both use it.
    public static (string Type, int Id)? WowheadThing(Entry e) => e.Kind switch
    {
        "loot" or "mount" when e.ItemId > 0 => ("item", e.ItemId),
        "mount" when e.SpellId > 0 => ("spell", e.SpellId),
        _ => null,
    };

    string? ThingUrl(Entry e) => WowheadThing(e) is { } t ? WowheadUrl(t.Type, t.Id) : null;

    // itemPicture: the post shows the item's (or mount's) tooltip as a picture, so the text leaves out the item line.
    public Dictionary<string, object> Build(Entry e, bool isTest, bool itemPicture = false)
    {
        var (title, lines) = e.Kind switch
        {
            "boss" => BossText(e),
            "keyrun" => KeyRunText(e),
            "loot" when e.Quality >= 5 => LegendaryText(e),
            "loot" => LootText(e, itemPicture),
            "mount" => MountText(e, itemPicture),
            "death" => DeathText(e),
            "duel" => DuelText(e),
            "makgora" => MakgoraText(e),
            "bg" => BattlegroundText(e),
            "profession" => ProfessionText(e),
            "mplus" => MPlusText(e),
            "pvp" => PvpText(e),
            "test" => TestText(e),
            _ when e.MaxLevel => MaxLevelText(e),
            _ => LevelText(e),
        };

        var embed = new Dictionary<string, object>
        {
            ["title"] = title,
            ["description"] = string.Join("\n", lines),
            ["timestamp"] = DateTimeOffset.FromUnixTimeSeconds(e.At).ToString("o"),
        };

        if (e.MaxLevel)
            embed["color"] = 0xFFD100; // gold, it's a big one
        else if (e.Kind == "loot" && e.Quality is >= 3 and <= 6 && GameColors.Quality(e.Quality) is int qc) // rare to artifact
            embed["color"] = qc;
        else if (e.Kind is "death" or "makgora")
            embed["color"] = 0x8B0000;
        else if (e.Kind == "mplus" || (e.Kind == "keyrun" && e.Timed))
            embed["color"] = 0xA335EE;
        else if (e.Kind == "pvp")
            embed["color"] = 0xC41E3A;
        else if (GameColors.Class(e.ClassFile) is int color)
            embed["color"] = color;

        if (ThingUrl(e) is { } url)
            embed["url"] = url;

        // "Human Fury Warrior", leaving out whatever we don't know.
        var who = string.Join(" ", new[] { e.Race, e.Spec, e.Class }.Where(s => s.Length > 0));
        if (who.Length > 0)
        {
            var author = new Dictionary<string, object> { ["name"] = who };
            if (CharacterArt.ClassIconUrl(e.ClassFile) is { } classIcon) author["icon_url"] = classIcon;
            embed["author"] = author;
        }

        // The thumbnail (the character's picture) is added by DiscordPoster, by CharacterPicture's rule.

        var footer = isTest ? "Test message" : e.Realm; // DiscordWebhook adds the app's name and logo
        if (footer.Length > 0) embed["footer"] = new { text = footer };

        return embed;
    }

    // The Discord panel's Test button: checks the webhook without pretending something happened.
    static (string, List<string>) TestText(Entry e)
    {
        var lines = new List<string> { "This webhook works. Level-ups, loot, boss kills and more will show up here." };
        if (e.Zone.Length > 0) lines.Add($"Last seen in **{e.Zone}** at level {e.Level}.");
        return ($"{e.Char} sent a test message.", lines);
    }

    // The owner's format: who dinged, how long it took, played, where, professions, talents.
    static (string, List<string>) LevelText(Entry e)
    {
        var lines = new List<string>();
        if (e.Seconds > 0) lines.Add($"{e.Level - 1} to {e.Level} took **{Format.Duration(e.Seconds)}**."); // unknown when the addon came in mid-level
        if (e.Played > 0) lines.Add($"Total played: **{Format.Played(e.Played)}**.");
        if (e.Zone.Length > 0) lines.Add($"They dinged in **{e.Zone}**.");
        AddProfessionsAndTalents(e, lines);
        return ($"{e.Char} just dinged level {e.Level}.", lines);
    }

    // Max level: the big one. Time to max, the last level, where, and who they were with.
    static (string, List<string>) MaxLevelText(Entry e)
    {
        var lines = new List<string> { $"\U0001F389 **{e.Char} has reached level {e.Level}, the max level!** \U0001F389", "" };
        if (e.Played > 0) lines.Add($"Time to max: **{Format.Played(e.Played)}** played.");
        if (e.Seconds > 0) lines.Add($"The last level took {Format.Duration(e.Seconds)}.");
        if (e.Zone.Length > 0) lines.Add($"The final ding happened in **{e.Zone}**.");
        if (e.Members.Count > 0)
            lines.Add("Celebrating with: " + string.Join(", ", e.Members.Select(m => m.Name)) + ".");
        AddProfessionsAndTalents(e, lines);
        return ($"\U0001F3C6 {e.Char} hit max level {e.Level}!", lines);
    }

    static void AddProfessionsAndTalents(Entry e, List<string> lines)
    {
        if (e.Professions.Length > 0 || e.Talents.Length > 0) lines.Add("");
        if (e.Professions.Length > 0) lines.Add($"**Professions:** {Format.ProfessionsWithEmoji(e.Professions)}");
        if (e.Talents.Length > 0) lines.Add($"**Talents:** {e.Talents}");
    }

    static (string, List<string>) BossText(Entry e)
    {
        var lines = new List<string>();
        if (e.Instance.Length > 0) lines.Add($"Final boss of **{e.Instance}** is down.");
        AddGroup(e, lines);
        return ($"{e.Char} killed {e.Boss}!", lines);
    }

    // A finished Mythic+ run (Retail's end boss kill): the key, how it went, the score and the group.
    static (string, List<string>) KeyRunText(Entry e)
    {
        var lines = new List<string>();
        var time = e.Seconds > 0 ? $" in **{Format.Clock(e.Seconds)}**" : "";
        lines.Add(e.Timed ? $"Timed{time}, the key goes up **+{e.Upgrades}**." : $"Finished{time}, over the timer.");
        if (e.Rating > 0) lines.Add($"Mythic+ rating: **{e.Rating}**.");
        AddGroup(e, lines);
        return (e.Timed ? $"\U0001F5DD️ {e.Char} timed {Format.KeyRun(e)}!" : $"{e.Char} finished {Format.KeyRun(e)}", lines);
    }

    // The team: this character first, then the rest of the party.
    static void AddGroup(Entry e, List<string> lines)
    {
        var members = e.Members;
        if (members.Count > 0)
        {
            lines.Add("");
            lines.Add("**Group:**");
            lines.Add($"{e.Char}, {e.Level} {e.Race} {e.Spec} {e.Class}".Replace("  ", " ").TrimEnd());
            lines.AddRange(members.Select(m => $"{m.Name}, {m.Level} {m.Race} {m.Spec} {m.Class}".Replace("  ", " ").TrimEnd()));
        }
        else if (e.GroupSize > 5)
        {
            lines.Add($"With a raid of {e.GroupSize}.");
        }
        else if (e.Level > 0)
        {
            lines.Add($"Solo at level {e.Level}.");
        }
    }

    // The item links to Wowhead, in the title (the embed's url) and in the text (unless the item's
    // picture is there, which shows the name already).
    (string, List<string>) LootText(Entry e, bool itemPicture)
    {
        var quality = Format.Quality(e.Quality);
        var lines = new List<string>();
        if (e.ItemId > 0 && !itemPicture) lines.Add($"**[{e.Item}]({ItemUrl(e.ItemId)})**");
        lines.Add(e.Zone.Length > 0 ? $"{quality} item, looted in **{e.Zone}**." : $"{quality} item.");
        return ($"{e.Char} looted [{e.Item}]", lines);
    }

    (string, List<string>) LegendaryText(Entry e)
    {
        var lines = new List<string>
        {
            $"\U0001F525 **{e.Char} just got their hands on a legendary item!** \U0001F525",
            "",
            e.ItemId > 0 ? $"**[{e.Item}]({ItemUrl(e.ItemId)})**" : $"**[{e.Item}]**",
        };
        if (e.Zone.Length > 0) lines.Add($"Looted in **{e.Zone}** at level {e.Level}.");
        return ($"\U0001F7E7 LEGENDARY! {e.Char} looted [{e.Item}]", lines);
    }

    // Like loot: the mount links to Wowhead (its item, or its spell), and the picture shows the name already.
    (string, List<string>) MountText(Entry e, bool itemPicture)
    {
        var lines = new List<string>();
        if (!itemPicture) lines.Add(ThingUrl(e) is { } url ? $"**[{e.Mount}]({url})**" : $"**{e.Mount}**");
        if (e.Level > 0) lines.Add($"At level {e.Level}.");
        return (e.Bought ? $"{e.Char} bought a mount!" : $"{e.Char} got a new mount!", lines);
    }

    // Hardcore deaths: who, what killed them (when known), and what they lose.
    static (string, List<string>) DeathText(Entry e)
    {
        var lines = new List<string>();
        var where = e.Zone.Length > 0 ? $" in **{e.Zone}**" : "";
        lines.Add(e.Killer.Length > 0
            ? $"Killed by **{e.Killer}**{where} at level {e.Level}."
            : $"Died{where} at level {e.Level}.");
        if (e.Played > 0) lines.Add($"Total played: **{Format.Played(e.Played)}**.");
        lines.Add($"Gold lost: **{Format.Money(e.Gold)}**.");
        return ($"\U0001F480 {e.Char} has died.", lines);
    }

    static (string, List<string>) DuelText(Entry e)
    {
        if (e.Lost) return ($"{e.Char} lost a duel to {e.Opponent}", [e.Fled ? "Ran away mid-duel." : $"Beaten by **{e.Opponent}**."]);
        var lines = new List<string> { e.Fled ? $"**{e.Opponent}** ran away mid-duel." : $"Beat **{e.Opponent}** in a duel." };
        if (e.OpponentText.Length > 0) lines.Add($"Opponent: {e.OpponentText}.");
        if (e.Zone.Length > 0) lines.Add($"In **{e.Zone}**, at level {e.Level}.");
        return ($"⚔️ {e.Char} won a duel against {e.Opponent}!", lines);
    }

    // Mak'gora: the hardcore duel to the death. The loser's character is gone for good.
    static (string, List<string>) MakgoraText(Entry e)
    {
        if (e.Lost) return ($"{e.Char} lost a Mak'gora to {e.Opponent}", [$"The duel to the death went to **{e.Opponent}**."]);
        var lines = new List<string> { $"☠️ **{e.Opponent}** challenged fate and lost. Their character is no more." };
        if (e.OpponentText.Length > 0) lines.Add($"Opponent: {e.OpponentText}.");
        if (e.Zone.Length > 0) lines.Add($"Fought in **{e.Zone}**, at level {e.Level}.");
        return ($"\U0001FA78 {e.Char} won a Mak'gora against {e.Opponent}!", lines);
    }

    // A battleground won (losses aren't posted).
    static (string, List<string>) BattlegroundText(Entry e)
    {
        var where = e.Instance.Length > 0 ? e.Instance : "a battleground";
        var lines = new List<string> { $"Victory in **{where}**" + (e.Level > 0 ? $", at level {e.Level}." : ".") };
        return ($"\U0001F6A9 {e.Char} won {where}!", lines);
    }

    // A profession's skill passing a step: "⛏️ Mining is now **152**."
    static (string, List<string>) ProfessionText(Entry e)
    {
        var lines = new List<string>();
        if (e.Rating > 0) lines.Add($"{Format.ProfessionsWithEmoji(e.Profession)} is now **{e.Rating}**.");
        if (e.Level > 0) lines.Add($"At level {e.Level}.");
        return ($"\U0001F6E0️ {e.Char} reached {e.Step} {e.Profession}!", lines);
    }

    static (string, List<string>) MPlusText(Entry e)
    {
        var lines = new List<string> { $"Mythic+ rating is now **{e.Rating}**." };
        if (e.Spec.Length > 0) lines.Add($"As {e.Spec}.");
        return ($"\U0001F5DD️ {e.Char} reached {e.Step} Mythic+ rating!", lines);
    }

    static (string, List<string>) PvpText(Entry e)
    {
        var lines = new List<string> { $"{e.Bracket} rating is now **{e.Rating}**." };
        if (e.Spec.Length > 0) lines.Add($"As {e.Spec}.");
        return ($"\U0001F3C5 {e.Char} reached {e.Step} in {e.Bracket}!", lines);
    }
}
