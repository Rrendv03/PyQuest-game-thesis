using UnityEngine;

/// <summary>
/// Centralized data container for puzzle information.
/// Holds the puzzle template, format handler, and metadata.
/// This is the single source of truth for puzzle state across all format types.
/// </summary>
[System.Serializable]
public class PuzzleData
{
    public PuzzleTemplate template;

    [System.NonSerialized]
    public IPuzzleFormat formatHandler;

    public string knowledgeComponent => template?.knowledgeComponent ?? "";
    public PuzzleType puzzleType => template?.puzzleType ?? PuzzleType.SpotTheBug;
    public DifficultyTier difficulty => template?.difficulty ?? DifficultyTier.Remembering;

    public PuzzleData(PuzzleTemplate template, IPuzzleFormat formatHandler)
    {
        this.template = template;
        this.formatHandler = formatHandler;
    }

    /// <summary>
    /// Evaluates a player's answer using the format-specific logic.
    /// </summary>
    /// <param name="playerAnswer">The player's answer (format-specific type)</param>
    /// <returns>True if the answer is correct, false otherwise</returns>
    public bool IsAnswerCorrect(object playerAnswer)
    {
        if (formatHandler == null)
        {
            Debug.LogError("[PuzzleData] Format handler is null");
            return false;
        }

        return formatHandler.EvaluateAnswer(playerAnswer);
    }

    /// <summary>
    /// Gets the correct answer for this puzzle.
    /// </summary>
    public object GetCorrectAnswer()
    {
        return formatHandler?.GetCorrectAnswer();
    }
}
