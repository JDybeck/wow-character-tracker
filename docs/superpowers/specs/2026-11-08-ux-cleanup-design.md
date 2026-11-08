# UX cleanup: fewer buttons, easy setup, the fun up front

Date: 2026-11-08. Status: design, waiting for review. Mockup: `docs/ux/poc.png` (source `docs/ux/poc.html`). Where they differ, this spec wins: the mockup's Discord flyout on the rail became a quick toggle, and stream settings moved into Sharing.

## Why

The app and the addon both feel like control panels. Measured on 0.38.1:

- App: about 270 interactive controls in the XAML, 7 page tabs, 8 controls in the header, and a right column (setup checks, Discord, Stream overlay, Console) that takes a third of the window on every page, even when everything is set up.
- What gets posted alone has about 66 controls, 20 of them Test/Screenshot buttons.
- The same settings are edited in three places: the app's In game page, the addon window and the addon's Options page.
- Before setup, "you're not done" is said four times (install banner, setup chips, Discord warning, console).
- Addon: a 7-tab window, 9 slash commands, and three separate reload nags (ding popup, flight box, chat reminder every 30 min).
- The fun part (highlights, sharing them) is a small arrow inside an expanded character row.

## Goal

Clean, easy to use, and above all easy to set up. Fun to open and to share. Nothing is removed: rare options move behind Advanced instead of disappearing.

Success looks like:

- A new player goes from download to a first Discord post with only the setup helper, without reading anything else.
- After setup, the main view is about characters and moments, not settings.
- Each setting has exactly one place in the app and at most one in game.
- Fewer lines of view code than today, because merged pages share one source.

## The design

### 1. Shell: 4 tabs, a quiet header, a rail

- **Tabs:** Home, Characters, Friends, Sharing (was 7: Characters, Items, Professions, What gets posted, In game, Followed items, Friends).
- **Header:** logo and name, the Retail/Forever switch, a search box, "Synced N min ago" with a small refresh icon (replaces the big orange Sync now; it still syncs on click), a `?` menu and the gear.
  - The `?` menu holds: Help me set it up, What's new, Feedback, About, Check for updates.
  - The gear opens a **Settings page** (not a tab, reached from the gear), replacing today's settings menu. Sections: App (open with WoW, updates, animations, colors, games tracked), In game (reload reminders, keep all history, minimap button, data grid corner and label, under Advanced), Uninstall.
- **Search** ("Search characters and items"): one box in the header, from any page. Typing opens Characters on the matching view (a character name: List; anything else: Items with the search filled in). Reuses `ItemsViewModel`'s search, no second search.
- **Right rail:** a 52 px strip on the right edge, always visible, with quick toggles, not settings. Each icon has a status dot.
  - Discord: on/off for this game's posts (new `FlavorSettings.DiscordPaused`, see below). Dot: green on, grey paused, amber no webhook.
  - Guild chat: on/off (`AnnounceGuild`, as now).
  - Group chat: on/off (`AnnounceParty`, as now).
  - Stream overlay: on/off (`Config.StreamEnabled`, as now).
  - Console: the only icon that opens a panel: hovering for 300 ms or clicking slides `ConsolePanel` out over the page, leaving the rail and the panel hides it, a pin keeps it open (saved in `Config`). Dot amber when a warning or error came in since it was last opened.
  - Hovering a toggle shows a small tooltip with what it does and the current state ("Posting to #leveling"). Right-click or a link in the tooltip opens that section of Sharing.
  - Keyboard: each rail button is focusable and toggles on Enter/Space; Ctrl+` opens the console.
- **Big blockers stay big:** `SetupCard` (no game folder, no addon) still covers the page, but its button now opens the setup helper at that step. `SetupBar` and its placement setting (`Config.SetupPlacement`) go away: the rail dots and the helper replace them.

`DiscordPaused` is new because turning every webhook off would lose which ones were on. Paused means nothing for this game posts; like every other switched-off rule, what happens while paused is not posted later. It goes through `Tracker`'s existing `Wants` path (a check next to `Ignored`), not a second filter.

### 2. Home

Two columns, full width (no right column anymore):

- **Left, Playing now** while WoW for this tab is running (`GameProcesses`), else **Last played**. The character the stream overlay would show (`StreamSnapshot.Latest`), with portrait, level, class, zone, an XP bar with this level's time against the usual pace, and four stat tiles (item level, gold, played, today's duel record or levels today). The numbers come from the same `StreamSnapshot` the overlay uses, so the overlay and Home always agree.
- Under it, **This session**: levels, bosses, epics and posts since WoW started (from the events since the process start time).
- **Right, Recent moments**: all characters of this tab, newest first, the same kinds as character highlights plus level-ups. Each row: icon, what, who and when, and either "✓ Posted" or a **Share** button (posts to Discord, the existing "Post to Discord" command). Epic/legendary and max level get a highlighted row.
- Rows are `HighlightRow`s built by the code that builds the character details' highlights today (moved to one builder both use, not copied).
- "Posted" needs to know what went to Discord: `Tracker` already raises `Posted`; the ids are kept in the state file next to the handled ids (a new `posted` list, capped like the handled list). It's in the existing state file, so Uninstall already covers it.
- Empty state (no characters yet): one card that says what will show up here, with "Help me set it up" when setup isn't done.

### 3. Characters: one tab, four views

A segmented switch at the top: **List, Items, Professions, Followed**. A switch, not a second row of tabs, so it doesn't look like the character card's own tabs (Overview, Gear and bags, Leveling, Notes).

- The existing pages (`CharactersPage`, `ItemsPage`, `ProfessionsPage`, `FollowedItemsPage`) and their view models move under Characters unchanged. No rebuild.
- The realm/ruleset/faction chips stay where each view has them today.
- Clicking the open tab again still starts it over (`FlavorViewModel.ResetPage`), now also back to List.
- `ProfessionsView` stays the one view shared by Professions and Friends' professions.

### 4. Sharing: one page for everything that tells others

Replaces What gets posted, In game, and the Stream overlay panel's settings.

Top to bottom:

1. **Where:** three cards with an on/off each: Discord (with the webhook list and Add/Send test, today's `DiscordPanel` content), Guild and group chat (one card, two switches), Stream overlay (address to copy plus a **Layout** button that opens today's stream options: card fields, sizes, colors, highlight and alert kinds).
2. **How much gets shared:** three presets, then Custom.
   - **Quiet:** every 10th level and max level, epic and legendary loot. Asks before skipping a boss kill or a rare.
   - **Normal** (default for new users): the game's default level rule (`Flavor.Defaults`: Forever every 5 to 30 then every level), max level, epic loot, end bosses (first kill only), mounts, Mak'gora, hardcore deaths, profession milestones, followed items. Screenshots for levels, bosses and deaths. Asks before skipping a boss kill or a rare.
   - **Everything:** every kind on, every level, rares (BoE only stays on), screenshots on for every kind that has them, no asking (nothing is skipped).
   - On Retail, end bosses are Mythic+ runs (+10 and up) and the rating kinds are on in Normal; deaths and Mak'gora don't show. `EventKinds.OfferedIn` already decides this.
   - A preset sets the Discord, screenshot, guild/group and ask columns of this game. It does not change the stream columns (the overlay is shared by both games) and never touches webhooks, the destination toggles, followed items or names.
   - The page shows Custom when the settings match no preset. Picking a preset over Custom asks first ("Replace your custom choices?").
3. **Customize** (folded, open when Custom): **one table, one row per moment, one column per place:** Discord, Screenshot, Guild, Group, Ask first, Stream alert. Cells a kind doesn't support show a dash. Under a row's name, its one-line rule ("every 5 to 30, then every level", "Bind on Equip only"). Hovering a row shows **Test** at its end; that's the only Test button.
4. **Advanced** (folded): level rule numbers, profession steps, rating steps, key run minimum, first kill only, rare BoE only, group skip regex, name in posts, hide chat/UI in screenshots, ask box time, copy/paste/reset settings.

One source for the table: `Models/EventKinds` grows into the catalog every list reads. Each `EventKind` gets its display name, its guild chat sample line (today's `FlavorViewModel.AnnounceNames`), its stream names (today's `StreamViewModel.KindNames`/`ForeverKindNames`/`RetailKindNames`) and which columns it has. The kinds that aren't addon event kinds but are listed today (max level, epic, rare, followed, legendary) become entries with a flag, so `OfferedIn` keeps working. Those three name tables are deleted. `SharingRow` (one view model per row) replaces `AnnounceRow`, `ShotOption` and the posting checkboxes, and binds to the existing `FlavorSettings` fields; no settings move or get renamed, so configs and Settings.lua stay compatible.

Presets live in one place, `Models/SharingPresets`, as functions that edit a `FlavorSettings` (the way `CopyPostingFrom` and `Flavor.Defaults` work), plus `Matches(FlavorSettings)` for showing Custom. Unit tested.

### 5. Help me set it up

Replaces the 10-step `IntroTour`. It does the setup instead of describing the window. Four steps, each with Back, Skip and a primary action:

1. **Games:** shows the folders found per game, change or turn a game off.
2. **Addon:** Install (or "installed, version x") per game.
3. **Discord:** paste a webhook and an optional name, "Send test and continue" (the existing add-webhook form and Send test, reused), or "Skip, no Discord".
4. **How chatty:** the three presets, plus guild chat on/off.

A last screen points at the rail ("quick switches live here") and Home. The helper opens on first run (`Config.WantsIntro` keeps its meaning), from the `?` menu, and from `SetupCard`'s button at the step that's missing. `IntroDone` stays the flag.

### 6. Addon

**Window, 7 tabs to 4:**

- **Sharing:** the preset (Quiet / Normal / Everything / Custom, Custom only shown when it is), guild chat and group chat on/off, "Ask before skipping" on/off, and a line saying the per-moment table is in the app.
- **Friends:** today's Friends and Sharing tabs together (friends list on top, what you share below).
- **Note:** as now.
- **Settings:** instant posting, reload reminders, minimap button, then Advanced: data grid corner and label, keep all history, ignore this character.

The 11x2 chat grid and the ask kinds list leave the addon window (they're in the app's table). Without the app (`ns.hasApp()` false) the Sharing tab shows only guild/group on/off and the full chat grid, since there's no app table to point to.

**Reload reminders, three checkboxes to one choice:** Off / Gentle / Every chance.

- Off: no ding popup, no flight box, no chat reminder.
- Gentle (default): the chat reminder only.
- Every chance: all three.
- It's a view over the existing keys (`popup`, `flightReload`, `chatReminder`), no new saved key. Any other mix shows as "Custom" with the three checkboxes under Advanced.

**Preset in game:** a new In game key `preset` ("quiet", "normal", "everything"). Picking one in game writes it through `ns.editSettings`; the app takes it on sync like the other In game keys (`IN_GAME`, `InGameSettings`, `SavedVariables.ReadInGame`, `FlavorSettings.TakeInGame`, `AddonInstaller.WriteSettings`) and applies `SharingPresets`. The addon never applies a preset itself: posting rules stay app-owned.

**Options page** (Esc, Options, AddOns): one line and an "Open the Character Tracker window" button. Its own copy of settings goes away.

**Slash commands:** `/wct help` lists four: `/wct`, `/wct note`, `/wct friends`, `/wct test`. The others keep working.

**Popups:** the ask box stays (it's about the fun part). The friend request box stays. The app update line stays (once per login).

## What goes away

- App: `PostingPage`, `InGamePage`, `SetupBar`, `IntroTour` steps, `Config.SetupPlacement`, the settings popup menu, the three kind-name tables, `AnnounceRow`, `ShotOption`, the right column layout code (`MainWindow.FitRightColumn`).
- Addon: the General/Chat/Highlights/App tab builders as they are (their controls move), the Options page's settings.
- Each removal is replaced, not dropped: every setting reachable today is reachable after, checked against the list in "Settings map" below.

## Settings map (every setting keeps a home)

| Today | After |
|---|---|
| What gets posted: per-kind on/off, screenshot, post screenshot | Sharing table: Discord, Screenshot columns |
| Level rule, profession/rating steps, key min, first kill, rare BoE, regex, name style | Sharing, Advanced |
| Hide chat/UI in shots, copy/paste/reset | Sharing, Advanced |
| In game: guild/group on/off and kinds | Sharing: Where card + Guild/Group columns; rail toggles |
| In game: ask kinds, box time | Sharing: Ask first column; Advanced |
| In game: flight reload, chat reminder | Settings page, In game: Reload reminders |
| In game: keep all history, grid corner/label | Settings page, In game, Advanced |
| Stream panel on/off, address | Sharing: Stream card; rail toggle |
| Stream options (fields, sizes, colors, kinds) | Sharing: Stream card, Layout |
| Discord webhooks, add, edit, remove, per-game on, rulesets/tags | Sharing: Discord card (today's `DiscordPanel`) |
| Settings menu items | Settings page |
| Setup checks | Rail dots, `SetupCard`, the helper |

## Shared parts (one of each)

- `EventKinds`: the only list of moment kinds and their names, for the table, presets, stream and announcements.
- `SharingPresets`: the only definition of a preset, used by the Sharing page, the helper and the addon's `preset` key.
- One highlight row builder for Home and the character details.
- `StreamSnapshot` for Home's character card and the overlay.
- `DiscordPanel` content hosted in the Sharing page and the helper's Discord step (one view, two places).
- The rail is one `RailButton` style plus a list of toggles, not four hand-made buttons.
- Addon: `ui.*` helpers as now; one `ui.choice` (segmented choice) for both the preset and reload reminders.

## Phases (one PR each, release after all are merged)

1. **Catalog and Sharing page:** `EventKinds` catalog, `SharingPresets`, `SharingRow`, the Sharing page replacing What gets posted and In game, `DiscordPaused`. The old right column stays for now.
2. **Shell:** 4 tabs, Characters views, header with search and `?` menu, Settings page, the rail (replaces the right column and `SetupBar`).
3. **Home.**
4. **Setup helper** (replaces `IntroTour`).
5. **Addon:** 4-tab window, reload choice, `preset` key, Options page, help text. Addon version bump.

Each phase leaves the app working and shippable on its own.

## Testing

- Unit tests: `SharingPresets` (each preset per game, `Matches`, Custom), the catalog (every kind the addon records is in `EventKinds`, every stream kind too; replaces the `StreamTests` cross-check), `DiscordPaused` in `Wants`, the `preset` key in `TakeInGame`, the reload choice mapping (Lua, MoonSharp).
- `preset` is an In game setting read from the saved file, not a live key, so `SHORT`/`Payload.ShortKeys` don't change.
- Screenshot mode: new views `home`, `sharing`, `sharing:custom`, `settings`, `setup:N`, `characters:items` etc. Rendered before/after each phase at 1320x900 and at the 1100x680 minimum.
- Jesper in game: the addon window, the reload choice, picking a preset in game and seeing it in the app after /reload.

## Out of scope

- New features (new moment kinds, new destinations).
- Changing the look (themes, fonts, the character rows).
- Retail-specific redesign beyond what `EventKinds.OfferedIn` already does.

## Risks

- Existing users lose muscle memory: What gets posted and In game are gone. The What's new entry says where things went, with a picture.
- Hover-to-open on the console can open by accident; mitigated by the 300 ms delay and the console being the only flyout.
- Preset definitions are a product choice; the numbers above are a first guess and easy to change in one file.
