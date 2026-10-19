namespace WoWCharacterTracker.Models;

// A character's note and when it was last written (unix seconds). It's written in the app (the
// Notes tab) or in game (Edit in the /wct note box), and the newer one wins on both sides.
// Text is plain: "|" stays "|" (the addon's "||" is undone when the saved file is read).
record GameNote(string Guid, string Text, long At);
