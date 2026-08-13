using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks story/quest completion state for the current play session.
/// Persistent across scene loads (DontDestroyOnLoad).
/// Notifies QuestManager whenever a quest is completed so the
/// active quest pointer updates automatically.
/// </summary>
public class StoryProgressionManager : MonoBehaviour
{
    public static StoryProgressionManager Instance;

    private HashSet<string> completedQuestIDs = new HashSet<string>();
    private string currentActiveQuestID = "";

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

    public void CompleteQuest(string questID)
    {
        if (string.IsNullOrEmpty(questID)) return;

        if (completedQuestIDs.Add(questID))
        {
            Debug.Log($"[StoryProgressionManager] Quest completed: {questID}");

            if (QuestManager.Instance != null)
                QuestManager.Instance.OnQuestCompleted(questID);
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
    public void ResetProgression()
    {
        completedQuestIDs.Clear();
        currentActiveQuestID = "";
        Debug.Log("[StoryProgressionManager] Progression reset.");
    }
}