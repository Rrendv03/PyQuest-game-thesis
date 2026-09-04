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

        // FIX: this used to do mutatedDistractor.Replace(template.variableName,
        // template.variableName) -- replacing a string with itself, a no-op --
        // so distractors were shown completely unmutated, verbatim from the
        // original template, on every single puzzle instance. PCGEngine's
        // MutatePuzzlePublic now mutates template.distractors alongside
        // codeLines directly, so by the time this runs they already reflect
        // the current variable name/value; no local sync step needed here.
        if (template.distractors != null)
        {
            foreach (string d in template.distractors)
                if (!options.Contains(d) && d != correctAnswer)
                    options.Add(d);
        }

        // Pad with fallback distractors if not enough options.
        // FIX: previously had no duplicate check, so two independent random
        // picks from the same small pool could add the identical string
        // twice as if they were two different wrong answers. Also mixes in
        // a guaranteed-wrong-but-thematically-related option (a real line
        // pulled from elsewhere in this exact snippet) alongside the
        // generic hardcoded pool, rather than relying on the generic pool
        // alone. Capped so a pathologically small pool can't spin forever;
        // fewer than 4 options is a safe degradation, not a crash.
        int fallbackAttempts = 0;
        while (options.Count < 4 && fallbackAttempts < 20)
        {
            string candidate = (fallbackAttempts % 3 == 0 && PCGEngine.Instance != null)
                ? PCGEngine.Instance.GenerateGuaranteedWrongOption(template.codeLines, correctAnswer)
                : GenerateFallbackDistractor();

            if (!options.Contains(candidate))
                options.Add(candidate);

            fallbackAttempts++;
        }

        Debug.Log($"[PairACodePuzzleFormat] Blank index: {blankIndex} | Correct: {correctAnswer} | Options: {string.Join(", ", options)}");
    }

    private string GenerateFallbackDistractor()
    {
        // FIX: these variableName-based entries used to always be in the
        // pool, even for templates with no variableName set, producing
        // garbled options like " == " or "print('')" with nothing filled
        // in. Now they're only added when there's an actual variable to
        // reference.
        List<string> fallbacks = new List<string> { "pass", "break", "continue", "return None" };

        if (!string.IsNullOrEmpty(template.variableName))
        {
            fallbacks.Add($"print('{template.variableName}')");
            fallbacks.Add($"{template.variableName} == {template.variableValue}");
            fallbacks.Add($"return {template.variableName}");
            fallbacks.Add($"input('{template.variableName}: ')");
            fallbacks.Add($"{template.variableName} = None");
            fallbacks.Add($"print({template.variableValue})");
        }

        return fallbacks[Random.Range(0, fallbacks.Count)];
    }
}