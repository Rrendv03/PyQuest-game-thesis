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

    private void EvaluateActiveQuest()
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
            new QuestEntry { unlockedByQuestID = "intro_complete",
                questID = "echoing_atrium_find_echo",
                displayName = "Find Echo in the Echoing Atrium" },
            new QuestEntry { unlockedByQuestID = "echoing_atrium_find_echo",
                questID = "echoing_atrium_speak_echo",
                displayName = "Speak with Echo" },
            new QuestEntry { unlockedByQuestID = "echoing_atrium_speak_echo",
                questID = "echoing_atrium_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "echoing_atrium_defeat_enemy",
                questID = "echoing_atrium_restore_crystal",
                displayName = "Restore the Rune Crystal" },
            new QuestEntry { unlockedByQuestID = "echoing_atrium_restore_crystal",
                questID = "vault_find_lyra",
                displayName = "Find Lyra in the Vault of Essence" },
            new QuestEntry { unlockedByQuestID = "vault_find_lyra",
                questID = "vault_speak_lyra",
                displayName = "Speak with Lyra" },
            new QuestEntry { unlockedByQuestID = "vault_speak_lyra",
                questID = "vault_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "vault_defeat_enemy",
                questID = "vault_restore_crystal",
                displayName = "Restore the Rune Crystal" },
            new QuestEntry { unlockedByQuestID = "vault_restore_crystal",
                questID = "whitewake_find_auralis",
                displayName = "Find Auralis in the Whitewake Mist" },
            new QuestEntry { unlockedByQuestID = "whitewake_find_auralis",
                questID = "whitewake_speak_auralis",
                displayName = "Speak with Auralis" },
            new QuestEntry { unlockedByQuestID = "whitewake_speak_auralis",
                questID = "whitewake_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "whitewake_defeat_enemy",
                questID = "whitewake_restore_crystal",
                displayName = "Restore the Rune Crystal" },
            new QuestEntry { unlockedByQuestID = "whitewake_restore_crystal",
                questID = "labyrinth_find_selvara",
                displayName = "Find Selvara in the Labyrinth of Logic" },
            new QuestEntry { unlockedByQuestID = "labyrinth_find_selvara",
                questID = "labyrinth_speak_selvara",
                displayName = "Speak with Selvara" },
            new QuestEntry { unlockedByQuestID = "labyrinth_speak_selvara",
                questID = "labyrinth_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption" },
            new QuestEntry { unlockedByQuestID = "labyrinth_defeat_enemy",
                questID = "labyrinth_restore_crystal",
                displayName = "Restore the Rune Crystal" }
        };
    }
}