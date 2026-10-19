namespace WoWCharacterTracker.Services.Discord;

// Posts to one webhook for one game tab: events (with an optional screenshot) and character cards.
// nameStyle: what posts call the character on Forever ("full", "first" or "last").
// standardPortrait: Forever characters (by GUID) the user set to the standard picture instead of the in-game one.
// known: the character as the app knows it (by GUID), whose region and realm fill in what an event
// leaves out, so posts get the same picture as the app (CharacterPicture).
sealed class DiscordPoster(string webhookUrl, Flavor flavor, string nameStyle = "full", Func<string, bool>? standardPortrait = null,
    Func<string, PictureSubject?>? known = null)
{
    readonly DiscordWebhook webhook = new(webhookUrl);
    readonly EventEmbeds embeds = new(flavor);
    // Test characters' surname is TESTCHAR; when the tab's name style leaves the surname out, it's added.
    static string Marked(string name, bool test) => test && !name.Contains("TESTCHAR") ? name + " TESTCHAR" : name;

    // Posts the event. Loot gets a picture of the item's tooltip (like Wowhead's) as the embed's
    // image. A screenshot is uploaded with the message: the embed's image, or a second embed under
    // the item picture.
    public async Task<bool> PostAsync(Entry e, bool isTest = false, string? screenshot = null)
    {
        var picture = await CharacterPicture.StandardAsync(flavor, SubjectOf(e));
        // A made-up test character says so in the name and under the post, so nobody takes it for real.
        var testChar = TestCharacters.IsTest(e.Guid);
        e = e with
        {
            Char = Marked(Format.Name(e.Char, e.Surname, nameStyle), testChar),
            Realm = testChar ? $"Test character, not a real one ({e.Realm})" : e.Realm,
        };
        var item = e.Kind == "loot" && e.ItemId > 0 ? await ItemTooltipImage.RenderAsync(e.ItemId, flavor) : null;
        var embed = embeds.Build(e, isTest, itemPicture: item is not null);
        if (picture is not null) embed["thumbnail"] = new { url = picture };

        var list = new List<Dictionary<string, object>> { embed };
        var files = new List<(byte[] Data, string Name)>();
        await AddPortraitAsync(embed, files, e.Guid);
        if (item is not null)
        {
            embed["image"] = new { url = "attachment://item.png" };
            files.Add((item, "item.png"));
        }
        if (screenshot is not null && !File.Exists(screenshot))
            Log.Warn($"The screenshot for this post was gone before it could be sent ({Path.GetFileName(screenshot)}), so it went out without one.");
        if (screenshot is null || !File.Exists(screenshot))
            return await webhook.SendAsync(list, files);

        var shotEmbed = embed;
        if (item is not null)
        {
            shotEmbed = new Dictionary<string, object>();
            if (embed.TryGetValue("color", out var color)) shotEmbed["color"] = color; // same stripe, so it reads as one post
            list.Add(shotEmbed);
        }

        // The full screenshot first (boosted servers take big files), then a smaller JPEG if
        // Discord says it's too large, and the post without it as the last resort.
        var image = await File.ReadAllBytesAsync(screenshot);
        var name = "screenshot" + Path.GetExtension(screenshot).ToLowerInvariant();
        shotEmbed["image"] = new { url = "attachment://" + name };
        var result = await webhook.SendOrReportAsync(list, [.. files, (image, name)]);
        if (result != DiscordWebhook.Result.TooLarge) return result == DiscordWebhook.Result.Sent;

        if (await Task.Run(() => ImageShrinker.ToJpegUnder(image, DiscordWebhook.FreeUploadLimit)) is { } smaller)
        {
            Log.Info($"The screenshot was too big for this Discord server ({image.Length / 1_000_000.0:0.#} MB), so a smaller copy was sent.");
            shotEmbed["image"] = new { url = "attachment://screenshot.jpg" };
            if (await webhook.SendOrReportAsync(list, [.. files, (smaller, "screenshot.jpg")]) is var r && r != DiscordWebhook.Result.TooLarge)
                return r == DiscordWebhook.Result.Sent;
        }
        if (shotEmbed == embed) embed.Remove("image");
        else list.Remove(shotEmbed);
        Log.Warn("The screenshot was too big for Discord even when shrunk, so the post went out without it.");
        return await webhook.SendAsync(list, files);
    }

    // A character card, sent from the character details in the app. With a rendered card image
    // (icons and all, see CardImage) that's the whole message; without one, the same parts as text.
    // hidden: the card parts the user turned off (MainViewModel.CardParts).
    public async Task<bool> PostCharacterAsync(CharacterRow c, IReadOnlyCollection<string> hidden, byte[]? card = null)
    {
        bool Shows(string part) => !hidden.Contains(part);
        var subtitle = $"Level {c.Level} {c.Description}".Trim();
        if (Shows("guild") && c.HasGuild) subtitle += $"\n<{c.Guild}>";
        var embed = CharacterEmbed(c, CharacterName(c) + (c.Dead ? " (dead)" : ""), subtitle);

        if (card is not null)
        {
            embed.Remove("description"); // the card says it already
            embed["image"] = new { url = "attachment://card.png" };
            return await webhook.SendAsync(embed, card, "card.png");
        }

        embed["fields"] = CardFields(c, Shows);
        return await SendWithPortraitAsync(embed, c);
    }

    // The character's talents: name, level, race and class, the points per tree, the talents picked
    // (Forever) and the build in Wowhead's talent calculator (the title links there too).
    public async Task<bool> PostTalentsAsync(CharacterRow c)
    {
        var url = Wowhead.TalentCalcUrl(flavor, c.ClassFile, c.TalentCalc, c.TalentExport);
        var lines = new List<string> { $"**Level {c.Level} {c.Description}**".Replace("  ", " ") };
        if (c.Talents.Length > 0) lines.Add(c.Talents);
        var embed = CharacterEmbed(c, $"{CharacterName(c)}'s talents", string.Join("\n", lines));
        if (url is not null) embed["url"] = url;
        var fields = new List<object>();
        // Discord fields take up to 1024 characters; long pick lists are cut at a whole talent.
        var picks = string.Join(", ", c.TalentPickList.Select(p => p.Text).Where(t => t.Length > 0));
        if (picks.Length > 1000) picks = picks[..picks.LastIndexOf(", ", 1000, StringComparison.Ordinal)] + "...";
        if (picks.Length > 0) fields.Add(new { name = "Picked", value = picks, inline = false });
        if (url is not null) fields.Add(new { name = "Wowhead", value = $"[Open in the talent calculator]({url})", inline = false });
        if (fields.Count > 0) embed["fields"] = fields;
        return await SendWithPortraitAsync(embed, c);
    }

    // Sends a text post with the character's picture as the thumbnail (see AddPortraitAsync).
    async Task<bool> SendWithPortraitAsync(Dictionary<string, object> embed, CharacterRow c)
    {
        var picture = await PictureAsync(c);
        if (picture is not null) embed["thumbnail"] = new { url = picture };
        var files = new List<(byte[] Data, string Name)>();
        await AddPortraitAsync(embed, files, c.Guid);
        return await webhook.SendAsync([embed], files);
    }

    // Every post's thumbnail is the picture the app shows for the character (CharacterRow.PictureUrl).
    // Forever's in-game portrait is a file on this PC, so it's uploaded with the message and replaces
    // the standard thumbnail. The sender is always the app's logo (DiscordWebhook).
    async Task AddPortraitAsync(Dictionary<string, object> embed, List<(byte[] Data, string Name)> files, string guid)
    {
        if (CharacterPicture.GamePortrait(flavor, guid, standardPortrait?.Invoke(guid) == true) is not { } own) return;
        try { files.Add((await File.ReadAllBytesAsync(own), "portrait.png")); }
        catch (IOException) { return; } // being replaced by a new check-in right now, the standard one will do
        embed["thumbnail"] = new { url = "attachment://portrait.png" };
    }

    string CharacterName(CharacterRow c) => Marked(Format.Name(c.Name, c.Surname, nameStyle), c.IsTest);

    Task<string?> PictureAsync(CharacterRow c) => CharacterPicture.StandardAsync(flavor, c.Picture);

    // The character an event is about, for its picture: as the app knows it when it does (events
    // often don't carry the region a Retail render needs), else from the event itself.
    PictureSubject SubjectOf(Entry e) =>
        known?.Invoke(e.Guid) ?? new PictureSubject(e.Guid, e.Region, e.Realm, e.Char, e.RaceFile, e.Sex, e.ClassFile);

    // What every post about a character has: the title, the class color stripe, the realm with the
    // class icon on top, and the footer.
    Dictionary<string, object> CharacterEmbed(CharacterRow c, string title, string description)
    {
        var embed = new Dictionary<string, object>
        {
            ["title"] = title,
            ["description"] = description,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
            ["footer"] = new { text = c.IsTest ? "Test character" : flavor.Name }, // DiscordWebhook adds the app's name and logo
        };
        if (CharacterArt.ClassColor(c.ClassFile) is int color) embed["color"] = color;
        if (CharacterArt.ClassIconUrl(c.ClassFile) is { } classIcon)
            embed["author"] = new Dictionary<string, object> { ["name"] = c.Realm, ["icon_url"] = classIcon };
        return embed;
    }

    // The card as text fields, for when the picture couldn't be drawn.
    List<object> CardFields(CharacterRow c, Func<string, bool> shows)
    {
        var fields = new List<object>();
        void Field(string part, string name, string value, bool inline = true)
        {
            if ((part.Length == 0 || shows(part)) && value.Length > 0 && value != "-") fields.Add(new { name, value, inline });
        }

        Field("", "Level", c.Level.ToString());
        Field("ilvl", "Item level", c.ItemLevelText);
        Field("played", "Total played", c.PlayedText);
        if (flavor.HasRulesets) Field("", "Ruleset", c.Ruleset);
        Field("gold", "Gold", c.HasMoney ? Format.Money(c.Money) : "");
        Field("crests", "Crests", Format.Crests(c.Crests));
        Field("professions", "Professions", Format.ProfessionsWithEmoji(c.Professions), inline: false);
        Field("talents", "Talents", c.TalentsText, inline: false);
        Field("zone", "Last seen", c.ZoneText != "-" ? $"{c.ZoneText}, {c.LastSeenText}" : c.LastSeenText, inline: false);
        if (c.Levels.Count > 0)
            Field("levels", "Recent levels", string.Join("\n", c.Levels.Take(3).Select(l => $"{l.LevelText}: **{l.Took}**")), inline: false);
        if (c.Highlights.Count > 0)
            Field("highlights", "Highlights", string.Join("\n", c.Highlights.Take(3).Select(h =>
                h.ItemId > 0 ? $"{h.Verb}[{h.Subject}]({embeds.ItemUrl(h.ItemId)})" : h.Text)), inline: false);
        return fields;
    }
}
