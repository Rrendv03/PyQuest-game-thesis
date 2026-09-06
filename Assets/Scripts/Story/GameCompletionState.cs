using UnityEngine;

/// <summary>
/// Tracks whether the player has ever reached the epilogue end screen.
/// Persisted via PlayerPrefs, a device/install-level fact, not save-slot
/// data, so it survives New Game and app restarts alike.
///
/// Set exactly once, from EpilogueEndScreenController.Show(), at the
/// moment the epilogue sequence fully completes. That is deliberately
/// BEFORE the player can click Continue, so accidentally dismissing the
/// end screen can never un-set this or prevent it from being set.
/// </summary>
public static class GameCompletionState
{
    private const string CompletedKey = "GameCompleted";

    public static void MarkGameCompleted()
    {
        PlayerPrefs.SetInt(CompletedKey, 1);
        PlayerPrefs.Save();
    }

    public static bool HasCompletedGame()
    {
        return PlayerPrefs.GetInt(CompletedKey, 0) == 1;
    }
}