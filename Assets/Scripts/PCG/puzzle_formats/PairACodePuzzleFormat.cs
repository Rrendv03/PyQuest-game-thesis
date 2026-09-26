using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// Bare `Random` means UnityEngine.Random (Random.Range API) — keeps CS0104
// away if `using System;` is ever added to this file.
using Random = UnityEngine.Random;

/// <summary>
/// PairACode format handler, adapted to the current generation pipeline
/// (PuzzleVariationEngine slot expansion + distractor forge).
///
/// Division of labor with the pipeline:
///   - PCGEngine.MutatePuzzlePublic renames variables and then calls
///     PuzzleVariationEngine.ForgeDistractors, which for PairACode either
///     replaces template.distractors with three contract-validated options
///     or -- when it cannot reach three -- leaves the authored distractors
///     in place untouched. That shortfall path is why this format still
///     runs its own defensive gate over every distractor before it becomes
///     a playable option: distinct from the answer, nuance-free (quote
///     toggle, = vs ==, case, colon), gibberish-free (no invented
///     identifiers) and same-statement-family as the blanked line.
///   - template.goalText (tokens filled per instance by the slot
///     expansion) is rendered as a "# Goal: ..." header so near-miss
///     distractors are wrong for reasons a player can REASON about, not
///     one-character gotchas.
///   - The blanked line is always the LAST code line, which is the same
///     line ForgePac treats as the correct answer when it synthesizes
///     options, so snippet and options can never disagree about what is
///     being asked.
/// </summary>
public class PairACodePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.PairACode;

    private PuzzleTemplate template;
    private string correctAnswer;
    private List<string> options;
    private string codeSnippetWithBlank;

    // Mirrors PuzzleVariationEngine's private word scanner: identifiers only.
    private static readonly Regex WordRegex = new Regex(@"[A-Za-z_]\w*");

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(FillInTheBlankUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(SpotTheBugUIController uiController) { }
    public void RenderPuzzle(LineScrambleUIController uiController) { }
    public void RenderPuzzle(Text displayField) { }

    public int GetOptionCount()
    {
        // Null-safe: PuzzleManager can query this before Initialize ran.
        return options != null ? options.Count : 0;
    }

    public void RenderPuzzle(PairACodeUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[PairACodePuzzleFormat] UIController is null");
            return;
        }

        // Shuffle before handing options to the UI: the correct answer used
        // to always be added FIRST, so any UI that renders options in order
        // made the answer positionally predictable. PairACodeUIController
        // shuffles too; shuffling here means the format is safe regardless
        // of what the UI does.
        List<string> shuffled = new List<string>(options);
        Shuffle(shuffled);

        uiController.PopulateUI(codeSnippetWithBlank, shuffled);
        Debug.Log($"[PairACodePuzzleFormat] Rendered puzzle. Correct answer: {correctAnswer}");
    }

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is string strAnswer)
        {
            bool isCorrect = strAnswer.Trim() == correctAnswer.Trim();
            Debug.Log($"[PairACodePuzzleFormat] Player: {strAnswer} | Correct: {correctAnswer} | Result: {isCorrect}");
            return isCorrect;
        }

        Debug.LogError("[PairACodePuzzleFormat] Invalid answer type. Expected string.");
        return false;
    }

    public object GetCorrectAnswer() => correctAnswer;

    private void GeneratePuzzle()
    {
        if (template.codeLines == null || template.codeLines.Count == 0)
        {
            Debug.LogError("[PairACodePuzzleFormat] Template has no code lines");
            return;
        }

        // Always blank the LAST line so context from previous lines is always
        // visible. ForgePac treats the last line as the correct answer when it
        // synthesizes options, so this format and the forge stay aligned.
        int blankIndex = template.codeLines.Count - 1;

        // The goal comment is what makes PairACode decidable: it states the
        // INTENT of the missing line, so near-miss distractors (swapped
        // arguments, wrong operator, wrong value) are wrong for reasons a
        // player can state: "the goal says message first", "the goal says
        // add". Templates that never set goalText keep the legacy headers.
        string header = string.IsNullOrEmpty(template.goalText)
            ? (template.codeLines.Count == 1
                ? "# What is the missing line?"
                : "# Complete the missing line:")
            : "# Goal: " + template.goalText;

        if (template.codeLines.Count == 1)
        {
            correctAnswer = template.codeLines[0];
            codeSnippetWithBlank = header + "\n[ DRAG HERE ]";
        }
        else
        {
            correctAnswer = template.codeLines[blankIndex];

            // Build display: show all lines except the blanked one
            List<string> displayLines = new List<string>(template.codeLines);
            displayLines[blankIndex] = "[ ? ]";
            codeSnippetWithBlank = header + "\n" + string.Join("\n", displayLines);
        }

        // Build options: correct answer + distractors. Distractors arrive
        // forged when PuzzleVariationEngine.ForgeDistractors reached three
        // validated options, but when it falls short the authored JSON
        // distractors pass through unvalidated -- so this pass re-checks
        // every candidate against the same contract (distinct, nuance-free,
        // gibberish-free, same family) before it can become an option.
        options = new List<string> { correctAnswer };

        if (template.distractors != null)
            foreach (string d in template.distractors)
                AddOption(d);

        PadOptions();

        if (options.Count < 4)
            Debug.LogWarning($"[PairACodePuzzleFormat] Only {options.Count} contract-valid options available for {template.id} (degraded, not stuck)");

        Debug.Log($"[PairACodePuzzleFormat] Blank index: {blankIndex} | Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
    }

    /// <summary>Whitespace-insensitive comparison key used by the option
    /// gate so duplicate cards can't differ only by spacing.</summary>
    private static string NormalizedKey(string s)
        => s == null ? "" : Regex.Replace(s, @"\s+", "");

    /// <summary>
    /// Defensive option gate, mirroring the distractor contract enforced by
    /// PuzzleVariationEngine.ForgeDistractors. Returns true when the
    /// candidate was added. Reuses the engine's own public validators so
    /// both layers stay one contract: IsNuancePair rejects the observation
    /// gotchas (quote-toggle, = vs ==, case-only, colon-only -- the
    /// "runes == 25" and print('name') classes), IsGibberishLine rejects
    /// invented identifiers (the runic(25) class).
    /// </summary>
    private bool AddOption(string raw)
    {
        string candidate = raw != null ? raw.Trim() : null;
        if (string.IsNullOrEmpty(candidate)) return false;

        string correct = correctAnswer != null ? correctAnswer.Trim() : "";
        // Compare on whitespace-collapsed text: exact-trim comparison let a
        // whitespace-variant twin of an option through as a second card (the
        // duplicated "print(endurance * 63 - 11)" the playtest reported).
        string candKey = NormalizedKey(candidate);
        if (NormalizedKey(correct) == candKey) return false;
        if (options != null && options.Any(o => o != null && NormalizedKey(o) == candKey))
            return false;

        if (PuzzleVariationEngine.IsNuancePair(correct, candidate)) return false;
        if (PuzzleVariationEngine.IsGibberishLine(candidate, template.codeLines)) return false;

        // Same statement family as the blanked line: shares its leading verb
        // or one of its variables, so options can't be weeded by surface
        // pattern alone ("the weird one").
        if (!SharesFamily(candidate, correct)) return false;

        options.Add(candidate);
        return true;
    }

    /// <summary>
    /// Pads to four options only when the forge and the authored distractors
    /// left us short. Candidates come from the snippet itself first (same
    /// family by construction -- this replaces the old
    /// GenerateGuaranteedWrongOption call, whose generic pool could return
    /// "pass"/"break" junk for single-line snippets), then from verb-matched
    /// templates around the variables actually in play. Every candidate
    /// passes the AddOption gate; the bounded attempt count means a
    /// pathological template degrades to fewer options rather than spinning
    /// or duplicating.
    /// </summary>
    private void PadOptions()
    {
        int snippetCursor = 0;
        int fallbackCursor = 0;
        int suffix = 2;

        for (int attempts = 0; options.Count < 4 && attempts < 24; attempts++)
        {
            string candidate;
            switch (attempts % 3)
            {
                case 0: // a real line from elsewhere in this snippet
                    candidate = NextSnippetLine(ref snippetCursor);
                    break;
                case 1: // verb-matched fallback around the template's variable
                    candidate = NextVariableFallback(ref fallbackCursor);
                    break;
                default: // unique-by-construction numbered variant
                    candidate = NextNumberedVariant(ref suffix);
                    break;
            }

            if (candidate == null) continue;
            AddOption(candidate);
        }
    }

    /// <summary>Walks the snippet's other lines, skipping comments and the
    /// blanked line itself. Returns null once every line was offered.</summary>
    private string NextSnippetLine(ref int cursor)
    {
        List<string> lines = template.codeLines;
        if (lines == null || lines.Count <= 1) return null;

        for (int scanned = 0; scanned < lines.Count; scanned++)
        {
            int i = (cursor + scanned) % lines.Count;
            string line = lines[i] != null ? lines[i].Trim() : null;
            if (string.IsNullOrEmpty(line)) continue;
            if (line == correctAnswer.Trim()) continue;
            if (line.StartsWith("#")) continue;
            cursor = i + 1;
            return line;
        }
        return null;
    }

    /// <summary>
    /// Plausible code of the same family as the blanked line, built only
    /// from the variables this template actually defines. The old pool here
    /// ("pass", "break", "{name} == {value}", print('{name}')) contained
    /// exactly the junk and nuance gotchas the variety pass bans -- these
    /// candidates are all real statements, and the AddOption gate filters
    /// any that collide with the correct answer.
    /// </summary>
    private string NextVariableFallback(ref int cursor)
    {
        string varName = template.variableName;
        string varValue = template.variableValue;

        List<string> pool = new List<string>();
        if (!string.IsNullOrEmpty(varName))
        {
            pool.Add($"print({varName} + 1)");
            if (!string.IsNullOrEmpty(varValue))
            {
                pool.Add($"{varName} = {varValue}");
                pool.Add($"print({varValue})");
            }
            pool.Add($"print({varName}, {varName})");
            pool.Add($"print('{varName}: ' + {varName})");
            pool.Add($"{varName} = input('{varName}: ')");
        }
        pool.Add($"print('{RandomMessage()}')");

        string candidate = pool[cursor % pool.Count];
        cursor++;
        return candidate;
    }

    /// <summary>Last-resort variant that cannot collide with anything:
    /// the suffix increments until the gate accepts a distinct option.</summary>
    private string NextNumberedVariant(ref int suffix)
    {
        string varName = template.variableName;
        string varValue = template.variableValue;
        if (string.IsNullOrEmpty(varName) || string.IsNullOrEmpty(varValue)) return null;

        string variant = $"{varName} = {varValue} + {suffix}";
        suffix++;
        return variant;
    }

    /// <summary>
    /// Same-statement-family test used by ForgePac: the candidate shares the
    /// blanked line's leading verb (print / input / the variable being
    /// assigned) or references a variable that is defined in the snippet and
    /// also appears in the blanked line.
    /// </summary>
    private bool SharesFamily(string candidate, string correct)
    {
        string correctHead = Regex.Split(correct, @"[(\s=]")[0];
        string candidateHead = Regex.Split(candidate, @"[(\s=]")[0];
        if (candidateHead == correctHead) return true;

        HashSet<string> defined = DefinedVariables(template.codeLines);
        foreach (Match m in WordRegex.Matches(candidate))
            if (defined.Contains(m.Value) && correct.Contains(m.Value))
                return true;
        return false;
    }

    /// <summary>Port of PuzzleVariationEngine.DefinedVariables: names bound
    /// by plain assignment or a for-range loop header.</summary>
    private static HashSet<string> DefinedVariables(List<string> codeLines)
    {
        HashSet<string> defs = new HashSet<string>();
        if (codeLines == null) return defs;

        foreach (string line in codeLines)
        {
            string s = line != null ? line.Trim() : "";
            if (s.StartsWith("#")) continue;

            Match m = Regex.Match(s, @"^(\w+)\s*=(?!=)");
            if (m.Success) defs.Add(m.Groups[1].Value);

            m = Regex.Match(s, @"^for\s+(\w+)\s+in\s+range");
            if (m.Success) defs.Add(m.Groups[1].Value);
        }
        return defs;
    }

    private static string RandomMessage()
    {
        string[] messages =
        {
            "Level Up", "Quest Complete", "Well Done",
            "Keep Going", "Path Unlocked", "Victory"
        };
        return messages[Random.Range(0, messages.Length)];
    }

    private static void Shuffle(List<string> list)
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