using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Tests;

// WoW's Lua 5.1 refuses to load a file with a function that uses more than 60 locals from outside it
// (upvalues, counting what its nested functions use too) or has more than 200 locals at once. MoonSharp,
// which the other tests run the addon in, has neither limit, so a file can pass every test and still
// not load in game (0.39.12's Friends tab did: "function at line 1407 has more than 60 upvalues").
// This reads the addon's files with Lua 5.1's scoping and checks both limits.
public partial class LuaLimitsTests
{
    const int MaxUpvalues = 60, MaxLocals = 200;

    public static TheoryData<string> AddonFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(AddonLuaTests.AddonDir, "*.lua").Order(StringComparer.Ordinal)) data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(AddonFiles))]
    public void Every_function_stays_inside_lua_5_1s_limits(string file)
    {
        var problems = Check(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)));
        Assert.True(problems.Count == 0, $"{file}: {string.Join("; ", problems)}");
    }

    // The checker itself: too many upvalues are found, also when only a nested function uses them.
    [Fact]
    public void The_checker_finds_too_many_upvalues()
    {
        var outer = string.Concat(Enumerable.Range(1, 61).Select(i => $"local u{i} = {i}\n"));
        var uses = string.Join(" + ", Enumerable.Range(1, 61).Select(i => $"u{i}"));
        Assert.Contains(Check(outer + $"local function f()\n  return function() return {uses} end\nend\n"), p => p.Contains("line 62") && p.Contains("61 upvalues"));
        Assert.Empty(Check(outer.Replace("local u61 = 61\n", "") + $"local function f() return {uses.Replace(" + u61", "")} end\n"));
        Assert.Empty(Check("local t = { a = 1, b = c }\nlocal function f(x, ...) local y = x.a:b(t) for i = 1, 2 do y = i end return y end\n"));
    }

    sealed class Func(int line, Func? parent)
    {
        public int Line { get; } = line;
        public Func? Parent { get; } = parent;
        public List<List<string>> Scopes { get; } = [[]];
        public HashSet<string> Upvalues { get; } = [];
        public int MaxActive { get; set; }

        public bool Declares(string name) => Scopes.Any(s => s.Contains(name));
        public void Declare(string name)
        {
            Scopes[^1].Add(name);
            MaxActive = Math.Max(MaxActive, Scopes.Sum(s => s.Count));
        }
    }

    record Token(string Text, int Line, bool IsName);

    static readonly HashSet<string> Keywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];

    // Problems as "function at line N has M upvalues" / "... M locals".
    internal static List<string> Check(string source)
    {
        var tokens = Tokenize(source);
        var funcs = new List<Func>();
        var main = new Func(0, null);
        funcs.Add(main);
        var fn = main;
        // What each open "end" (or "until") closes: a function, or a block of the current function.
        var closers = new Stack<(bool IsFunction, Func Owner)>();
        var brackets = new Stack<string>();
        List<string>? pendingFor = null; // for-loop names, declared in the loop's block

        void Use(string name)
        {
            for (var f = fn; f is not null; f = f.Parent)
            {
                if (!f.Declares(name)) continue;
                for (var g = fn; g != f; g = g.Parent!) g.Upvalues.Add(name);
                return;
            }
        }

        void OpenBlock() => closers.Push((false, fn));
        void PushScope() => fn.Scopes.Add([]);

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var prev = i > 0 ? tokens[i - 1].Text : "";
            var next = i + 1 < tokens.Count ? tokens[i + 1].Text : "";
            switch (t.Text)
            {
                case "(" or "[" or "{": brackets.Push(t.Text); continue;
                case ")" or "]" or "}": if (brackets.Count > 0) brackets.Pop(); continue;
                case "function":
                {
                    // function a.b:c(...) uses a; local function f declares f first (handled at "local").
                    var j = i + 1;
                    var method = false;
                    if (j < tokens.Count && tokens[j].IsName)
                    {
                        if (prev != "local") Use(tokens[j].Text);
                        j++;
                        while (j + 1 < tokens.Count && tokens[j].Text is "." or ":")
                        {
                            method |= tokens[j].Text == ":";
                            j += 2;
                        }
                    }
                    var f = new Func(t.Line, fn);
                    funcs.Add(f);
                    fn = f;
                    closers.Push((true, f));
                    if (method) f.Declare("self");
                    j++; // "("
                    while (j < tokens.Count && tokens[j].Text != ")")
                    {
                        if (tokens[j].IsName) f.Declare(tokens[j].Text);
                        j++;
                    }
                    i = j;
                    continue;
                }
                case "local":
                    if (next == "function")
                    {
                        fn.Declare(tokens[i + 2].Text);
                        continue;
                    }
                    for (var j = i + 1; j < tokens.Count && (tokens[j].IsName || tokens[j].Text == ","); j++)
                        if (tokens[j].IsName) fn.Declare(tokens[j].Text);
                    // Skip the names: they aren't uses.
                    while (i + 1 < tokens.Count && (tokens[i + 1].IsName || tokens[i + 1].Text == ",")) i++;
                    continue;
                case "for":
                    pendingFor = [];
                    for (var j = i + 1; j < tokens.Count && (tokens[j].IsName || tokens[j].Text == ","); j++)
                        if (tokens[j].IsName) pendingFor.Add(tokens[j].Text);
                    while (i + 1 < tokens.Count && (tokens[i + 1].IsName || tokens[i + 1].Text == ",")) i++;
                    continue;
                case "do":
                    OpenBlock();
                    PushScope();
                    if (pendingFor is not null) foreach (var n in pendingFor) fn.Declare(n);
                    pendingFor = null;
                    continue;
                case "then" or "repeat":
                    OpenBlock();
                    PushScope();
                    continue;
                case "else":
                    fn.Scopes.RemoveAt(fn.Scopes.Count - 1);
                    PushScope();
                    continue;
                case "elseif":
                    // Closes the "then" block; the next "then" opens one again.
                    if (closers.Count > 0 && !closers.Peek().IsFunction) closers.Pop();
                    fn.Scopes.RemoveAt(fn.Scopes.Count - 1);
                    continue;
                case "end" or "until":
                    if (closers.Count == 0) continue;
                    var (isFunction, owner) = closers.Pop();
                    if (isFunction) fn = owner.Parent ?? main;
                    else fn.Scopes.RemoveAt(fn.Scopes.Count - 1);
                    continue;
            }
            if (!t.IsName || prev is "." or ":") continue;
            // { key = ... }: a field name, not a variable.
            if (next == "=" && brackets.Count > 0 && brackets.Peek() == "{" && prev is "{" or "," or ";") continue;
            Use(t.Text);
        }

        var problems = new List<string>();
        foreach (var f in funcs)
        {
            var where = f == main ? "the main chunk" : $"function at line {f.Line}";
            if (f.Upvalues.Count > MaxUpvalues) problems.Add($"{where} has {f.Upvalues.Count} upvalues (max {MaxUpvalues})");
            if (f.MaxActive > MaxLocals) problems.Add($"{where} has {f.MaxActive} locals at once (max {MaxLocals})");
        }
        return problems;
    }

    // Names, keywords and symbols with their line; comments, strings and numbers left out (a string or
    // number stays as one "x" token so "a = 'b'" still reads as an assignment).
    static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var line = 1;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                i += 2;
                if (LongBracket().Match(s, i) is { Success: true, Index: var at } m && at == i) i = SkipLong(s, i, m.Groups[1].Length, ref line);
                else while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '[' && LongBracket().Match(s, i) is { Success: true, Index: var lb } lm && lb == i)
            {
                i = SkipLong(s, i, lm.Groups[1].Length, ref line);
                tokens.Add(new Token("x", line, false));
                continue;
            }
            if (c is '"' or '\'')
            {
                i++;
                while (i < s.Length && s[i] != c)
                {
                    if (s[i] == '\\') i++;
                    else if (s[i] == '\n') line++;
                    i++;
                }
                i++;
                tokens.Add(new Token("x", line, false));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                var word = s[start..i];
                tokens.Add(new Token(word, line, !Keywords.Contains(word)));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || ((s[i] is '+' or '-') && s[i - 1] is 'e' or 'E'))) i++;
                tokens.Add(new Token("x", line, false));
                continue;
            }
            var two = i + 1 < s.Length ? s.Substring(i, 2) : "";
            if (two is "==" or "~=" or "<=" or ">=" or "..")
            {
                var three = i + 2 < s.Length && two == ".." && s[i + 2] == '.';
                tokens.Add(new Token(three ? "..." : two, line, false));
                i += three ? 3 : 2;
                continue;
            }
            tokens.Add(new Token(c.ToString(), line, false));
            i++;
        }
        return tokens;
    }

    // [[ ... ]] or [==[ ... ]==] from i; returns the index after it.
    static int SkipLong(string s, int i, int level, ref int line)
    {
        var close = "]" + new string('=', level) + "]";
        var end = s.IndexOf(close, i + level + 2, StringComparison.Ordinal);
        if (end < 0) end = s.Length;
        line += s.AsSpan(i, end - i).Count('\n');
        return Math.Min(s.Length, end + close.Length);
    }

    [GeneratedRegex(@"\G\[(=*)\[")]
    private static partial Regex LongBracket();
}
