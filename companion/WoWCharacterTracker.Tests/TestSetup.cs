using System.Runtime.CompilerServices;

namespace WoWCharacterTracker.Tests;

// Before any test: the app's data dir (config, caches like wowhead.json) points at a temp folder, so
// tests never read or write the real one.
static class TestSetup
{
    [ModuleInitializer]
    internal static void UseTempDataDir() =>
        Environment.SetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR", Path.Combine(Path.GetTempPath(), "WoWCharacterTracker.Tests"));
}
