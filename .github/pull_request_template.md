## What changed

## Release checklist
- [ ] Version bumped in the csproj, and a `## vX.Y.Z` section in CHANGELOG.md
- [ ] Anything in `addon/` changed? Raise `## Version` in the addon `.toc` (it has its own number)
- [ ] `dotnet test companion/WoWCharacterTracker.Tests` passes
- [ ] New Forever dungeon or raid out? Add its last boss to `END_BOSSES` in `addon/WoWCharacterTracker/Events.lua` (the comment above it says where to look)
