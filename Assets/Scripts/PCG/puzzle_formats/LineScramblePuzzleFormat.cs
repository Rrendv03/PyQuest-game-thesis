using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LineScramblePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.LineScramble;

    private PuzzleTemplate template;
    private List<string> shuffledLines;
    private List<int> shuffledLineRowNumbers;

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

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is bool boolAnswer)
            return boolAnswer;

        Debug.LogError("[LineScramblePuzzleFormat] Invalid answer type.");
        return false;
    }

    public object GetCorrectAnswer() => "Original row order: 0 to " + (template.codeLines.Count - 1);

    private void GeneratePuzzle()
    {
        int lineCount = template.codeLines.Count;

        // Step 1: Assign each line its fixed row number (0, 1, 2, ... lineCount-1)
        // This is the ground truth row number from the template, never changes
        List<int> rowNumbers = new List<int>();
        for (int i = 0; i < lineCount; i++)
            rowNumbers.Add(i);

        // Step 2: Pair each line with its row number, then shuffle the pairs together
        List<(string line, int rowNumber)> pairedLines = new List<(string, int)>();
        for (int i = 0; i < lineCount; i++)
            pairedLines.Add((template.codeLines[i], rowNumbers[i]));

        // Shuffle ensuring result differs from original order
        int attempts = 0;
        do
        {
            ShuffleList(pairedLines);
            attempts++;
        }
        while (IsAlreadyInOrder(pairedLines) && attempts < 10);

        // Step 3: Extract shuffled lines and their original row numbers separately
        shuffledLines = new List<string>();
        shuffledLineRowNumbers = new List<int>();

        foreach (var pair in pairedLines)
        {
            shuffledLines.Add(pair.line);
            shuffledLineRowNumbers.Add(pair.rowNumber);
        }

        Debug.Log($"[LineScramblePuzzleFormat] Original lines: {string.Join(" | ", template.codeLines)}");
        Debug.Log($"[LineScramblePuzzleFormat] Shuffled lines: {string.Join(" | ", shuffledLines)}");
        Debug.Log($"[LineScramblePuzzleFormat] Shuffled row numbers (these track original position): {string.Join(", ", shuffledLineRowNumbers)}");
    }

    private bool IsAlreadyInOrder(List<(string line, int rowNumber)> pairs)
    {
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].rowNumber != i) return false;
        return true;
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