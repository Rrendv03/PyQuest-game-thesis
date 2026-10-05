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

    // Phase C/D (09 §7/§10): machine-derived context on the generative path.
    // Null on the legacy path — every consumer must null-check and fall back
    // to today's headers. Also carries the served metadata so EncounterManager
    // / UI controllers never guess (single source of truth).

    /// <summary>Machine-derived puzzle context (PuzzleContextMetadata) or null.</summary>
    [System.NonSerialized]
    public PyQuest.Pcg.Ast.PuzzleContextMetadata context;

    /// <summary>Set by the serving path when the player picks a proven-wrong
    /// option: the misconception kind feeding DistractorExplanation / errorText.</summary>
    public string PickedWrongKind;

    public string knowledgeComponent => template?.knowledgeComponent ?? "";
    public PuzzleType puzzleType => template?.puzzleType ?? PuzzleType.SpotTheBug;
    public DifficultyTier difficulty => template?.difficulty ?? DifficultyTier.Beginner;

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
