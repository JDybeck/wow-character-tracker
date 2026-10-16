using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

sealed class WebhookRow(WebhookInfo info, bool isActive, bool isExpanded,
    IReadOnlyList<ToggleOption> games, IReadOnlyList<ToggleOption> rulesets, IReadOnlyList<ToggleOption> tags) : Observable
{
    public WebhookInfo Info { get; } = info;
    public string Name => Info.Name;
    public bool IsActive { get; } = isActive;

    // The options panel: which games this webhook posts for, which Forever rulesets, and which
    // character tags (none picked = every character).
    public IReadOnlyList<ToggleOption> Games { get; } = games;
    public IReadOnlyList<ToggleOption> Rulesets { get; } = rulesets;
    public IReadOnlyList<ToggleOption> Tags { get; } = tags;
    public bool HasTags => Tags.Count > 0;

    bool isExpanded = isExpanded;
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }

    // "Retail, Forever (Hardcore)" when collapsed, so the options show without opening them.
    public string Summary
    {
        get
        {
            var on = Games.Where(g => g.IsOn).Select(g => g.Name).ToList();
            if (on.Count == 0) return "Not posting anywhere";
            var rules = Rulesets.Where(r => r.IsOn).Select(r => r.Name).ToList();
            var forever = Flavors.Forever.Name;
            if (on.Contains(forever) && rules.Count < Rulesets.Count)
                on[on.IndexOf(forever)] = $"{forever} ({(rules.Count == 0 ? "no rulesets" : string.Join(", ", rules))})";
            var tagged = Tags.Where(t => t.IsOn).Select(t => t.Name).ToList();
            return "Posts for " + string.Join(", ", on) + (tagged.Count > 0 ? $", only {string.Join(", ", tagged)}" : "");
        }
    }

    bool isEditing;
    public bool IsEditing { get => isEditing; set => Set(ref isEditing, value); }

    string editName = info.Name;
    public string EditName { get => editName; set => Set(ref editName, value); }

    public void NameChanged() => Raise(nameof(Name));

    // https://discord.com/.../1234/AbCdE...WxYz
    public string Masked
    {
        get
        {
            try
            {
                var uri = new Uri(Info.Url);
                var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
                var token = parts[^1];
                var id = parts[^2];
                return token.Length > 9
                    ? $"{uri.Scheme}://{uri.Host}/.../{id}/{token[..5]}...{token[^4..]}"
                    : $"{uri.Scheme}://{uri.Host}/.../{id}/...";
            }
            catch (Exception) { return Info.Url.Length > 30 ? Info.Url[..30] + "..." : Info.Url; }
        }
    }
}
