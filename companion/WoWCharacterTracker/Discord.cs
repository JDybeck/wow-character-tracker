using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker;

partial class Discord(string webhookUrl, Flavor flavor, string nameStyle = "full")
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) }; // screenshots take a moment to upload

    // Class colors as the game uses them (RAID_CLASS_COLORS).
    public static readonly Dictionary<string, int> ClassColors = new()
    {
        ["WARRIOR"] = 0xC69B6D, ["PALADIN"] = 0xF48CBA, ["HUNTER"] = 0xAAD372, ["ROGUE"] = 0xFFF468,
        ["PRIEST"] = 0xFFFFFF, ["DEATHKNIGHT"] = 0xC41E3A, ["SHAMAN"] = 0x0070DD, ["MAGE"] = 0x3FC7EB,
        ["WARLOCK"] = 0x8788EE, ["MONK"] = 0x00FF98, ["DRUID"] = 0xFF7C0A, ["DEMONHUNTER"] = 0xA330C9,
        ["EVOKER"] = 0x33937F,
    };

    const string IconBase = "https://wow.zamimg.com/images/wow/icons/large/";
    const string AvatarUrl = "https://raw.githubusercontent.com/JDybeck/wow-character-tracker/main/companion/WoWCharacterTracker/Assets/logo.png";

    // Remembers which portrait URLs exist so a new race we don't know falls back to the class icon.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UrlExists = new();

    public static bool LooksLikeWebhook(string url) => WebhookPattern().IsMatch(url.Trim());

    // Asks Discord about the webhook. Returns its display name, or null with a reason.
    public static async Task<(string? Name, string? Problem)> CheckAsync(string url)
    {
        try
        {
            var res = await Http.GetAsync(url.Trim());
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                return (null, "Discord says this webhook doesn't exist. It may have been deleted, or the link was cut off when copying.");
            if (!res.IsSuccessStatusCode)
                return (null, $"Discord answered with an error ({(int)res.StatusCode}). Try again in a minute.");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            return (name ?? "your webhook", null);
        }
        catch (HttpRequestException)
        {
            return (null, "Couldn't reach Discord. Check your internet connection.");
        }
        catch (TaskCanceledException)
        {
            return (null, "Discord took too long to answer. Check your internet connection.");
        }
    }

    // Discord's upload limit for webhooks without a boosted server.
    const long MaxUpload = 9_500_000;

    // Posts the event. With a screenshot, it's uploaded with the message and shown as the embed's image.
    public async Task<bool> PostAsync(Entry e, bool isTest = false, string? screenshot = null)
    {
        e = e with { Char = Format.Name(e.Char, e.Surname, nameStyle) };
        var embed = (Dictionary<string, object>)await BuildEmbedAsync(e, isTest);
        byte[]? image = null;
        var imageName = "";
        if (screenshot is not null && new FileInfo(screenshot) is { Exists: true } fi && fi.Length <= MaxUpload)
        {
            image = await File.ReadAllBytesAsync(screenshot);
            imageName = "screenshot" + fi.Extension.ToLowerInvariant();
            embed["image"] = new { url = "attachment://" + imageName };
        }
        return await SendAsync(embed, image, imageName, await PictureUrlAsync(e.RaceFile, e.Sex, e.ClassFile));
    }

    // A character card, sent from the character details in the app. With a rendered card image
    // (icons and all, see CardImage) that's the whole message; without one, the same parts as text.
    // hidden: the card parts the user turned off (MainViewModel.CardParts).
    public async Task<bool> PostCharacterAsync(CharacterRow c, IReadOnlyCollection<string> hidden, byte[]? card = null)
    {
        bool Shows(string part) => !hidden.Contains(part);
        var subtitle = $"Level {c.Level} {c.Description}".Trim();
        if (Shows("guild") && c.HasGuild) subtitle += $"\n<{c.Guild}>";
        var embed = new Dictionary<string, object>
        {
            ["title"] = Format.Name(c.Name, c.Surname, nameStyle) + (c.Dead ? " (dead)" : ""),
            ["description"] = subtitle,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
            ["footer"] = new { text = c.IsTest ? "Test character, Character Tracker" : $"Character Tracker, {flavor.Name}" },
        };
        if (ClassColors.TryGetValue(c.ClassFile.ToUpperInvariant(), out var color)) embed["color"] = color;
        if (ClassIconUrl(c.ClassFile) is { } classIcon)
            embed["author"] = new Dictionary<string, object> { ["name"] = c.Realm, ["icon_url"] = classIcon };

        if (card is not null)
        {
            embed.Remove("description"); // the card says it already
            embed["image"] = new { url = "attachment://card.png" };
            return await SendAsync(embed, card, "card.png", await PictureUrlAsync(c.RaceFile, c.Sex, c.ClassFile));
        }

        var picture = await PictureUrlAsync(c.RaceFile, c.Sex, c.ClassFile);

        var fields = new List<object>();
        void Field(string part, string name, string value, bool inline = true)
        {
            if (Shows(part) && value.Length > 0 && value != "-") fields.Add(new { name, value, inline });
        }

        Field("", "Level", c.Level.ToString());
        Field("ilvl", "Item level", c.ItemLevelText);
        Field("played", "Total played", c.PlayedText);
        if (flavor == Flavors.Forever) Field("", "Ruleset", c.Ruleset);
        Field("gold", "Gold", c.HasMoney ? Format.Money(c.Money) : "");
        Field("crests", "Crests", Format.Crests(c.Crests));
        Field("professions", "Professions", Format.ProfessionsWithEmoji(c.Professions), inline: false);
        Field("talents", "Talents", c.TalentsText, inline: false);
        Field("zone", "Last seen", c.ZoneText != "-" ? $"{c.ZoneText}, {c.LastSeenText}" : c.LastSeenText, inline: false);

        if (c.Levels.Count > 0)
        {
            var recent = c.Levels.Take(3).Select(l => $"{l.LevelText}: **{l.Took}**");
            Field("levels", "Recent levels", string.Join("\n", recent), inline: false);
        }
        if (c.Highlights.Count > 0)
            Field("highlights", "Highlights", string.Join("\n", c.Highlights.Take(3).Select(h =>
                h.ItemId > 0 ? $"{h.Verb}[{h.Subject}]({ItemUrl(h.ItemId)})" : h.Text)), inline: false);

        embed["fields"] = fields;
        return await SendAsync(embed, null, "", picture);
    }

    string ItemUrl(int itemId) => $"https://www.wowhead.com/{flavor.WowheadPath}item={itemId}";

    async Task<bool> SendAsync(Dictionary<string, object> embed, byte[]? image, string imageName, string? avatar)
    {
        var body = new Dictionary<string, object>
        {
            ["avatar_url"] = avatar ?? AvatarUrl, // the character's portrait (or the tracker's logo) instead of the webhook's picture
            ["embeds"] = new[] { embed },
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };
        var json = JsonSerializer.Serialize(body);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage res;
            try
            {
                if (image is null)
                {
                    res = await Http.PostAsync(webhookUrl, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                }
                else
                {
                    // A fresh form each attempt, since sending disposes it.
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(json, System.Text.Encoding.UTF8, "application/json"), "payload_json");
                    var file = new ByteArrayContent(image);
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                        imageName.EndsWith(".png") ? "image/png" : "image/jpeg");
                    form.Add(file, "files[0]", imageName);
                    res = await Http.PostAsync(webhookUrl, form);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log.Warn("Couldn't reach Discord. Will try again on the next sync.");
                return false;
            }

            if (res.IsSuccessStatusCode) return true;

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
                continue;
            }

            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                Log.Error("The Discord webhook no longer works (was it deleted?). Add a new one in the Discord panel.");
            else
                Log.Warn($"Discord returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            return false;
        }

        return false;
    }

    static readonly Dictionary<int, int> QualityColors = new()
    {
        [3] = 0x0070DD, [4] = 0xA335EE, [5] = 0xFF8000, [6] = 0xE6CC80,
    };

    async Task<object> BuildEmbedAsync(Entry e, bool isTest)
    {
        var (title, lines) = e.Kind switch
        {
            "boss" => BossText(e),
            "loot" => LootText(e),
            "mount" => MountText(e),
            "death" => DeathText(e),
            "duel" => DuelText(e),
            "makgora" => MakgoraText(e),
            "mplus" => MPlusText(e),
            "pvp" => PvpText(e),
            _ when e.MaxLevel => MaxLevelText(e),
            _ => LevelText(e),
        };
        if (e.Kind == "loot" && e.Quality >= 5) (title, lines) = LegendaryText(e);

        var embed = new Dictionary<string, object>
        {
            ["title"] = title,
            ["description"] = string.Join("\n", lines),
            ["timestamp"] = DateTimeOffset.FromUnixTimeSeconds(e.At).ToString("o"),
        };

        if (e.MaxLevel)
            embed["color"] = 0xFFD100; // gold, it's a big one
        else if (e.Kind == "loot" && QualityColors.TryGetValue(e.Quality, out var qc))
            embed["color"] = qc;
        else if (e.Kind is "death" or "makgora")
            embed["color"] = 0x8B0000;
        else if (e.Kind == "mplus")
            embed["color"] = 0xA335EE;
        else if (e.Kind == "pvp")
            embed["color"] = 0xC41E3A;
        else if (ClassColors.TryGetValue(e.ClassFile.ToUpperInvariant(), out var color))
            embed["color"] = color;

        if (e.Kind is "loot" or "mount" && e.ItemId > 0)
            embed["url"] = ItemUrl(e.ItemId);

        var classIcon = ClassIconUrl(e.ClassFile);

        // "Human Fury Warrior", leaving out whatever we don't know.
        var who = string.Join(" ", new[] { e.Race, e.Spec, e.Class }.Where(s => s.Length > 0));
        if (who.Length > 0)
        {
            var author = new Dictionary<string, object> { ["name"] = who };
            if (classIcon is not null) author["icon_url"] = classIcon;
            embed["author"] = author;
        }

        var picture = await PictureUrlAsync(e.RaceFile, e.Sex, e.ClassFile);
        if (picture is not null) embed["thumbnail"] = new { url = picture };

        var footer = isTest ? "Test message from Character Tracker" : e.Realm;
        if (footer.Length > 0) embed["footer"] = new { text = footer };

        return embed;
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
        if (e.Professions.Length > 0 || e.Talents.Length > 0) lines.Add("");
        if (e.Professions.Length > 0) lines.Add($"**Professions:** {Format.ProfessionsWithEmoji(e.Professions)}");
        if (e.Talents.Length > 0) lines.Add($"**Talents:** {e.Talents}");
        return ($"\U0001F3C6 {e.Char} hit max level {e.Level}!", lines);
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

    // The owner's format: who dinged, how long it took, played, where, professions, talents.
    static (string, List<string>) LevelText(Entry e)
    {
        var lines = new List<string> { $"{e.Level - 1} to {e.Level} took **{Format.Duration(e.Seconds)}**." };
        if (e.Played > 0) lines.Add($"Total played: **{Format.Played(e.Played)}**.");
        if (e.Zone.Length > 0) lines.Add($"They dinged in **{e.Zone}**.");
        if (e.Professions.Length > 0 || e.Talents.Length > 0) lines.Add("");
        if (e.Professions.Length > 0) lines.Add($"**Professions:** {Format.ProfessionsWithEmoji(e.Professions)}");
        if (e.Talents.Length > 0) lines.Add($"**Talents:** {e.Talents}");
        return ($"{e.Char} just dinged level {e.Level}.", lines);
    }

    static (string, List<string>) BossText(Entry e)
    {
        var lines = new List<string>();
        if (e.Instance.Length > 0) lines.Add($"Final boss of **{e.Instance}** is down.");

        // The team: this character first, then the rest of the party.
        var members = e.Members;
        if (members.Count > 0)
        {
            lines.Add("");
            lines.Add("**Group:**");
            lines.Add($"{e.Char}, {e.Level} {e.Race} {e.Class}".TrimEnd());
            lines.AddRange(members.Select(m => $"{m.Name}, {m.Level} {m.Race} {m.Class}".TrimEnd()));
        }
        else if (e.GroupSize > 5)
        {
            lines.Add($"With a raid of {e.GroupSize}.");
        }
        else if (e.Level > 0)
        {
            lines.Add($"Solo at level {e.Level}.");
        }
        return ($"{e.Char} killed {e.Boss}!", lines);
    }

    (string, List<string>) LootText(Entry e)
    {
        var lines = new List<string>();
        var quality = Format.Quality(e.Quality);
        lines.Add(e.Zone.Length > 0 ? $"{quality} item, looted in **{e.Zone}**." : $"{quality} item.");
        return ($"{e.Char} looted [{e.Item}]", lines);
    }

    (string, List<string>) MountText(Entry e)
    {
        var lines = new List<string> { e.ItemId > 0 ? $"**[{e.Mount}]({ItemUrl(e.ItemId)})**" : $"**{e.Mount}**" };
        if (e.Level > 0) lines.Add($"At level {e.Level}.");
        return (e.Bought ? $"{e.Char} bought a mount!" : $"{e.Char} got a new mount!", lines);
    }

    static (string, List<string>) DuelText(Entry e)
    {
        var lines = new List<string> { e.Fled ? $"**{e.Opponent}** ran away mid-duel." : $"Beat **{e.Opponent}** in a duel." };
        if (e.Zone.Length > 0) lines.Add($"In **{e.Zone}**, at level {e.Level}.");
        return ($"⚔️ {e.Char} won a duel against {e.Opponent}!", lines);
    }

    // Mak'gora: the hardcore duel to the death. The loser's character is gone for good.
    static (string, List<string>) MakgoraText(Entry e)
    {
        var lines = new List<string> { $"☠️ **{e.Opponent}** challenged fate and lost. Their character is no more." };
        if (e.Zone.Length > 0) lines.Add($"Fought in **{e.Zone}**, at level {e.Level}.");
        return ($"\U0001FA78 {e.Char} won a Mak'gora against {e.Opponent}!", lines);
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

    // Hardcore deaths: who, what killed them, and what they lose.
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

    public static string? ClassIconUrl(string classFile) =>
        classFile.Length > 0 ? $"{IconBase}classicon_{classFile.ToLowerInvariant()}.jpg" : null;

    // The race portrait matches the character best; the class icon is the fallback.
    public static async Task<string?> PictureUrlAsync(string raceFile, string sex, string classFile)
    {
        if (raceFile.Length > 0)
        {
            var portrait = $"{IconBase}race_{raceFile.ToLowerInvariant()}_{sex}.jpg";
            if (await ExistsAsync(portrait)) return portrait;
        }
        return ClassIconUrl(classFile);
    }

    static async Task<bool> ExistsAsync(string url)
    {
        if (UrlExists.TryGetValue(url, out var known)) return known;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await Http.SendAsync(req);
            return UrlExists[url] = res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false; // don't cache, it may just be a hiccup
        }
    }

    [GeneratedRegex(@"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[\w-]+/?$")]
    private static partial Regex WebhookPattern();
}
