using UnityEngine;

/// <summary>
/// Factory for creating puzzle format instances based on puzzle type.
/// This centralized factory allows easy addition of new puzzle formats.
/// </summary>
public static class PuzzleFormatFactory
{
    /// <summary>
    /// Creates and initializes a puzzle format instance for the given template.
    /// </summary>
    /// <param name="template">The puzzle template to create a format for</param>
    /// <returns>An initialized IPuzzleFormat instance, or null if the type is not supported</returns>
    public static IPuzzleFormat CreatePuzzleFormat(PuzzleTemplate template)
    {
        if (template == null)
        {
            Debug.LogError("[PuzzleFormatFactory] Template is null");
            return null;
        }

        IPuzzleFormat format = null;

        switch (template.puzzleType)
        {
            case PuzzleType.FillInTheBlank:
                format = new FillInTheBlankPuzzleFormat();
                break;

            case PuzzleType.SpotTheBug:
                format = new SpotTheBugPuzzleFormat();
                break;

            case PuzzleType.LineScramble:
                format = new LineScramblePuzzleFormat();
                break;

            case PuzzleType.TrueOrFalse:
                format = new TrueOrFalsePuzzleFormat();
                break;

            case PuzzleType.PredictTheOutput:
                format = new PredictTheOutputPuzzleFormat();
                break;

            case PuzzleType.PairACode:
                format = new PairACodePuzzleFormat();
                break;

            default:
                Debug.LogError($"[PuzzleFormatFactory] Unknown puzzle type: {template.puzzleType}");
                return null;
        }

        format.Initialize(template);
        return format;
    }
}
