using UnityEngine;

/// <summary>
/// Place in scenes to trigger story events when player enters a zone,
/// interacts with an object, or conditions are met.
/// </summary>
public class StoryEventTrigger : MonoBehaviour
{
    public enum TriggerType { OnEnter, OnInteract, OnConditionMet }

    [Header("Trigger Settings")]
    public TriggerType triggerType = TriggerType.OnEnter;
    public string eventID;
    public string eventDescription;
    public string sanctumID;

    [Header("Conditions")]
    public bool requireQuestActive;
    public string requiredQuestID;
    public string requiredStageID;
    public bool requireBossDefeated;
    public string requiredBossSanctumID;
    public bool oneTimeOnly = true;

    [Header("Actions")]
    public bool unlockLore;
    public string loreID;
    public bool advanceQuest;
    public string targetQuestID;
    public string targetStageID;
    public bool spawnNPC;
    public GameObject npcPrefab;
    public Transform spawnPoint;

    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (triggerType != TriggerType.OnEnter) return;
        if (!other.CompareTag("Player")) return;

        TryTrigger();
    }

    public void TriggerInteraction()
    {
        if (triggerType != TriggerType.OnInteract) return;
        TryTrigger();
    }

    private void TryTrigger()
    {
        if (oneTimeOnly && hasTriggered) return;
        if (!CheckConditions()) return;

        hasTriggered = true;
        ExecuteActions();
    }

    private bool CheckConditions()
    {
        if (requireQuestActive && StoryProgressionManager.Instance != null)
        {
            if (StoryProgressionManager.Instance.GetCurrentQuest() != requiredQuestID)
                return false;
            if (!string.IsNullOrEmpty(requiredStageID) && StoryProgressionManager.Instance.GetCurrentStage() != requiredStageID)
                return false;
        }

        if (requireBossDefeated && StoryProgressionManager.Instance != null)
        {
            if (!StoryProgressionManager.Instance.HasDefeatedBoss(requiredBossSanctumID))
                return false;
        }

        return true;
    }

    private void ExecuteActions()
    {
        // Log the event
        StudentLogManager.Instance?.LogStoryEvent(eventID, triggerType.ToString().ToLower(), eventDescription, sanctumID);

        // Unlock lore
        if (unlockLore && StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.TriggerLoreFound(loreID, eventDescription);
        }

        // Advance quest (manual override)
        // Note: Most quest advancement is handled by SanctumManager calling StoryProgressionManager directly

        // Spawn NPC
        if (spawnNPC && npcPrefab != null && spawnPoint != null)
        {
            Instantiate(npcPrefab, spawnPoint.position, spawnPoint.rotation);
        }

        // Custom event
        StoryProgressionManager.Instance?.TriggerNPCDialogue("world", eventID);
    }

    private void Update()
    {
        if (triggerType == TriggerType.OnConditionMet)
        {
            TryTrigger();
        }
    }
}