namespace WoWCharacterTracker.Services.Discord;

// Posts to one webhook for one game tab: events (with an optional screenshot) and character cards.
// nameStyle: what posts call the character on Forever ("full", "first" or "last").
sealed class DiscordPoster(string webhookUrl, Flavor flavor, string nameStyle = "full")
{
    readonly DiscordWebhook webhook = new(webhookUrl);
    readonly EventEmbeds embeds = new(flavor);

    // Posts the event. Loot gets a picture of the item's tooltip (like Wowhead's) as the embed's
    // image. A screenshot is uploaded with the message: the embed's image, or a second embed under
    // the item picture.
    public async Task<bool> PostAsync(Entry e, bool isTest = false, string? screenshot = null)
    {
        e = e with { Char = Format.Name(e.Char, e.Surname, nameStyle) };
        var item = e.Kind == "loot" && e.ItemId > 0 ? await ItemTooltipImage.RenderAsync(e.ItemId, flavor) : null;
        var embed = await embeds.BuildAsync(e, isTest, itemPicture: item is not null);
        var avatar = await CharacterArt.PictureUrlAsync(flavor, e.Region, e.Realm, e.Char, e.RaceFile, e.Sex, e.ClassFile);

        var list = new List<Dictionary<string, object>> { embed };
        var files = new List<(byte[] Data, string Name)>();
        if (item is not null)
        {
            embed["image"] = new { url = "attachment://item.png" };
            files.Add((item, "item.png"));
        }
        if (screenshot is null || !File.Exists(screenshot))
            return await webhook.SendAsync(list, files, avatar);

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
        var result = await webhook.SendOrReportAsync(list, [.. files, (image, name)], avatar);
        if (result != DiscordWebhook.Result.TooLarge) return result == DiscordWebhook.Result.Sent;

        if (await Task.Run(() => ImageShrinker.ToJpegUnder(image, DiscordWebhook.FreeUploadLimit)) is { } smaller)
        {
            Log.Info($"The screenshot was too big for this Discord server ({image.Length / 1_000_000.0:0.#} MB), so a smaller copy was sent.");
            shotEmbed["image"] = new { url = "attachment://screenshot.jpg" };
            if (await webhook.SendOrReportAsync(list, [.. files, (smaller, "screenshot.jpg")], avatar) is var r && r != DiscordWebhook.Result.TooLarge)
                return r == DiscordWebhook.Result.Sent;
        }
        if (shotEmbed == embed) embed.Remove("image");
        else list.Remove(shotEmbed);
        Log.Warn("The screenshot was too big for Discord even when shrunk, so the post went out without it.");
        return await webhook.SendAsync(list, files, avatar);
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
        if (CharacterArt.ClassColors.TryGetValue(c.ClassFile.ToUpperInvariant(), out var color)) embed["color"] = color;
        if (CharacterArt.ClassIconUrl(c.ClassFile) is { } classIcon)
            embed["author"] = new Dictionary<string, object> { ["name"] = c.Realm, ["icon_url"] = classIcon };
        var picture = await CharacterArt.PictureUrlAsync(flavor, c.Region, c.Realm, c.Name, c.RaceFile, c.Sex, c.ClassFile);

        if (card is not null)
        {
            embed.Remove("description"); // the card says it already
            embed["image"] = new { url = "attachment://card.png" };
            return await webhook.SendAsync(embed, card, "card.png", picture);
        }

        if (picture is not null) embed["thumbnail"] = new { url = picture };
        embed["fields"] = CardFields(c, Shows);
        return await webhook.SendAsync(embed, null, "", picture);
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
        if (flavor == Flavors.Forever) Field("", "Ruleset", c.Ruleset);
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
