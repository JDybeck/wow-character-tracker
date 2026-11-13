namespace WoWCharacterTracker.Models;

// A character's note and when it was last written (unix seconds). It's written in the app (the
// Notes tab) or in game (Edit in the /wct note box), and the newer one wins on both sides.
// Text is plain: "|" stays "|" (the addon's "||" is undone when the saved file is read).
record GameNote(string Guid, string Text, long At);

// A Battle.net friend's nickname (by BattleTag) and when it was set, the same way: in the app (the
// Friends page's sharing settings) or in game (the friend's sharing panel, /wct friends name). An
// empty name is a cleared one.
record GameNickname(string Tag, string Name, long At);
