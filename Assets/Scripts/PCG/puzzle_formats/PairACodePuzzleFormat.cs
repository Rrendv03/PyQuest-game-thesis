using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PairACodePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.PairACode;

    private PuzzleTemplate template;
    private string correctAnswer;
    private List<string> options;
    private string codeSnippetWithBlank;

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(FillInTheBlankUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(SpotTheBugUIController uiController) { }
    public void RenderPuzzle(LineScrambleUIController uiController) { }
    public int GetOptionCount() => options.Count;
    public void RenderPuzzle(Text displayField) { }

    public void RenderPuzzle(PairACodeUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[PairACodePuzzleFormat] UIController is null");
            return;
        }

        uiController.PopulateUI(codeSnippetWithBlank, options);
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

        // Always blank the LAST line so context from previous lines is always visible
        // Exception: if last line is a print statement and there is only 1 line, blank index 0
        int blankIndex = template.codeLines.Count - 1;

        // If only one line exists, blank it but prepend a context comment
        if (template.codeLines.Count == 1)
        {
            correctAnswer = template.codeLines[0];
            codeSnippetWithBlank = "# What is the missing line?\n[ ? ]";
        }
        else
        {
            correctAnswer = template.codeLines[blankIndex];

            // Build display: show all lines except the blanked one
            List<string> displayLines = new List<string>(template.codeLines);
            displayLines[blankIndex] = "[ ? ]";
            codeSnippetWithBlank = "# Complete the missing line:\n" + string.Join("\n", displayLines);
        }

        // Build options: correct answer + distractors from template
        options = new List<string>();
        options.Add(correctAnswer);

        if (template.distractors != null)
        {
            foreach (string d in template.distractors)
            {
                // Also mutate distractors to match variable name changes from PCG
                string mutatedDistractor = d;
                if (!string.IsNullOrEmpty(template.variableName))
                    mutatedDistractor = mutatedDistractor.Replace(template.variableName, template.variableName);
                options.Add(mutatedDistractor);
            }
        }

        // Pad with fallback distractors if not enough options
        while (options.Count < 4)
            options.Add(GenerateFallbackDistractor());

        Debug.Log($"[PairACodePuzzleFormat] Blank index: {blankIndex} | Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
    }

    private string GenerateFallbackDistractor()
    {
        string[] fallbacks = new string[]
        {
        $"print('{template.variableName}')",
        $"{template.variableName} == {template.variableValue}",
        $"return {template.variableName}",
        "pass",
        $"input('{template.variableName}: ')",
        "break",
        $"{template.variableName} = None",
        $"print({template.variableValue})"
        };

        return fallbacks[Random.Range(0, fallbacks.Length)];
    }
}