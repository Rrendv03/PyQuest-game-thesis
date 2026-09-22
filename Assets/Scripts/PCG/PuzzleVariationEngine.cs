using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// Pin bare `Random` to UnityEngine.Random. Every call in this file is the
// static Random.Range API. Without this alias, adding `using System;`
// (e.g. via a Visual Studio quick-fix) makes `Random` ambiguous between
// UnityEngine.Random and System.Random (CS0104). Do NOT remove this alias.
using Random = UnityEngine.Random;

/// <summary>
/// Load-time and per-generation variety engine for PyQuest puzzles.
///
/// TWO responsibilities, both data-compatible with the existing pipeline:
///
/// 1. ExpandAll (called once after puzzle_templates.json is parsed):
///    templates containing slot tokens ({name}, {num}, {msg}, ...) are
///    expanded into VariantsPerSkeleton concrete instances with distinct
///    fills, and simulatable assignment/print skeletons additionally yield
///    Intermediate and Advanced variants (extra operand terms, harder value
///    ranges). Everything downstream (candidate filtering, history,
///    mutation, format handlers) sees ordinary PuzzleTemplates, so no UI,
///    scene or format-handler changes are needed.
///
/// 2. ForgeDistractors (called at the end of PCGEngine.MutatePuzzlePublic):
///    replaces stale, gibberish or nuance-gotcha distractors with
///    evaluator-verified, familiarity-filtered wrong options synthesized
///    from the ALREADY-RENAMED code lines, so wrong answers can never drift
///    out of sync with the code the player sees.
///
/// The distractor contract (mirrored by tools/validate_pcg.py):
///   - distinct from the correct answer and from each other
///   - same statement family as the correct line (PairACode): shares the
///     leading verb or one of its variables, so options can't be weeded by
///     surface pattern alone
///   - no nuance gotchas: quote-toggle, = vs ==, case-only and colon-only
///     differences are rejected outright
///   - no invented APIs: every identifier must be a known builtin or a
///     variable defined in the snippet (kills the runic(25) class)
///   - PredictTheOutput options must be near-misses of the real output:
///     numeric adjacency, snippet literals, or sanctioned misconceptions
///     (NameError, name-vs-value echoes, off-by-one loop listings)
///
/// Slots are drawn ONCE per instance and reused wherever the same token
/// appears, so {name} in an assignment and in its print always agree.
/// </summary>
public static class PuzzleVariationEngine
{
    public const int VariantsPerSkeleton = 10;

    public static readonly string[] NamePool =
    {
        "mana", "health", "score", "level", "gold", "damage", "defense",
        "stamina", "magic", "runes", "power", "shield", "energy", "speed",
        "armor", "quest", "rank", "coins", "lives", "points", "strength",
        "agility", "wisdom", "luck", "vigor", "guard", "focus", "morale",
        "essence", "charge", "rating", "tally", "streak", "combo", "supply",
        "reserve", "endurance", "fortune", "resolve", "insight"
    };

    static readonly string[] StrPool =
    {
        "'Hero'", "'Wizard'", "'Archer'", "'Knight'", "'Mage'", "'Rogue'",
        "'Paladin'", "'Hunter'", "'Warrior'", "'Sage'", "'Scout'", "'Ranger'",
        "'Monk'", "'Druid'", "'Bard'", "'Cleric'", "'Nomad'", "'Guardian'",
        "'Sentinel'", "'Wanderer'", "'Champion'", "'Seer'"
    };

    static readonly string[] MsgPool =
    {
        "'Level Up'", "'Quest Complete'", "'Victory'", "'Try Again'",
        "'Well Done'", "'Keep Going'", "'Almost There'", "'New Record'",
        "'Boss Defeated'", "'Path Unlocked'", "'Sanctum Cleared'", "'Not Yet'"
    };

    // Numeric ranges per difficulty tier: Beginner / Intermediate / Advanced.
    static readonly int[][] NumRanges =
    {
        new[] { 2, 12 },
        new[] { 5, 60 },
        new[] { 6, 99 }
    };

    static readonly Regex TokenRegex = new Regex(
        @"\{(name|name2|name3|num|num2|num3|small|str|str2|msg|msg2)\}");

    static readonly string[] ControlFlowMarkers =
    {
        "if ", "elif ", "else", "for ", "while ", "def ", "input("
    };

    static readonly string[] KnownCallables =
    {
        "print", "input", "int", "str", "len", "range", "float", "round",
        "list", "dict", "set", "tuple", "sum", "min", "max", "abs",
        "sorted", "enumerate", "bool"
    };

    static readonly Regex WordRegex = new Regex(@"[A-Za-z_]\w*");

    // Recently-used distractor kinds per bucket, to prevent the same
    // mutation family appearing two puzzles in a row (anti-stagnation).
    static readonly Dictionary<string, List<string>> recentKinds =
        new Dictionary<string, List<string>>();

    // ------------------------------------------------------------------
    // Expansion (load time)
    // ------------------------------------------------------------------

    public static bool HasTokens(PuzzleTemplate t)
    {
        string blob = string.Join(" ", t.codeLines ?? new List<string>())
                    + " " + string.Join(" ", t.distractors ?? new List<string>())
                    + " " + (t.correctAnswer ?? "")
                    + " " + (t.variableName ?? "")
                    + " " + (t.variableValue ?? "")
                    + " " + (t.goalText ?? "");
        return TokenRegex.IsMatch(blob);
    }

    /// <summary>
    /// Returns the playable template list: hand-authored templates pass
    /// through untouched; token templates become VariantsPerSkeleton
    /// concrete instances; evaluator-safe skeletons additionally spawn
    /// Intermediate and Advanced difficulty variants.
    /// </summary>
    public static List<PuzzleTemplate> ExpandAll(List<PuzzleTemplate> source)
    {
        var result = new List<PuzzleTemplate>();
        var seenIds = new HashSet<string>();
        if (source == null) return result;

        foreach (PuzzleTemplate original in source)
        {
            if (original == null) continue;

            if (!HasTokens(original))
            {
                result.Add(original);
                continue;
            }

            int tier = (int)original.difficulty;
            bool scalable = CanScale(original);

            for (int k = 0; k < VariantsPerSkeleton; k++)
                Push(CloneWithFill(original, tier), result, seenIds,
                     original.id + "_v" + k, null);

            if (scalable)
            {
                for (int scaledTier = 1; scaledTier <= 2; scaledTier++)
                {
                    for (int k = 0; k < VariantsPerSkeleton; k++)
                    {
                        PuzzleTemplate fill = CloneWithFill(original, scaledTier);
                        List<string> scaled = ScaleCodeLines(fill.codeLines, scaledTier);
                        if (scaled == null) continue;
                        fill.codeLines = scaled;
                        Push(fill, result, seenIds,
                             original.id + "_s" + scaledTier + "_v" + k,
                             (DifficultyTier)scaledTier);
                    }
                }
            }
        }
        return result;
    }

    static void Push(PuzzleTemplate variant, List<PuzzleTemplate> sink,
                     HashSet<string> seenIds, string id, DifficultyTier? forcedTier)
    {
        while (seenIds.Contains(id))
            id = id + "_x";
        seenIds.Add(id);

        variant.id = id;
        if (forcedTier.HasValue) variant.difficulty = forcedTier.Value;

        // For PredictTheOutput the evaluator is ground truth for the answer
        // whenever the snippet is simulatable (same policy as the mutator).
        if (variant.puzzleType == PuzzleType.PredictTheOutput)
        {
            string output;
            if (MiniPythonEvaluator.TrySimulate(variant.codeLines, out output))
                variant.correctAnswer = output;
        }
        sink.Add(variant);
    }

    static PuzzleTemplate CloneWithFill(PuzzleTemplate t, int tier)
    {
        var usedNames = new HashSet<string>();
        var usedNums = new HashSet<int>();
        var usedMsgs = new HashSet<string>();
        var usedStrs = new HashSet<string>();
        var drawn = new Dictionary<string, string>();

        MatchEvaluator fill = match =>
        {
            string kind = match.Groups[1].Value;
            if (!drawn.ContainsKey(kind))
            {
                string v = DrawSlot(kind, tier, usedNames, usedNums, usedMsgs, usedStrs);
                if (kind == "name" || kind == "name2" || kind == "name3")
                    usedNames.Add(v);
                else if (kind == "num" || kind == "num2" || kind == "num3"
                         || kind == "small")
                    usedNums.Add(int.Parse(v));
                else if (kind == "msg" || kind == "msg2")
                    usedMsgs.Add(v);
                else if (kind == "str" || kind == "str2")
                    usedStrs.Add(v);
                drawn[kind] = v;
            }
            return drawn[kind];
        };

        System.Func<string, string> sub = text => TokenRegex.Replace(text ?? "", fill);

        return new PuzzleTemplate
        {
            id = t.id,
            knowledgeComponent = t.knowledgeComponent,
            puzzleType = t.puzzleType,
            difficulty = t.difficulty,
            codeLines = (t.codeLines ?? new List<string>()).Select(sub).ToList(),
            correctAnswer = sub(t.correctAnswer ?? ""),
            bugLineIndex = t.bugLineIndex,
            correctOrder = new List<int>(t.correctOrder ?? new List<int>()),
            distractors = (t.distractors ?? new List<string>()).Select(sub).ToList(),
            variableName = sub(t.variableName ?? ""),
            variableValue = sub(t.variableValue ?? ""),
            goalText = sub(t.goalText ?? ""),
            additionalVariables = t.additionalVariables != null
                ? t.additionalVariables
                    .Select(v => new VariablePair { name = v.name, value = v.value })
                    .ToList()
                : new List<VariablePair>()
        };
    }

    static string DrawSlot(string kind, int tier,
                           HashSet<string> usedNames, HashSet<int> usedNums,
                           HashSet<string> usedMsgs, HashSet<string> usedStrs)
    {
        if (kind == "small")
        {
            for (int i = 0; i < 40; i++)
            {
                int v = Random.Range(1, 6); // 1..5 inclusive
                if (!usedNums.Contains(v)) return v.ToString();
            }
            return "1";
        }
        if (kind == "str" || kind == "str2")
        {
            // str2 excludes str (same convention as name/num slots), so a
            // template can never render two identical string slots.
            for (int i = 0; i < 40; i++)
            {
                string v = StrPool[Random.Range(0, StrPool.Length)];
                if (!usedStrs.Contains(v)) return v;
            }
            return StrPool[Random.Range(0, StrPool.Length)];
        }
        if (kind == "msg" || kind == "msg2")
        {
            // msg2 excludes msg: otherwise ~1 in 12 if/else PairACode
            // instances render "show X ... otherwise show X" with the same
            // message in both branches, and the {msg} distractor collapses
            // into the correct answer.
            for (int i = 0; i < 40; i++)
            {
                string m = MsgPool[Random.Range(0, MsgPool.Length)];
                if (!usedMsgs.Contains(m)) return m;
            }
            return MsgPool[Random.Range(0, MsgPool.Length)];
        }
        if (kind == "num" || kind == "num2" || kind == "num3")
            return DrawNumber(tier, usedNums).ToString();
        // name / name2 / name3: distinct identifiers
        return DrawName(usedNames);
    }

    static int DrawNumber(int tier, HashSet<int> usedNums)
    {
        int[] range = NumRanges[Mathf.Clamp(tier, 0, NumRanges.Length - 1)];
        for (int i = 0; i < 40; i++)
        {
            int n = Random.Range(range[0], range[1] + 1);
            if (!usedNums.Contains(n)) return n;
        }
        return range[1] + 1;
    }

    static string DrawName(HashSet<string> usedNames)
    {
        for (int i = 0; i < 60; i++)
        {
            string n = NamePool[Random.Range(0, NamePool.Length)];
            if (!usedNames.Contains(n)) return n;
        }
        return NamePool[Random.Range(0, NamePool.Length)] + "2";
    }

    // ------------------------------------------------------------------
    // Difficulty scaling (only evaluator-safe, numeric-tail snippets)
    // ------------------------------------------------------------------

    public static bool ContainsControlFlow(List<string> lines)
    {
        string blob = string.Join(" ", lines ?? new List<string>());
        foreach (string marker in ControlFlowMarkers)
            if (blob.Contains(marker)) return true;
        return false;
    }

    public static bool CanScale(PuzzleTemplate t)
    {
        if (t.puzzleType != PuzzleType.PredictTheOutput
            && t.puzzleType != PuzzleType.PairACode
            && t.puzzleType != PuzzleType.TrueOrFalse)
            return false;
        if (ContainsControlFlow(t.codeLines)) return false;

        Dictionary<string, bool> envIsString;
        string output;
        if (!MiniPythonEvaluator.TrySimulateDetailed(t.codeLines, out output,
                out envIsString))
            return false;

        int lastAssign = -1;
        for (int i = 0; i < t.codeLines.Count; i++)
        {
            string s = t.codeLines[i].Trim();
            if (Regex.IsMatch(s, @"^\w+\s*=(?!=)") && !s.StartsWith("print"))
                lastAssign = i;
        }
        if (lastAssign < 0) return false;

        Match m = Regex.Match(t.codeLines[lastAssign].Trim(),
                              @"^(\w+)\s*=(?!=)\s*(.+)$");
        if (m.Success && envIsString.ContainsKey(m.Groups[1].Value)
            && envIsString[m.Groups[1].Value])
            return false; // string tail (greetings, aliases): not extendable
        return true;
    }

    /// <summary>
    /// Extends the last assignment's expression to add cognitive load:
    /// tier 1 adds one small term, tier 2 adds a *small factor plus a +/-,
    /// term. The grammar stays flat (no parentheses) so MiniPythonEvaluator
    /// remains authoritative for PredictTheOutput answers.
    /// </summary>
    public static List<string> ScaleCodeLines(List<string> codeLines, int tier)
    {
        int lastAssign = -1;
        for (int i = 0; i < codeLines.Count; i++)
        {
            string s = codeLines[i].Trim();
            if (Regex.IsMatch(s, @"^\w+\s*=(?!=)") && !s.StartsWith("print"))
                lastAssign = i;
        }
        if (lastAssign < 0) return null;

        var lines = new List<string>(codeLines);
        if (tier == 1)
            lines[lastAssign] = lines[lastAssign] + " + " + Random.Range(2, 10);
        else
            lines[lastAssign] = lines[lastAssign] + " * " + Random.Range(2, 5)
                              + " + " + Random.Range(3, 20);
        return lines;
    }

    // ------------------------------------------------------------------
    // Distractor contract validators
    // ------------------------------------------------------------------

    static string Normalized(string text)
    {
        return text == null ? "" : Regex.Replace(text, @"\s+", "");
    }

    /// <summary>Quote-toggle, = vs ==, case-only and colon-only differences
    /// are observation gotchas, not reasoning: reject them outright.</summary>
    public static bool IsNuancePair(string correct, string candidate)
    {
        string a = Normalized(correct), b = Normalized(candidate);
        if (a == b) return false; // identical handled by distinctness checks
        if (a.Replace("'", "").Replace("\"", "")
             == b.Replace("'", "").Replace("\"", ""))
            return true;
        if (a.Replace("==", "=") == b.Replace("==", "=")) return true;
        if (a.ToLower() == b.ToLower()) return true;
        if (a.TrimEnd(':') == b.TrimEnd(':')) return true;
        return false;
    }

    static HashSet<string> DefinedVariables(List<string> codeLines)
    {
        var defs = new HashSet<string>();
        foreach (string line in codeLines)
        {
            string s = line.Trim();
            if (s.StartsWith("#")) continue;
            Match m = Regex.Match(s, @"^(\w+)\s*=(?!=)");
            if (m.Success) defs.Add(m.Groups[1].Value);
            m = Regex.Match(s, @"^for\s+(\w+)\s+in\s+range");
            if (m.Success) defs.Add(m.Groups[1].Value);
        }
        return defs;
    }

    /// <summary>True when the line references an identifier that is neither
    /// a known builtin nor defined in the snippet - the runic(25) class of
    /// instantly-weedable junk options.</summary>
    public static bool IsGibberishLine(string candidate, List<string> codeLines)
    {
        string s = candidate.Trim();
        if (s.StartsWith("#")) return false;

        var allowed = new HashSet<string>(KnownCallables);
        allowed.UnionWith(DefinedVariables(codeLines));
        foreach (string kw in new[]
                 { "True", "False", "None", "in", "and", "or", "not", "return",
                   "pass", "break", "continue", "for", "while", "if", "elif", "def" })
            allowed.Add(kw);

        // blank quoted literals, then every remaining word must be resolvable
        string stripped = Regex.Replace(s, "'[^']*'", "''");
        foreach (Match m in WordRegex.Matches(stripped))
            if (!allowed.Contains(m.Value)) return true;
        return false;
    }

    static bool IsInt(string s)
    {
        int v;
        return int.TryParse((s ?? "").Trim(), out v);
    }

    static float CharOverlap(string a, string b)
    {
        a = (a ?? "").ToLower();
        b = (b ?? "").ToLower();
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
        var charsA = new HashSet<char>(a);
        int shared = 0;
        foreach (char c in charsA)
            if (b.IndexOf(c) >= 0) shared++;
        return shared / Mathf.Max(1, charsA.Count);
    }

    /// <summary>PredictTheOutput options must look like near-misses of the
    /// real output: numerically adjacent, lexically overlapping, same shape,
    /// snippet literals, or a sanctioned misconception (error names,
    /// name-vs-value echoes, only-the-last-line listings).</summary>
    public static bool PtoDistractorFamiliar(string correct, string candidate,
                                             HashSet<string> snippetLiterals)
    {
        candidate = candidate ?? "";
        if (snippetLiterals != null && snippetLiterals.Contains(candidate)
            && candidate != correct)
            return true;

        if (correct == candidate) return false;

        bool cMulti = correct.Contains("\n"), dMulti = candidate.Contains("\n");
        if (cMulti && !dMulti && IsInt(candidate))
        {
            // sanctioned: printing ONLY the last line of a multi-line output
            string[] rows = correct.TrimEnd().Split('\n');
            int last, cand;
            return int.TryParse(rows[rows.Length - 1], out last)
                && int.TryParse(candidate, out cand) && cand == last;
        }
        if (cMulti != dMulti) return false;

        if (cMulti)
        {
            int cLines = correct.Split('\n').Length;
            int dLines = candidate.Split('\n').Length;
            if (Mathf.Abs(cLines - dLines) > 1) return false;
            return CharOverlap(candidate, correct) >= 0.4f;
        }

        if (IsInt(correct) && IsInt(candidate))
        {
            int c = int.Parse(correct.Trim());
            int d = int.Parse(candidate.Trim());
            return Mathf.Abs(c - d) <= Mathf.Max(6, Mathf.Abs(c));
        }

        if (IsInt(correct) != IsInt(candidate))
        {
            // sanctioned misconceptions: error names or variable-name echoes
            if (candidate == "NameError" || candidate == "TypeError"
                || candidate == "SyntaxError")
                return true;
            return Regex.IsMatch(candidate, @"^[a-z_]\w*$");
        }

        if (Regex.IsMatch(candidate, @"^[a-z_]\w*$")) return true; // name echo

        string normA = Normalized(candidate).Replace("'", "").Replace("\"", "");
        string normC = Normalized(correct).Replace("'", "").Replace("\"", "");
        if (normA == normC) return true; // quoting toggle
        return CharOverlap(candidate, correct) >= 0.4f;
    }

    // ------------------------------------------------------------------
    // Distractor forging (per generation, AFTER variable renaming)
    // ------------------------------------------------------------------

    static void NoteKind(string bucket, string kind)
    {
        List<string> q;
        if (!recentKinds.TryGetValue(bucket, out q))
        {
            q = new List<string>();
            recentKinds[bucket] = q;
        }
        q.Add(kind);
        while (q.Count > 2) q.RemoveAt(0);
    }

    static bool KindPenalized(string bucket, string kind)
    {
        List<string> q;
        return recentKinds.TryGetValue(bucket, out q) && q.Contains(kind);
    }

    /// <summary>
    /// Rebuilds the template's distractor list in place. Called at the end
    /// of PCGEngine.MutatePuzzlePublic so every synthesized option
    /// references the variables the player actually sees.
    /// </summary>
    public static void ForgeDistractors(PuzzleTemplate t)
    {
        if (t == null) return;

        if (t.puzzleType == PuzzleType.PredictTheOutput)
        {
            string correct = t.correctAnswer ?? "";
            List<string> forged = ForgePto(t, correct, 3);
            if (forged.Count >= 3)
            {
                t.distractors = forged;
                return;
            }
            // merge in authored distractors that pass the familiarity rule
            HashSet<string> lit = SnippetLiterals(t.codeLines);
            var merged = new List<string>();
            foreach (string x in forged) if (!merged.Contains(x)) merged.Add(x);
            foreach (string d in t.distractors ?? new List<string>())
                if (d != correct && !merged.Contains(d)
                    && PtoDistractorFamiliar(correct, d, lit))
                    merged.Add(d);
            if (merged.Count >= 3)
                t.distractors = merged.GetRange(0, 3);
            return;
        }

        if (t.puzzleType == PuzzleType.PairACode)
        {
            List<string> forged = ForgePac(t, 3);
            if (forged.Count == 3) t.distractors = forged;
            return;
        }

        if (t.puzzleType == PuzzleType.FillInTheBlank)
        {
            t.distractors = ForgeFitbOptions(t, 3);
        }
    }

    static HashSet<string> SnippetLiterals(List<string> codeLines)
    {
        var literals = new HashSet<string>();
        string snippet = string.Join(" ", codeLines ?? new List<string>());
        foreach (Match m in Regex.Matches(snippet, "'([^']*)'"))
            if (m.Groups[1].Value.Trim().Length > 0)
                literals.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(snippet, @"\b\d+\b"))
            literals.Add(m.Value);
        return literals;
    }

    // -- PTO ---------------------------------------------------------------

    static IEnumerable<Pair<string, List<string>>> PtoMutations(PuzzleTemplate t)
    {
        var lines = new List<string>(t.codeLines);

        // F1: swap operands in the last print. Only two-operand shapes; a
        // swapped + or * computes the same value and is filtered later.
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            Match m = Regex.Match(lines[i].Trim(), @"^print\((.+)\)$");
            if (m == null || !m.Success) continue;
            string inner = m.Groups[1].Value.Trim();
            if (inner.Contains(","))
            {
                var args = inner.Split(',').Select(a => a.Trim()).ToList();
                if (args.Count >= 2)
                {
                    var reordered = new List<string> { args[args.Count - 1] };
                    for (int j = 1; j < args.Count - 1; j++) reordered.Add(args[j]);
                    reordered.Add(args[0]);
                    var mutated = new List<string>(lines);
                    mutated[i] = "print(" + string.Join(", ", reordered) + ")";
                    yield return Pair<string, List<string>>.Create("swap-args", mutated);
                }
            }
            else
            {
                Match m2 = Regex.Match(inner, @"^(\w+)\s*([+\-*])\s*(\w+)$");
                if (m2.Success && m2.Groups[2].Value == "-")
                {
                    var mutated = new List<string>(lines);
                    mutated[i] = "print(" + m2.Groups[3].Value + " - "
                               + m2.Groups[1].Value + ")";
                    yield return Pair<string, List<string>>.Create("swap-terms", mutated);
                }
            }
            break;
        }

        // F2: flip the first +/- operator outside string literals
        for (int i = 0; i < lines.Count; i++)
        {
            string[] parts = lines[i].Split('\'');
            bool flipped = false;
            for (int j = 0; j < parts.Length; j += 2)
            {
                if (parts[j].Contains(" + "))
                {
                    parts[j] = ReplaceFirst(parts[j], " + ", " - ");
                    flipped = true;
                    break;
                }
                if (parts[j].Contains(" - "))
                {
                    parts[j] = ReplaceFirst(parts[j], " - ", " + ");
                    flipped = true;
                    break;
                }
            }
            if (flipped)
            {
                var mutated = new List<string>(lines);
                mutated[i] = string.Join("'", parts);
                yield return Pair<string, List<string>>.Create("flip-op", mutated);
                break;
            }
        }

        // F3: nudge one numeric literal
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("#")) continue;
            Match num = Regex.Match(lines[i], @"\d+");
            if (num.Success)
            {
                int n = int.Parse(num.Value);
                int[] deltas = { -1, 1, -2, 2, -3, 3 };
                int nudged = Mathf.Max(0, n + deltas[Random.Range(0, deltas.Length)]);
                if (nudged != n)
                {
                    var mutated = new List<string>(lines);
                    mutated[i] = mutated[i].Replace(num.Value, nudged.ToString());
                    yield return Pair<string, List<string>>.Create("nudge", mutated);
                }
                break;
            }
        }

        // F4: drop the last assignment -> NameError family
        int assignIdx = -1;
        for (int i = 0; i < lines.Count; i++)
            if (Regex.IsMatch(lines[i].Trim(), @"^\w+\s*=(?!=)"))
                assignIdx = i;
        if (assignIdx >= 0)
        {
            var mutated = new List<string>(lines);
            mutated.RemoveAt(assignIdx);
            yield return Pair<string, List<string>>.Create("drop-assign", mutated);
        }
    }

    static string ReplaceFirst(string source, string search, string replacement)
    {
        int idx = source.IndexOf(search);
        if (idx < 0) return source;
        return source.Substring(0, idx) + replacement
             + source.Substring(idx + search.Length);
    }

    static List<string> ForgePto(PuzzleTemplate t, string correct, int want)
    {
        correct = correct ?? "";
        string bucket = t.knowledgeComponent + "|" + (int)t.puzzleType + "|"
                      + (int)t.difficulty;
        HashSet<string> literals = SnippetLiterals(t.codeLines);

        var candidates = new List<Pair<string, string>>();
        var seenVals = new HashSet<string>();

        System.Action<string, string> add = (kind, outVal) =>
        {
            outVal = outVal ?? "";
            if (outVal == correct || seenVals.Contains(outVal)) return;
            if (!PtoDistractorFamiliar(correct, outVal, literals)) return;
            seenVals.Add(outVal);
            candidates.Add(Pair<string, string>.Create(kind, outVal));
        };

        foreach (Pair<string, List<string>> mutation in PtoMutations(t))
        {
            string outVal;
            if (!MiniPythonEvaluator.TrySimulate(mutation.Second, out outVal))
            {
                if (mutation.First == "drop-assign") outVal = "NameError";
                else continue;
            }
            add(mutation.First, outVal);
        }

        // Fallback families: classic misconceptions, all verdict-by-reasoning.
        Dictionary<string, bool> envIsString;
        string traceOut;
        bool traced = MiniPythonEvaluator.TrySimulateDetailed(t.codeLines,
            out traceOut, out envIsString);
        if (traced)
        {
            // printing a DIFFERENT variable's value: wrong-variable mixup.
            // Probe appends print(v) to the FULL snippet so reassigned
            // variables report their FINAL values, matching the traced env.
            foreach (string vname in envIsString.Keys)
            {
                string varOut;
                var probe = new List<string>(t.codeLines);
                probe.Add("print(" + vname + ")");
                if (MiniPythonEvaluator.TrySimulate(probe, out varOut))
                    add("other-var", varOut);
            }
        }
        if (IsInt(correct))
        {
            int c = int.Parse(correct.Trim());
            int[] deltas = { 1, -1, 2, -2, 3, -3, 10, -10 };
            foreach (int d in deltas)
                add("offset", (c + d).ToString());
        }
        else if (!correct.Contains("\n"))
        {
            string quoted = correct.StartsWith("'")
                ? correct.Trim('\'')
                : "'" + correct + "'";
            add("quote-toggle", quoted);
            if (traced)
                foreach (string vname in envIsString.Keys)
                    add("name-echo", vname);
            foreach (string litValue in literals)
                add("literal-swap", litValue);
            add("suffix", correct + "!");
        }
        else
        {
            // multi-line outputs: numeric loops get the four classic
            // misreadings; string rows get reordering mixups.
            string[] rows = correct.Split('\n');
            bool allInt = true;
            foreach (string r in rows) if (!IsInt(r)) { allInt = false; break; }
            if (allInt)
            {
                int last = int.Parse(rows[rows.Length - 1].Trim());
                add("only-last", last.ToString());
                var rev = new List<string>(rows);
                rev.Reverse();
                add("reversed", string.Join("\n", rev.ToArray()));
                add("inclusive", correct + "\n" + (last + 1));
                var oneBased = rows
                    .Select(r => (int.Parse(r.Trim()) + 1).ToString()).ToList();
                add("one-based", string.Join("\n", oneBased.ToArray()));
            }
            else
            {
                var rev = new List<string>(rows);
                rev.Reverse();
                add("reversed", string.Join("\n", rev.ToArray()));
                var rotated = rows.Skip(1).Concat(rows.Take(1)).ToList();
                add("rotated", string.Join("\n", rotated.ToArray()));
                add("drop-last",
                    string.Join("\n", rows.Take(rows.Length - 1).ToArray()));
            }
        }

        // tier 0: semantic mutations; tier 1: mixups; tier 2: near-misses.
        // Shuffle inside each tier, deprioritize recently-used kinds, and
        // never let the penalized fallback introduce duplicates.
        var tierMap = new Dictionary<string, int>
        {
            { "other-var", 1 }, { "quote-toggle", 1 }, { "name-echo", 1 },
            { "literal-swap", 1 }, { "only-last", 1 }, { "reversed", 1 },
            { "one-based", 1 }, { "offset", 2 }, { "suffix", 2 }, { "inclusive", 2 }
        };
        var tiered = new List<Pair<string, string>>[3];
        for (int i = 0; i < 3; i++) tiered[i] = new List<Pair<string, string>>();
        int tierOf;
        foreach (var c in candidates)
        {
            tierOf = tierMap.TryGetValue(c.First, out tierOf) ? tierOf : 0;
            tiered[tierOf].Add(c);
        }
        foreach (var list in tiered) Shuffle(list);

        var ranked = new List<Pair<string, string>>();
        foreach (var list in tiered)
            foreach (var c in list)
                if (!KindPenalized(bucket, c.First)) ranked.Add(c);
        if (ranked.Count < want)
        {
            foreach (var list in tiered)
                foreach (var c in list)
                    if (!ranked.Any(x => x.Second == c.Second)) ranked.Add(c);
        }

        var pickedVals = new HashSet<string>();
        var chosen = new List<Pair<string, string>>();
        foreach (var c in ranked)
        {
            if (chosen.Count >= want) break;
            if (pickedVals.Contains(c.Second)) continue;
            pickedVals.Add(c.Second);
            chosen.Add(c);
        }
        foreach (var c in chosen) NoteKind(bucket, c.First);
        return chosen.Select(c => c.Second).ToList();
    }

    // -- PAC ----------------------------------------------------------------

    static List<string> ForgePac(PuzzleTemplate t, int want)
    {
        var lines = new List<string>(t.codeLines);
        string correct = lines[lines.Count - 1].Trim();

        HashSet<string> definedVars = DefinedVariables(lines);
        var headIds = new HashSet<string>();
        foreach (Match m in WordRegex.Matches(correct))
            if (definedVars.Contains(m.Value)) headIds.Add(m.Value);

        System.Func<string, string> headOf = cand =>
            Regex.Split(cand.Trim(), @"[(\s=]")[0];
        System.Func<string, bool> sharesFamily = cand =>
        {
            bool sameHead = headOf(cand) == headOf(correct);
            if (sameHead) return true;
            foreach (Match m in WordRegex.Matches(cand))
                if (definedVars.Contains(m.Value) && correct.Contains(m.Value))
                    return true;
            return false;
        };

        var fam = new List<string>();

        Match printMatch = Regex.Match(correct, @"^print\((.+)\)$");
        if (printMatch.Success)
        {
            string inner = printMatch.Groups[1].Value.Trim();
            if (inner.Contains(","))
            {
                var args = inner.Split(',').Select(a => a.Trim()).ToList();
                var rev = new List<string>(args);
                rev.Reverse();
                fam.Add("print(" + string.Join(", ", rev) + ")");
                fam.Add("print(" + args[0] + ")");
                fam.Add("print(" + string.Join(", ", args) + ", " + args[0] + ")");
            }
            else
            {
                int small = Random.Range(1, 6);
                fam.Add("print(" + inner + " + " + small + ")");
                fam.Add("print(" + MsgPool[Random.Range(0, MsgPool.Length)] + ")");
                fam.Add("print(" + inner + ", " + inner + ")");
            }
        }
        else
        {
            Match m2 = Regex.Match(correct, @"^(\w+)\s*=\s*(.+)$");
            if (m2.Success && m2.Groups[2].Value.Contains("input("))
            {
                string prompt = m2.Groups[2].Value;
                Match pm = Regex.Match(prompt, @"input\((.+)\)");
                string ptxt = pm.Success ? pm.Groups[1].Value : prompt;
                fam.Add("print(" + ptxt + ")");
                fam.Add(m2.Groups[2].Value);
                fam.Add(m2.Groups[1].Value + " = " + ptxt);
            }
            else if (m2.Success)
            {
                string rhs = m2.Groups[2].Value.Trim();
                Match m3 = Regex.Match(rhs, @"^(\w+)\s*([+\-*])\s*(\w+)$");
                if (m3.Success)
                {
                    string swapped = m3.Groups[3].Value + " " + m3.Groups[2].Value
                                   + " " + m3.Groups[1].Value;
                    fam.Add("print(" + rhs + ")");
                    fam.Add(m2.Groups[1].Value + " = " + swapped);
                    fam.Add("print(" + MsgPool[Random.Range(0, MsgPool.Length)] + ")");
                }
                else
                {
                    fam.Add("print(" + rhs + ")");
                    fam.Add(m2.Groups[1].Value + " = " + Random.Range(2, 13));
                    fam.Add("print(" + MsgPool[Random.Range(0, MsgPool.Length)] + ")");
                }
            }
        }

        var seenKeys = new HashSet<string> { Normalized(correct) };
        var result = new List<string>();

        System.Action<string> tryAdd = cand =>
        {
            if (result.Count >= want) return;
            cand = cand.Trim();
            if (cand.Length == 0 || cand == correct) return;
            string key = Normalized(cand);
            if (seenKeys.Contains(key)) return;
            if (IsNuancePair(correct, cand) || IsGibberishLine(cand, lines)) return;
            if (!sharesFamily(cand)) return;
            seenKeys.Add(key);
            result.Add(cand);
        };

        foreach (string d in t.distractors ?? new List<string>())
            tryAdd(d);
        foreach (string cand in fam)
            tryAdd(cand);

        return result;
    }

    // -- FITB ----------------------------------------------------------------
    //
    // Root cause of the old behavior: every fill-in-the-blank template ships
    // correctAnswer == "" and bugLineIndex == -1, so the blank position was
    // inferred by the format file, which always landed on the same token
    // (print) -- and ForgeFitbTopup passed the authored static distractor
    // lists (input/int/str, while/for/else, ...) straight through whenever
    // there were three of them. Players could read the pattern in one session.
    //
    // The fix has two halves, both engine-side so the format file stays dumb:
    //
    //   1. RotateFitbBlank -- called per draw from MutatePuzzlePublic. Scans
    //      the SERVED snippet (post rename, post slot substitution) for
    //      unambiguous blankable tokens -- keywords, identifiers, numbers,
    //      string literals, operators, booleans -- and picks one with a
    //      per-skeleton rotating cursor, ordered round-robin across token
    //      categories with tier-weighted priority. A session-level guard
    //      additionally guarantees two consecutive FITB encounters never ask
    //      for the same token, so "the correct answer is always different"
    //      holds every time the format fires up.
    //
    //   2. ForgeFitbOptions -- rebuilds the option set for the NEW answer
    //      every draw, category-matched: a keyword blank gets keyword
    //      options, a math blank gets operators/numbers, an identifier blank
    //      gets in-scope identifiers and game-pool names. Cross-category
    //      authored junk is only ever a last-resort top-up, never the base.
    //      For single tokens the pool IS the plausibility filter, so the
    //      line-level gibberish validator is deliberately not applied here
    //      (it would reject sanctioned NamePool identifiers for not being
    //      defined in the snippet), and for operator blanks the = vs ==
    //      confusion is the lesson, not a gotcha, so the nuance gate is
    //      relaxed for pure-symbol answers only.

    enum FitbCategory { Keyword, Identifier, Number, StringLiteral, Operator, Boolean, Unknown }

    class FitbCandidate
    {
        public string Token;
        public int LineIndex;
        public FitbCategory Category;
    }

    class FitbTok
    {
        public string Text;
        public FitbCategory Category;
    }

    // Ordered alternation: strings first (atomic -- a word inside a prompt
    // literal must never count as an occurrence of a bare identifier), then
    // multi-char operators before their single-char prefixes, then words,
    // numbers, and finally lone operator symbols.
    static readonly Regex FitbTokenRegex = new Regex(
        @"'[^']*'|\+=|-=|\*=|==|!=|<=|>=|[A-Za-z_]\w*|\d+|[+\-*/%<>=!]");

    static readonly Regex FitbSymbolRegex = new Regex(@"^[+\-*/%<>=!]+$");
    static readonly Regex FitbNumberRegex = new Regex(@"^\d+$");
    static readonly Regex FitbIdentRegex = new Regex(@"^[A-Za-z_]\w*$");
    static readonly Regex FitbStringRegex = new Regex(@"'[^']*'");

    static readonly string[] FitbIoKeywords = { "print", "input" };
    static readonly string[] FitbConvertKeywords =
        { "int", "str", "float", "bool", "len", "list", "range" };
    static readonly string[] FitbLoopKeywords =
        { "for", "while", "in", "break", "continue" };
    static readonly string[] FitbBranchKeywords =
        { "if", "elif", "else", "and", "or", "not" };
    static readonly string[] FitbDefKeywords = { "def", "return" };
    static readonly string[] FitbBooleanTokens = { "True", "False", "None" };

    static readonly string[] AllFitbKeywords =
        FitbIoKeywords.Concat(FitbConvertKeywords).Concat(FitbLoopKeywords)
            .Concat(FitbBranchKeywords).Concat(FitbDefKeywords).ToArray();

    static readonly string[] FitbArithOps = { "+", "-", "*", "/", "%" };
    static readonly string[] FitbCompOps = { "==", "!=", "<", ">", "<=", ">=" };
    static readonly string[] FitbAssignOps = { "=", "+=", "-=", "*=" };

    // Rotation state: one cursor per skeleton (variant/mutation suffixes
    // stripped) plus the last FITB answer served this session.
    static readonly Dictionary<string, int> FitbCursor =
        new Dictionary<string, int>();
    static string LastFitbAnswer = "";

    /// <summary>Picks which token is missing from a fill-in-the-blank
    /// puzzle. Deterministic per skeleton (no RNG): consecutive draws walk
    /// the candidate list, so the blank -- and therefore the correct
    /// answer -- changes every encounter.</summary>
    public static void RotateFitbBlank(PuzzleTemplate t)
    {
        if (t == null || t.puzzleType != PuzzleType.FillInTheBlank) return;

        List<FitbCandidate> candidates = FitbBlankCandidates(t);
        if (candidates.Count == 0) return; // legacy: format infers as before

        string skeleton = FitbSkeletonKey(t.id);
        int cursor;
        FitbCursor.TryGetValue(skeleton, out cursor);

        FitbCandidate pick = candidates[cursor % candidates.Count];

        // Session promise: two fill-in-the-blank encounters in a row never
        // ask for the same token, even when consecutive draws hit different
        // skeletons that share a top candidate.
        for (int guard = 0;
             guard < candidates.Count && pick.Token == LastFitbAnswer;
             guard++)
        {
            cursor++;
            pick = candidates[cursor % candidates.Count];
        }

        FitbCursor[skeleton] = cursor + 1;
        LastFitbAnswer = pick.Token;
        t.correctAnswer = pick.Token;
        t.bugLineIndex = pick.LineIndex;

        // The distractors were forged at expansion time for the ORIGINAL
        // answer. Re-forge now, after the rotation, so the served option
        // set always matches the token this draw actually blanks -- a
        // keyword blank gets keyword options, never a stale family.
        t.distractors = ForgeFitbOptions(t, 3);
    }

    /// <summary>Strips expansion and mutation suffixes so every variant of
    /// one authored template shares a single rotation cursor.</summary>
    static string FitbSkeletonKey(string id)
    {
        if (string.IsNullOrEmpty(id)) return id ?? "";
        string key = Regex.Replace(id, @"_mut_\d+$", "");
        key = Regex.Replace(key, @"_s\d+_v\d+$", "");
        key = Regex.Replace(key, @"_v\d+$", "");
        while (key.EndsWith("_x"))
            key = key.Substring(0, key.Length - 2);
        return key;
    }

    static FitbCategory ClassifyFitbToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return FitbCategory.Unknown;
        if (token.Length >= 2 && token.StartsWith("'") && token.EndsWith("'"))
            return FitbCategory.StringLiteral;
        if (Array.IndexOf(FitbBooleanTokens, token) >= 0)
            return FitbCategory.Boolean;
        if (Array.IndexOf(AllFitbKeywords, token) >= 0)
            return FitbCategory.Keyword;
        if (FitbNumberRegex.IsMatch(token)) return FitbCategory.Number;
        if (FitbSymbolRegex.IsMatch(token)) return FitbCategory.Operator;
        if (FitbIdentRegex.IsMatch(token)) return FitbCategory.Identifier;
        return FitbCategory.Unknown;
    }

    static List<FitbTok> FitbTokenizeLine(string line)
    {
        var toks = new List<FitbTok>();
        foreach (Match m in FitbTokenRegex.Matches(line ?? ""))
            toks.Add(new FitbTok
            {
                Text = m.Value,
                Category = ClassifyFitbToken(m.Value)
            });
        return toks;
    }

    /// <summary>Every token that occurs EXACTLY ONCE in the served snippet,
    /// in scan order. Uniqueness matters twice over: the blank position is
    /// unambiguous on screen, and the format file can locate the token to
    /// blank with a simple first-occurrence replace.</summary>
    static List<FitbCandidate> FitbBlankCandidates(PuzzleTemplate t)
    {
        var candidates = new List<FitbCandidate>();
        if (t == null || t.codeLines == null || t.codeLines.Count == 0)
            return candidates;

        var counts = new Dictionary<string, int>();
        for (int i = 0; i < t.codeLines.Count; i++)
            foreach (FitbTok tok in FitbTokenizeLine(t.codeLines[i]))
            {
                if (tok.Category == FitbCategory.Unknown) continue;
                if (!counts.ContainsKey(tok.Text)) counts[tok.Text] = 0;
                counts[tok.Text]++;
            }

        var added = new HashSet<string>();
        for (int i = 0; i < t.codeLines.Count; i++)
            foreach (FitbTok tok in FitbTokenizeLine(t.codeLines[i]))
            {
                if (tok.Category == FitbCategory.Unknown) continue;
                if (counts[tok.Text] != 1) continue; // ambiguous: never blank
                if (!added.Add(tok.Text)) continue;  // first line wins
                candidates.Add(new FitbCandidate
                {
                    Token = tok.Text,
                    LineIndex = i,
                    Category = tok.Category
                });
            }

        // Only KEYWORD blanks are offered. A blanked number or string
        // ("hero = [ ? ]") leaves every option plausible with no context
        // to pick between them -- the player rightly calls that
        // unanswerable. Keywords are dictated by the snippet's own
        // syntax, so the missing token is always decidable on screen.
        candidates.RemoveAll(c => c.Category != FitbCategory.Keyword);

        return OrderFitbCandidates(candidates,
            Mathf.Clamp((int)t.difficulty, 0, 2));
    }

    /// <summary>Round-robin interleave across token categories so the
    /// rotation cycles through DIFFERENT KINDS of answers, not just
    /// different occurrences of the same kind. Beginners start on keywords;
    /// later tiers start on operators and identifiers.</summary>
    static List<FitbCandidate> OrderFitbCandidates(List<FitbCandidate> candidates,
                                                   int tier)
    {
        FitbCategory[] priority =
            tier == 0
                ? new[] { FitbCategory.Keyword, FitbCategory.Identifier,
                          FitbCategory.Number, FitbCategory.StringLiteral,
                          FitbCategory.Operator, FitbCategory.Boolean }
            : tier == 1
                ? new[] { FitbCategory.Identifier, FitbCategory.Number,
                          FitbCategory.Keyword, FitbCategory.Operator,
                          FitbCategory.StringLiteral, FitbCategory.Boolean }
                : new[] { FitbCategory.Operator, FitbCategory.Identifier,
                          FitbCategory.Number, FitbCategory.Keyword,
                          FitbCategory.StringLiteral, FitbCategory.Boolean };

        var buckets = new Dictionary<FitbCategory, List<FitbCandidate>>();
        foreach (FitbCategory cat in priority)
            buckets[cat] = new List<FitbCandidate>();
        foreach (FitbCandidate c in candidates)
            if (buckets.ContainsKey(c.Category)) buckets[c.Category].Add(c);

        var ordered = new List<FitbCandidate>();
        bool remaining = true;
        while (remaining)
        {
            remaining = false;
            foreach (FitbCategory cat in priority)
            {
                List<FitbCandidate> bucket = buckets[cat];
                if (bucket.Count > 0)
                {
                    ordered.Add(bucket[0]);
                    bucket.RemoveAt(0);
                    remaining = true;
                }
            }
        }
        return ordered;
    }

    /// <summary>Rebuilds the option set for the CURRENT correct answer.
    /// Category-matched, fresh per draw (seeded by the rotation cursor),
    /// contract-gated. Authored distractors of the right category may top
    /// up, but never lead.</summary>
    static List<string> ForgeFitbOptions(PuzzleTemplate t, int want)
    {
        string correct = t.correctAnswer ?? "";
        FitbCategory cat = ClassifyFitbToken(correct);
        if (cat == FitbCategory.Unknown)
            return LegacyFitbTopup(t, want); // unrotated template: old path

        string skeleton = FitbSkeletonKey(t.id);
        int cursor;
        FitbCursor.TryGetValue(skeleton, out cursor);
        int seed = FitbSeed(skeleton, cursor);

        var result = new List<string>();
        var seen = new HashSet<string> { Normalized(correct) };
        // For operator answers, = vs == and + vs += are the misconceptions
        // the puzzle teaches -- the nuance gate would strip exactly those,
        // so it only applies to answers with letters/digits in them.
        bool symbolOnly = FitbSymbolRegex.IsMatch(correct);

        foreach (string cand in FitbCandidatePool(t, correct, cat, seed))
        {
            if (result.Count >= want) break;
            if (string.IsNullOrWhiteSpace(cand) || cand == correct) continue;
            string key = Normalized(cand);
            if (seen.Contains(key)) continue;
            if (!symbolOnly && IsNuancePair(correct, cand)) continue;
            seen.Add(key);
            result.Add(cand);
        }

        // Same-category authored distractors may top up. This is what
        // retires the stagnant sets: input/int/str can no longer ride along
        // on a math-line template, and forged options always lead.
        foreach (string d in t.distractors ?? new List<string>())
        {
            if (result.Count >= want) break;
            if (string.IsNullOrWhiteSpace(d) || d == correct) continue;
            if (ClassifyFitbToken(d) != cat) continue;
            string key = Normalized(d);
            if (seen.Contains(key)) continue;
            if (!symbolOnly && IsNuancePair(correct, d)) continue;
            seen.Add(key);
            result.Add(d);
        }
        return result;
    }

    static List<string> FitbCandidatePool(PuzzleTemplate t, string correct,
                                          FitbCategory cat, int seed)
    {
        var pool = new List<string>();
        switch (cat)
        {
            case FitbCategory.Keyword:
                {
                    string[] family = FitbKeywordFamily(correct);
                    pool.AddRange(SeededShuffle(family.Where(k => k != correct)
                        .ToList(), seed));
                    var rest = AllFitbKeywords
                        .Where(k => Array.IndexOf(family, k) < 0 && k != correct)
                        .ToList();
                    pool.AddRange(SeededShuffle(rest, seed + 1));
                    break;
                }
            case FitbCategory.Identifier:
                {
                    // In-scope identifiers first (the snippet's own variables),
                    // then game-pool names -- real words, never invented junk.
                    var scope = new List<string>();
                    var seenScope = new HashSet<string> { correct };
                    foreach (string line in t.codeLines ?? new List<string>())
                        foreach (Match m in WordRegex.Matches(line ?? ""))
                            if (ClassifyFitbToken(m.Value) == FitbCategory.Identifier
                                && seenScope.Add(m.Value))
                                scope.Add(m.Value);
                    pool.AddRange(SeededShuffle(scope, seed));
                    pool.AddRange(SeededShuffle(NamePool
                        .Where(n => n != correct).ToList(), seed + 1));
                    break;
                }
            case FitbCategory.Number:
                {
                    int n;
                    if (!int.TryParse(correct, out n)) break;
                    var nums = new List<string>();
                    for (int d = 1; d <= 5; d++)
                    {
                        if (n - d >= 0) nums.Add((n - d).ToString());
                        nums.Add((n + d).ToString());
                    }
                    nums.Add((n * 2).ToString());
                    nums.Add((n + 10).ToString());
                    if (n > 0 && n % 2 == 0) nums.Add((n / 2).ToString());
                    pool.AddRange(SeededShuffle(nums.Distinct().ToList(), seed));
                    break;
                }
            case FitbCategory.StringLiteral:
                {
                    var others = new List<string>();
                    var seenStr = new HashSet<string> { correct };
                    foreach (string line in t.codeLines ?? new List<string>())
                        foreach (Match m in FitbStringRegex.Matches(line ?? ""))
                            if (seenStr.Add(m.Value)) others.Add(m.Value);
                    pool.AddRange(SeededShuffle(others, seed));
                    pool.AddRange(SeededShuffle(NamePool
                        .Where(w => "'" + w + "'" != correct)
                        .Select(w => "'" + w + "'").ToList(), seed + 1));
                    break;
                }
            case FitbCategory.Operator:
                {
                    string fam = FitbOperatorFamily(correct);
                    string[] famOps = fam == "arith" ? FitbArithOps
                                    : fam == "comp" ? FitbCompOps
                                    : FitbAssignOps;
                    pool.AddRange(SeededShuffle(famOps
                        .Where(o => o != correct).ToList(), seed));
                    var allOps = FitbArithOps.Concat(FitbCompOps)
                        .Concat(FitbAssignOps).Where(o => o != correct)
                        .Distinct().ToList();
                    pool.AddRange(SeededShuffle(allOps, seed + 1));
                    break;
                }
            case FitbCategory.Boolean:
                {
                    var bools = new List<string>
                    { "True", "False", "None", "0", "1" };
                    pool.AddRange(SeededShuffle(bools
                        .Where(b => b != correct).ToList(), seed));
                    break;
                }
        }
        return pool;
    }

    static string[] FitbKeywordFamily(string keyword)
    {
        if (Array.IndexOf(FitbIoKeywords, keyword) >= 0) return FitbIoKeywords;
        if (Array.IndexOf(FitbConvertKeywords, keyword) >= 0)
            return FitbConvertKeywords;
        if (Array.IndexOf(FitbLoopKeywords, keyword) >= 0)
            return FitbLoopKeywords;
        if (Array.IndexOf(FitbBranchKeywords, keyword) >= 0)
            return FitbBranchKeywords;
        if (Array.IndexOf(FitbDefKeywords, keyword) >= 0)
            return FitbDefKeywords;
        return new string[0];
    }

    static string FitbOperatorFamily(string op)
    {
        if (Array.IndexOf(FitbArithOps, op) >= 0) return "arith";
        if (Array.IndexOf(FitbCompOps, op) >= 0) return "comp";
        if (Array.IndexOf(FitbAssignOps, op) >= 0) return "assign";
        return "";
    }

    /// <summary>Deterministic Fisher-Yates driven by an LCG (not
    /// UnityEngine.Random) so option sets are reproducible from the rotation
    /// cursor yet differ every draw.</summary>
    static List<string> SeededShuffle(List<string> items, int seed)
    {
        var list = new List<string>(items ?? new List<string>());
        uint s = (uint)seed;
        for (int i = list.Count - 1; i > 0; i--)
        {
            s = s * 1664525u + 1013904223u;
            int j = (int)((s >> 8) % (uint)(i + 1));
            string tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
        return list;
    }

    static int FitbSeed(string skeleton, int cursor)
    {
        // FNV-1a over the skeleton id, mixed with the rotation cursor -- all
        // in explicit uint domain so the wrap is well-defined and matches the
        // validation mirror (tools_check_fitb.py) bit for bit.
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in skeleton ?? "") h = (h ^ c) * 16777619u;
            return (int)(h ^ ((uint)cursor * 2654435761u));
        }
    }

    /// <summary>Old behavior, kept only for templates the rotator could not
    /// handle (no unique blankable token, correctAnswer still empty).</summary>
    static List<string> LegacyFitbTopup(PuzzleTemplate t, int want)
    {
        string correct = t.variableValue ?? "";
        var distractors = new List<string>();
        foreach (string d in t.distractors ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(d) && !distractors.Contains(d))
                distractors.Add(d);
        if (distractors.Count >= want || correct.Length == 0)
            return distractors;

        List<string> top;
        if (IsInt(correct))
        {
            int n = int.Parse(correct.Trim());
            top = new List<string>
            {
                (n + Random.Range(1, 6)).ToString(),
                (n * 2).ToString(),
                "'" + n + "'"
            };
        }
        else if (correct == "True" || correct == "False")
        {
            top = new List<string> { correct == "True" ? "False" : "True",
                                     "None", "0" };
        }
        else
        {
            top = new List<string> { correct.ToUpper(), correct + "_", "None" };
        }
        foreach (string cand in top)
        {
            if (distractors.Count >= want) break;
            if (!distractors.Contains(cand) && cand != correct)
                distractors.Add(cand);
        }
        return distractors;
    }

    // -- helpers -------------------------------------------------------------

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    class Pair<TFirst, TSecond>
    {
        public TFirst First;
        public TSecond Second;

        public static Pair<TFirst, TSecond> Create(TFirst first, TSecond second)
        {
            return new Pair<TFirst, TSecond> { First = first, Second = second };
        }
    }
}
