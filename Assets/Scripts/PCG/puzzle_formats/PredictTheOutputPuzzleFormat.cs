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

        // 12: player-facing goal block (const sentence) + shared `__` frame;
        // the answer/expected-output fact is never rendered here (spoil risk).
        string question = isErrorVariant
            ? "What error does this code produce?"
            : "What is the output of this code?";
        string header = template.context != null
            ? PyQuest.Pcg.Ast.DistractorExplanation.GoalBlock(template.context, question)
            : "# " + question;

        // 2026-10 compact: header lines then raw code, no fence/blank lines
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

            // Phase C/D (09 §8): why-wrong pipe on a wrong pick.
            if (!isCorrect && template != null && template.context != null)
            {
                foreach (var o in template.context.Options)
                    if (!o.Correct && o.Text != null && o.Text.Trim() == strAnswer.Trim())
                        PyQuest.Pcg.Ast.PuzzleFeedback.ReportWrongOption(o.MisconceptionKind);
                PyQuest.Pcg.Ast.PuzzleFeedback.ServedContext = template.context;
            }
            return isCorrect;
        }

        Debug.LogError("[PredictTheOutputPuzzleFormat] Invalid answer type. Expected string.");
        return false;
    }

    public object GetCorrectAnswer() => correctAnswer;

    private void GeneratePuzzle()
    {
        Debug.Log($"[PredictTheOutputPuzzleFormat] template.variableValue={template.variableValue} | template.correctAnswer={template.correctAnswer}");

        // Phase C/D (09 §10): metadata-driven option forging. The generative
        // path already executed every option; proven distractors displace the
        // heuristic families below. Legacy templates (context null) keep this
        // file's whole code path verbatim.
        var ctx = template.context;
        if (ctx != null && ctx.Options != null && ctx.Options.Count >= 3)
        {
            correctAnswer = template.correctAnswer;
            options = new List<string> { correctAnswer };
            foreach (var o in ctx.Options)
                if (!o.Correct && !options.Contains(o.Text) && options.Count < 3)
                    options.Add(o.Text);
            if (options.Count >= 3)
            {
                isErrorVariant = false;
                Debug.Log($"[PredictTheOutputPuzzleFormat] Metadata-driven | Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
                return;
            }
        }

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
        // FIX: pool widened (ZeroDivisionError, IndentationError added --
        // both genuinely reachable in this KC scope, unlike some
        // suggested names that aren't real Python exceptions; using fake
        // ones risks teaching a wrong exception name or letting a player
        // rule an option out just by recognizing it isn't real Python).
        // Also now shuffled before picking: previously this walked the
        // array in FIXED order, so a given correctAnswer always paired
        // with the same 2 distractors every time.
        if (isErrorVariant)
        {
            List<string> errorTypes = new List<string> {
                "NameError", "TypeError", "SyntaxError", "IndexError",
                "ValueError", "ZeroDivisionError", "IndentationError"
            };
            errorTypes.RemoveAll(e => e == correctAnswer);
            ShuffleList(errorTypes);

            // Mix in ONE plausible-looking output as a distractor (not
            // just other error names), so the player has to recognize
            // this is an error case in the first place, not just guess
            // among exception names.
            if (errorTypes.Count > 0)
            {
                string plausibleOutput = GeneratePlausibleOutputDistractor();
                if (!string.IsNullOrEmpty(plausibleOutput) && options.Count < 3)
                    options.Add(plausibleOutput);
            }

            foreach (string e in errorTypes)
                if (options.Count < 3)
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

    /// <summary>
    /// For error-variant questions (correctAnswer is an exception name),
    /// returns a plausible-looking VALUE as one of the distractors, so
    /// the player has to recognize "this errors" in the first place
    /// rather than just picking among exception names by elimination.
    /// </summary>
    private string GeneratePlausibleOutputDistractor()
    {
        if (!string.IsNullOrEmpty(template.variableValue))
        {
            int parsedInt;
            if (int.TryParse(template.variableValue, out parsedInt))
                return (parsedInt + Random.Range(1, 10)).ToString();
            return "'" + template.variableValue + "'";
        }
        return null;
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