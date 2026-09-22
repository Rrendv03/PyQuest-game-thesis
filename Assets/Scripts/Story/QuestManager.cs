using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the full quest chain for PyQuest in order.
/// Each entry is one atomic objective shown on the HUD.
/// When a questIDToComplete is marked done by DialogueManager or
/// EncounterManager, QuestManager advances to the next quest and
/// updates StoryProgressionManager's active quest pointer.
///
/// NEW: each QuestEntry now carries a sanctumID so the HUD can pair the
/// main quest name (the sanctum's quest) with a live, specific objective
/// description, mission-tablet checkmarks, the XP check, and the
/// boss-unlock phase (see QuestHUDDisplay).
///
/// Place on the same DontDestroyOnLoad GameObject as StoryProgressionManager.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [System.Serializable]
    public class QuestEntry
    {
        public string unlockedByQuestID;
        public string questID;
        public string displayName;
        [Tooltip("Sanctum this quest belongs to (matches MissionTabletQuests.json sanctumID). Empty for non-sanctum quests.")]
        public string sanctumID;
    }

    [Header("Quest Chain")]
    public List<QuestEntry> quests = new List<QuestEntry>();

    /// <summary>Legacy single-line event (kept for compatibility).</summary>
    public event System.Action<string> OnQuestUpdated;

    /// <summary>
    /// NEW: fired whenever the active quest changes (and once on Start),
    /// carrying the full entry so the HUD can render name + objective +
    /// checklist. Also fired by RefreshActiveQuest for live HUD updates.
    /// </summary>
    public event System.Action<QuestEntry> OnActiveQuestChanged;

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
            return;
        }

        if (quests == null || quests.Count == 0)
            BuildDefaultChain();
    }

    void Start()
    {
        EvaluateActiveQuest();
    }

    public void OnQuestCompleted(string completedQuestID)
    {
        EvaluateActiveQuest();
    }

    public void EvaluateActiveQuest()
    {
        if (StoryProgressionManager.Instance == null) return;

        foreach (var quest in quests)
        {
            if (StoryProgressionManager.Instance.IsQuestComplete(quest.questID))
                continue;

            bool unlocked = string.IsNullOrEmpty(quest.unlockedByQuestID)
                || StoryProgressionManager.Instance.IsQuestComplete(quest.unlockedByQuestID);

            if (unlocked)
            {
                StoryProgressionManager.Instance.SetActiveQuest(quest.questID);
                OnQuestUpdated?.Invoke(quest.displayName);
                OnActiveQuestChanged?.Invoke(quest);
                Debug.Log($"[QuestManager] Active quest: {quest.displayName}");
                return;
            }
        }

        StoryProgressionManager.Instance.SetActiveQuest("game_complete");
        OnQuestUpdated?.Invoke("Aethelscript Restored");
        OnActiveQuestChanged?.Invoke(null);
        Debug.Log("[QuestManager] All quests complete.");
    }

    /// <summary>
    /// NEW: ask the HUD to re-render the CURRENT active quest without the
    /// quest having changed (XP gained, mission completed, boss unlocked).
    /// Re-evaluates the chain so the returned entry is always accurate.
    /// </summary>
    public void RefreshActiveQuest()
    {
        EvaluateActiveQuest();
    }

    public QuestEntry GetActiveQuestEntry()
    {
        if (StoryProgressionManager.Instance == null) return null;

        string activeID = StoryProgressionManager.Instance.GetActiveQuestID();
        if (activeID == "game_complete") return null;

        foreach (var quest in quests)
            if (quest.questID == activeID)
                return quest;

        return null;
    }

    public string GetActiveQuestDisplayName()
    {
        QuestEntry entry = GetActiveQuestEntry();
        if (entry != null) return entry.displayName;
        if (StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.GetActiveQuestID() == "game_complete")
            return "Aethelscript Restored";
        return "";
    }

    private void BuildDefaultChain()
    {
        quests = new List<QuestEntry>
        {
            // --- Print Console ---
            new QuestEntry { unlockedByQuestID = "intro_complete",
                questID = "print_console_find_printessa",
                displayName = "Find Printessa in the Print Console",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_find_printessa",
                questID = "print_console_speak_printessa",
                displayName = "Speak with Printessa",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_speak_printessa",
                questID = "print_console_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_defeat_enemy",
                questID = "print_console_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "print_console" },

            // --- Vars Vault ---
            new QuestEntry { unlockedByQuestID = "print_console_restore_crystal",
                questID = "vars_vault_find_variel",
                displayName = "Find Variel in the Vars Vault",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_find_variel",
                questID = "vars_vault_speak_variel",
                displayName = "Speak with Variel",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_speak_variel",
                questID = "vars_vault_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_defeat_enemy",
                questID = "vars_vault_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "vars_vault" },

            // --- Input Mists ---
            new QuestEntry { unlockedByQuestID = "vars_vault_restore_crystal",
                questID = "input_mists_find_evalyn",
                displayName = "Find Evalyn in the Input Mists",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_find_evalyn",
                questID = "input_mists_speak_evalyn",
                displayName = "Speak with Evalyn",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_speak_evalyn",
                questID = "input_mists_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_defeat_enemy",
                questID = "input_mists_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "input_mists" },

            // --- Elif Labyrinth ---
            new QuestEntry { unlockedByQuestID = "input_mists_restore_crystal",
                questID = "elif_labyrinth_find_whilow",
                displayName = "Find Whilow in the Elif Labyrinth",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_find_whilow",
                questID = "elif_labyrinth_speak_whilow",
                displayName = "Speak with Whilow",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_speak_whilow",
                questID = "elif_labyrinth_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_defeat_enemy",
                questID = "elif_labyrinth_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_restore_crystal",
                questID = "epilogue_return_to_mainmap",
                displayName = "Return to Aethelscript" },

            // --- Epilogue handled separately in EpilogueSequenceController.cs ---
            new QuestEntry { unlockedByQuestID = "epilogue_return_to_mainmap",
                questID = "world_restored",
                displayName = "Aethelscript Restored" },
        };
    }
}
