using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class LineScramblePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.LineScramble;

    private PuzzleTemplate template;
    private List<string> shuffledLines;
    private List<int> shuffledLineRowNumbers;

    // Pairwise "row i must come before row j" constraints derived from
    // define/use analysis of template.codeLines. Computed once per puzzle
    // instance in GeneratePuzzle() since it only depends on the (fixed)
    // template content, not on any particular proposed ordering.
    private List<(int i, int j)> mustPrecedePairs;

    // Constructs/builtins that ExtractUses should never treat as a variable
    // reference. Scoped to what the six KCs actually teach.
    private static readonly HashSet<string> pythonKeywords = new HashSet<string>
    {
        "print", "input", "int", "str", "len", "range", "True", "False", "None",
        "if", "elif", "else", "for", "while", "in", "and", "or", "not", "def", "return"
    };

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(Text displayField) { }
    public void RenderPuzzle(PairACodeUIController uiController) { }
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(SpotTheBugUIController uiController) { }

    public int GetOptionCount()
    {
        // Factorial guess probability is mathematically correct for blind
        // random shuffling but does not reflect actual player guessing
        // behavior, which involves partial structural judgments rather
        // than uniform random permutation. Use a flat denominator instead,
        // scaled mildly by line count but capped to avoid near-zero p_guess.
        int n = template.codeLines.Count;
        int cappedDenominator = Mathf.Min(n * 2, 10); // caps at p_guess = 0.10 minimum
        return cappedDenominator;
    }

    public void RenderPuzzle(LineScrambleUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[LineScramblePuzzleFormat] UIController is null");
            return;
        }

        uiController.PopulateUI(shuffledLines, shuffledLineRowNumbers);
        Debug.Log($"[LineScramblePuzzleFormat] Rendered | Lines: {shuffledLines.Count} | " +
                  $"Shuffled row numbers: {string.Join(", ", shuffledLineRowNumbers)}");
    }

    /// <summary>
    /// PREFERRED: pass the player's submitted order as a List&lt;int&gt; of
    /// original row numbers, in the sequence the player arranged them (e.g.
    /// [2,0,1] means the player placed original line 2 first, line 0
    /// second, line 1 third). This is validated against the dependency
    /// graph rather than requiring an exact match to the original array
    /// order, so any two lines with no data dependency between them can be
    /// swapped freely.
    ///
    /// LEGACY: a bool is still accepted so this doesn't hard-break before
    /// the caller (LineScrambleUIController) is updated to pass the actual
    /// order instead of pre-computing its own correctness check. The bool
    /// path skips dependency validation entirely, so it should be migrated
    /// away from -- see the integration note this ships with.
    /// </summary>
    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is List<int> proposedOrder)
        {
            bool valid = IsValidDependencyOrder(proposedOrder);
            Debug.Log($"[LineScramblePuzzleFormat] Proposed order: " +
                      $"{string.Join(",", proposedOrder)} | Valid: {valid}");
            return valid;
        }

        if (playerAnswer is bool boolAnswer)
        {
            Debug.LogWarning("[LineScramblePuzzleFormat] Legacy bool submission received; " +
                             "dependency validation was skipped. Update the caller to pass " +
                             "the player's order as List<int> instead.");
            return boolAnswer;
        }

        Debug.LogError("[LineScramblePuzzleFormat] Invalid answer type: " +
                       $"{playerAnswer?.GetType().Name ?? "null"}");
        return false;
    }

    public object GetCorrectAnswer() =>
        "Any ordering where every variable is defined before it is used or reassigned";

    private void GeneratePuzzle()
    {
        int lineCount = template.codeLines.Count;

        mustPrecedePairs = BuildMustPrecedePairs();

        List<(string line, int rowNumber)> pairedLines = new List<(string, int)>();
        for (int i = 0; i < lineCount; i++)
            pairedLines.Add((template.codeLines[i], i));

        // Re-roll if the shuffle landed on ANY dependency-valid arrangement
        // (not just the literal original order), so the puzzle always
        // requires real rearranging even when the snippet has multiple
        // valid solutions.
        int attempts = 0;
        do
        {
            ShuffleList(pairedLines);
            attempts++;
        }
        while (IsValidDependencyOrder(ExtractRowNumbers(pairedLines)) && attempts < 10);

        shuffledLines = new List<string>();
        shuffledLineRowNumbers = new List<int>();
        foreach (var pair in pairedLines)
        {
            shuffledLines.Add(pair.line);
            shuffledLineRowNumbers.Add(pair.rowNumber);
        }

        Debug.Log($"[LineScramblePuzzleFormat] Original lines: {string.Join(" | ", template.codeLines)}");
        Debug.Log($"[LineScramblePuzzleFormat] Shuffled lines: {string.Join(" | ", shuffledLines)}");
        Debug.Log($"[LineScramblePuzzleFormat] Must-precede pairs: " +
                  $"{string.Join(", ", mustPrecedePairs.ConvertAll(p => $"{p.i}<{p.j}"))}");
    }

    private List<int> ExtractRowNumbers(List<(string line, int rowNumber)> pairs)
    {
        List<int> result = new List<int>();
        foreach (var p in pairs) result.Add(p.rowNumber);
        return result;
    }

    /// <summary>
    /// Builds "row i must come before row j" constraints for every pair of
    /// lines (i, j) with i before j in the ORIGINAL template order, where:
    ///   - j uses or redefines a variable that i defines (read/write-after-write), or
    ///   - i uses a variable that j (re)defines (write-after-read)
    /// A naive "every used variable has SOME earlier definition" check is
    /// NOT sufficient here: it would happily accept moving a later
    /// redefinition (e.g. "score = score + 5") before a read of the
    /// original value ("print(score)"), which changes what actually gets
    /// printed even though no variable is technically "undefined" at any
    /// point. Pairwise ordering constraints catch that; a plain topological
    /// "defined somewhere earlier" check does not.
    /// </summary>
    private List<(int i, int j)> BuildMustPrecedePairs()
    {
        int n = template.codeLines.Count;
        var defs = new List<HashSet<string>>();
        var uses = new List<HashSet<string>>();
        for (int i = 0; i < n; i++)
        {
            defs.Add(new HashSet<string>(ExtractDefines(template.codeLines[i])));
            uses.Add(new HashSet<string>(ExtractUses(template.codeLines[i])));
        }

        var pairs = new List<(int, int)>();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                bool rawOrWaw = defs[i].Overlaps(uses[j]) || defs[i].Overlaps(defs[j]);
                bool war = uses[i].Overlaps(defs[j]);
                if (rawOrWaw || war)
                    pairs.Add((i, j));
            }
        }
        return pairs;
    }

    /// <summary>
    /// Validates a proposed ordering (list of original row numbers) against
    /// the cached must-precede pairs, rather than requiring an exact match
    /// to the original array order.
    /// </summary>
    private bool IsValidDependencyOrder(List<int> proposedRowOrder)
    {
        int n = template.codeLines.Count;
        if (proposedRowOrder == null || proposedRowOrder.Count != n) return false;

        HashSet<int> seen = new HashSet<int>();
        foreach (int r in proposedRowOrder)
        {
            if (r < 0 || r >= n) return false;
            if (!seen.Add(r)) return false; // duplicate row, not a real permutation
        }

        Dictionary<int, int> position = new Dictionary<int, int>();
        for (int idx = 0; idx < proposedRowOrder.Count; idx++)
            position[proposedRowOrder[idx]] = idx;

        foreach (var (i, j) in mustPrecedePairs)
            if (position[i] > position[j])
                return false;

        return true;
    }

    private List<string> ExtractDefines(string line)
    {
        List<string> result = new List<string>();
        string trimmed = line.Trim();

        Match forMatch = Regex.Match(trimmed, @"^for\s+(\w+)\s+in\s+");
        if (forMatch.Success)
        {
            result.Add(forMatch.Groups[1].Value);
            return result;
        }

        Match assignMatch = Regex.Match(trimmed, @"^(\w+)\s*=(?!=)");
        if (assignMatch.Success)
            result.Add(assignMatch.Groups[1].Value);

        return result;
    }

    private List<string> ExtractUses(string line)
    {
        string trimmed = line.Trim();
        string searchScope = trimmed;

        Match assignMatch = Regex.Match(trimmed, @"^\w+\s*=(?!=)(.+)$");
        if (assignMatch.Success)
            searchScope = assignMatch.Groups[1].Value;

        Match forMatch = Regex.Match(trimmed, @"^for\s+\w+\s+in\s+(.+):$");
        if (forMatch.Success)
            searchScope = forMatch.Groups[1].Value;

        // Strip quoted string content BEFORE extracting identifiers, so a
        // word that happens to appear inside a string literal (e.g.
        // greeting = 'score') is never mistaken for a reference to an
        // actual variable named the same thing.
        searchScope = Regex.Replace(searchScope, @"'[^']*'|""[^""]*""", "");

        List<string> result = new List<string>();
        foreach (Match m in Regex.Matches(searchScope, @"\b[a-zA-Z_]\w*\b"))
        {
            string token = m.Value;
            if (pythonKeywords.Contains(token)) continue;
            if (!result.Contains(token)) result.Add(token);
        }
        return result;
    }

    private void ShuffleList(List<(string line, int rowNumber)> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}