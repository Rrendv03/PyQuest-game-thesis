using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the full quest chain for PyQuest in order.
/// Each entry is one atomic objective shown on the HUD.
/// When a questIDToComplete is marked done by DialogueManager or
/// EncounterManager, QuestManager advances to the next quest and
/// updates StoryProgressionManager's active quest pointer.
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
    }

    [Header("Quest Chain")]
    public List<QuestEntry> quests = new List<QuestEntry>();

    public event System.Action<string> OnQuestUpdated;

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
                Debug.Log($"[QuestManager] Active quest: {quest.displayName}");
                return;
            }
        }

        StoryProgressionManager.Instance.SetActiveQuest("game_complete");
        OnQuestUpdated?.Invoke("Aethelscript Restored");
        Debug.Log("[QuestManager] All quests complete.");
    }

    public string GetActiveQuestDisplayName()
    {
        if (StoryProgressionManager.Instance == null) return "";

        string activeID = StoryProgressionManager.Instance.GetActiveQuestID();
        if (activeID == "game_complete") return "Aethelscript Restored";

        foreach (var quest in quests)
            if (quest.questID == activeID)
                return quest.displayName;

        return "";
    }

    private void BuildDefaultChain()
    {
        quests = new List<QuestEntry>
        {
            // --- Print Console ---
            new QuestEntry { unlockedByQuestID = "intro_complete",
                questID = "print_console_find_printessa",
                displayName = "Find Printessa in the Print Console" },
            new QuestEntry { unlockedByQuestID = "print_console_find_printessa",
                questID = "print_console_speak_printessa",
                displayName = "Speak with Printessa" },
            new QuestEntry { unlockedByQuestID = "print_console_speak_printessa",
                questID = "print_console_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "print_console_defeat_enemy",
                questID = "print_console_restore_crystal",
                displayName = "Restore the Kernel Crystal" },

            // --- Vars Vault ---
            new QuestEntry { unlockedByQuestID = "print_console_restore_crystal",
                questID = "vars_vault_find_variel",
                displayName = "Find Variel in the Vars Vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_find_variel",
                questID = "vars_vault_speak_variel",
                displayName = "Speak with Variel" },
            new QuestEntry { unlockedByQuestID = "vars_vault_speak_variel",
                questID = "vars_vault_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "vars_vault_defeat_enemy",
                questID = "vars_vault_restore_crystal",
                displayName = "Restore the Kernel Crystal" },

            // --- Input Mists ---
            new QuestEntry { unlockedByQuestID = "vars_vault_restore_crystal",
                questID = "input_mists_find_evalyn",
                displayName = "Find Evalyn in the Input Mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_find_evalyn",
                questID = "input_mists_speak_evalyn",
                displayName = "Speak with Evalyn" },
            new QuestEntry { unlockedByQuestID = "input_mists_speak_evalyn",
                questID = "input_mists_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "input_mists_defeat_enemy",
                questID = "input_mists_restore_crystal",
                displayName = "Restore the Kernel Crystal" },

            // --- Elif Labyrinth ---
            new QuestEntry { unlockedByQuestID = "input_mists_restore_crystal",
                questID = "elif_labyrinth_find_whilow",
                displayName = "Find Whilow in the Elif Labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_find_whilow",
                questID = "elif_labyrinth_speak_whilow",
                displayName = "Speak with Whilow" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_speak_whilow",
                questID = "elif_labyrinth_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_defeat_enemy",
                questID = "elif_labyrinth_restore_crystal",
                displayName = "Restore the Kernel Crystal" }
        };
    }
}