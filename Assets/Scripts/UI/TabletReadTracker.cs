using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks whether the player has READ (viewed/opened) the mission tablet
/// quests and the lesson tablet of each sanctum. Purely informational for
/// the HUD quest checklist — it does NOT gate the boss unlock (that stays
/// XP + mission completion, via MissionTabletManager.IsBossUnlockReady).
///
/// In-memory only, matching MissionTabletManager's _completedMissionIDs
/// (also in-memory): read state resets when the app restarts.
///
/// Wire-up (one line each, call when the player actually views the content):
///   MissionTabletUI: when the mission tablet panel is opened / missions shown:
///       TabletReadTracker.MarkSanctumMissionsRead(sanctumID);
///   LessonTablet (or its UI): when the tablet is opened or its last page is read:
///       TabletReadTracker.MarkLessonRead(sanctumID);
/// </summary>
public static class TabletReadTracker
{
    /// <summary>Fired whenever any read state changes. HUD re-renders on this.</summary>
    public static event Action ReadStateChanged;

    // Sanctums whose mission tablet quest list has been viewed.
    private static readonly HashSet<string> _missionsRead = new HashSet<string>();
    // Sanctums whose lesson tablet has been read.
    private static readonly HashSet<string> _lessonsRead = new HashSet<string>();

    // === Mission tablet quests ==================================================

    /// <summary>Call when the player opens/views a sanctum's mission tablet quests.</summary>
    public static void MarkSanctumMissionsRead(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return;
        if (_missionsRead.Add(sanctumID))
        {
            Debug.Log($"[TabletReadTracker] Mission tablet quests read: {sanctumID}");
            ReadStateChanged?.Invoke();
        }
    }

    public static bool AreSanctumMissionsRead(string sanctumID)
    {
        return !string.IsNullOrEmpty(sanctumID) && _missionsRead.Contains(sanctumID);
    }

    // === Lesson tablet ==========================================================

    /// <summary>Call when the player opens/finishes reading a sanctum's lesson tablet.</summary>
    public static void MarkLessonRead(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return;
        if (_lessonsRead.Add(sanctumID))
        {
            Debug.Log($"[TabletReadTracker] Lesson tablet read: {sanctumID}");
            ReadStateChanged?.Invoke();
        }
    }

    public static bool IsLessonRead(string sanctumID)
    {
        return !string.IsNullOrEmpty(sanctumID) && _lessonsRead.Contains(sanctumID);
    }

    // === Debug / testing ========================================================

    public static void ResetAll()
    {
        _missionsRead.Clear();
        _lessonsRead.Clear();
        ReadStateChanged?.Invoke();
    }
}
