using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpotTheBugPuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.SpotTheBug;

    private PuzzleTemplate template;
    private int correctLineIndex;
    private string correctFix;
    private List<string> fixOptions;
    private List<List<string>> allLineFixOptions;

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
        if (playerAnswer is bool boolAnswer)
            return boolAnswer;

        Debug.LogError("[SpotTheBugPuzzleFormat] Invalid answer type.");
        return false;
    }

    public object GetCorrectAnswer() => correctFix;

    private void GeneratePuzzle()
    {
        // Step 1: Pick a random line to inject a bug into
        // Prefer lines with operators or function calls for more interesting bugs
        List<int> candidateLines = new List<int>();
        for (int i = 0; i < template.codeLines.Count; i++)
            candidateLines.Add(i);

        ShuffleList(candidateLines);
        correctLineIndex = candidateLines[0];

        // Step 2: Store clean version of the buggy line as the correct fix
        correctFix = template.codeLines[correctLineIndex];

        // Step 3: Inject bug into that line
        string buggedLine = InjectBug(correctFix);
        template.codeLines[correctLineIndex] = buggedLine;

        // Step 4: Build fix options per line
        // Each line gets 3 options based on what that line contains
        allLineFixOptions = new List<List<string>>();
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            List<string> lineOptions = new List<string>();

            if (i == correctLineIndex)
            {
                // Buggy line: correct fix + 2 wrong options
                lineOptions.Add(correctFix);
                List<string> wrongOptions = GenerateWrongOptions(correctFix, buggedLine);
                foreach (string w in wrongOptions)
                    if (!lineOptions.Contains(w) && lineOptions.Count < 3)
                        lineOptions.Add(w);
            }
            else
            {
                // Correct line: show the line itself + 2 plausible mutations
                // that look wrong so the player knows this line is fine
                string cleanLine = template.codeLines[i];
                lineOptions.Add(cleanLine);
                List<string> decoys = GenerateDecoys(cleanLine);
                foreach (string d in decoys)
                    if (!lineOptions.Contains(d) && lineOptions.Count < 3)
                        lineOptions.Add(d);
            }

            // Pad to 3
            while (lineOptions.Count < 3)
                lineOptions.Add(GenerateFallbackOption(i));

            // Trim to 3
            while (lineOptions.Count > 3)
                lineOptions.RemoveAt(lineOptions.Count - 1);

            // Shuffle
            ShuffleList(lineOptions);
            allLineFixOptions.Add(lineOptions);
        }

        Debug.Log($"[SpotTheBugPuzzleFormat] Bug injected on line {correctLineIndex} | " +
                  $"Clean: {correctFix} | Bugged: {buggedLine}");
    }

    /// <summary>
    /// Injects a subtle but unambiguous bug into a clean line.
    /// </summary>
    private string InjectBug(string line)
    {
        // Strategy 1: flip comparison operator
        if (line.Contains("!=")) return line.Replace("!=", "=!");
        if (line.Contains("==")) return line.Replace("==", "=");
        if (line.Contains(">=")) return line.Replace(">=", "=>");
        if (line.Contains("<=")) return line.Replace("<=", "=<");
        if (line.Contains(" > ")) return line.Replace(" > ", " < ");
        if (line.Contains(" < ")) return line.Replace(" < ", " > ");

        // Strategy 2: misspell function keyword
        if (line.Contains("print(")) return line.Replace("print(", "pritn(");
        if (line.Contains("input(")) return line.Replace("input(", "inpput(");
        if (line.Contains("range(")) return line.Replace("range(", "rang(");
        if (line.Contains("while ")) return line.Replace("while ", "whlie ");
        if (line.Contains("elif ")) return line.Replace("elif ", "elseif ");

        // Strategy 3: flip arithmetic operator
        if (line.Contains(" + ")) return line.Replace(" + ", " - ");
        if (line.Contains(" * ")) return line.Replace(" * ", " / ");

        // Strategy 4: wrong assignment operator
        if (line.Contains(" = ") && !line.Contains("=="))
            return line.Replace(" = ", " == ");

        // Strategy 5: remove indentation
        if (line.StartsWith("    "))
            return line.TrimStart();

        // Strategy 6: add wrong indentation
        if (!line.StartsWith("    ") && !line.StartsWith("for")
            && !line.StartsWith("if") && !line.StartsWith("while")
            && !line.StartsWith("def") && !line.StartsWith("else")
            && !line.StartsWith("elif"))
            return "    " + line;

        // Fallback: wrap variable in quotes
        if (!string.IsNullOrEmpty(template.variableName)
            && line.Contains(template.variableName))
            return line.Replace(template.variableName,
                                $"'{template.variableName}'");

        return line + " # error";
    }

    /// <summary>
    /// Generates wrong fix options for the buggy line.
    /// These should look plausible but still be wrong.
    /// </summary>
    private List<string> GenerateWrongOptions(string correctLine, string buggedLine)
    {
        List<string> result = new List<string>();

        // Wrong option 1: the bugged line itself
        if (buggedLine != correctLine)
            result.Add(buggedLine);

        // Wrong option 2: a different mutation of the correct line
        if (correctLine.Contains("!="))
            result.Add(correctLine.Replace("!=", "=="));
        else if (correctLine.Contains("=="))
            result.Add(correctLine.Replace("==", ">="));
        else if (correctLine.Contains(" > "))
            result.Add(correctLine.Replace(" > ", " >= "));
        else if (correctLine.Contains(" < "))
            result.Add(correctLine.Replace(" < ", " <= "));
        else if (correctLine.Contains("print("))
            result.Add(correctLine.Replace("print(", "Print("));
        else if (correctLine.Contains(" + "))
            result.Add(correctLine.Replace(" + ", " * "));
        else if (correctLine.Contains(" = ") && !correctLine.Contains("=="))
            result.Add(correctLine.Replace(" = ", " += "));
        else if (!string.IsNullOrEmpty(template.variableName)
              && correctLine.Contains(template.variableName))
            result.Add(correctLine.Replace(template.variableName,
                                           $"'{template.variableName}'"));

        return result;
    }

    /// <summary>
    /// Generates decoy options for a correct line.
    /// These should look like common bugs so the player
    /// sees plausible options but can tell the line is fine.
    /// </summary>
    private List<string> GenerateDecoys(string cleanLine)
    {
        List<string> result = new List<string>();

        if (cleanLine.Contains("print("))
        {
            result.Add(cleanLine.Replace("print(", "pritn("));
            result.Add(cleanLine.Replace("print(", "Print("));
        }
        else if (cleanLine.Contains("=="))
        {
            result.Add(cleanLine.Replace("==", "="));
            result.Add(cleanLine.Replace("==", "!="));
        }
        else if (cleanLine.Contains(" = ") && !cleanLine.Contains("=="))
        {
            result.Add(cleanLine.Replace(" = ", " == "));
            result.Add(cleanLine.Replace(" = ", " += "));
        }
        else if (cleanLine.Contains(" + "))
        {
            result.Add(cleanLine.Replace(" + ", " - "));
            result.Add(cleanLine.Replace(" + ", " * "));
        }
        else if (cleanLine.Contains("range("))
        {
            result.Add(cleanLine.Replace("range(", "rang("));
            result.Add(cleanLine.Replace("range(", "Range("));
        }
        else if (cleanLine.Contains("    "))
        {
            result.Add(cleanLine.TrimStart());
            result.Add("        " + cleanLine.TrimStart());
        }
        else
        {
            result.Add("    " + cleanLine);
            if (!string.IsNullOrEmpty(template.variableName)
                && cleanLine.Contains(template.variableName))
                result.Add(cleanLine.Replace(template.variableName,
                                             template.variableName + "a"));
        }

        return result;
    }

    private string GenerateFallbackOption(int lineIndex)
    {
        string line = template.codeLines[lineIndex];
        if (!string.IsNullOrEmpty(template.variableName)
            && line.Contains(template.variableName))
            return line.Replace(template.variableName,
                                template.variableName + "_err");
        return line + " # ?";
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