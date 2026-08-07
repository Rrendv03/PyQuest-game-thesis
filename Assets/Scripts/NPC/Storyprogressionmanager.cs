using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks story/quest completion state for the current play session.
/// Persistent across scene loads (DontDestroyOnLoad). Place exactly one
/// instance in whichever scene loads first in the actual boot sequence,
/// a second instance elsewhere will self-destroy on Awake.
///
/// This class tracks two separate things:
///   1. A completion ledger: which quest IDs has the player finished.
///   2. A single active-quest pointer: what should the HUD tell the
///      player to do right now.
/// These are not the same data. The active-quest pointer must be set by
/// the quest/HUD system, which does not exist yet. Until it does,
/// GetActiveQuestID() has nothing meaningful to return, it is a seam,
/// not a working feature.
///
/// SaveLoadManager reads the Export methods to write a save file, and
/// calls the Import methods after loading one.
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

    // ?? Completion Ledger ?????????????????????????????????????????????????????
    public void CompleteQuest(string questID)
    {
        if (string.IsNullOrEmpty(questID)) return;

        if (completedQuestIDs.Add(questID))
            Debug.Log($"[StoryProgressionManager] Quest completed: {questID}");
    }

    public bool IsQuestComplete(string questID)
    {
        return !string.IsNullOrEmpty(questID) && completedQuestIDs.Contains(questID);
    }

    // ?? Active Quest Pointer (seam, not yet populated by anything) ?????????????
    public void SetActiveQuest(string questID)
    {
        currentActiveQuestID = questID ?? "";
        Debug.Log($"[StoryProgressionManager] Active quest set: {currentActiveQuestID}");
    }

    public string GetActiveQuestID()
    {
        return currentActiveQuestID;
    }

    // ?? Save/Load Bridge ??????????????????????????????????????????????????????
    public List<string> ExportCompletedQuestIDs()
    {
        return new List<string>(completedQuestIDs);
    }

    public void ImportCompletedQuestIDs(List<string> ids)
    {
        completedQuestIDs.Clear();
        if (ids == null) return;
        foreach (var id in ids)
            completedQuestIDs.Add(id);
    }

    public string ExportActiveQuestID()
    {
        return currentActiveQuestID;
    }

    public void ImportActiveQuestID(string questID)
    {
        currentActiveQuestID = questID ?? "";
    }
}