# Addon window look (direction B)

Chosen 2026-11-07 from the three mockups in `docs/design/addon-window-mockups.html`. C (sidebar) goes in the backlog as an optional theme for the app and the addon.

## Goal

The addon window looks like the app and the Friends tab reads at a glance. Clicking a friend opens what's known about that character.

## Look

- Palette in `Ui.lua` (`ui.COLOR`): warm dark window, darker tab strip, cards with a thin border, amber accent. Plain color textures (`ui.box`), no backdrop template, so it's the same on Forever and Retail.
- Title bar with the addon icon (shared with the minimap button), tabs as a strip of outlined buttons, the open one amber-outlined.
- Buttons: `primary` (amber, the one thing to do: Find online friends, Ask, Invite, Allow), the rest outlined. Headings in amber.
- Checkboxes stay the game's own: they're readable and the settings tabs don't need a rewrite.

## Friends tab

- Requests as amber banners on top with Allow (primary), Not now, Never.
- Online friends as cards: class stripe, name in class color, a state tag (green when sharing, red when blocked), "level, zone, nickname (BattleTag)" below, buttons on the right.
- Shared with you: the friend's name, then a card per character with the one-line info.

## Details panel

Click a card (online or shared) to open a panel beside the window, click it again to close it. Esc closes it.

- Title: name, surname and realm in class color, then "Level 60 Frost Mage".
- Facts, each only when known: Friend, Race (with faction), Item level, Zone, Gold, Professions, Talents, Updated.
- Gear with icons, enchants and the game's item tooltips.
- An online friend: their shared character when they share the one they're on (tag + guid), else what Battle.net says, with a hint to ask them.

Replaces the gear panel and the hover tooltip on character lines.

## Not in this

The post box (`Ask.lua`), the note box and the Blizzard Options page stay as they are. Map tracking is next.
