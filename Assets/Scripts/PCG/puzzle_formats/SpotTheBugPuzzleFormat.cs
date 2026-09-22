using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class SpotTheBugPuzzleFormat : IPuzzleFormat
{
    [System.Serializable]
    public struct SubmissionData
    {
        public int selectedLineIndex;
        public string selectedFixText;

        public SubmissionData(int lineIndex, string fixText)
        {
            selectedLineIndex = lineIndex;
            selectedFixText = fixText;
        }

        public override string ToString()
        {
            return $"[Line {selectedLineIndex}] {selectedFixText}";
        }
    }

    public PuzzleType FormatType => PuzzleType.SpotTheBug;

    private PuzzleTemplate template;
    private int correctLineIndex;
    private string correctFix;
    private string buggedLine;
    private bool authoredBug;
    private List<List<string>> allLineFixOptions;

    // ------------------------------------------------------------------
    // BUG-KIND ROTATION (session memory)
    //
    // Playtest: "one template only mutates its print function to be
    // misspelled, this happened 3 times in a row" and "the bugs always
    // involve spotting what variable is spelled wrong, making the puzzle
    // repetitive". Cause: InjectBug was a deterministic first-match chain
    // -- every print( line became pritn(, every line naming
    // template.variableName became the mangled variable -- so with a
    // small template pool the SAME defect family served over and over.
    //
    // Fix: bug kinds are now explicit, the applicable kind is drawn at
    // random, and a session-level memory (static, so it survives across
    // puzzle instances within one play session) avoids re-serving any of
    // the last RecentKindMemory kinds whenever another kind applies.
    //
    // DESIGN RULE (same playtest notes): basic operators are NEVER
    // mutated -- not in the injected bug, not in the wrong-fix options,
    // not in the decoys. Operator flips (+ -> *, = -> ==, > -> <) run
    // without error and only change semantics, which is the "nuanced
    // solution" trap this puzzle is supposed to avoid. Every bug kind
    // below is a structural/spelling defect that at least breaks syntax
    // or name resolution instead.
    // ------------------------------------------------------------------
    private const int RecentKindMemory = 3;
    private static readonly List<int> recentBugKinds = new List<int>();

    private const int BugKind_KeywordMisspell = 0;
    private const int BugKind_IdentifierMangle = 1;
    private const int BugKind_MissingColon = 2;
    private const int BugKind_MissingParen = 3;
    private const int BugKind_QuoteMismatch = 4;
    private const int BugKind_Indentation = 5;

    // Keywords eligible for BugKind_KeywordMisspell, each with a pool of
    // believable typos. print alone has five variants, so even a repeat
    // hit on print no longer renders the identical "pritn(" every time.
    private static readonly Dictionary<string, string[]> MisspellPool =
        new Dictionary<string, string[]>
    {
        { "print", new[] { "pritn", "pirnt", "prnt", "prinnt", "Print" } },
        { "input", new[] { "inpput", "imput", "inupt", "Input" } },
        { "range", new[] { "rang", "rnge", "rane", "Range" } },
        { "while", new[] { "whlie", "whil", "whyle", "While" } },
        { "if",    new[] { "fi", "If" } },
        { "elif",  new[] { "eliif", "elifs", "elfi" } },
        { "else",  new[] { "esle", "Else" } },
        { "for",   new[] { "fro", "forr", "For" } },
        { "def",   new[] { "dfe", "Def" } },
        { "int",   new[] { "itn", "Int" } },
        { "str",   new[] { "strn", "Str" } },
        { "len",   new[] { "lne", "Len" } }
    };

    // Identifiers never eligible for mangling.
    private static readonly HashSet<string> KeywordSet = new HashSet<string>
    {
        "print", "input", "range", "while", "if", "elif", "else", "for",
        "def", "int", "str", "len", "float", "in", "not", "and", "or",
        "True", "False", "None", "return", "break", "continue", "pass",
        "import", "from"
    };

    // Design-rule guard: a GENERATED bug may never change the count of any
    // of these between the clean and the served line.
    private static readonly string[] OperatorTokens =
        { "+", "-", "*", "/", "%", "==", "!=", ">=", "<=", "+=", "-=", "*=", "/=", ">", "<", "=" };

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(Text displayField) { }
    public void RenderPuzzle(PairACodeUIController uiController) { }
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(LineScrambleUIController uiController) { }
    public int GetOptionCount()
    {
        // Player must guess correct line AND correct fix.
        // True guess probability = 1 / (lineCount * fixOptionsPerLine)
        // Approximate using line count from template times 3 fix options per line
        return template.codeLines.Count * 3;
    }

    public void RenderPuzzle(SpotTheBugUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[SpotTheBugPuzzleFormat] UIController is null");
            return;
        }

        uiController.PopulateUI(
            template.codeLines,
            correctLineIndex,
            correctFix,
            allLineFixOptions);

        Debug.Log($"[SpotTheBugPuzzleFormat] Rendered | Bug line: {correctLineIndex} | Fix: {correctFix}");
    }

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is SubmissionData submission)
        {
            bool lineCorrect = submission.selectedLineIndex == correctLineIndex;
            bool fixCorrect = !string.IsNullOrEmpty(submission.selectedFixText)
                              && submission.selectedFixText.Trim() == correctFix.Trim();
            bool result = lineCorrect && fixCorrect;

            Debug.Log($"[SpotTheBugPuzzleFormat] Evaluated structured submission | " +
                      $"Line: {submission.selectedLineIndex} ({(lineCorrect ? "correct" : "wrong")}) | " +
                      $"Fix: {submission.selectedFixText} ({(fixCorrect ? "correct" : "wrong")}) | " +
                      $"Result: {result}");
            return result;
        }

        if (playerAnswer is bool boolAnswer)
        {
            Debug.LogWarning("[SpotTheBugPuzzleFormat] Legacy bool submission received. " +
                             "For reproducibility, use SubmissionData instead.");
            return boolAnswer;
        }

        Debug.LogError("[SpotTheBugPuzzleFormat] Invalid answer type: " +
                       $"{playerAnswer?.GetType().Name ?? "null"}");
        return false;
    }

    public object GetCorrectAnswer() => new SubmissionData(correctLineIndex, correctFix);

    private void GeneratePuzzle()
    {
        // Step 1: Determine the bug line. Two template conventions meet
        // here:
        //   (a) bugLineIndex >= 0  -> author-pinned line (either the
        //       author baked the bug into codeLines and stored the clean
        //       line in correctAnswer, or the author wants the GENERATED
        //       bug injected on exactly this line -- see Step 2).
        //   (b) bugLineIndex == -1 -> pick a random code line. Blank and
        //     pure-comment lines are skipped for random picks when real
        //     code lines exist: a comment can host a *changed* line but
        //     never a meaningful Python bug.
        if (template.bugLineIndex >= 0 && template.bugLineIndex < template.codeLines.Count)
        {
            correctLineIndex = template.bugLineIndex;
        }
        else
        {
            List<int> candidateLines = new List<int>();
            for (int i = 0; i < template.codeLines.Count; i++)
            {
                if (template.codeLines[i].TrimStart().StartsWith("#")) continue;
                candidateLines.Add(i);
            }
            if (candidateLines.Count == 0)
                for (int i = 0; i < template.codeLines.Count; i++)
                    candidateLines.Add(i);

            ShuffleList(candidateLines);
            correctLineIndex = candidateLines[0];
        }

        string displayedLine = template.codeLines[correctLineIndex];

        // Step 2: AUTHORED BUG vs GENERATED BUG.
        //
        // Templates like print_stb_001 author the bug directly in
        // codeLines ("print({msg}" -- closing paren missing) and store the
        // CLEAN line in correctAnswer. The old pipeline treated that
        // already-buggy line as the clean fix and injected a SECOND bug on
        // top, so the served "correct fix" was still broken -- the
        // playtest's "nearest correct answer was missing a parenthesis on
        // the end technically making it wrong also", i.e. NO option was
        // actually correct. When the template carries an authored fix for
        // its bug line, the bug is already on screen and correctFix comes
        // from correctAnswer verbatim; no injection runs. Templates with
        // an empty correctAnswer (or whose authored line is clean, e.g.
        // stb_003) keep the generated pipeline below.
        authoredBug = template.bugLineIndex == correctLineIndex
                      && !string.IsNullOrEmpty(template.correctAnswer)
                      && NormKey(template.correctAnswer) != NormKey(displayedLine);

        if (authoredBug)
        {
            correctFix = template.correctAnswer;
            buggedLine = displayedLine;
        }
        else
        {
            correctFix = displayedLine;
            buggedLine = InjectBug(correctFix);

            // A no-op injection would mean the "bugged" line equals the
            // correct fix -- an undecidable puzzle. Force a real change.
            // DiffKey (NOT NormKey) here: NormKey strips leading
            // whitespace, which would misread every successful indentation
            // bug ("    " + line) as a no-op and silently degrade the
            // whole Indentation kind into forced transpositions.
            if (DiffKey(buggedLine) == DiffKey(correctFix))
                buggedLine = ForceBug(correctFix);

            template.codeLines[correctLineIndex] = buggedLine;
        }

        // Step 3: Build fix options per line. Each line gets exactly 3
        // DISTINCT options (whitespace-normalized compare -- the old
        // exact-string Contains() let a whitespace-variant twin ship as a
        // second card, the same duplicate-card class fixed in PairACode).
        allLineFixOptions = new List<List<string>>();
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            List<string> lineOptions = new List<string>();

            if (i == correctLineIndex)
            {
                // Buggy line: correct fix first (it is added before
                // anything else and nothing ever removes index 0), then
                // wrong options.
                lineOptions.Add(correctFix);
                foreach (string w in GenerateWrongOptions(correctFix, buggedLine))
                    AddDistinct(lineOptions, w, 3);
            }
            else
            {
                // Correct line: the line itself + 2 plausible decoys.
                string cleanLine = template.codeLines[i];
                lineOptions.Add(cleanLine);
                foreach (string d in GenerateDecoys(cleanLine))
                    AddDistinct(lineOptions, d, 3);
            }

            PadToThree(lineOptions, i);
            ShuffleList(lineOptions);
            allLineFixOptions.Add(lineOptions);
        }

        // Step 4: Hard validity gate. "No correct answer available" must
        // be structurally impossible to serve.
        ValidatePuzzle();

        Debug.Log($"[SpotTheBugPuzzleFormat] Bug line: {correctLineIndex} " +
                  $"({(authoredBug ? "authored" : "generated/" + LastKindName())}) | " +
                  $"Clean: {correctFix} | Bugged: {buggedLine}");
    }

    // ------------------------------------------------------------------
    // Bug injection with kind rotation.
    // ------------------------------------------------------------------

    private string InjectBug(string line)
    {
        List<int> applicable = new List<int>();
        if (TryKeywordMisspell(line) != null) applicable.Add(BugKind_KeywordMisspell);
        if (TryIdentifierMangle(line, null) != null) applicable.Add(BugKind_IdentifierMangle);
        if (TryDropColon(line) != null) applicable.Add(BugKind_MissingColon);
        if (TryDropParen(line) != null) applicable.Add(BugKind_MissingParen);
        if (TryQuoteMismatch(line) != null) applicable.Add(BugKind_QuoteMismatch);
        if (TryIndentationBug(line) != null) applicable.Add(BugKind_Indentation);

        if (applicable.Count == 0)
        {
            // No structural kind applies (shouldn't happen for authored
            // content). The guaranteed transposition IS an identifier
            // mangle of the first token, so record it as one.
            recentBugKinds.Add(BugKind_IdentifierMangle);
            TrimRecentKinds();
            lastKind = BugKind_IdentifierMangle;
            return ForceBug(line);
        }

        // Session rotation: prefer kinds not used in the last
        // RecentKindMemory SpotTheBug encounters; fall back to all
        // applicable kinds only when every applicable kind is recent.
        List<int> fresh = applicable.Where(k => !recentBugKinds.Contains(k)).ToList();
        List<int> pool = fresh.Count > 0 ? fresh : applicable;

        int kind = pool[Random.Range(0, pool.Count)];
        string bugged = BuildBug(line, kind);
        // DiffKey, not NormKey -- see the comment at the call site above.
        if (bugged == null || DiffKey(bugged) == DiffKey(line))
            bugged = ForceBug(line);

        recentBugKinds.Add(kind);
        TrimRecentKinds();
        lastKind = kind;
        return bugged;
    }

    private int lastKind = -1;

    private string LastKindName()
    {
        return lastKind >= 0 ? BugKindName(lastKind) : "unknown";
    }

    private string BuildBug(string line, int kind)
    {
        switch (kind)
        {
            case BugKind_KeywordMisspell: return TryKeywordMisspell(line);
            case BugKind_IdentifierMangle: return TryIdentifierMangle(line, template.variableName);
            case BugKind_MissingColon: return TryDropColon(line);
            case BugKind_MissingParen: return TryDropParen(line);
            case BugKind_QuoteMismatch: return TryQuoteMismatch(line);
            case BugKind_Indentation: return TryIndentationBug(line);
            default: return ForceBug(line);
        }
    }

    private static void TrimRecentKinds()
    {
        while (recentBugKinds.Count > RecentKindMemory)
            recentBugKinds.RemoveAt(0);
    }

    private static string BugKindName(int kind)
    {
        switch (kind)
        {
            case BugKind_KeywordMisspell: return "keyword misspell";
            case BugKind_IdentifierMangle: return "identifier mangle";
            case BugKind_MissingColon: return "missing colon";
            case BugKind_MissingParen: return "missing parenthesis";
            case BugKind_QuoteMismatch: return "quote mismatch";
            case BugKind_Indentation: return "wrong indentation";
            default: return "forced transposition";
        }
    }

    // ------------------------- bug builders ---------------------------
    // Each Try* returns null when the kind does not apply to the line,
    // and never returns the input unchanged when non-null.

    private string TryKeywordMisspell(string line)
    {
        List<string> present = new List<string>();
        foreach (KeyValuePair<string, string[]> kv in MisspellPool)
            if (Regex.IsMatch(line, @"\b" + Regex.Escape(kv.Key) + @"\b"))
                present.Add(kv.Key);
        if (present.Count == 0) return null;

        string token = present[Random.Range(0, present.Count)];
        string[] variants = MisspellPool[token];

        for (int attempt = 0; attempt < 10; attempt++)
        {
            string repl = variants[Random.Range(0, variants.Length)];
            if (repl == token) continue;
            // If the "typo" is already a whole word on the line the
            // defect is not obvious (or not a defect at all) -- redraw.
            if (Regex.IsMatch(line, @"\b" + Regex.Escape(repl) + @"\b")) continue;
            string result = new Regex(@"\b" + Regex.Escape(token) + @"\b")
                                .Replace(line, repl, 1);
            if (result != line) return result;
        }
        return null;
    }

    /// <summary>
    /// Transposes two adjacent characters within an identifier, producing
    /// a believable typo that no longer matches any defined name (a real
    /// NameError), rather than an obviously-random replacement.
    /// </summary>
    private static string MangleIdentifier(string identifier)
    {
        if (identifier.Length < 2) return identifier + "x";
        char[] chars = identifier.ToCharArray();
        char tmp = chars[0];
        chars[0] = chars[1];
        chars[1] = tmp;
        string result = new string(chars);
        return result == identifier ? identifier + "x" : result;
    }

    private string TryIdentifierMangle(string line, string prefer)
    {
        // Work on a string-stripped copy so identifiers inside string
        // literals are never candidates.
        string noStrings = Regex.Replace(line, @"""[^""]*""|'[^']*'", " ");
        List<string> names = new List<string>();
        foreach (Match m in Regex.Matches(noStrings, @"[A-Za-z_][A-Za-z0-9_]*"))
        {
            if (KeywordSet.Contains(m.Value)) continue;
            if (!names.Contains(m.Value)) names.Add(m.Value);
        }
        if (names.Count == 0) return null;

        // Roughly half of identifier manglings hit the template's primary
        // variable when it appears on the line; the rest hit any other
        // identifier. The old code always mangled the primary variable,
        // which is why playtesters felt every bug was "a variable spelled
        // wrong".
        string target;
        if (!string.IsNullOrEmpty(prefer) && names.Contains(prefer) && Random.Range(0, 2) == 0)
            target = prefer;
        else
            target = names[Random.Range(0, names.Count)];

        string mangled = MangleIdentifier(target);
        int extra = 0;
        while (extra < 6 && (mangled == target
               || Regex.IsMatch(line, @"\b" + Regex.Escape(mangled) + @"\b")))
        {
            extra++;
            mangled = MangleIdentifier(target) + new string('x', extra);
        }
        if (mangled == target || Regex.IsMatch(line, @"\b" + Regex.Escape(mangled) + @"\b"))
            return null;

        // All occurrences, word-bounded (mirrors PCGEngine's
        // ReplaceWholeWord so partial-word hits can't corrupt neighbors).
        return Regex.Replace(line, @"\b" + Regex.Escape(target) + @"\b", mangled);
    }

    private string TryDropColon(string line)
    {
        string trimmed = line.TrimEnd();
        if (trimmed.Length < 2 || !trimmed.EndsWith(":")) return null;
        return trimmed.Substring(0, trimmed.Length - 1);
    }

    private string TryDropParen(string line)
    {
        string trimmed = line.TrimEnd();
        if (trimmed.Length < 2 || !trimmed.EndsWith(")") || !trimmed.Contains("(")) return null;
        return trimmed.Substring(0, trimmed.Length - 1);
    }

    private string TryQuoteMismatch(string line)
    {
        // 'text' -> "text' (or the double-quote mirror): unbalanced quote,
        // a real SyntaxError. Same defect family as the old Strategy 3.
        Match single = Regex.Match(line, @"'[^']*'");
        if (single.Success)
        {
            string original = single.Value;
            string askew = "\"" + original.Substring(1, original.Length - 2) + "'";
            return line.Remove(single.Index, single.Length).Insert(single.Index, askew);
        }
        Match dbl = Regex.Match(line, "\"[^\"]*\"");
        if (dbl.Success)
        {
            string original = dbl.Value;
            string askew = "'" + original.Substring(1, original.Length - 2) + "\"";
            return line.Remove(dbl.Index, dbl.Length).Insert(dbl.Index, askew);
        }
        return null;
    }

    private string TryIndentationBug(string line)
    {
        // Remove indentation from an indented line (IndentationError), or
        // add a bogus level to a plain statement. Block headers only get
        // the add-branch exclusion because their own bugs (missing colon,
        // keyword misspell) are more instructive.
        if (line.StartsWith("    ")) return line.TrimStart();
        if (!line.StartsWith("for") && !line.StartsWith("if")
            && !line.StartsWith("while") && !line.StartsWith("def")
            && !line.StartsWith("else") && !line.StartsWith("elif"))
            return "    " + line;
        return null;
    }

    private static string ForceBug(string line)
    {
        // Last-resort transposition of the first two non-whitespace
        // characters (the old Strategy 8). Breaks almost any keyword,
        // identifier, or literal without recognizing a pattern.
        string trimmedStart = line.TrimStart();
        string indent = line.Substring(0, line.Length - trimmedStart.Length);
        if (trimmedStart.Length >= 2)
        {
            char[] chars = trimmedStart.ToCharArray();
            char tmp = chars[0];
            chars[0] = chars[1];
            chars[1] = tmp;
            string swapped = indent + new string(chars);
            return swapped != line ? swapped : line + "x";
        }
        return line + "x";
    }

    // ------------------------------------------------------------------
    // Option synthesis.
    // ------------------------------------------------------------------

    /// <summary>
    /// Wrong fix options for the BUGGY line: the unchanged bug ("leave it
    /// as is") plus plausible-but-wrong variants of the CLEAN line.
    /// Collected from every applicable family and shuffled (carried over
    /// from the earlier fix that replaced the deterministic if/elif
    /// chain). Per the design rule, NO operator mutations: the old pool
    /// flipped + - * / and = == here, and those options are gone.
    /// </summary>
    private List<string> GenerateWrongOptions(string correctLine, string buggedLine)
    {
        List<string> result = new List<string>();
        if (NormKey(buggedLine) != NormKey(correctLine))
            result.Add(buggedLine);

        List<string> candidates = new List<string>();

        string kw = TryKeywordMisspell(correctLine);
        if (kw != null) candidates.Add(kw);
        string kw2 = TryKeywordMisspell(correctLine);
        if (kw2 != null) candidates.Add(kw2);

        string qm = TryQuoteMismatch(correctLine);
        if (qm != null) candidates.Add(qm);

        string dp = TryDropParen(correctLine);
        if (dp != null) candidates.Add(dp);

        string dc = TryDropColon(correctLine);
        if (dc != null) candidates.Add(dc);

        string idm = TryIdentifierMangle(correctLine, null);
        if (idm != null) candidates.Add(idm);

        if (!string.IsNullOrEmpty(template.variableName)
            && correctLine.Contains(template.variableName))
        {
            // String-vs-variable lesson option, kept from the old pool.
            candidates.Add(correctLine.Replace(template.variableName,
                                               $"'{template.variableName}'"));
            candidates.Add(correctLine.Replace(template.variableName,
                                               template.variableName + "a"));
        }

        string ind = TryIndentationBug(correctLine);
        if (ind != null) candidates.Add(ind);

        candidates = candidates
            .Where(c => NormKey(c) != NormKey(correctLine))
            .Distinct()
            .ToList();
        ShuffleList(candidates);
        result.AddRange(candidates);
        return result;
    }

    /// <summary>
    /// Decoy options for a CORRECT line: plausible "looks like a bug"
    /// variants the player must reject. Same families as the injected
    /// bugs so surface patterns alone never give the answer away, and --
    /// per the design rule -- no operator mutations.
    /// </summary>
    private List<string> GenerateDecoys(string cleanLine)
    {
        List<string> candidates = new List<string>();

        string kw = TryKeywordMisspell(cleanLine);
        if (kw != null) candidates.Add(kw);
        string kw2 = TryKeywordMisspell(cleanLine);
        if (kw2 != null) candidates.Add(kw2);

        string qm = TryQuoteMismatch(cleanLine);
        if (qm != null) candidates.Add(qm);

        string dp = TryDropParen(cleanLine);
        if (dp != null) candidates.Add(dp);

        string dc = TryDropColon(cleanLine);
        if (dc != null) candidates.Add(dc);

        string idm = TryIdentifierMangle(cleanLine, null);
        if (idm != null) candidates.Add(idm);

        if (!string.IsNullOrEmpty(template.variableName)
            && cleanLine.Contains(template.variableName))
            candidates.Add(cleanLine.Replace(template.variableName,
                                             template.variableName + "a"));

        if (cleanLine.StartsWith("    "))
        {
            candidates.Add(cleanLine.TrimStart());
            candidates.Add("        " + cleanLine.TrimStart());
        }
        else
        {
            candidates.Add("    " + cleanLine);
        }

        if (candidates.Count == 0)
            candidates.Add("    " + cleanLine);

        candidates = candidates.Where(c => NormKey(c) != NormKey(cleanLine)).Distinct().ToList();
        ShuffleList(candidates);
        return candidates;
    }

    /// <summary>
    /// Pads an option list to exactly 3 DISTINCT entries. The numbered
    /// comment backstop can never collide with anything produced above,
    /// so termination is guaranteed.
    /// </summary>
    private void PadToThree(List<string> lineOptions, int lineIndex)
    {
        string line = template.codeLines[lineIndex];
        int variant = 0;
        int guard = 0;
        while (lineOptions.Count < 3 && guard++ < 12)
        {
            string pad = GenerateFallbackOption(lineIndex, variant++);
            if (!ContainsNorm(lineOptions, pad)) lineOptions.Add(pad);
        }
        while (lineOptions.Count < 3)
            lineOptions.Add(line + " # fix " + lineOptions.Count);
    }

    private string GenerateFallbackOption(int lineIndex, int variant)
    {
        string line = template.codeLines[lineIndex];
        switch (variant % 4)
        {
            case 0:
                if (!string.IsNullOrEmpty(template.variableName)
                    && line.Contains(template.variableName))
                    return line.Replace(template.variableName,
                                        template.variableName + "_err");
                break;
            case 1:
                {
                    string qm = TryQuoteMismatch(line);
                    if (qm != null) return qm;
                    break;
                }
            case 2:
                {
                    string idm = TryIdentifierMangle(line, null);
                    if (idm != null) return idm;
                    break;
                }
            case 3:
                {
                    string dc = TryDropColon(line);
                    if (dc != null) return dc;
                    break;
                }
        }
        return "    " + line;
    }

    // ------------------------------------------------------------------
    // Validity gate + normalization helpers.
    // ------------------------------------------------------------------

    private void ValidatePuzzle()
    {
        if (DiffKey(buggedLine) == DiffKey(correctFix))
            Debug.LogError("[SpotTheBugPuzzleFormat] INVALID PUZZLE: the bugged line equals the correct fix.");

        if (correctLineIndex < 0 || correctLineIndex >= allLineFixOptions.Count)
        {
            Debug.LogError("[SpotTheBugPuzzleFormat] INVALID PUZZLE: bug line index out of range.");
            return;
        }

        if (!ContainsNorm(allLineFixOptions[correctLineIndex], correctFix))
            Debug.LogError("[SpotTheBugPuzzleFormat] INVALID PUZZLE: correct fix missing from the bug line's options.");

        for (int i = 0; i < allLineFixOptions.Count; i++)
        {
            List<string> opts = allLineFixOptions[i];
            if (opts.Count != 3)
                Debug.LogError($"[SpotTheBugPuzzleFormat] INVALID PUZZLE: line {i} has {opts.Count} options, expected 3.");
            for (int a = 0; a < opts.Count; a++)
                for (int b = a + 1; b < opts.Count; b++)
                    if (NormKey(opts[a]) == NormKey(opts[b]))
                        Debug.LogError($"[SpotTheBugPuzzleFormat] INVALID PUZZLE: duplicate option card on line {i} ('{opts[a]}').");
        }

        // Design rule: a GENERATED bug never touches operators. (Authored
        // bugs are exempt: loop_stb_001's real fix legitimately swaps
        // "count + 1" for "count - 1".)
        if (!authoredBug)
        {
            foreach (string op in OperatorTokens)
                if (CountOccurrences(correctFix, op) != CountOccurrences(buggedLine, op))
                    Debug.LogError($"[SpotTheBugPuzzleFormat] OPERATOR MUTATED ('{op}') between the clean and bugged line -- basic operators must stay untouched.");
        }
    }

    /// Whitespace-collapsed identity that PRESERVES leading indentation.
    /// Used for the "did the bug actually change anything" checks, where
    /// adding/removing indentation IS the bug and must not read as a
    /// no-op. (NormKey strips the indent, so it must never be used
    /// there.)
    private static string DiffKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string trimmedEnd = s.TrimEnd();
        int leading = trimmedEnd.Length - trimmedEnd.TrimStart().Length;
        string body = Regex.Replace(trimmedEnd.Substring(leading), @"\s+", " ");
        return new string(' ', leading) + "\u0001" + body;
    }

    /// Whitespace-collapsed identity, so "print(x)  " and "print(x)" can
    /// never ship as two separate cards and trailing-space variants can
    /// never hide a duplicate.
    private static string NormKey(string s)
    {
        if (s == null) return "";
        return Regex.Replace(s.Trim(), @"\s+", " ");
    }

    private static bool ContainsNorm(List<string> list, string item)
    {
        string key = NormKey(item);
        foreach (string x in list)
            if (NormKey(x) == key) return true;
        return false;
    }

    private static void AddDistinct(List<string> list, string item, int cap)
    {
        if (list.Count >= cap) return;
        if (ContainsNorm(list, item)) return;
        list.Add(item);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private void ShuffleList(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    private void ShuffleList(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}