using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic interactable for non-NPC world objects (Mission Tablet monolith,
/// Rune Crystal, etc). Reuses the same InteractButtonController registration
/// pattern as NPCController so the same HUD interact button drives both.
///
/// Attach to any GameObject with a trigger Collider.
/// Set interactionType in the Inspector to control what happens on interact.
/// </summary>
public class InteractableObject : MonoBehaviour
{
    public enum InteractionType
    {
        MissionTablet,
        RuneCrystal
        // Add more types here as new interactables are built
    }

    [Header("Identity")]
    public string objectID;
    public string promptText = "Inspect";
    public InteractionType interactionType;

    [Header("Interact Prompt UI")]
    public GameObject interactPromptUI;
    public Text interactPromptText;

    [Header("Sanctum (for Rune Crystal type)")]
    public string sanctumID = "print_console";

    [Header("Guide NPC (for Rune Crystal type)")]
    [Tooltip("npcID of the guide who should give the farewell sequence, e.g. 'printessa'.")]
    public string guideNpcID = "printessa";
    [Tooltip("Sequence to switch the guide to once the crystal is restored, e.g. 'printessa_after_restore'.")]
    public string guideAfterRestoreSequenceID = "printessa_after_restore";

    private bool playerInRange = false;
    private bool interactionActive = false;
    private Transform playerTransform;

    public bool IsPlayerInRange() => playerInRange;

    void Start()
    {
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        if (interactPromptText != null) interactPromptText.text = promptText;
    }

    void Update()
    {
        if (playerInRange && !interactionActive)
        {
            if (Input.GetKeyDown(KeyCode.E))
                TriggerInteraction();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = true;
        playerTransform = other.transform;

        if (interactPromptUI != null) interactPromptUI.SetActive(true);

        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.RegisterInteractable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        playerTransform = null;

        if (interactPromptUI != null) interactPromptUI.SetActive(false);

        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearInteractable(this);
    }

    public virtual void TriggerInteraction()
    {
        if (interactionActive) return;
        interactionActive = true;

        switch (interactionType)
        {
            case InteractionType.MissionTablet:
                HandleMissionTablet();
                break;
            case InteractionType.RuneCrystal:
                HandleRuneCrystal();
                break;
        }

        interactionActive = false;
    }

    private void HandleMissionTablet()
    {
        if (MissionTabletUI.Instance != null)
            MissionTabletUI.Instance.ShowTablet();
        else
            Debug.LogWarning("[InteractableObject] MissionTabletUI.Instance is null.");
    }

    private void HandleRuneCrystal()
    {
        if (StoryProgressionManager.Instance == null) return;

        string crystalQuestID = $"{sanctumID}_restore_crystal";

        // Guard against re-triggering. Without this, pressing E again while
        // still standing in the (now collider-disabled but still
        // "in range") trigger zone re-runs this whole method, including
        // replaying the guide's farewell dialogue every time.
        if (StoryProgressionManager.Instance.IsQuestComplete(crystalQuestID))
        {
            Debug.Log("[InteractableObject] Crystal already restored, ignoring repeat interaction.");
            return;
        }

        // Only allow restoration if boss has been defeated
        string bossQuestID = $"{sanctumID}_boss_defeated";
        if (!StoryProgressionManager.Instance.IsQuestComplete(bossQuestID))
        {
            Debug.Log("[InteractableObject] Boss not yet defeated. Crystal cannot be restored.");
            return;
        }

        // Mark crystal restored
        StoryProgressionManager.Instance.CompleteQuest(crystalQuestID);

        // =========================================================
        // Actually swap the 3D meshes!
        // =========================================================
        RuneCrystal crystal = GetComponent<RuneCrystal>();
        if (crystal != null)
        {
            crystal.Restore();
        }
        else
        {
            Debug.LogError("[InteractableObject] RuneCrystal component is missing from this GameObject!");
        }
        // =========================================================

        // Find the guide and trigger their farewell sequence.
        // guideNpcID / guideAfterRestoreSequenceID are Inspector fields now,
        // not hardcoded, so this same script works unchanged on every
        // sanctum's crystal once you copy-paste the scene setup.
        NPCController guide = FindNPCByID(guideNpcID);
        if (guide != null)
        {
            guide.SetNextSequence(guideAfterRestoreSequenceID);
            guide.TriggerInteraction();
        }
        else
        {
            Debug.LogWarning($"[InteractableObject] Could not find guide NPC with npcID '{guideNpcID}'.");
        }

        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        gameObject.GetComponent<Collider>().enabled = false;

        Debug.Log($"[InteractableObject] Rune Crystal restored: {sanctumID}");
    }

    private NPCController FindNPCByID(string id)
    {
        foreach (var npc in FindObjectsOfType<NPCController>())
            if (npc.npcID == id) return npc;
        return null;
    }
}