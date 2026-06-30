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
    public void RenderPuzzle(SpotTheBugUIController uiController) { }
    public void RenderPuzzle(LineScrambleUIController uiController) { }
    public int GetOptionCount() => options.Count;
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
        correctAnswer = template.correctAnswer;
        isErrorVariant = correctAnswer == "NameError"
                      || correctAnswer == "TypeError"
                      || correctAnswer == "SyntaxError"
                      || correctAnswer == "IndexError"
                      || correctAnswer == "ValueError";

        // Build options
        options = new List<string>();
        options.Add(correctAnswer);

        // Generate distractors dynamically instead of using stale template distractors
        if (isErrorVariant)
        {
            string[] errorTypes = new string[] { "NameError", "TypeError", "SyntaxError", "IndexError", "ValueError" };
            foreach (string e in errorTypes)
                if (e != correctAnswer && options.Count < 3)
                    options.Add(e);
        }
        else
        {
            // Generate plausible wrong answers based on correct answer type
            int parsedInt;
            if (int.TryParse(correctAnswer, out parsedInt))
            {
                // Numeric distractors: nearby values
                options.Add((parsedInt + Random.Range(1, 10)).ToString());
                options.Add((parsedInt - Random.Range(1, 10)).ToString());
            }
            else if (correctAnswer.Contains("\n"))
            {
                // Multiline output: shuffle the lines as distractors
                string[] lines = correctAnswer.Split('\n');
                List<string> shuffled = new List<string>(lines);
                ShuffleList(shuffled);
                options.Add(string.Join("\n", shuffled));

                List<string> reversed = new List<string>(lines);
                reversed.Reverse();
                options.Add(string.Join("\n", reversed));
            }
            else
            {
                // String output: use mutated variable name and value as distractors
                if (!string.IsNullOrEmpty(template.variableName))
                    options.Add(template.variableName);
                if (!string.IsNullOrEmpty(template.variableValue)
                    && template.variableValue != correctAnswer)
                    options.Add("'" + template.variableValue + "'");

                // Pad with themed fallbacks
                string[] themedFallbacks = new string[]
                {
                "None", "True", "False", "0",
                "Error", template.variableName + " = " + template.variableValue
                };
                foreach (string f in themedFallbacks)
                    if (!options.Contains(f) && options.Count < 3)
                        options.Add(f);
            }
        }

        // Trim to exactly 3
        while (options.Count > 3)
            options.RemoveAt(options.Count - 1);

        // Pad to exactly 3
        while (options.Count < 3)
            options.Add(GenerateFallbackDistractor());

        Debug.Log($"[PredictTheOutputPuzzleFormat] Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
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