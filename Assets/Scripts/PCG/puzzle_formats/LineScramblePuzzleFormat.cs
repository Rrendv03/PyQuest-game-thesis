using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public class LineScramblePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.LineScramble;

    private PuzzleTemplate template;
    private List<string> shuffledLines;
    private List<int> shuffledLineRowNumbers;

    // Rows grouped into structural chunks (see BuildChunks). A chunk's
    // member rows must always appear CONTIGUOUS and in their own original
    // relative order in any valid arrangement; only chunks as a whole can
    // be reordered relative to each other, and only when nothing depends
    // on the difference.
    private List<List<int>> chunks;

    // Pairwise "chunk i must come before chunk j" constraints, indices
    // into `chunks`, derived from define/use analysis aggregated per
    // chunk. Computed once per puzzle instance.
    private List<(int i, int j)> mustPrecedeChunkPairs;

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
        int n = template.codeLines.Count;
        int cappedDenominator = Mathf.Min(n * 2, 10);
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
        "Any ordering where control-flow blocks stay intact and every variable is defined before it is used or reassigned";

    private void GeneratePuzzle()
    {
        chunks = BuildChunks();
        mustPrecedeChunkPairs = BuildMustPrecedeChunkPairs();

        List<List<int>> shuffledChunks = new List<List<int>>(chunks);
        int attempts = 0;
        do
        {
            ShuffleChunks(shuffledChunks);
            attempts++;
        }
        while (IsChunkArrangementFullyOrdered(shuffledChunks) && attempts < 10);

        List<int> flatOrder = new List<int>();
        foreach (var chunk in shuffledChunks)
            flatOrder.AddRange(chunk);

        shuffledLines = new List<string>();
        shuffledLineRowNumbers = new List<int>();
        foreach (int row in flatOrder)
        {
            shuffledLines.Add(template.codeLines[row]);
            shuffledLineRowNumbers.Add(row);
        }

        Debug.Log($"[LineScramblePuzzleFormat] Original lines: {string.Join(" | ", template.codeLines)}");
        Debug.Log($"[LineScramblePuzzleFormat] Chunks: " +
                  $"{string.Join(" / ", chunks.Select(c => "[" + string.Join(",", c) + "]"))}");
        Debug.Log($"[LineScramblePuzzleFormat] Shuffled lines: {string.Join(" | ", shuffledLines)}");
    }

    /// <summary>
    /// Groups codeLines into structural chunks: a line plus any lines that
    /// follow it at GREATER indentation (its body), plus any elif/else
    /// continuations at the same indentation as the header, are fused
    /// into one atomic chunk. A plain data-dependency check has no
    /// concept of "this print belongs inside that if-block and can't
    /// float away from it," so an if/elif/else chain with no variable
    /// references inside its branches (very common, e.g. printing string
    /// literals) looked almost fully reorderable even though rearranging
    /// it produces nonsense or invalid Python. Chunking fixes that
    /// structurally instead of per-template.
    /// </summary>
    private List<List<int>> BuildChunks()
    {
        List<List<int>> result = new List<List<int>>();
        int n = template.codeLines.Count;
        int i = 0;
        while (i < n)
        {
            List<int> chunk = new List<int> { i };
            int baseIndent = IndentOf(template.codeLines[i]);
            int j = i + 1;
            while (j < n)
            {
                string trimmed = template.codeLines[j].TrimStart();
                bool moreIndented = IndentOf(template.codeLines[j]) > baseIndent;
                bool isContinuation = trimmed.StartsWith("elif") || trimmed.StartsWith("else");
                if (moreIndented || isContinuation)
                {
                    chunk.Add(j);
                    j++;
                }
                else break;
            }
            result.Add(chunk);
            i = j;
        }
        return result;
    }

    private int IndentOf(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ') count++;
        return count;
    }

    private List<(int i, int j)> BuildMustPrecedeChunkPairs()
    {
        int m = chunks.Count;
        var defs = new List<HashSet<string>>();
        var uses = new List<HashSet<string>>();
        foreach (var chunk in chunks)
        {
            HashSet<string> chunkDefs = new HashSet<string>();
            HashSet<string> chunkUses = new HashSet<string>();
            foreach (int row in chunk)
            {
                chunkDefs.UnionWith(ExtractDefines(template.codeLines[row]));
                chunkUses.UnionWith(ExtractUses(template.codeLines[row]));
            }
            defs.Add(chunkDefs);
            uses.Add(chunkUses);
        }

        var pairs = new List<(int, int)>();
        for (int i = 0; i < m; i++)
        {
            for (int j = i + 1; j < m; j++)
            {
                bool rawOrWaw = defs[i].Overlaps(uses[j]) || defs[i].Overlaps(defs[j]);
                bool war = uses[i].Overlaps(defs[j]);
                if (rawOrWaw || war)
                    pairs.Add((i, j));
            }
        }
        return pairs;
    }

    private bool IsValidDependencyOrder(List<int> proposedRowOrder)
    {
        int n = template.codeLines.Count;
        if (proposedRowOrder == null || proposedRowOrder.Count != n) return false;

        HashSet<int> seen = new HashSet<int>();
        foreach (int r in proposedRowOrder)
        {
            if (r < 0 || r >= n) return false;
            if (!seen.Add(r)) return false;
        }

        Dictionary<int, int> position = new Dictionary<int, int>();
        for (int idx = 0; idx < proposedRowOrder.Count; idx++)
            position[proposedRowOrder[idx]] = idx;

        foreach (var chunk in chunks)
        {
            List<int> positions = chunk.Select(r => position[r]).OrderBy(p => p).ToList();
            if (positions[positions.Count - 1] - positions[0] != chunk.Count - 1)
                return false;
            for (int k = 0; k < chunk.Count; k++)
                if (proposedRowOrder[positions[0] + k] != chunk[k])
                    return false;
        }

        foreach (var (i, j) in mustPrecedeChunkPairs)
        {
            bool allIBeforeAllJ = chunks[i].All(a => chunks[j].All(b => position[a] < position[b]));
            if (!allIBeforeAllJ)
                return false;
        }

        return true;
    }

    private bool IsChunkArrangementFullyOrdered(List<List<int>> arrangement)
    {
        List<int> flat = new List<int>();
        foreach (var c in arrangement) flat.AddRange(c);
        return IsValidDependencyOrder(flat);
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

        Match ifMatch = Regex.Match(trimmed, @"^(?:if|elif|while)\s+(.+):$");
        if (ifMatch.Success)
            searchScope = ifMatch.Groups[1].Value;

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

    private void ShuffleChunks(List<List<int>> list)
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