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
        RuneCrystal,
        LessonTablet
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
    [Tooltip("Direct reference to the guide NPC. Assign in Inspector.")]
    public NPCController guideNPC;
    [Tooltip("Where to teleport the guide before their farewell dialogue.")]
    public Transform npcTeleportPoint;
    [Tooltip("Sequence to switch the guide to once the crystal is restored, e.g. 'printessa_after_restore'.")]
    public string guideAfterRestoreSequenceID = "printessa_after_restore";

    [Header("Corruption Meshes")]
    [Tooltip("Meshes to disable when the crystal is restored (e.g., corruption surrounding the sanctum).")]
    public GameObject[] corruptionMeshes;

    [Header("Lesson Tablet (for Lesson Tablet type)")]
    [Tooltip("Must match a key in LessonTabletUI's content dictionary, e.g. 'print_statements'.")]
    public string knowledgeComponentID;

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
            case InteractionType.LessonTablet:
                HandleLessonTablet();
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
        if (!StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID))
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

        // =========================================================
        // Disable corruption meshes surrounding the sanctum
        // =========================================================
        if (corruptionMeshes != null)
        {
            foreach (var mesh in corruptionMeshes)
            {
                if (mesh != null) mesh.SetActive(false);
            }
            Debug.Log($"[InteractableObject] Disabled {corruptionMeshes.Length} corruption mesh(es) in {sanctumID}.");
        }
        // =========================================================

        // Trigger the guide's farewell sequence AFTER crystal is restored.
        if (guideNPC != null && !guideNPC.HasDeparted())
        {
            if (npcTeleportPoint != null)
            {
                guideNPC.transform.position = npcTeleportPoint.position;
                guideNPC.transform.rotation = npcTeleportPoint.rotation;

                // Also teleport the visual model in case it's on a separate GameObject
                if (guideNPC.npcModel != null)
                {
                    guideNPC.npcModel.transform.position = npcTeleportPoint.position;
                    guideNPC.npcModel.transform.rotation = npcTeleportPoint.rotation;
                }

                Debug.Log($"[InteractableObject] Teleported guide '{guideNPC.npcID}' + model to crystal.");
            }
            if (npcTeleportPoint != null)
            {
                guideNPC.transform.position = npcTeleportPoint.position;
                guideNPC.transform.rotation = npcTeleportPoint.rotation;
                Debug.Log($"[InteractableObject] Teleported guide '{guideNPC.npcID}' to crystal.");
            }
            guideNPC.SetNextSequence(guideAfterRestoreSequenceID);
            guideNPC.TriggerInteraction();
        }
        else
        {
            Debug.LogWarning("[InteractableObject] Guide NPC is missing, null, or has already departed.");
        }

        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        gameObject.GetComponent<Collider>().enabled = false;

        Debug.Log($"[InteractableObject] Rune Crystal restored: {sanctumID}");
    }

    private void HandleLessonTablet()
    {
        if (LessonTabletUI.Instance != null)
            LessonTabletUI.Instance.ShowLesson(knowledgeComponentID, sanctumID);
        else
            Debug.LogWarning("[InteractableObject] LessonTabletUI.Instance is null.");
    }
}