// PyQuest PCG engine — all invariants/documented behavior live in PCG_MIGRATION_PLAN.md (do not re-document inline).
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using System;

using Random = UnityEngine.Random;

public static class PuzzleVariationEngine
{
    public const int VariantsPerSkeleton = 10;

    public static bool useStructuralMutations = true;

    public static readonly string[] NamePool =
    {
        "mana", "health", "score", "level", "gold", "damage", "defense",
        "stamina", "magic", "runes", "power", "shield", "energy", "speed",
        "armor", "quest", "rank", "coins", "lives", "points", "strength",
        "agility", "wisdom", "luck", "vigor", "guard", "focus", "morale",
        "essence", "charge", "rating", "tally", "streak", "combo", "supply",
        "reserve", "endurance", "fortune", "resolve", "insight",
        "arcana", "valor", "spirit", "glyph", "ember", "thunder",
        "cinder", "radiance", "zenith", "apex", "stride", "pulse",
        "cadence", "lore", "sigil", "crest", "momentum", "gravity",
        "harvest", "beacon", "quiver", "talent", "bounty", "tempo"
    };

    static readonly string[] StrPool =
    {
        "'Hero'", "'Wizard'", "'Archer'", "'Knight'", "'Mage'", "'Rogue'",
        "'Paladin'", "'Hunter'", "'Warrior'", "'Sage'", "'Scout'", "'Ranger'",
        "'Monk'", "'Druid'", "'Bard'", "'Cleric'", "'Nomad'", "'Guardian'",
        "'Sentinel'", "'Wanderer'", "'Champion'", "'Seer'", "'Dragon'",
        "'Alchemist'", "'Oracle'", "'Voyager'", "'Crusader'", "'Mystic'",
        "'Pilgrim'", "'Vanguard'", "'Warlord'", "'Enigma'", "'Templar'"
    };

    static readonly string[] MsgPool =
    {
        "'Level Up'", "'Quest Complete'", "'Victory'", "'Try Again'",
        "'Well Done'", "'Keep Going'", "'Almost There'", "'New Record'",
        "'Boss Defeated'", "'Path Unlocked'", "'Sanctum Cleared'", "'Not Yet'",
        "'Game Over'", "'You Win'", "'Next Round'", "'Combo Broken'",
        "'Skill Up'", "'Final Blow'", "'Rune Found'", "'Gate Open'",
        "'Tower Cleared'", "'Slow Down'", "'One More'", "'Perfect Run'"
    };

    static readonly int[][] NumRanges =
    {
        new[] { 2, 12 },
        new[] { 5, 60 },
        new[] { 6, 99 }
    };

    static readonly Regex TokenRegex = new Regex(
        @"\{(name|name2|name3|name4|num|num2|num3|small|str|str2|msg|msg2)\}");

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

    static readonly string[] ShapeBindingCandidates =
    {
        "msg", "greeting", "result", "text", "info", "output", "line", "label"
    };

    static readonly Dictionary<string, List<string>> recentKinds =
        new Dictionary<string, List<string>>();

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
                if (kind == "name" || kind == "name2" || kind == "name3"
                    || kind == "name4")
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

        System.Func<string, string> subDist = text => TokenRegex.Replace(
            text ?? "", match => StripWrappingQuotes(fill(match)));

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
            distractors = (t.distractors ?? new List<string>()).Select(subDist).ToList(),
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

    static string StripWrappingQuotes(string v)
    {
        if (!string.IsNullOrEmpty(v) && v.Length >= 2
            && v[0] == '\'' && v[v.Length - 1] == '\'')
            return v.Substring(1, v.Length - 2);
        return v;
    }

    static string DrawSlot(string kind, int tier,
                           HashSet<string> usedNames, HashSet<int> usedNums,
                           HashSet<string> usedMsgs, HashSet<string> usedStrs)
    {
        if (kind == "small")
        {
            for (int i = 0; i < 40; i++)
            {
                int v = Random.Range(1, 6);
                if (!usedNums.Contains(v)) return v.ToString();
            }
            return "1";
        }
        if (kind == "str" || kind == "str2")
        {

            for (int i = 0; i < 40; i++)
            {
                string v = StrPool[Random.Range(0, StrPool.Length)];
                if (!usedStrs.Contains(v)) return v;
            }
            return StrPool[Random.Range(0, StrPool.Length)];
        }
        if (kind == "msg" || kind == "msg2")
        {

            for (int i = 0; i < 40; i++)
            {
                string m = MsgPool[Random.Range(0, MsgPool.Length)];
                if (!usedMsgs.Contains(m)) return m;
            }
            return MsgPool[Random.Range(0, MsgPool.Length)];
        }
        if (kind == "num" || kind == "num2" || kind == "num3")
            return DrawNumber(tier, usedNums).ToString();

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

    public static PuzzleTemplate ExpandSkeleton(PuzzleTemplate skeleton, int serveTier)
    {
        if (skeleton == null) return null;
        int authoredTier = (int)skeleton.difficulty;

        if (!HasTokens(skeleton))
        {

            if (serveTier != authoredTier) return null;
            PuzzleTemplate passthrough = CloneWithFill(skeleton, authoredTier);
            passthrough.acceptedOrders = ComputeAcceptedOrders(passthrough);
            return passthrough;
        }

        bool scaleUp = serveTier != authoredTier;
        if (scaleUp)
        {
            if (serveTier < 1 || serveTier > 2) return null;

            if (!CanScale(skeleton)) return null;
        }

        PuzzleTemplate fill = CloneWithFill(skeleton, scaleUp ? serveTier : authoredTier);

        if (scaleUp)
        {
            List<string> scaled = ScaleCodeLines(fill.codeLines, serveTier);
            if (scaled == null) return null;
            fill.codeLines = scaled;
        }
        fill.difficulty = (DifficultyTier)serveTier;

        if (useStructuralMutations)
            ApplyStructuralMutation(fill);

        if (fill.puzzleType == PuzzleType.PredictTheOutput)
        {
            string output = null;
            if (MiniPythonEvaluator.TrySimulate(fill.codeLines, out output))
                fill.correctAnswer = output;
        }
        fill.acceptedOrders = ComputeAcceptedOrders(fill);
        return fill;
    }

    static void ApplyStructuralMutation(PuzzleTemplate t)
    {
        if (t == null || t.codeLines == null || t.codeLines.Count == 0) return;
        int tier = (int)t.difficulty;
        if (tier < 1) return;
        if (t.puzzleType != PuzzleType.LineScramble
            && t.puzzleType != PuzzleType.TrueOrFalse
            && t.puzzleType != PuzzleType.PredictTheOutput
            && t.puzzleType != PuzzleType.FillInTheBlank)
            return;
        if (ContainsControlFlow(t.codeLines)) return;

        float roll = Random.value;
        if (tier >= 2)
        {
            if (roll < 0.45f) { TrySplitPrintBinding(t); return; }
            if (roll < 0.80f) TryPrintArgForm(t);
        }
        else if (roll < 0.5f)
        {
            TryPrintArgForm(t);
        }
    }

    static int SolePrintIndex(List<string> lines)
    {
        int idx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string s = (lines[i] ?? "").Trim();
            if (s.StartsWith("print(") && s.EndsWith(")"))
            {
                if (idx >= 0) return -1;
                idx = i;
            }
        }
        return idx;
    }

    static bool HasTopLevelComma(string expr)
    {
        bool inSingle = false, inDouble = false;
        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == ',' && !inSingle && !inDouble) return true;
        }
        return false;
    }

    static string InnerOfPrint(string line)
    {

        return line.Substring("print(".Length, line.Length - "print(".Length - 1).Trim();
    }

    static IEnumerable<string> WordsIn(string text)
    {
        foreach (Match m in WordRegex.Matches(text ?? ""))
            yield return m.Value;
    }

    static string PickFreshShapeName(PuzzleTemplate t)
    {
        var used = new HashSet<string>();
        foreach (string w in WordsIn(string.Join("\n", t.codeLines ?? new List<string>())
                 + "\n" + string.Join("\n", t.distractors ?? new List<string>())
                 + "\n" + (t.correctAnswer ?? "") + "\n" + (t.goalText ?? "")))
            used.Add(w);
        foreach (string c in KnownCallables) used.Add(c);
        foreach (string cand in ShapeBindingCandidates)
            if (!used.Contains(cand)) return cand;
        return null;
    }

    static void TrySplitPrintBinding(PuzzleTemplate t)
    {
        int idx = SolePrintIndex(t.codeLines);
        if (idx < 0) return;
        string inner = InnerOfPrint(t.codeLines[idx].Trim());
        if (inner.Length == 0 || HasTopLevelComma(inner)) return;
        if (!inner.Contains("+")) return;

        string name = PickFreshShapeName(t);
        if (name == null) return;

        var backupLines = new List<string>(t.codeLines);
        int backupBug = t.bugLineIndex;
        var backupOrder = t.correctOrder != null
            ? new List<int>(t.correctOrder) : new List<int>();

        t.codeLines[idx] = "print(" + name + ")";
        t.codeLines.Insert(idx, name + " = " + inner);

        if (t.bugLineIndex >= idx) t.bugLineIndex++;
        if (backupOrder.Count > 0)
        {

            bool identity = true;
            for (int k = 0; k < backupOrder.Count; k++)
                if (backupOrder[k] != k) { identity = false; break; }
            if (!identity)
            {
                t.codeLines = backupLines;
                t.bugLineIndex = backupBug;
                t.correctOrder = backupOrder;
                return;
            }
            t.correctOrder = new List<int>();
            for (int k = 0; k < t.codeLines.Count; k++) t.correctOrder.Add(k);
        }

        string before, after;
        if (!MiniPythonEvaluator.TrySimulate(backupLines, out before)
            || !MiniPythonEvaluator.TrySimulate(t.codeLines, out after)
            || before != after)
        {
            t.codeLines = backupLines;
            t.bugLineIndex = backupBug;
            t.correctOrder = backupOrder;
            return;
        }
        Debug.Log($"[PCG] Shape mutation: split-print (binding '{name}') on {t.id}");
    }

    static void TryPrintArgForm(PuzzleTemplate t)
    {
        int idx = SolePrintIndex(t.codeLines);
        if (idx < 0) return;
        string inner = InnerOfPrint(t.codeLines[idx].Trim());
        if (inner.Length == 0 || HasTopLevelComma(inner)) return;

        Match m = Regex.Match(inner, @"^(?<lhs>.+?)\s*\+\s*'\s*'\s*\+\s*(?<rhs>.+)$");
        if (!m.Success) return;
        string lhs = m.Groups["lhs"].Value.Trim();
        string rhs = m.Groups["rhs"].Value.Trim();
        if (lhs.Length == 0 || rhs.Length == 0) return;

        var mutated = new List<string>(t.codeLines);
        mutated[idx] = "print(" + lhs + ", " + rhs + ")";

        string before, after;
        if (!MiniPythonEvaluator.TrySimulate(t.codeLines, out before)
            || !MiniPythonEvaluator.TrySimulate(mutated, out after)
            || before != after)
            return;

        t.codeLines = mutated;
        Debug.Log($"[PCG] Shape mutation: print-arg-form on {t.id}");
    }

    public static bool ValidateInstance(PuzzleTemplate t, PuzzleTemplate skeleton,
                                        out string failureReason)
    {
        failureReason = "ok";
        if (t == null || t.codeLines == null || t.codeLines.Count == 0)
        { failureReason = "empty codeLines"; return false; }
        if (t.codeLines.Any(l => string.IsNullOrWhiteSpace(l)))
        { failureReason = "blank code line"; return false; }

        string blob = string.Join("\n", t.codeLines)
                    + "\n" + string.Join("\n", t.distractors ?? new List<string>())
                    + "\n" + (t.correctAnswer ?? "")
                    + "\n" + (t.variableName ?? "")
                    + "\n" + (t.variableValue ?? "")
                    + "\n" + (t.goalText ?? "");
        if (TokenRegex.IsMatch(blob))
        { failureReason = "unresolved slot token"; return false; }

        if (Regex.IsMatch(blob, @"\w''|''\w"))
        { failureReason = "doubled-quote artifact"; return false; }

        bool simulated = false;
        string simOutput = null;
        if (t.puzzleType == PuzzleType.PredictTheOutput)
        {
            if (string.IsNullOrWhiteSpace(t.correctAnswer))
            { failureReason = "PTO correctAnswer empty"; return false; }

            simulated = MiniPythonEvaluator.TrySimulate(t.codeLines, out simOutput);
            if (simulated)
            {

                if (simOutput != t.correctAnswer)
                {
                    failureReason = $"PTO key '{t.correctAnswer}' != simulated '{simOutput}'";
                    return false;
                }
            }
            else if (!ContainsControlFlow(t.codeLines))
            {

                bool skeletonSimulates = skeleton != null
                    && MiniPythonEvaluator.TrySimulate(skeleton.codeLines,
                           out string _);
                if (skeletonSimulates)
                {
                    failureReason = "PTO snippet not simulatable and not control flow";
                    return false;
                }
            }

        }
        else if (t.puzzleType == PuzzleType.TrueOrFalse)
        {

            if (MiniPythonEvaluator.TrySimulate(t.codeLines, out simOutput))
                simulated = true;
        }
        else if (t.puzzleType == PuzzleType.PairACode)
        {
            if (string.IsNullOrWhiteSpace(t.codeLines[t.codeLines.Count - 1]))
            { failureReason = "PAC tail line empty"; return false; }
        }
        else if (t.puzzleType == PuzzleType.LineScramble)
        {

            if (!IsValidOrder(t.correctOrder, t.codeLines.Count))
            { failureReason = "correctOrder not a permutation of the lines"; return false; }
            string ambReason;
            if (!IsLineScrambleUnambiguous(t, out ambReason))
            { failureReason = ambReason; return false; }
            if (t.acceptedOrders != null && t.acceptedOrders.Count > 0
                && !t.acceptedOrders.Contains(OrderKey(t.correctOrder)))
            { failureReason = "acceptedOrders missing the canonical order"; return false; }
        }

        bool lineShapedOptions = t.puzzleType == PuzzleType.PairACode
            || t.puzzleType == PuzzleType.SpotTheBug;
        List<string> dist = t.distractors ?? new List<string>();
        string effectiveCorrect = t.puzzleType == PuzzleType.PairACode
            ? t.codeLines[t.codeLines.Count - 1].Trim()
            : t.correctAnswer ?? "";
        var seenKeys = new HashSet<string>();
        foreach (string d in dist)
        {
            if (string.IsNullOrWhiteSpace(d))
            { failureReason = "empty distractor"; return false; }
            string key = Normalized(d);
            if (!seenKeys.Add(key))
            { failureReason = $"duplicate distractor '{d}'"; return false; }
            if (effectiveCorrect.Length > 0 && key == Normalized(effectiveCorrect))
            { failureReason = $"distractor equals correct answer '{d}'"; return false; }
            if (lineShapedOptions && effectiveCorrect.Length > 0
                && IsNuancePair(effectiveCorrect, d))
            { failureReason = $"nuance distractor '{d}'"; return false; }
            if (lineShapedOptions && IsGibberishLine(d, t.codeLines))
            { failureReason = $"gibberish distractor '{d}'"; return false; }
            if (lineShapedOptions && effectiveCorrect.Length > 0
                && IsCommutativeSwapLine(effectiveCorrect, d))
            { failureReason = $"commutatively equivalent distractor '{d}'"; return false; }
            if (lineShapedOptions && IsSemanticallyEquivalentOption(t, d))
            { failureReason = $"semantically equivalent distractor '{d}'"; return false; }
        }

        if (!string.IsNullOrEmpty(t.variableName)
            && !t.codeLines.Any(l => l.Contains(t.variableName)))
        {
            failureReason = $"variableName '{t.variableName}' absent from code";
            return false;
        }
        if (skeleton != null && !string.IsNullOrEmpty(skeleton.goalText)
            && skeleton.goalText.Contains("{name}")
            && !string.IsNullOrEmpty(t.variableName)
            && !(t.goalText ?? "").Contains(t.variableName))
        {
            failureReason = "goalText lost the renamed variable (goal/code mismatch)";
            return false;
        }

        int maxLines = t.difficulty == DifficultyTier.Beginner ? 6
                     : t.difficulty == DifficultyTier.Intermediate ? 8 : 10;
        int maxVars = t.difficulty == DifficultyTier.Beginner ? 4
                    : t.difficulty == DifficultyTier.Intermediate ? 5 : 6;
        int lineCount = t.codeLines.Count;
        if (lineCount > maxLines)
        {
            failureReason = $"tier {(int)t.difficulty} budget: {lineCount} lines > {maxLines}";
            return false;
        }
        int varCount = DefinedVariables(t.codeLines).Count;
        if (varCount > maxVars)
        {
            failureReason = $"tier {(int)t.difficulty} budget: {varCount} variables > {maxVars}";
            return false;
        }

        failureReason = simulated
            ? "ok (validationDepth: full)"
            : "ok (validationDepth: structural)";
        return true;
    }

    static bool IsValidOrder(List<int> order, int lineCount)
    {
        if (order == null || order.Count != lineCount) return false;
        var seen = new HashSet<int>();
        foreach (int i in order)
            if (i < 0 || i >= lineCount || !seen.Add(i)) return false;
        return true;
    }

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
            return false;
        return true;
    }

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

    static string Normalized(string text)
    {
        return text == null ? "" : Regex.Replace(text, @"\s+", "");
    }

    public static bool IsNuancePair(string correct, string candidate)
    {
        string a = Normalized(correct), b = Normalized(candidate);
        if (a == b) return false;
        if (a.Replace("'", "").Replace("\"", "")
             == b.Replace("'", "").Replace("\"", ""))
            return true;
        if (a.Replace("==", "=") == b.Replace("==", "=")) return true;
        if (a.ToLower() == b.ToLower()) return true;
        if (a.TrimEnd(':') == b.TrimEnd(':')) return true;
        return false;
    }

    static bool IsCommutativeSwapLine(string correct, string cand)
    {
        string a = Normalized(correct), b = Normalized(cand);
        if (a == b || a.Contains("'") || b.Contains("'")
            || a.Contains("\"") || b.Contains("\"")) return false;
        string la = "", lb = "";
        Match ma = Regex.Match(a, @"^(\w+=)?print\((.+)\)$");
        string ra;
        if (ma.Success) { la = ma.Groups[1].Value; ra = ma.Groups[2].Value; }
        else
        {
            ma = Regex.Match(a, @"^(\w+=)?(.+)$");
            la = ma.Groups[1].Value; ra = ma.Groups[2].Value;
        }
        Match mb = Regex.Match(b, @"^(\w+=)?print\((.+)\)$");
        string rb;
        if (mb.Success) { lb = mb.Groups[1].Value; rb = mb.Groups[2].Value; }
        else
        {
            mb = Regex.Match(b, @"^(\w+=)?(.+)$");
            lb = mb.Groups[1].Value; rb = mb.Groups[2].Value;
        }
        if (la != lb) return false;
        Match m1 = Regex.Match(ra, @"^([^+*]+)([+*])([^+*]+)$");
        Match m2 = Regex.Match(rb, @"^([^+*]+)([+*])([^+*]+)$");
        if (!m1.Success || !m2.Success || m1.Groups[2].Value != m2.Groups[2].Value)
            return false;
        return m1.Groups[1].Value.Trim() == m2.Groups[3].Value.Trim()
            && m1.Groups[3].Value.Trim() == m2.Groups[1].Value.Trim();
    }

    static readonly HashSet<string> LsPyBuiltins = new HashSet<string>
    { "print", "input", "range", "str", "int", "float", "len", "round", "list",
      "dict", "set", "tuple", "sum", "min", "max", "abs", "sorted",
      "enumerate", "bool", "True", "False", "None", "in", "and", "or", "not" };

    static void LsLineDefsUses(string line, out string lhs, out HashSet<string> uses)
    {
        lhs = null;
        uses = new HashSet<string>();
        string s = Regex.Replace(line ?? "", "'[^']*'", "''").Trim();
        Match m = Regex.Match(s, @"^(\w+)\s*=(?!=)");
        string rhs = s;
        if (m.Success) { lhs = m.Groups[1].Value; rhs = s.Substring(m.Length); }
        foreach (Match w in Regex.Matches(rhs, @"[A-Za-z_]\w*"))
            if (!LsPyBuiltins.Contains(w.Value)) uses.Add(w.Value);
    }

    static IEnumerable<int[]> Permutations(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        yield return (int[])a.Clone();
        var c = new int[n];
        int i2 = 0;
        while (i2 < n)
        {
            if (c[i2] < i2)
            {
                if (i2 % 2 == 0) { int t = a[0]; a[0] = a[i2]; a[i2] = t; }
                else { int t = a[c[i2]]; a[c[i2]] = a[i2]; a[i2] = t; }
                yield return (int[])a.Clone();
                c[i2]++;
                i2 = 0;
            }
            else { c[i2] = 0; i2++; }
        }
    }

    static bool HasRepeatedAssignment(List<string> lines)
    {
        var seen = new HashSet<string>();
        foreach (string l in lines)
        {
            string lhs;
            LsLineDefsUses(l, out lhs, out _);
            if (lhs != null && !seen.Add(lhs)) return true;
        }
        return false;
    }

    // E1: for LineScramble, every dependency-valid order that simulates to
    // the SAME output as the canonical order is an acceptable answer. The
    // F32 gate still rejects scrambles whose dependency-valid orders produce
    // different outputs; this only widens acceptance within one output.
    public static List<string> ComputeAcceptedOrders(PuzzleTemplate t)
    {
        if (t == null || t.codeLines == null
            || t.puzzleType != PuzzleType.LineScramble) return null;
        var lines = t.codeLines;
        int n = lines.Count;
        if (n < 2 || n > 6 || ContainsControlFlow(lines)) return null;
        string refOutput;
        if (!MiniPythonEvaluator.TrySimulate(lines, out refOutput)
            || refOutput == null) return null;
        var accepted = new List<string>();
        foreach (int[] perm in Permutations(n))
        {
            var defined = new HashSet<string>();
            bool ok = true;
            foreach (int i in perm)
            {
                string lhs;
                HashSet<string> uses;
                LsLineDefsUses(lines[i], out lhs, out uses);
                if (!uses.IsSubsetOf(defined)) { ok = false; break; }
                if (lhs != null) defined.Add(lhs);
            }
            if (!ok) continue;
            string o;
            if (MiniPythonEvaluator.TrySimulate(
                    perm.Select(i => lines[i]).ToList(), out o) && o == refOutput)
                accepted.Add(string.Join(",", perm));
        }
        return accepted.Count > 1 ? accepted : null;
    }

    public static string OrderKey(List<int> order)
    {
        return order == null ? "" : string.Join(",", order);
    }

    static bool IsLineScrambleUnambiguous(PuzzleTemplate t, out string reason)
    {
        reason = "ok";
        var lines = t.codeLines;
        int n = lines.Count;
        if (ContainsControlFlow(lines)) return true;
        string refOutput;
        bool sim = MiniPythonEvaluator.TrySimulate(lines, out refOutput);
        if (!sim || n > 6)
        {
            if (HasRepeatedAssignment(lines))
            {
                reason = "ambiguous scramble (reassignment, not order-verifiable)";
                return false;
            }
            return true;
        }
        var outs = new HashSet<string>();
        int valid = 0;
        foreach (int[] perm in Permutations(n))
        {
            var defined = new HashSet<string>();
            bool ok = true;
            foreach (int i in perm)
            {
                string lhs;
                HashSet<string> uses;
                LsLineDefsUses(lines[i], out lhs, out uses);
                if (!uses.IsSubsetOf(defined)) { ok = false; break; }
                if (lhs != null) defined.Add(lhs);
            }
            if (!ok) continue;
            valid++;
            string o;
            if (MiniPythonEvaluator.TrySimulate(
                    perm.Select(i => lines[i]).ToList(), out o) && o != null)
                outs.Add(o);
        }
        if (valid > 1 && outs.Count > 1)
        {
            reason = $"ambiguous scramble: {valid} dependency-valid orders, "
                   + $"{outs.Count} different outputs";
            return false;
        }
        return true;
    }

    static bool IsSemanticallyEquivalentOption(PuzzleTemplate t, string candidate)
    {
        if (t == null || string.IsNullOrWhiteSpace(candidate)) return false;
        int subIdx = t.puzzleType == PuzzleType.PairACode
            ? t.codeLines.Count - 1 : t.bugLineIndex;
        if (subIdx < 0 || subIdx >= t.codeLines.Count) return false;
        string outC;
        if (!MiniPythonEvaluator.TrySimulate(t.codeLines, out outC)) return false;
        var test = new List<string>(t.codeLines);
        test[subIdx] = candidate.Trim();
        string outD;
        if (!MiniPythonEvaluator.TrySimulate(test, out outD)) return false;
        return outC == outD;
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

            if (candidate == "NameError" || candidate == "TypeError"
                || candidate == "SyntaxError")
                return true;
            return Regex.IsMatch(candidate, @"^[a-z_]\w*$");
        }

        if (Regex.IsMatch(candidate, @"^[a-z_]\w*$")) return true;

        string normA = Normalized(candidate).Replace("'", "").Replace("\"", "");
        string normC = Normalized(correct).Replace("'", "").Replace("\"", "");
        if (normA == normC) return true;
        return CharOverlap(candidate, correct) >= 0.4f;
    }

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

    static IEnumerable<Pair<string, List<string>>> PtoMutations(PuzzleTemplate t)
    {
        var lines = new List<string>(t.codeLines);

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

        Dictionary<string, bool> envIsString;
        string traceOut;
        bool traced = MiniPythonEvaluator.TrySimulateDetailed(t.codeLines,
            out traceOut, out envIsString);
        if (traced)
        {

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
                    if (m3.Groups[2].Value == "+")
                        fam.Add(m2.Groups[1].Value + " = " + m3.Groups[1].Value
                              + " - " + m3.Groups[3].Value);
                    else if (m3.Groups[2].Value == "*")
                        fam.Add(m2.Groups[1].Value + " = " + m3.Groups[1].Value
                              + " + " + m3.Groups[3].Value);
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
            if (IsCommutativeSwapLine(correct, cand)) return;
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

    static readonly Dictionary<string, int> FitbCursor =
        new Dictionary<string, int>();
    static string LastFitbAnswer = "";

    public static void RotateFitbBlank(PuzzleTemplate t)
    {
        if (t == null || t.puzzleType != PuzzleType.FillInTheBlank) return;

        List<FitbCandidate> candidates = FitbBlankCandidates(t);
        if (candidates.Count == 0) return;

        string skeleton = FitbSkeletonKey(t.id);
        int cursor;
        FitbCursor.TryGetValue(skeleton, out cursor);

        FitbCandidate pick = candidates[cursor % candidates.Count];

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

        t.distractors = ForgeFitbOptions(t, 3);
    }

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
                if (counts[tok.Text] != 1) continue;
                if (!added.Add(tok.Text)) continue;
                candidates.Add(new FitbCandidate
                {
                    Token = tok.Text,
                    LineIndex = i,
                    Category = tok.Category
                });
            }

        candidates.RemoveAll(c => c.Category != FitbCategory.Keyword);

        return OrderFitbCandidates(candidates,
            Mathf.Clamp((int)t.difficulty, 0, 2));
    }

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

    static List<string> ForgeFitbOptions(PuzzleTemplate t, int want)
    {
        string correct = t.correctAnswer ?? "";
        FitbCategory cat = ClassifyFitbToken(correct);
        if (cat == FitbCategory.Unknown)
            return LegacyFitbTopup(t, want);

        string skeleton = FitbSkeletonKey(t.id);
        int cursor;
        FitbCursor.TryGetValue(skeleton, out cursor);
        int seed = FitbSeed(skeleton, cursor);

        var result = new List<string>();
        var seen = new HashSet<string> { Normalized(correct) };

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

        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in skeleton ?? "") h = (h ^ c) * 16777619u;
            return (int)(h ^ ((uint)cursor * 2654435761u));
        }
    }

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
