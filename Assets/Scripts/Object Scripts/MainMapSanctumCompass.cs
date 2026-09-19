using UnityEngine;

/// <summary>
/// On MainMap, points the HUD compass at the next sanctum entrance the
/// player should go to: the first one in progression order whose boss
/// hasn't been defeated yet.
///
/// Uses StoryProgressionManager.HasDefeatedBoss(), the SAME source of
/// truth SanctumEntranceLock already uses to decide which entrances are
/// unlocked. "Next sanctum" here always agrees with "next unlocked
/// entrance", nothing new to keep in sync.
///
/// No event subscription needed: a boss can only be defeated inside a
/// sanctum scene, and SanctumManager.ExitSequence() already reloads
/// MainMap on every exit, so Start() re-evaluating on every load is
/// sufficient to stay correct.
///
/// Place ONCE in the MainMap scene. Fill sanctumEntrances in progression
/// order (Print Console, Vars Vault, Input Mists, Elif Labyrinth), each
/// with the Transform of that sanctum's entrance in MainMap, matching
/// the same GameObjects SanctumEntranceLock sits on.
///
/// Note: HUDCompassController currently supports one active target at a
/// time. If anything else on MainMap also calls ShowCompassTo, whichever
/// call happens last wins. Nothing else does today, per project files,
/// but worth knowing if you add another compass user to MainMap later.
/// </summary>
public class MainMapSanctumCompass : MonoBehaviour
{
    [System.Serializable]
    public class SanctumEntranceEntry
    {
        [Tooltip("Must match SanctumManager.sanctumID for this sanctum exactly.")]
        public string sanctumID;
        [Tooltip("Shown on the compass label, e.g. 'Vars Vault'.")]
        public string displayLabel;
        [Tooltip("Same entrance GameObject/Transform that SanctumEntranceLock sits on.")]
        public Transform entranceTransform;
    }

    [Tooltip("Fill in progression order: Print Console, Vars Vault, Input Mists, Elif Labyrinth.")]
    public SanctumEntranceEntry[] sanctumEntrances;

    void Start()
    {
        RefreshCompass();
    }

    /// <summary>
    /// Public in case you want to force a re-check from elsewhere later
    /// (e.g. a debug menu), though normal play doesn't need to call this.
    /// </summary>
    public void RefreshCompass()
    {
        if (StoryProgressionManager.Instance == null || HUDCompassController.Instance == null)
            return;

        foreach (var entry in sanctumEntrances)
        {
            if (StoryProgressionManager.Instance.HasDefeatedBoss(entry.sanctumID))
                continue;

            if (entry.entranceTransform != null)
                HUDCompassController.Instance.ShowCompassTo(entry.entranceTransform, entry.displayLabel);
            else
                Debug.LogWarning($"[MainMapSanctumCompass] '{entry.sanctumID}' has no entranceTransform assigned.");

            return;
        }

        // Every sanctum's boss is defeated, nothing left to point to.
        HUDCompassController.Instance.Hide();
    }
}