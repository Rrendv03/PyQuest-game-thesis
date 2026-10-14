using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Display-only Python syntax colorizer for the puzzle code displays
/// (PredictTheOutput, PairACode, TrueOrFalse, FillInTheBlank, Line Scramble,
/// SpotTheBug). Emits standard UnityEngine.UI.Text rich-text markup
/// (&lt;color=#...&gt; tags) that is equally valid TextMeshPro markup, so
/// nothing here changes gameplay, evaluation, or any answer strings.
///
/// SAFETY CONTRACT (why this cannot break the puzzle system):
///   1. Display-only. Callers invoke this at the exact point where a Text
///      component's .text is assigned. The plain string is still what the
///      formats store, compare, log, and submit. Nothing downstream of
///      PuzzleManager.UserSubmission ever sees styled text.
///   2. Kill switch. Set PythonSyntaxHighlighter.Enabled = false (one line)
///      and every display reverts to the previous plain-text rendering
///      verbatim — no scene changes, no other rollback needed.
///   3. Reversible bytes. When Enabled, the output contains only color tags
///      wrapped around spans of the ORIGINAL characters. The only character
///      substitution is escaping &lt; to &amp;#60; and &gt; to &amp;#62;, which
///      Unity renders back as the literal characters (same mechanism as the
///      well-known &amp;#160; non-breaking-space trick in uGUI, and the
///      standard HTML-entity behavior of TextMeshPro).
///   4. Idempotent. Feeding already-styled text back through Highlight() is
///      a no-op, so a double render can never stack tags.
///   5. Balanced markup. Every color span opens AND closes within itself;
///      tags never straddle lines, so malformed/partial lines (unterminated
///      quotes, half-typed strings) can never produce unbalanced output.
///
/// IDE MONOSPACE FONT: Unity fonts are assets on your side, so the typeface
/// itself lives in the scene — but the ASSIGNMENT is automated. Download
/// JetBrains Mono (official link + install steps in
/// 13_python_style_puzzle_display.md), put this file's companion component
/// CodeDisplayFontInstaller on one always-active scene object and drag the
/// TTF into it. Its Awake() calls SetIdeFont(font) here, and from then on
/// every Apply() call stamps the registered font onto the display Text it
/// renders — no per-scene font dragging. Per-object choice still wins: a
/// Text whose font was deliberately assigned in the Inspector is never
/// overridden (see StampIdeFont's ownership rules). Apply() also
/// force-enables the Rich Text flag so a missed checkbox in the scene
/// can't silently defeat the styling.
/// </summary>
public static class PythonSyntaxHighlighter
{
    // ------------------------------------------------------------------ //
    // Global kill switch. Flip to false to restore 100% legacy displays. //
    // ------------------------------------------------------------------ //
    public static bool Enabled = true;

    // VS Code Dark+ / JetBrains-dark palette. Public so a tune-up is a
    // one-constant change; hex strings mean no asset dependencies at all.
    public static string KeywordColor = "#569CD6"; // def, class, import, in...
    public static string FlowColor = "#C586C0"; // if / for / while / return...
    public static string ConstantColor = "#4FC1FF"; // True / False / None
    public static string BuiltinColor = "#DCDCAA"; // print, len + def names
    public static string TypeColor = "#4EC9B0"; // ValueError, TypeError...
    public static string VariableColor = "#9CDCFE"; // known variable names
    public static string StringColor = "#CE9178"; // "..." / '...' / f"..."
    public static string NumberColor = "#B5CEA8"; // 5, 3.14, 0x1F
    public static string CommentColor = "#6A9955"; // # comments + #[Goal: ...]

    private static readonly HashSet<string> FlowKeywords = new HashSet<string> {
        "if", "elif", "else", "for", "while", "break", "continue", "return",
        "try", "except", "finally", "with", "raise", "await", "async"
    };

    private static readonly HashSet<string> ValueKeywords = new HashSet<string> {
        "True", "False", "None"
    };

    private static readonly HashSet<string> Keywords = new HashSet<string> {
        "def", "class", "import", "from", "as", "in", "is", "and", "or",
        "not", "lambda", "global", "nonlocal", "del", "pass", "assert",
        "yield", "match", "case"
    };

    private static readonly HashSet<string> Builtins = new HashSet<string> {
        "print", "len", "range", "input", "int", "float", "str", "bool",
        "list", "tuple", "dict", "set", "frozenset", "sum", "min", "max",
        "abs", "round", "sorted", "reversed", "enumerate", "zip", "map",
        "filter", "any", "all", "type", "isinstance", "issubclass", "repr",
        "ord", "chr", "hex", "oct", "bin", "pow", "divmod", "format", "hash",
        "id", "iter", "next", "open", "super", "vars", "globals", "locals",
        "callable", "hasattr", "getattr", "setattr", "delattr", "bytes",
        "bytearray", "complex", "object", "slice", "compile", "eval", "exec",
        "help", "dir", "staticmethod", "classmethod", "property"
    };

    // Exception names in the error-variant puzzles' legitimate pool (plus
    // the two story exceptions some variants reference).
    private static readonly HashSet<string> ErrorTypes = new HashSet<string> {
        "ValueError", "TypeError", "NameError", "SyntaxError", "IndexError",
        "KeyError", "ZeroDivisionError", "IndentationError", "AttributeError",
        "RuntimeError", "StopIteration", "FileNotFoundError", "OverflowError",
        "RecursionError"
    };

    // Short string prefixes: f"...", r'...', rb"..." — max 2 chars AND
    // immediately followed by the opening quote, so a normal identifier
    // can never be mistaken for a prefix.
    private static readonly HashSet<string> StringPrefixes = new HashSet<string> {
        "f", "r", "b", "u", "rb", "fr", "rf", "br", "R", "F", "B", "U"
    };

    // ------------------------------------------------------------------ //
    // IDE font plumbing. The installer component registers the font once;  //
    // Apply() then stamps it at the very points the styled string lands on  //
    // screen. stampedFontIds remembers which Texts we took over, so (a) a   //
    // deliberate Inspector font assignment is never overridden and (b) a    //
    // later SetIdeFont call re-stamps the same displays with the new font.  //
    // ------------------------------------------------------------------ //
    private static Font ideFont;

    /// <summary>The font CodeDisplayFontInstaller registered, or null when
    /// no installer ran (displays keep exactly what the Inspector holds).</summary>
    public static Font IdeFont { get { return ideFont; } }

    /// <summary>Register the monospace code font every unlocked code display
    /// adopts on its next render. Pass null to unregister (displays revert to
    /// whatever their scene font was — already-stamped ones keep the last
    /// stamped font until re-rendered by the scene or a new registration).</summary>
    public static void SetIdeFont(Font font)
    {
        ideFont = font;
        stampedFontIds.Clear(); // re-evaluate ownership against the new font
    }

    // Instance IDs displayed with our registered font (see StampIdeFont).
    private static readonly HashSet<int> stampedFontIds = new HashSet<int>();

    /// <summary>
    /// Ownership rules, deliberately strict so nothing outside the code
    /// displays can drift: Null ideFont → no-op. A Text already stamped by
    /// us adopts whatever ideFont currently is (installer swaps stay
    /// automatic). Otherwise the stamp happens ONLY when the Text's font is
    /// still a Unity default ("Arial" or "LegacyRuntime"), i.e. the scene
    /// never cared about it. Any other font must have been assigned on
    /// purpose, so it wins forever — a per-object choice is never overridden.
    /// </summary>
    private static void StampIdeFont(Text target)
    {
        if (ideFont == null || target.font == ideFont) return;
        int id = target.GetInstanceID();
        if (stampedFontIds.Contains(id)) { target.font = ideFont; return; }
        if (target.font == null || target.font.name == "Arial"
            || target.font.name == "LegacyRuntime")
        {
            target.font = ideFont;
            stampedFontIds.Add(id);
        }
    }

    // ---------------------------------------------------------------- //

    /// <summary>
    /// Preferred entry point: styles source and assigns it to a legacy
    /// UI.Text, force-enabling its Rich Text flag and stamping the registered
    /// IDE font (SetIdeFont) when this display's font is still default.
    /// Null-safe; content identical to Highlight(source).
    /// </summary>
    public static void Apply(Text target, string source)
    {
        if (target == null) return;
        target.supportRichText = true; // legacy UI.Text's name for the Rich Text flag (TMP calls it richText)
        StampIdeFont(target);
        target.text = Highlight(source);
    }

    /// <summary>
    /// Returns a colored rich-text rendering of Python source for display.
    /// When Enabled is false, or the input already carries color tags, the
    /// input is returned untouched.
    /// </summary>
    public static string Highlight(string source)
    {
        if (!Enabled || string.IsNullOrEmpty(source)) return source;
        if (source.Contains("<color=") || source.Contains("<size=")) return source;

        string[] lines = source.Split('\n');
        var knownVars = new HashSet<string>();
        for (int li = 0; li < lines.Length; li++)
        {
            string ln = lines[li];
            if (ln.EndsWith("\r")) ln = ln.Substring(0, ln.Length - 1);
            lines[li] = ln;
            RegisterVariables(ln, knownVars); // pass 1: cosmetic var registry
        }

        var sb = new StringBuilder(source.Length + 64);
        for (int li = 0; li < lines.Length; li++)
        {
            HighlightLine(lines[li], sb, knownVars);
            if (li < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapes literal &lt; / &gt; into numeric character references so the
    /// rich-text parser can never mistake code (e.g. "if x &lt; 3 and y &gt; 0")
    /// for a markup tag. A bare ampersand is left as-is: the parser only
    /// consumes entity-like sequences after &lt;, not &amp;.
    /// </summary>
    public static string EscapeForRichText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (raw.IndexOf('<') < 0 && raw.IndexOf('>') < 0) return raw;
        var sb = new StringBuilder(raw.Length + 8);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '<') sb.Append("&#60;");
            else if (c == '>') sb.Append("&#62;");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- //
    // Internals — per-line scanner                                       //
    // ---------------------------------------------------------------- //

    /// <summary>Colorizes ONE line into sb. Every span self-closes, so the
    /// tag stream is always balanced regardless of line contents.</summary>
    private static void HighlightLine(string line, StringBuilder sb, HashSet<string> knownVars)
    {
        int i = 0;
        int n = line.Length;

        // Leading indentation passes through verbatim (monospace makes the
        // indentation itself the visual structure).
        while (i < n && (line[i] == ' ' || line[i] == '\t')) { sb.Append(line[i]); i++; }

        // Whole-line comment — covers the goal blocks #[Goal: "..."],
        // #[Inputted code was: "..."] and #[Output: "..."] plus headers like
        // "# Fill in the missing token:". They read exactly like comment
        // blocks in an IDE, which is the story those lines already tell.
        if (i < n && line[i] == '#')
        {
            AppendSpan(line, i, n, CommentColor, sb);
            return;
        }

        bool nextNameIsDefClass = false; // just saw 'def' or 'class'

        while (i < n)
        {
            char c = line[i];

            // Trailing comment after code.
            if (c == '#')
            {
                AppendSpan(line, i, n, CommentColor, sb);
                return;
            }

            if (c == '"' || c == '\'')
            {
                int end = FindStringEnd(line, i);
                AppendSpan(line, i, end, StringColor, sb);
                i = end;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && IsWordChar(line[i])) i++;
                string word = line.Substring(start, i - start);

                // f"/r'... string prefix shares the string color.
                if (i < n && (line[i] == '"' || line[i] == '\'') && StringPrefixes.Contains(word))
                {
                    AppendTagged(word, StringColor, sb);
                    continue;
                }

                if (nextNameIsDefClass)
                {
                    AppendTagged(word, BuiltinColor, sb); // def name / class name
                    nextNameIsDefClass = false;
                }
                else if (ValueKeywords.Contains(word))
                {
                    AppendTagged(word, ConstantColor, sb);
                }
                else if (FlowKeywords.Contains(word))
                {
                    AppendTagged(word, FlowColor, sb);
                    nextNameIsDefClass = false;
                }
                else if (Keywords.Contains(word))
                {
                    AppendTagged(word, KeywordColor, sb);
                    nextNameIsDefClass = word == "def" || word == "class";
                }
                else if (ErrorTypes.Contains(word))
                {
                    AppendTagged(word, TypeColor, sb);
                }
                else if (Builtins.Contains(word))
                {
                    AppendTagged(word, BuiltinColor, sb);
                }
                else if (knownVars.Contains(word))
                {
                    AppendTagged(word, VariableColor, sb);
                }
                else
                {
                    sb.Append(EscapeForRichText(word));
                }
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                i = ScanNumberTail(line, i);
                AppendSpan(line, start, i, NumberColor, sb);
                nextNameIsDefClass = false;
                continue;
            }

            // Operators, punctuation, spaces: neutral, escaped where needed.
            sb.Append(EscapeForRichText(line.Substring(i, 1)));
            if (c != ' ' && c != '\t') nextNameIsDefClass = false;
            i++;
        }
    }

    // Pass-1 bookkeeping: remember plausible variable names so identifiers
    // tint like they do in an IDE (uses light heuristics, display-only).
    private static void RegisterVariables(string line, HashSet<string> into)
    {
        if (string.IsNullOrEmpty(line)) return;
        string t = line.TrimStart(' ', '\t');
        if (t.Length == 0 || t[0] == '#') return;

        // Simple assignment: name = (but not == != <= >=)
        int eq = FindTopLevelAssign(t);
        if (eq > 0)
        {
            string name = LastWord(t.Substring(0, eq).TrimEnd());
            if (IsIdentifier(name) && !Keywords.Contains(name) && !FlowKeywords.Contains(name)
                && !Builtins.Contains(name))
                into.Add(name);
        }

        // Loop variables: "for NAME[, NAME2] in"
        if (StartsWith(t, "for "))
        {
            string rest = t.Substring(4).TrimStart();
            int inIdx = FindWordBoundary(rest, "in");
            if (inIdx > 0)
            {
                string heads = rest.Substring(0, inIdx);
                foreach (string tok in heads.Split(','))
                {
                    string name = tok.Trim();
                    if (IsIdentifier(name) && !Keywords.Contains(name)) into.Add(name);
                }
            }
        }
    }

    // ---------------------------------------------------------------- //
    // Small scanners                                                     //
    // ---------------------------------------------------------------- //

    /// <summary>Index just past the string that opens at `at` (handles
    /// triple quotes and \ escapes); end of line when unterminated.</summary>
    private static int FindStringEnd(string line, int at)
    {
        char q = line[at];
        bool triple = at + 2 < line.Length && line[at + 1] == q && line[at + 2] == q;
        int i = at + (triple ? 3 : 1);
        while (i < line.Length)
        {
            if (line[i] == '\\') { i += 2; continue; } // skip escaped char
            if (triple)
            {
                if (i + 2 < line.Length && line[i] == q && line[i + 1] == q && line[i + 2] == q)
                    return i + 3;
            }
            else if (line[i] == q)
            {
                return i + 1;
            }
            i++;
        }
        return line.Length; // unterminated: rest of line is the string (safe)
    }

    /// <summary>Next scan index after the numeric literal at `at`.</summary>
    private static int ScanNumberTail(string line, int at)
    {
        int i = at;
        int n = line.Length;
        // Hex literals: 0x1F / 0X1F
        if (line[i] == '0' && i + 1 < n && (line[i + 1] == 'x' || line[i + 1] == 'X'))
        {
            i += 2;
            while (i < n && IsHexDigit(line[i])) i++;
            return i;
        }
        bool seenDot = false;
        while (i < n)
        {
            char c = line[i];
            if (char.IsDigit(c) || c == '_') { i++; continue; }
            if (c == '.' && !seenDot && i + 1 < n && char.IsDigit(line[i + 1])) { seenDot = true; i++; continue; }
            // Exponent tails: 1e5, 2.5e-3
            if ((c == 'e' || c == 'E') && i > at && i + 1 < n)
            {
                char next = line[i + 1];
                if (char.IsDigit(next) || ((next == '+' || next == '-') && i + 2 < n && char.IsDigit(line[i + 2])))
                {
                    i++;
                    if (line[i] == '+' || line[i] == '-') i++;
                    while (i < n && char.IsDigit(line[i])) i++;
                }
            }
            break;
        }
        return i > at ? i : at + 1;
    }

    /// <summary>Index of the first '=' in t that is a true assignment
    /// (skips strings and comments; ignores == != &lt;= &gt;=).</summary>
    private static int FindTopLevelAssign(string t)
    {
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\'') { i = SkipQuoted(t, i, '\''); continue; }
            if (c == '"') { i = SkipQuoted(t, i, '"'); continue; }
            if (c == '#') break;
            if (c != '=') continue;
            if (i > 0 && (t[i - 1] == '=' || t[i - 1] == '!' || t[i - 1] == '<' || t[i - 1] == '>')) continue;
            if (i + 1 < t.Length && t[i + 1] == '=') { i++; continue; }
            return i;
        }
        return -1;
    }

    /// <summary>Index just past the quoted run opening at `at`.</summary>
    private static int SkipQuoted(string s, int at, char quote)
    {
        int i = at + 1;
        while (i < s.Length && s[i] != quote)
        {
            if (s[i] == '\\') i += 2; else i++;
        }
        return i + 1 >= s.Length ? s.Length : i + 1;
    }

    private static string LastWord(string s)
    {
        int end = s.Length;
        while (end > 0 && !IsWordChar(s[end - 1])) end--;
        int start = end;
        while (start > 0 && IsWordChar(s[start - 1])) start--;
        return end > start ? s.Substring(start, end - start) : "";
    }

    private static int FindWordBoundary(string s, string word)
    {
        int at = s.IndexOf(word);
        if (at < 0) return -1;
        bool leftOk = at == 0 || !IsWordChar(s[at - 1]);
        int after = at + word.Length;
        bool rightOk = after >= s.Length || !IsWordChar(s[after]);
        return (leftOk && rightOk) ? at : -1;
    }

    private static bool StartsWith(string s, string prefix) =>
        s.Length >= prefix.Length && s.Substring(0, prefix.Length) == prefix;

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsDigit(s[0])) return false;
        for (int i = 0; i < s.Length; i++)
            if (!IsWordChar(s[i])) return false;
        return true;
    }

    // ---------------------------------------------------------------- //
    // Output helpers                                                     //
    // ---------------------------------------------------------------- //

    /// <summary>Wraps line[start..end) in a color span, escaping literals.</summary>
    private static void AppendSpan(string line, int start, int end, string hexColor, StringBuilder sb)
    {
        sb.Append("<color=").Append(hexColor).Append('>')
          .Append(EscapeForRichText(line.Substring(start, end - start)))
          .Append("</color>");
    }

    private static void AppendTagged(string word, string hexColor, StringBuilder sb)
    {
        sb.Append("<color=").Append(hexColor).Append('>')
          .Append(EscapeForRichText(word))
          .Append("</color>");
    }
}