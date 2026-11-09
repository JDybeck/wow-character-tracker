using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Models;

// A stretch of incognito the addon kept (WoWCharacterTrackerDB.incognito, Shared.lua): nothing that
// happened from From to Until is posted, also when it arrives later with the saved file. Friends:
// its highlights weren't shared with friends either (the addon handles that).
sealed record IncognitoWindow(long From, long Until, bool Friends)
{
    public bool Covers(long at) => at >= From && at <= Until;

    public static bool Any(IEnumerable<IncognitoWindow> windows, long at) => windows.Any(w => w.Covers(at));

    // The saved list, odd entries skipped.
    public static List<IncognitoWindow> Read(DynValue list)
    {
        var out_ = new List<IncognitoWindow>();
        if (list.Type != DataType.Table) return out_;
        foreach (var p in list.Table.Pairs)
        {
            if (p.Value.Type != DataType.Table) continue;
            var t = p.Value.Table;
            var from = t.Get("from").CastToNumber() ?? 0;
            var until = t.Get("until").CastToNumber() ?? 0;
            if (from > 0 && until >= from && double.IsFinite(until))
                out_.Add(new((long)from, (long)until, t.Get("friends").CastToBool()));
        }
        return out_;
    }
}
