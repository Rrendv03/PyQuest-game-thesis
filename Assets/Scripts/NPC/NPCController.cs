using UnityEngine;
using UnityEngine.UI;

public class NPCController : MonoBehaviour
{
    [Header("NPC Configuration")]
    public NPCDialogueData dialogueData;
    public string startingSequenceID = "intro";
    public GameObject npcModel;

    [Header("Interaction Marker")]
    public Transform markerSlot;

    [Header("Interact Prompt UI")]
    public GameObject interactPromptUI;
    public Text interactPromptText;

    [Header("Camera Pan Settings")]
    public Vector3 cameraPanOffset = new Vector3(-1.5f, 0.5f, -3f);
    public Vector3 cameraPanRotation = new Vector3(10f, 15f, 0f);
    public float cameraPanDuration = 0.6f;

    private bool playerInRange = false;
    private bool interactionActive = false;
    private string currentSequenceID;
    private Transform playerTransform;
    /// <summary>
    /// Called by the mobile HUD interact button.
    /// Only triggers if this NPC is the active in-range NPC.
    /// </summary>
    public bool IsPlayerInRange() => playerInRange;

    private void Start()
    {
        currentSequenceID = startingSequenceID;

        if (interactPromptUI != null)
            interactPromptUI.SetActive(false);

        if (interactPromptText != null)
            interactPromptText.text = $"Talk to {dialogueData?.npcName ?? "NPC"}";
    }

    private void Update()
    {
        // Always face the player while in range
        if (playerInRange && playerTransform != null && npcModel != null)
        {
            Vector3 direction = playerTransform.position - npcModel.transform.position;
            direction.y = 0f;

            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                npcModel.transform.rotation = Quaternion.Slerp(
                    npcModel.transform.rotation,
                    targetRotation,
                    Time.deltaTime * 5f);
            }
        }

        // Check for interact button press
        if (playerInRange && !interactionActive)
        {
            // Mobile: tap interact button via UI
            // Desktop: press E key for testing
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

        // Register with HUD interact button
        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.RegisterNPC(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        playerTransform = null;
        if (interactPromptUI != null) interactPromptUI.SetActive(false);

        // Unregister from HUD interact button
        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearNPC(this);
    }

    /// <summary>
    /// Called by the interact button UI or key press.
    /// Public so a UI button OnClick can call it directly.
    /// </summary>
    public void TriggerInteraction()
    {
        if (interactionActive || dialogueData == null) return;

        DialogueSequence sequence = dialogueData.GetSequence(currentSequenceID);
        if (sequence == null) return;

        interactionActive = true;

        if (interactPromptUI != null)
            interactPromptUI.SetActive(false);

        // Disable player movement
        if (playerTransform != null)
        {
            PlayerMovement pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = false;
        }

        Debug.Log($"[NPCController] Starting interaction: {currentSequenceID}");

        NPCDialogueManager.Instance.StartDialogue(
            sequence,
            this,
            dialogueData.npcName,
            cameraPanOffset,
            cameraPanRotation,
            cameraPanDuration);
    }

    /// <summary>
    /// Called by NPCDialogueManager when the dialogue sequence ends.
    /// </summary>
    public void OnDialogueEnded(string completedSequenceID)
    {
        interactionActive = false;

        // Re-enable player movement
        if (playerTransform != null)
        {
            PlayerMovement pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }

        if (interactPromptUI != null && playerInRange)
            interactPromptUI.SetActive(true);

        Debug.Log($"[NPCController] Dialogue ended: {completedSequenceID}");

        // Hook: advance story progression here in future
        // e.g. StoryProgressionManager.Instance.OnSequenceCompleted(completedSequenceID);
    }

    /// <summary>
    /// Advances to the next dialogue sequence by ID.
    /// Called externally by story progression systems.
    /// </summary>
    public void SetNextSequence(string sequenceID)
    {
        currentSequenceID = sequenceID;
        Debug.Log($"[NPCController] Next sequence set to: {sequenceID}");
    }
}