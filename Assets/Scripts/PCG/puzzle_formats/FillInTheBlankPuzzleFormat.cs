using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
    public int GetOptionCount() => tokens.Count;
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
        if (template.codeLines == null || template.codeLines.Count == 0)
        {
            Debug.LogError("[FillInTheBlankPuzzleFormat] Template has no code lines");
            return;
        }

        // Find a line containing a blankable keyword
        int targetLine = -1;
        string foundKeyword = null;

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

        // Fallback: blank the variable value if no keyword found
        if (targetLine == -1)
        {
            targetLine = 0;
            foundKeyword = template.variableValue;
        }

        correctAnswer = foundKeyword;

        // Build code snippet with blank
        List<string> displayLines = new List<string>(template.codeLines);
        Debug.Log($"[FillInTheBlankPuzzleFormat] Target line content: '{displayLines[targetLine]}' | Replacing: '{foundKeyword}'");
        displayLines[targetLine] = ReplaceFirstOccurrence(
            displayLines[targetLine], foundKeyword, "____");
        codeSnippetWithBlank = "# Fill in the missing keyword:\n" + string.Join("\n", displayLines);

        // Build tokens: correct + distractors
        tokens = new List<string>();
        tokens.Add(correctAnswer);

        // Add keyword distractors that are similar but wrong
        List<string> distractorPool = GetKeywordDistractors(foundKeyword);
        foreach (string d in distractorPool)
        {
            if (tokens.Count >= 4) break;
            tokens.Add(d);
        }

        // Pad from template distractors if needed
        if (template.distractors != null)
        {
            foreach (string d in template.distractors)
            {
                if (tokens.Count >= 4) break;
                tokens.Add(d);
            }
        }

        // Final pad
        while (tokens.Count < 4)
            tokens.Add(GetFallbackKeyword(foundKeyword));

        Debug.Log($"[FillInTheBlankPuzzleFormat] Blanked: {foundKeyword} on line {targetLine} | Tokens: {string.Join(", ", tokens)}");
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
        // Return semantically similar but wrong keywords
        Dictionary<string, List<string>> similar = new Dictionary<string, List<string>>()
        {
            { "print",  new List<string> { "input", "output", "display" } },
            { "input",  new List<string> { "print", "read", "scan" } },
            { "if",     new List<string> { "elif", "else", "while" } },
            { "elif",   new List<string> { "if", "else", "when" } },
            { "else",   new List<string> { "elif", "if", "otherwise" } },
            { "for",    new List<string> { "while", "loop", "each" } },
            { "while",  new List<string> { "for", "until", "loop" } },
            { "in",     new List<string> { "of", "from", "at" } },
            { "range",  new List<string> { "len", "list", "count" } },
            { "def",    new List<string> { "class", "func", "var" } },
            { "return", new List<string> { "yield", "output", "print" } },
            { "True",   new List<string> { "False", "None", "1" } },
            { "False",  new List<string> { "True", "None", "0" } },
            { "len",    new List<string> { "range", "count", "size" } },
            { "int",    new List<string> { "str", "float", "bool" } },
            { "str",    new List<string> { "int", "float", "char" } }
        };

        if (similar.ContainsKey(keyword))
            return similar[keyword];

        return new List<string> { "print", "input", "if" };
    }

    private string GetFallbackKeyword(string exclude)
    {
        foreach (string k in blankableKeywords)
            if (k != exclude) return k;
        return "pass";
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
}