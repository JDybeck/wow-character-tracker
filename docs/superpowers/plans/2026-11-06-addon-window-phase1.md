# Addon window, phase 1 Implementation Plan

> Built by parallel agents in their own worktrees, merged by the main session. Each task below is self-contained: read only your task, the spec, and the files it names.

**Goal:** One addon window with tabs, opened from a minimap button with the WCT icon, holding every option of today's Options page. The Options page shrinks to a button. `/wct` opens the window. Location is on by default for friends.

**Spec:** `docs/superpowers/specs/2026-11-06-addon-window-design.md` (phase 1 = build order step 1). Open questions answered by default: a friend with sharing off still answers PING (phase 2), the icon ships as `.tga`, the short Options page stays.

## Global Constraints

- WoW addon Lua 5.1, no build step. CLAUDE.md taint rules: never replace Blizzard globals, nothing protected, no CVar changes in combat. Plain frames only (like `Ask.lua`, `NoteBox.lua`).
- Original code only: no code from other addons or libraries (no LibDBIcon, no LibStub).
- Read `.claude/notes/addon.md` first. `Options.lua` today builds one long page with helpers `text`, `checkbox`, `slider`, `heading`, `hint`, `checkGrid`, `checkTable` and a `refreshers` list run on show.
- Never use `|` in chat or UI text unless it's a real WoW code (`|t` and `|c` are codes).
- Tests: xUnit + MoonSharp in `companion/WoWCharacterTracker.Tests`. Run `dotnet test companion/WoWCharacterTracker.Tests`. The existing `AddonLuaTests.The_options_page_registers_and_lists_the_apps_settings` shows how to stub frames (`widget()` with a metatable); `FriendsLuaTests.Client` shows a fuller harness. Every addon file must compile (`Addon_file_compiles`), and the `.toc` must list every `.lua` file (`The_toc_has_every_file_and_loads_the_friend_files_after_what_they_use`).
- Commits: Conventional Commits, scope `addon` or `companion`, trailers `Co-Authored-By: Claude <noreply@anthropic.com>` and `Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0`. Commit as the repo's git user. Never push.
- Don't change the addon `.toc` `## Version` (the main session does that once). Don't touch files owned by the other task.
- Comments brief, no em dashes (use commas).

## Interfaces between the tasks

- Task A provides `ns.toggleWindow()`, `ns.openWindow(tab)` (tab key: `general`, `chat`, `highlights`, `note`, `friends`, `sharing`, `app`; nil = last open), `ns.windowShown()`.
- Task B provides `ns.minimapButtonShown() -> bool` and `ns.setMinimapButtonShown(on)`, saved in `WoWCharacterTrackerDB.minimap = { angle, hidden }`, and `ns.createMinimapButton()`.
- Task A's General tab has the checkbox "Show the minimap button" calling Task B's two functions, guarded with `if ns.setMinimapButtonShown then ... end` so A's tests work without B.
- Task B calls `ns.toggleWindow` on click, guarded the same way.
- Both add their new files to `WoWCharacterTracker.toc` (A: `Ui.lua` and `Window.lua` right before `Options.lua`; B: `Minimap.lua` right after `Options.lua`). The main session resolves that one-line conflict.

---

### Task A: the window and its tabs (agent A)

**Files:** create `addon/WoWCharacterTracker/Ui.lua` (the shared control helpers, moved out of Options.lua), create `addon/WoWCharacterTracker/Window.lua` (the window and its seven tabs), rewrite `addon/WoWCharacterTracker/Options.lua` (short page: title, version, one line, a button "Open the Character Tracker window" calling `ns.openWindow()`), modify `Commands.lua` (`/wct` alone opens the window; the level times move to `/wct times`; help lists `/wct times`), modify `WoWCharacterTracker.toc`, tests in a new `companion/WoWCharacterTracker.Tests/AddonWindowTests.cs`, and update `AddonLuaTests.The_options_page_registers_and_lists_the_apps_settings` to the new short page.

**Window:** a parentless-to-UIParent movable frame (about 720 x 560), dark background like the Ask box, title "WoW Character Tracker" and version, a close button (plain Button, not a Blizzard template that touches protected code), its name in `UISpecialFrames` so Esc closes it, position saved in `WoWCharacterTrackerDB.window = { point, relative, x, y, tab }`, clamped to screen, strata DIALOG. A row of tab buttons; each tab's content is a frame built on first open by its own builder function, with its own scroll frame when taller than the window. Opening the window runs that tab's refreshers (like Options.lua's OnShow).

**Tabs and contents** (move every control from today's Options.lua, same behavior, same `ns.editSettings`/DB writes, same labels and tips):
- General: Instant posting, Reload popup after a ding, Offer a reload on flight paths, Remind me in chat to /reload, Instant post pattern (corner button, label checkbox), Keep all history, Show the minimap button (Task B interface).
- Chat: Tell your guild or group (the two chat checkboxes and the kinds table, `ANNOUNCE_KINDS` as today, Retail vs Forever kinds via `ns.IS_RETAIL` as today).
- Highlights: Ask before a highlight goes unposted.
- Note: show, lock, buttons on mouseover, reset position, opacity slider.
- Friends: phase 2 fills it. For now: one line "Find and ask friends here soon. Until then: /wct friends help." and a button that prints `/wct friends` (calls `ns.friendsCommand("")`).
- Sharing: today's Share with Battle.net friends section (share on, this character, new characters, What friends get grid).
- App: today's "Set in the Character Tracker app" summary (read-only).

**Ui.lua helpers** take a parent frame instead of the global `page`: `ns.ui.text(parent, template, s)`, `ns.ui.checkbox(parent, label, tip, get, set)`, `ns.ui.slider(parent, label, get, set)`, `ns.ui.heading(parent, s, anchor, gap)`, `ns.ui.hint(parent, s, anchor)`, `ns.ui.checkGrid(parent, items, anchor, get, set)`, `ns.ui.checkTable(parent, items, columns, anchor, get, set)`, `ns.ui.button(parent, label, width, onClick)`. Each returns what today's returns and registers its refresher with the page it's on (a `refreshers` list per tab, `ns.ui.refresh(parent)` runs them).

**Tests (write first, watch them fail):**
- The window builds with all seven tabs, and opening a tab builds it once (`ns.openWindow('chat')` twice builds once).
- Every setting moved works the same: toggling Chat's guild checkbox calls `ns.editSettings` and sets `announce.guild`; General's Keep all history sets `keepAllHistory`; Instant posting sets `WoWCharacterTrackerDB.live`; Note's opacity calls `ns.setNoteAlpha`; Sharing's gold sets `share.defaults.gold` via `ns.editShare`.
- The minimap checkbox calls `ns.setMinimapButtonShown` when it exists and doesn't fail when it doesn't.
- `/wct` alone calls `ns.openWindow`, `/wct times` shows the level times (as `/wct` did), `/wct friends ...` still routes with its case.
- The window's position and last tab are saved in `WoWCharacterTrackerDB.window`.
- The short Options page registers and its button calls `ns.openWindow`.

**Commit:** `feat(addon): one window with tabs for every option, opened with /wct`.

---

### Task B: the minimap button and its icon (agent B)

**Files:** create `addon/WoWCharacterTracker/Minimap.lua`, create `addon/WoWCharacterTracker/Icon.tga` (64x64, 32-bit uncompressed TGA with alpha, made from `companion/WoWCharacterTracker/Assets/logo.png`), create `tools/make-addon-icon.ps1` (makes it: System.Drawing resize to 64x64, write the TGA header and BGRA rows bottom-up), modify `WoWCharacterTracker.lua` (create the button on `PLAYER_LOGIN`), modify `WoWCharacterTracker.toc`, modify `companion/WoWCharacterTracker/Services/AddonInstaller.cs` (binary files), tests in a new `companion/WoWCharacterTracker.Tests/MinimapButtonTests.cs` plus an installer test.

**Button:** a 31x31 `Button` parented to `Minimap`, round border look (a ring texture of our own drawing or the Blizzard minimap tracking border texture path used as a texture only, `Interface\\Minimap\\MiniMap-TrackingBorder`, which is a game file we reference, not ship), our icon inside (`Interface\\AddOns\\WoWCharacterTracker\\Icon`), positioned on the minimap's edge by `angle` (degrees, default 225), drag with the left button moves it around the edge (OnUpdate while dragging: angle from the cursor relative to the minimap center, `GetCursorPosition` / `UIParent:GetEffectiveScale()`), saved in `WoWCharacterTrackerDB.minimap`. Left click: `ns.toggleWindow()` (guarded). Tooltip on enter: "WoW Character Tracker" and "Click to open. Drag to move.". `ns.setMinimapButtonShown(false)` hides it and saves `hidden = true`.

**AddonInstaller:** today `Install` reads every bundled file as text (`ReadBundled` -> StreamReader) and writes it with `Files.WriteAtomic(path, string)`, which would break a `.tga`. Copy non-text files (anything not `.lua`, `.toc`, `.xml`, `.md`, `.txt`) as bytes. If `Files` has no byte overload of `WriteAtomic`, add one next to the string one (same temp-file-and-move), never a second way to write files. `Remove` and the Lua-cleanup loop stay as they are.

**Tests (write first, watch them fail):**
- MoonSharp with stubbed frames: the button is created once, at the saved angle; dragging sets a new angle and saves it; click calls `ns.toggleWindow` when present and doesn't fail without it; hide/show saves `hidden` and hides/shows the frame.
- The installer writes `Icon.tga` byte-identical to the embedded resource (install into a temp fake game folder as other installer tests do).
- The TGA file is 64x64, 32 bits, header 18 bytes, size 18 + 64*64*4.

**Commit:** `feat(addon): a minimap button with the wct icon`, and `fix(companion): install binary addon files as bytes` for the installer part.

---

### Task C: location on by default (main session)

`DEFAULTS.location = true` in `Friends.lua`, `GeneralCategories(Location = true)` in the app, tests updated first (`Allow` and defaults tests), texts (GUIDE, CHANGELOG, Options tips) say gear, talents and location on, gold off.

---

### After the merge (main session)

Resolve the `.toc` line conflict, run the full suite, screenshot-check the app (no app change beyond Task C), bump the addon `.toc` to 0.33.3, update `.claude/notes/addon.md` (Ui.lua, Window.lua, Minimap.lua) and GUIDE (the window and minimap button), whole-branch review, rebuild dist. In-game check by Jesper: every tab, the button (drag, hide, show), Esc, combat, the icon.
