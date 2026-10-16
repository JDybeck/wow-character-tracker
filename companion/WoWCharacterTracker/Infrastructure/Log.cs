namespace WoWCharacterTracker.Infrastructure;

enum LogLevel { Info, Success, Warn, Error }

record LogLine(DateTime Time, LogLevel Level, string Text)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

// Everything the app wants to tell the user ends up in the console panel through here.
static class Log
{
    public static event Action<LogLine>? Written;

    public static void Info(string text) => Write(LogLevel.Info, text);
    public static void Success(string text) => Write(LogLevel.Success, text);
    public static void Warn(string text) => Write(LogLevel.Warn, text);
    public static void Error(string text) => Write(LogLevel.Error, text);

    static void Write(LogLevel level, string text) => Written?.Invoke(new LogLine(DateTime.Now, level, text));
}
