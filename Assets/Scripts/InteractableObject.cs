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
    public string sanctumID = "echoing_atrium";

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

    public void TriggerInteraction()
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

        // Only allow restoration if boss has been defeated
        string bossQuestID = $"{sanctumID}_boss_defeated";
        if (!StoryProgressionManager.Instance.IsQuestComplete(bossQuestID))
        {
            Debug.Log("[InteractableObject] Boss not yet defeated. Crystal cannot be restored.");
            return;
        }

        // Mark crystal restored and trigger Echo's farewell dialogue
        string crystalQuestID = $"{sanctumID}_restore_crystal";
        StoryProgressionManager.Instance.CompleteQuest(crystalQuestID);

        // Find Echo and trigger her farewell sequence
        NPCController echo = FindNPCByID("echo");
        if (echo != null)
        {
            echo.SetNextSequence("echo_after_restore");
            echo.TriggerInteraction();
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