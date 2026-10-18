namespace WoWCharacterTracker.ViewModels;

// Everything from the game a tab shows (items, crests, talents, enchants), one ItemInfo per Wowhead type
// and id, so each is looked up once (characters and the Items page share it). Ui/ItemTip shows any of
// them the same way. loaded: told when an ItemInfo has its name (the Items page re-sorts).
sealed class GameLookup(Flavor flavor, Action loaded)
{
    readonly Dictionary<(string Type, int Id), ItemInfo> infos = [];
    Dictionary<int, int> itemClasses = [];

    public ItemInfo Item(int id) => For("item", id);

    public ItemInfo For(string type, int id)
    {
        if (infos.TryGetValue((type, id), out var info)) return info;
        infos[(type, id)] = info = new ItemInfo(id, $"https://www.wowhead.com/{flavor.WowheadPath}{type}={id}", type);
        if (type == "item" && itemClasses.TryGetValue(id, out var c)) info.ClassId = c;
        _ = LoadAsync(info);
        return info;
    }

    async Task LoadAsync(ItemInfo info)
    {
        await info.LoadAsync(flavor);
        loaded();
    }

    // The game's item classes (Inventory.lua's itemClasses), for the Items page's type chips.
    public void SetItemClasses(Dictionary<int, int> classes)
    {
        itemClasses = classes;
        foreach (var (id, c) in classes)
            if (infos.TryGetValue(("item", id), out var known)) known.ClassId = c;
    }

    // An enchant, which the game gives as an id and a line of text: its Wowhead thing once found
    // (Services/Enchants), until then (or when nothing matches) Info stays null.
    public PendingInfo Enchant(int enchantId, string text)
    {
        var pending = new PendingInfo();
        _ = FindEnchantAsync(pending, enchantId, text);
        return pending;
    }

    async Task FindEnchantAsync(PendingInfo pending, int enchantId, string text)
    {
        if (await Enchants.FindAsync(enchantId, text, flavor) is { } hit) pending.Info = For(hit.Type, hit.Id);
    }
}

// A thing from the game whose Wowhead type and id are only known after a lookup (an enchant):
// bind ui:ItemTip.Info to its Info, which fills in when found.
sealed class PendingInfo : Observable
{
    ItemInfo? info;
    public ItemInfo? Info { get => info; set => Set(ref info, value); }
}
