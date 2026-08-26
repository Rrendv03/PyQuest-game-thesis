using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks story/quest completion state for the current play session.
/// Persistent across scene loads (DontDestroyOnLoad).
/// Notifies QuestManager whenever a quest is completed so the
/// active quest pointer updates automatically.
///
/// Extended to also track per-sanctum first-visit and boss-defeat
/// state for SanctumManager, kept separate from the quest ledger
/// above since a sanctum can be re-entered after its boss is
/// defeated while the quest chain only tracks completion order.
/// </summary>
public class StoryProgressionManager : MonoBehaviour
{
    public static StoryProgressionManager Instance;

    private HashSet<string> completedQuestIDs = new HashSet<string>();
    private string currentActiveQuestID = "";

    private HashSet<string> visitedSanctumIDs = new HashSet<string>();
    private HashSet<string> defeatedBossSanctumIDs = new HashSet<string>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #region Quest Ledger (existing, unchanged)

    public void CompleteQuest(string questID)
    {
        if (string.IsNullOrEmpty(questID)) return;

        if (completedQuestIDs.Add(questID))
        {
            Debug.Log($"[StoryProgressionManager] Quest completed: {questID}");

            if (QuestManager.Instance != null)
                QuestManager.Instance.OnQuestCompleted(questID);

            StudentLogManager.Instance?.LogQuestEvent(questID, "completed", "", $"Completed quest: {questID}");
        }
    }

    public bool IsQuestComplete(string questID)
    {
        return !string.IsNullOrEmpty(questID) && completedQuestIDs.Contains(questID);
    }

    public void SetActiveQuest(string questID)
    {
        currentActiveQuestID = questID ?? "";
        Debug.Log($"[StoryProgressionManager] Active quest set: {currentActiveQuestID}");
        StudentLogManager.Instance?.LogQuestEvent(currentActiveQuestID, "started", "", $"Active quest: {currentActiveQuestID}");
    }

    public string GetActiveQuestID() => currentActiveQuestID;

    public List<string> ExportCompletedQuestIDs()
        => new List<string>(completedQuestIDs);

    public void ImportCompletedQuestIDs(List<string> ids)
    {
        completedQuestIDs.Clear();
        if (ids == null) return;
        foreach (var id in ids)
            completedQuestIDs.Add(id);
    }

    public string ExportActiveQuestID() => currentActiveQuestID;

    public void ImportActiveQuestID(string questID)
        => currentActiveQuestID = questID ?? "";

    #endregion

    #region Sanctum Visit / Boss Defeat Tracking (added for SanctumManager)

    /// <summary>
    /// Call on a sanctum's first entry. Returns true if this was
    /// genuinely the first visit (false if already visited, so
    /// callers can skip first-visit-only logic on repeat calls).
    /// </summary>
    public bool TriggerSanctumFirstVisit(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return false;
        if (!visitedSanctumIDs.Add(sanctumID)) return false;

        Debug.Log($"[StoryProgressionManager] First visit: {sanctumID}");
        StudentLogManager.Instance?.LogStoryEvent(
            $"first_visit_{sanctumID}", "first_visit",
            $"Player first entered {sanctumID}", sanctumID);
        return true;
    }

    public bool HasVisitedSanctum(string sanctumID)
        => !string.IsNullOrEmpty(sanctumID) && visitedSanctumIDs.Contains(sanctumID);

    public void TriggerBossUnlocked(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return;
        StudentLogManager.Instance?.LogStoryEvent(
            $"boss_unlocked_{sanctumID}", "boss_unlocked",
            $"Boss unlocked in {sanctumID}", sanctumID);
    }

    public void TriggerBossDefeated(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return;
        if (!defeatedBossSanctumIDs.Add(sanctumID)) return;

        Debug.Log($"[StoryProgressionManager] Boss defeated: {sanctumID}");
        StudentLogManager.Instance?.LogStoryEvent(
            $"boss_defeated_{sanctumID}", "boss_defeated",
            $"Boss defeated in {sanctumID}", sanctumID);
    }

    public bool HasDefeatedBoss(string sanctumID)
        => !string.IsNullOrEmpty(sanctumID) && defeatedBossSanctumIDs.Contains(sanctumID);

    public List<string> ExportVisitedSanctums() => new List<string>(visitedSanctumIDs);

    public void ImportVisitedSanctums(List<string> ids)
    {
        visitedSanctumIDs.Clear();
        if (ids == null) return;
        foreach (var id in ids) visitedSanctumIDs.Add(id);
    }

    public List<string> ExportDefeatedBosses() => new List<string>(defeatedBossSanctumIDs);

    public void ImportDefeatedBosses(List<string> ids)
    {
        defeatedBossSanctumIDs.Clear();
        if (ids == null) return;
        foreach (var id in ids) defeatedBossSanctumIDs.Add(id);
    }

    public void ResetProgression()
    {
        completedQuestIDs.Clear();
        visitedSanctumIDs.Clear();
        defeatedBossSanctumIDs.Clear();
        currentActiveQuestID = "";
    }

    #endregion

    #region StoryEventTrigger Compatibility (thin aliases, no new state)

    /// <summary>
    /// Alias for GetActiveQuestID(). StoryEventTrigger compares this
    /// against a required quest ID before firing.
    /// </summary>
    public string GetCurrentQuest() => currentActiveQuestID;

    /// <summary>
    /// The ledger design has no stage concept, quests are flat
    /// completion IDs, not staged sub-states. Always returns "" until
    /// a real stage system exists. StoryEventTrigger only checks this
    /// when requiredStageID is set in the Inspector, leave that field
    /// blank on any trigger using this StoryProgressionManager.
    /// </summary>
    public string GetCurrentStage() => "";

    /// <summary>
    /// Logs a lore-unlock event. Not tracked as separate ledger state,
    /// same story-event log StudentLogManager already writes to.
    /// </summary>
    public void TriggerLoreFound(string loreID, string description)
    {
        if (string.IsNullOrEmpty(loreID)) return;
        Debug.Log($"[StoryProgressionManager] Lore found: {loreID}");
        StudentLogManager.Instance?.LogStoryEvent(loreID, "lore_found", description);
    }

    /// <summary>
    /// Logs a world/NPC dialogue trigger event. Not tracked as separate
    /// ledger state, same story-event log as everything else here.
    /// </summary>
    public void TriggerNPCDialogue(string npcID, string eventID)
    {
        if (string.IsNullOrEmpty(eventID)) return;
        Debug.Log($"[StoryProgressionManager] NPC dialogue triggered: {npcID}/{eventID}");
        StudentLogManager.Instance?.LogStoryEvent(eventID, "npc_dialogue", $"Triggered by {npcID}");
    }

    #endregion
}