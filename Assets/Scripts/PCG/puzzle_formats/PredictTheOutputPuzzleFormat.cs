using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PredictTheOutputPuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.PredictTheOutput;

    private PuzzleTemplate template;
    private string correctAnswer;
    private List<string> options;
    private bool isErrorVariant;

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GeneratePuzzle();
    }

    public void RenderPuzzle(Text displayField) { }
    public void RenderPuzzle(PairACodeUIController uiController) { }
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }

    public void RenderPuzzle(PredictTheOutputUIController uiController)
    {
        if (uiController == null)
        {
            Debug.LogError("[PredictTheOutputPuzzleFormat] UIController is null");
            return;
        }

        string header = isErrorVariant
            ? "# What error does this code produce?"
            : "# What is the output of this code?";

        string codeSnippet = header + "\n" + string.Join("\n", template.codeLines);

        uiController.PopulateUI(codeSnippet, options);
        Debug.Log($"[PredictTheOutputPuzzleFormat] Rendered | Correct: {correctAnswer} | ErrorVariant: {isErrorVariant}");
    }

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is string strAnswer)
        {
            bool isCorrect = strAnswer.Trim() == correctAnswer.Trim();
            Debug.Log($"[PredictTheOutputPuzzleFormat] Player: {strAnswer} | Correct: {correctAnswer} | Result: {isCorrect}");
            return isCorrect;
        }

        Debug.LogError("[PredictTheOutputPuzzleFormat] Invalid answer type. Expected string.");
        return false;
    }

    public object GetCorrectAnswer() => correctAnswer;

    private void GeneratePuzzle()
    {
        // Detect error variant by checking if correctAnswer is an error type
        correctAnswer = template.correctAnswer;
        isErrorVariant = correctAnswer == "NameError"
                      || correctAnswer == "TypeError"
                      || correctAnswer == "SyntaxError"
                      || correctAnswer == "IndexError"
                      || correctAnswer == "ValueError";

        // Build options: correct + distractors
        options = new List<string>();
        options.Add(correctAnswer);

        if (template.distractors != null)
            foreach (string d in template.distractors)
                options.Add(d);

        // Pad to exactly 3 options
        while (options.Count < 3)
            options.Add(GenerateFallbackDistractor());

        // Trim to 3 if somehow over
        while (options.Count > 3)
            options.RemoveAt(options.Count - 1);

        Debug.Log($"[PredictTheOutputPuzzleFormat] Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
    }

    private string GenerateFallbackDistractor()
    {
        string[] fallbacks = new string[]
        {
            "0", "None", "True", "False",
            "NameError", "TypeError", "SyntaxError",
            "Error", "null", "undefined"
        };

        foreach (string f in fallbacks)
            if (!options.Contains(f))
                return f;

        return "None";
    }
}