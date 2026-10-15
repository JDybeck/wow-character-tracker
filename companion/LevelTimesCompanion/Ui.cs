namespace LevelTimesCompanion;

// Small console helpers so every step reads the same way: [ OK ], [ !! ], [ ?? ].
static class Ui
{
    public static void Title(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void Ok(string text) => Tag(" OK ", ConsoleColor.Green, text);
    public static void Warn(string text) => Tag(" !! ", ConsoleColor.Yellow, text);
    public static void Error(string text) => Tag("FAIL", ConsoleColor.Red, text);
    public static void Info(string text) => Tag(" .. ", ConsoleColor.DarkGray, text);

    // Indented follow-up line under a step, for "here is what to do" text.
    public static void Hint(string text)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("       " + text);
        Console.ResetColor();
    }

    public static bool Confirm(string question, bool defaultYes = true)
    {
        while (true)
        {
            Tag(" ?? ", ConsoleColor.Cyan, question + (defaultYes ? " [Y/n] " : " [y/N] "), newLine: false);
            var answer = Console.ReadLine()?.Trim().Trim('﻿').ToLowerInvariant();
            if (answer is null) return false; // no one to answer (input redirected), so don't change anything
            if (answer == "") return defaultYes;
            if (answer is "y" or "yes" or "j" or "ja") return true;
            if (answer is "n" or "no" or "nej") return false;
        }
    }

    public static string Ask(string question)
    {
        Tag(" ?? ", ConsoleColor.Cyan, question + " ", newLine: false);
        return Console.ReadLine()?.Trim().Trim('﻿') ?? "";
    }

    // Keeps the window open when the exe was double-clicked and we have to stop.
    public static void PressAnyKeyToExit()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close.");
        try { Console.ReadKey(true); } catch (InvalidOperationException) { }
    }

    static void Tag(string tag, ConsoleColor color, string text, bool newLine = true)
    {
        Console.Write("[");
        Console.ForegroundColor = color;
        Console.Write(tag);
        Console.ResetColor();
        Console.Write("] ");
        if (newLine) Console.WriteLine(text); else Console.Write(text);
    }
}
