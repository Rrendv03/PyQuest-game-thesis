using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Bare `Random` means UnityEngine.Random (Random.Range API) -- keeps CS0104
// away if `using System;` is ever added to this file. Do not remove.
using Random = UnityEngine.Random;

/// <summary>
/// FillInTheBlank format handler. This file is the ORIGINAL working version
/// (keyword scan, variableValue/variableName fallbacks, "____" marker the UI
/// controller replaces, correct answer added first, tokens padded to four)
/// with the variety changes adapted INTO it:
///
///   1. PuzzleVariationEngine.RotateFitbBlank picks the missing token per
///      draw (rotating cursor, session no-repeat guard) and re-forges the
///      distractor set for that token. This file blanks the token the engine
///      chose; the original keyword scan below stays as the fallback for
///      legacy templates the engine could not rotate.
///   2. Distractors come from the engine forge first, then a cleaned
///      keyword-family dictionary (REAL Python tokens only -- the old
///      input/output/show filler is gone), then a keyword pad. Four tokens
///      are always served, so the fourth answer box is always active.
///   3. The engine only rotates KEYWORD blanks: a blanked number or string
///      ("score = ____") would leave every option plausible with no context
///      to pick between it. Keywords are dictated by the snippet's own
///      syntax, so the missing token is always decidable on screen.
/// </summary>
public class FillInTheBlankPuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.FillInTheBlank;

    private PuzzleTemplate template;
    private string correctAnswer;
    private List<string> tokens;
    private string codeSnippetWithBlank;

    // Keywords that can be blanked out
    private static readonly string[] blankableKeywords = new string[]
    {
        "print", "input", "if", "else", "elif", "for",
        "while", "in", "range", "def", "return", "not",
        "and", "or", "True", "False", "len", "int", "str"
    };

    // Real Python tokens a distractor may use when the correct answer is a
    // keyword/builtin. Everything else (output, show, display, write, loop,
    // made-up words, random identifiers) is rejected -- an option set of
    // real, same-class tokens gives nothing away by being "obviously wrong".
    private static readonly HashSet<string> pythonTokens = new HashSet<string>
    {
        "print", "input", "if", "else", "elif", "for", "while", "in",
        "range", "def", "return", "not", "and", "or", "True", "False",
        "None", "len", "int", "str",
        "pass", "break", "continue", "class", "lambda", "yield", "is",
        "bool", "float", "list", "sum", "type", "abs", "min", "max",
        "sorted", "round"
    };

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(Text displayField) { }

    public void RenderPuzzle(PairACodeUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(SpotTheBugUIController uiController) { }
    public void RenderPuzzle(LineScrambleUIController uiController) { }
    public int GetOptionCount() => tokens != null ? tokens.Count : 0;
    public void RenderPuzzle(FillInTheBlankUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[FillInTheBlankPuzzleFormat] UIController is null");
            return;
        }

        uiController.PopulateUI(codeSnippetWithBlank, tokens);
        Debug.Log($"[FillInTheBlankPuzzleFormat] Rendered. Correct: {correctAnswer}");
    }

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is string strAnswer)
        {
            bool isCorrect = strAnswer.Trim() == correctAnswer.Trim();
            Debug.Log($"[FillInTheBlankPuzzleFormat] Player: {strAnswer} | Correct: {correctAnswer} | Result: {isCorrect}");
            return isCorrect;
        }

        Debug.LogError("[FillInTheBlankPuzzleFormat] Invalid answer type. Expected string.");
        return false;
    }

    public object GetCorrectAnswer() => correctAnswer;

    private void GeneratePuzzle()
    {
        Debug.Log($"[FillInTheBlankPuzzleFormat] GeneratePuzzle() called with codeLines: {string.Join(" | ", template.codeLines)}");
        if (template.codeLines == null || template.codeLines.Count == 0)
        {
            Debug.LogError("[FillInTheBlankPuzzleFormat] Template has no code lines");
            return;
        }

        int targetLine = -1;
        string foundKeyword = null;

        // 1) Honor the engine's rotated blank. RotateFitbBlank names the
        //    token in correctAnswer and the line that holds it in
        //    bugLineIndex. The token is always a keyword, so the header
        //    below stays truthful. If the line hint is stale, scan the
        //    whole snippet before giving up on the rotation.
        string rotated = (template.correctAnswer ?? "").Trim();
        if (rotated.Length > 0)
        {
            int hint = template.bugLineIndex;
            if (hint >= 0 && hint < template.codeLines.Count
                && LineContainsKeyword(template.codeLines[hint], rotated))
            {
                targetLine = hint;
                foundKeyword = rotated;
            }
            else
            {
                for (int i = 0; i < template.codeLines.Count; i++)
                {
                    if (LineContainsKeyword(template.codeLines[i], rotated))
                    {
                        targetLine = i;
                        foundKeyword = rotated;
                        break;
                    }
                }
            }
        }

        // 2) Original scan -- unchanged -- for templates the engine could
        //    not rotate (no unique blankable keyword in the snippet).
        if (targetLine == -1)
        {
            // Shuffle line order to avoid always blanking the same line
            List<int> lineIndices = new List<int>();
            for (int i = 0; i < template.codeLines.Count; i++)
                lineIndices.Add(i);
            ShuffleList(lineIndices);

            foreach (int i in lineIndices)
            {
                string line = template.codeLines[i];
                foreach (string keyword in blankableKeywords)
                {
                    if (LineContainsKeyword(line, keyword))
                    {
                        targetLine = i;
                        foundKeyword = keyword;
                        break;
                    }
                }
                if (targetLine != -1) break;
            }

            // Fallback: blank the variable value if no keyword found.
            // Searches for the line that actually contains the value (or
            // the variable name, as a second try) before falling back to
            // line 0 as a last resort.
            if (targetLine == -1)
            {
                string valueTarget = template.variableValue;
                if (!string.IsNullOrEmpty(valueTarget))
                {
                    for (int i = 0; i < template.codeLines.Count; i++)
                    {
                        if (template.codeLines[i].Contains(valueTarget))
                        {
                            targetLine = i;
                            foundKeyword = valueTarget;
                            break;
                        }
                    }
                }

                if (targetLine == -1 && !string.IsNullOrEmpty(template.variableName))
                {
                    for (int i = 0; i < template.codeLines.Count; i++)
                    {
                        if (template.codeLines[i].Contains(template.variableName))
                        {
                            targetLine = i;
                            foundKeyword = template.variableName;
                            break;
                        }
                    }
                }

                if (targetLine == -1)
                {
                    Debug.LogWarning("[FillInTheBlankPuzzleFormat] No keyword, variableValue, " +
                                     "or variableName found anywhere in codeLines. Falling back " +
                                     "to line 0; this template likely needs a real blank target.");
                    targetLine = 0;
                    foundKeyword = template.variableValue;
                }
            }
        }

        correctAnswer = foundKeyword;

        // Build code snippet with blank. The marker must stay "____" --
        // FillInTheBlankUIController.OnTokenSelected replaces exactly that.
        List<string> displayLines = new List<string>(template.codeLines);
        Debug.Log($"[FillInTheBlankPuzzleFormat] Target line content: '{displayLines[targetLine]}' | Replacing: '{foundKeyword}'");
        displayLines[targetLine] = ReplaceFirstOccurrence(
            displayLines[targetLine], foundKeyword, "____");
        codeSnippetWithBlank = "# Fill in the missing keyword:\n" + string.Join("\n", displayLines);

        // Build tokens: the correct answer is added FIRST and is never
        // filtered out -- the console log and the boxes can never disagree.
        tokens = new List<string>();
        tokens.Add(correctAnswer);

        // 1) Engine-forged distractors: re-forged for THIS draw's answer,
        //    so the family always matches (keyword blank -> keyword options).
        if (template.distractors != null)
        {
            foreach (string d in template.distractors)
                AddDistractor(d);
        }

        // 2) Keyword-family top-up from the cleaned dictionary, sampled per
        //    generation so the option set keeps varying.
        List<string> distractorPool = GetKeywordDistractors(correctAnswer);
        foreach (string d in distractorPool)
        {
            if (tokens.Count >= 4) break;
            AddDistractor(d);
        }

        // 3) Final pad: always exactly four tokens, so the fourth answer
        //    box is always populated and selectable.
        while (tokens.Count < 4)
        {
            string pad = GetFallbackKeyword(correctAnswer);
            if (pad == null || tokens.Contains(pad)) break; // cannot happen with 19 keywords, but never loop forever
            tokens.Add(pad);
        }

        Debug.Log($"[FillInTheBlankPuzzleFormat] Blanked: {foundKeyword} on line {targetLine} | Tokens: {string.Join(", ", tokens)}");
    }

    /// <summary>Defensive gate for every distractor (forged, authored, or
    /// padded): distinct, not the answer, and plausible for the answer's
    /// class. Word-like answers only ever get real Python tokens.</summary>
    private void AddDistractor(string candidate)
    {
        if (tokens.Count >= 4) return;
        if (string.IsNullOrWhiteSpace(candidate)) return;
        candidate = candidate.Trim();
        if (candidate == correctAnswer) return;
        if (tokens.Contains(candidate)) return;
        if (!IsPlausibleForAnswer(correctAnswer, candidate)) return;
        // Same-statement confusion is the lesson for symbols (= vs ==);
        // for word-like answers, near-duplicates give it away instead.
        if (!IsSymbolOnly(correctAnswer)
            && PuzzleVariationEngine.IsNuancePair(correctAnswer, candidate))
            return;
        tokens.Add(candidate);
    }

    private static bool IsSymbolOnly(string text)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(text ?? "",
            @"^[\+\-*/%<>=!]+$");
    }

    /// <summary>A distractor is plausible when it belongs to the same
    /// Python token class as the answer: keywords with keywords, numbers
    /// with numbers, quoted strings with quoted strings.</summary>
    private static bool IsPlausibleForAnswer(string answer, string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (candidate.Length >= 2 && candidate.StartsWith("'") && candidate.EndsWith("'"))
            return true; // quoted literals are always syntactically valid
        if (pythonTokens.Contains(candidate))
            return pythonTokens.Contains(answer)
                || answer == null
                || (!char.IsDigit(answer[0]) && !IsSymbolOnly(answer));
        // Numeric answers (legacy variableValue path): numbers pair with
        // numbers, never with keywords.
        if (answer != null && answer.Length > 0 && char.IsDigit(answer[0]))
            return char.IsDigit(candidate[0]);
        // Anything else: reject invented words outright.
        return false;
    }

    private bool LineContainsKeyword(string line, string keyword)
    {
        // Match whole word only to avoid partial matches
        int idx = line.IndexOf(keyword);
        if (idx < 0) return false;

        bool leftOk = idx == 0 || !char.IsLetterOrDigit(line[idx - 1]);
        bool rightOk = idx + keyword.Length >= line.Length
                    || !char.IsLetterOrDigit(line[idx + keyword.Length]);

        return leftOk && rightOk;
    }

    private string ReplaceFirstOccurrence(string source, string find, string replace)
    {
        int idx = source.IndexOf(find);
        if (idx < 0) return source;
        return source.Substring(0, idx) + replace + source.Substring(idx + find.Length);
    }

    private List<string> GetKeywordDistractors(string keyword)
    {
        // Same shape as the original (sample 3 of a widened pool per
        // generation) but every entry is a REAL Python token now. The old
        // fillers -- output, show, display, write, loop, when, unless...
        // -- taught players to read the junk instead of the code.
        Dictionary<string, List<string>> similar = new Dictionary<string, List<string>>()
        {
            { "print",  new List<string> { "input", "len", "int", "str", "range", "type" } },
            { "input",  new List<string> { "print", "int", "str", "float", "len" } },
            { "if",     new List<string> { "elif", "else", "while", "for", "and", "or", "not" } },
            { "elif",   new List<string> { "else", "if", "while", "and", "or", "not" } },
            { "else",   new List<string> { "elif", "if", "while", "pass", "break" } },
            { "for",    new List<string> { "while", "if", "in", "and", "def", "return" } },
            { "while",  new List<string> { "for", "if", "elif", "and", "not", "break" } },
            { "in",     new List<string> { "not", "and", "or", "is", "for" } },
            { "range",  new List<string> { "len", "list", "sum", "int", "str" } },
            { "def",    new List<string> { "return", "class", "lambda", "pass", "print" } },
            { "return", new List<string> { "yield", "pass", "break", "print", "def" } },
            { "not",    new List<string> { "and", "or", "in", "is", "if" } },
            { "and",    new List<string> { "or", "not", "in", "is", "if" } },
            { "or",     new List<string> { "and", "not", "in", "is", "if" } },
            { "True",   new List<string> { "False", "None", "not", "and", "or" } },
            { "False",  new List<string> { "True", "None", "not", "or", "and" } },
            { "len",    new List<string> { "range", "sum", "list", "int", "str" } },
            { "int",    new List<string> { "str", "float", "bool", "len", "range" } },
            { "str",    new List<string> { "int", "float", "bool", "len", "list" } }
        };

        List<string> pool = similar.ContainsKey(keyword)
            ? new List<string>(similar[keyword])
            : new List<string> { "pass", "break", "continue", "in", "not" };

        ShuffleList(pool);
        return pool.GetRange(0, Mathf.Min(3, pool.Count));
    }

    private string GetFallbackKeyword(string exclude)
    {
        foreach (string k in blankableKeywords)
        {
            if (k == exclude || tokens.Contains(k)) continue;
            return k;
        }
        return null;
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
