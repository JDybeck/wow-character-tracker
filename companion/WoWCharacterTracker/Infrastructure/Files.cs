namespace WoWCharacterTracker.Infrastructure;

// File chores that may fail without it mattering (the game or another program has the file open).
static class Files
{
    // Deletes the file if it's there; a file in use or not ours to delete is left alone.
    public static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
