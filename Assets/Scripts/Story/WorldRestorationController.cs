using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reflects world-corruption state in MainMap: hides corrupted meshes and
/// shows their restored counterparts once the epilogue has played, matching
/// the Null Wraith's corruption being lifted from all of Aethelscript.
///
/// State is DERIVED, not stored. It reads
/// StoryProgressionManager.IsQuestComplete("epilogue_return_to_mainmap") every time it
/// runs and drives BOTH branches explicitly: corrupted meshes are actively
/// re-shown when the epilogue has NOT been played, not just hidden when it
/// has. This is what prevents world-restoration bleeding from a completed
/// save into a New Game. MainMenuController.OnNewGameClicked() already calls
/// StoryProgressionManager.ResetProgression(), which clears the same
/// completedQuestIDs ledger "epilogue_return_to_mainmap" lives in. So on a fresh
/// session the flag is false, and this script re-shows corruption instead
/// of trusting whatever active/inactive state the scene happened to be
/// left in.
///
/// No changes to SaveSlotData or SaveLoadManager are needed. "epilogue_return_to_mainmap"
/// already rides through the existing completedQuestIDs export/import, so
/// this state survives manual saves, autosave, and load exactly like every
/// other quest flag already does.
///
/// Place one instance in the MainMap scene.
/// </summary>
public class WorldRestorationController : MonoBehaviour
{
    [Tooltip("Must match EpilogueSequenceController.epilogueCompletedQuestID exactly.")]
    public string epilogueCompletedQuestID = "epilogue_return_to_mainmap";

    [System.Serializable]
    public class CorruptionPair
    {
        [Tooltip("The corrupted version of this piece of the map. Hidden once the epilogue has played.")]
        public GameObject corrupted;
        [Tooltip("Optional. The restored version to show in its place. Leave empty if there is no separate restored mesh, corrupted will just be hidden with nothing swapped in.")]
        public GameObject restored;
    }

    [Tooltip("Every corrupted mesh in MainMap, paired with its optional restored counterpart. Assign in Inspector.")]
    public List<CorruptionPair> corruptionPairs = new List<CorruptionPair>();

    private void Start()
    {
        // Runs on every MainMap load (fresh game, continue, load slot,
        // returning from a sanctum) and re-derives the correct visual
        // state from the quest ledger each time. Does not assume any
        // particular Start() order relative to EpilogueSequenceController,
        // both read the same StoryProgressionManager state independently.
        ApplyState();
    }

    private void OnEnable()
    {
        EpilogueSequenceController.OnEpilogueCompleted += HandleEpilogueCompleted;
    }

    private void OnDisable()
    {
        EpilogueSequenceController.OnEpilogueCompleted -= HandleEpilogueCompleted;
    }

    private void HandleEpilogueCompleted()
    {
        // Fires mid-session, the moment the epilogue quest is marked
        // complete, so the corruption is already gone by the time
        // EpilogueSequenceController's optional camera showcase runs.
        ApplyState();
    }

    /// <summary>
    /// Reapplies corruption/restoration state from scratch based on current
    /// StoryProgressionManager state. Safe to call at any time, including
    /// repeatedly, since it always sets both branches explicitly rather
    /// than only ever hiding.
    /// </summary>
    public void ApplyState()
    {
        bool worldRestored = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.IsQuestComplete(epilogueCompletedQuestID);

        foreach (var pair in corruptionPairs)
        {
            if (pair.corrupted != null) pair.corrupted.SetActive(!worldRestored);
            if (pair.restored != null) pair.restored.SetActive(worldRestored);
        }

        Debug.Log($"[WorldRestorationController] Applied state. World restored: {worldRestored}. Pairs affected: {corruptionPairs.Count}");
    }
} 