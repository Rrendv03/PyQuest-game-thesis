using System.Collections.Generic;
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
        // Step 1: Determine the bug line. Prefer the template author's
        // explicit bugLineIndex when it's a valid index into codeLines.
        // Convention: bugLineIndex of -1 (or anything outside range) means
        // "not authored, pick randomly" -- existing templates that never
        // set this field keep behaving exactly as before, and only
        // templates that DO set it get the benefit of an author-controlled,
        // guaranteed-on-topic bug line instead of a blind random pick.
        if (template.bugLineIndex >= 0 && template.bugLineIndex < template.codeLines.Count)
        {
            correctLineIndex = template.bugLineIndex;
        }
        else
        {
            List<int> candidateLines = new List<int>();
            for (int i = 0; i < template.codeLines.Count; i++)
                candidateLines.Add(i);

            ShuffleList(candidateLines);
            correctLineIndex = candidateLines[0];
        }

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

        Debug.Log($"[SpotTheBugPuzzleFormat] Bug line: {correctLineIndex} " +
                  $"({(template.bugLineIndex == correctLineIndex ? "authored" : "random")}) | " +
                  $"Clean: {correctFix} | Bugged: {buggedLine}");
    }

    /// <summary>
    /// Injects a subtle but unambiguous bug into a clean line. Strategies
    /// are tried in order; the first one that applies wins. Strategies 7
    /// and 8 are new: the old code fell back to appending "# error" as a
    /// comment when nothing else matched, which is not an actual bug in
    /// Python (a trailing comment doesn't change execution), so some lines
    /// (e.g. a bare "else:" or "if flag:" with no comparison operator) were
    /// being marked "bugged" while still running identically to the clean
    /// version. Strategies 7/8 guarantee a real change before that
    /// harmless fallback is ever reached.
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

        // Strategy 7 (NEW): drop the trailing colon on a block header
        // (if/elif/else/for/while/def). Python requires it, so removing it
        // is always a real SyntaxError.
        string trimmedEnd = line.TrimEnd();
        if (trimmedEnd.EndsWith(":"))
            return trimmedEnd.Substring(0, trimmedEnd.Length - 1);

        // Strategy 8 (NEW): last-resort character transposition. Swaps the
        // first two non-whitespace characters, which breaks almost any
        // keyword, identifier, or literal it touches, without relying on
        // recognizing a specific pattern.
        string trimmedStart = line.TrimStart();
        string indent = line.Substring(0, line.Length - trimmedStart.Length);
        if (trimmedStart.Length >= 2)
        {
            char[] chars = trimmedStart.ToCharArray();
            char tmp = chars[0];
            chars[0] = chars[1];
            chars[1] = tmp;
            return indent + new string(chars);
        }

        // True last resort: only reachable for lines with 0-1 non-whitespace
        // characters, which shouldn't occur in authored puzzle content.
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