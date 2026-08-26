using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class NPCController : MonoBehaviour
{
    [Header("NPC Identity")]
    public string npcID;              // stable ID for save matching, e.g. "printessa", "variel"
    public string npcDisplayName;     // shown in the interact prompt, e.g. "Printessa"
    [Tooltip("Quest ID to auto-complete when player first interacts (e.g. print_console_find_printessa).")]
    public string questIDToCompleteOnInteract;

    [Header("Dialogue")]
    public string startingSequenceID;
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
    public Camera mainGameplayCamera; // optional, falls back to Camera.main if unset

    [Header("Departure")]
    public float departFadeDuration = 1.5f;

    [Header("HUD Compass (optional)")]
    [Tooltip("If set, the HUD compass will point here after THIS NPC's current dialogue sequence finishes. Leave empty for NPCs that don't need to direct the player anywhere.")]
    public Transform compassTargetOnComplete;
    [Tooltip("Label shown above the compass arrow, e.g. 'Exit' or 'Vars Vault'.")]
    public string compassLabelOnComplete = "Exit";
    [Tooltip("Only show the compass when THIS sequenceID is the one that just finished. Leave empty to fire on ANY sequence completion for this NPC, which is what currently causes the compass to appear after first-meeting/intro dialogue too.")]
    public string compassTriggerSequenceID = "";

    private bool playerInRange = false;
    private bool interactionActive = false;
    private string currentSequenceID;
    private bool hasDeparted = false;
    private Transform playerTransform;
    private ThirdPersonCamera thirdPersonCamera;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;
    private Coroutine cameraPanCoroutine;

    public bool IsPlayerInRange() => playerInRange;
    public bool HasDeparted() => hasDeparted;
    public string GetCurrentSequenceID() => currentSequenceID;

    private void Start()
    {
        currentSequenceID = startingSequenceID;

        if (interactPromptUI != null)
            interactPromptUI.SetActive(false);

        if (interactPromptText != null)
            interactPromptText.text = $"Talk to {npcDisplayName}";
    }

    private void Update()
    {
        if (playerInRange && playerTransform != null && npcModel != null && !hasDeparted)
        {
            Vector3 direction = playerTransform.position - npcModel.transform.position;
            direction.y = 0f;
            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                npcModel.transform.rotation = Quaternion.Slerp(
                    npcModel.transform.rotation, targetRotation, Time.deltaTime * 5f);
            }
        }

        if (playerInRange && !interactionActive && !hasDeparted)
        {
            if (Input.GetKeyDown(KeyCode.E))
                TriggerInteraction();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || hasDeparted) return;

        playerInRange = true;
        playerTransform = other.transform;

        if (interactPromptUI != null) interactPromptUI.SetActive(true);

        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.RegisterNPC(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        playerTransform = null;

        if (interactPromptUI != null) interactPromptUI.SetActive(false);

        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearNPC(this);
    }

    public void TriggerInteraction()
    {
        if (interactionActive || hasDeparted) return;

        if (DialogueManager.Instance == null || !DialogueManager.Instance.HasSequence(currentSequenceID))
        {
            Debug.LogWarning($"[NPCController] No sequence '{currentSequenceID}' found for {npcID}.");
            return;
        }

        interactionActive = true;

        if (interactPromptUI != null) interactPromptUI.SetActive(false);

        if (playerTransform != null)
        {
            PlayerMovement pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = false;
        }

        // Defensive: guard against double-subscription if TriggerInteraction
        // is ever re-entered unexpectedly.
        DialogueManager.Instance.OnSequenceComplete -= HandleSequenceComplete;
        DialogueManager.Instance.OnSequenceComplete += HandleSequenceComplete;

        if (cameraPanCoroutine != null) StopCoroutine(cameraPanCoroutine);
        cameraPanCoroutine = StartCoroutine(PanCamera(cameraPanOffset, cameraPanRotation, cameraPanDuration));

        // In NPCController.TriggerInteraction(), right after the null checks and before Play()
        if (!string.IsNullOrEmpty(questIDToCompleteOnInteract) && StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.CompleteQuest(questIDToCompleteOnInteract);

        DialogueManager.Instance.Play(currentSequenceID);

        Debug.Log($"[NPCController] Starting interaction: {currentSequenceID}");
    }

    private void HandleSequenceComplete(DialogueSequence finished)
    {
        DialogueManager.Instance.OnSequenceComplete -= HandleSequenceComplete;

        interactionActive = false;

        if (playerTransform != null)
        {
            PlayerMovement pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }

        if (cameraPanCoroutine != null) StopCoroutine(cameraPanCoroutine);
        cameraPanCoroutine = StartCoroutine(RestoreCamera(0.6f));

        if (!string.IsNullOrEmpty(finished.questIDToComplete) && StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.CompleteQuest(finished.questIDToComplete);

        // Compass hook: fires only when compassTargetOnComplete is assigned AND
        // (compassTriggerSequenceID is empty, meaning "any sequence", OR the
        // sequence that just finished matches compassTriggerSequenceID). Leaving
        // compassTriggerSequenceID empty reproduces the old unconditional
        // behavior, set it to a specific farewell/directional sequenceID to stop
        // the compass firing on intro/tutorial dialogue.
        bool compassSequenceMatches = string.IsNullOrEmpty(compassTriggerSequenceID)
            || finished.sequenceID == compassTriggerSequenceID;

        if (compassTargetOnComplete != null && compassSequenceMatches && HUDCompassController.Instance != null)
            HUDCompassController.Instance.ShowCompassTo(compassTargetOnComplete, compassLabelOnComplete);

        switch (finished.endBehavior)
        {
            case "depart":
                StartCoroutine(Depart());
                break;
            case "stay":
                currentSequenceID = finished.nextSequenceIfStay;
                if (interactPromptUI != null && playerInRange)
                    interactPromptUI.SetActive(true);
                break;
            default:
                if (interactPromptUI != null && playerInRange)
                    interactPromptUI.SetActive(true);
                break;
        }

        Debug.Log($"[NPCController] Dialogue ended: {finished.sequenceID}, behavior: {finished.endBehavior}");
    }

    private IEnumerator Depart()
    {
        // BUGFIX: Set departed flag immediately so player can't re-trigger
        // dialogue during the 1.5s fade. Also clean up HUD references
        // because gameObject.SetActive(false) does NOT fire OnTriggerExit.
        hasDeparted = true;

        playerInRange = false;
        playerTransform = null;
        if (interactPromptUI != null) interactPromptUI.SetActive(false);

        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearNPC(this);

        Debug.Log($"[NPCController] Depart() cleanup executed for {npcID}. playerInRange={playerInRange}, hasDeparted={hasDeparted}");

        // Fade to black, disable, fade back, matching the seamless
        // departure behavior specified for NPCs the script sends away.
        // Reuses DialogueManager's fadeOverlay since it already sits on
        // a top-level canvas above the game world.
        Image overlay = DialogueManager.Instance != null ? DialogueManager.Instance.fadeOverlay : null;

        if (overlay != null)
        {
            float elapsed = 0f;
            while (elapsed < departFadeDuration)
            {
                elapsed += Time.deltaTime;
                Color c = overlay.color;
                c.a = Mathf.Lerp(0f, 1f, elapsed / departFadeDuration);
                overlay.color = c;
                yield return null;
            }
        }

        gameObject.SetActive(false);

        if (overlay != null)
        {
            float elapsed = 0f;
            while (elapsed < departFadeDuration)
            {
                elapsed += Time.deltaTime;
                Color c = overlay.color;
                c.a = Mathf.Lerp(1f, 0f, elapsed / departFadeDuration);
                overlay.color = c;
                yield return null;
            }
        }
    }

    private Camera ResolveCamera()
    {
        if (mainGameplayCamera != null) return mainGameplayCamera;
        return Camera.main;
    }

    private IEnumerator PanCamera(Vector3 panOffset, Vector3 panRotation, float duration)
    {
        Camera cam = ResolveCamera();
        if (cam == null) yield break;

        if (thirdPersonCamera == null)
            thirdPersonCamera = cam.GetComponent<ThirdPersonCamera>();
        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = false;

        originalCameraPosition = cam.transform.position;
        originalCameraRotation = cam.transform.rotation;

        Vector3 targetPosition = transform.position + panOffset;
        Quaternion targetRotation = Quaternion.Euler(panRotation);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            cam.transform.position = Vector3.Lerp(originalCameraPosition, targetPosition, t);
            cam.transform.rotation = Quaternion.Slerp(originalCameraRotation, targetRotation, t);
            yield return null;
        }

        cam.transform.position = targetPosition;
        cam.transform.rotation = targetRotation;
    }

    private IEnumerator RestoreCamera(float duration)
    {
        Camera cam = ResolveCamera();
        if (cam == null) yield break;

        Vector3 currentPos = cam.transform.position;
        Quaternion currentRot = cam.transform.rotation;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            cam.transform.position = Vector3.Lerp(currentPos, originalCameraPosition, t);
            cam.transform.rotation = Quaternion.Slerp(currentRot, originalCameraRotation, t);
            yield return null;
        }

        cam.transform.position = originalCameraPosition;
        cam.transform.rotation = originalCameraRotation;

        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = true;
    }

    /// <summary>
    /// Called by SaveLoadManager when restoring a save.
    /// </summary>
    public void RestoreState(string sequenceID, bool departed)
    {
        currentSequenceID = sequenceID;
        hasDeparted = departed;

        if (hasDeparted)
            gameObject.SetActive(false);
    }

    public void SetNextSequence(string sequenceID)
    {
        currentSequenceID = sequenceID;
        Debug.Log($"[NPCController] Sequence set to: {sequenceID}");
    }

    /// <summary>
    /// Called by SanctumManager when loading a sanctum that was already
    /// cleared. Permanently disables the NPC without playing a fade.
    /// </summary>
    public void ForceDepart()
    {
        hasDeparted = true;
        gameObject.SetActive(false);

        if (npcModel != null)
            npcModel.SetActive(false);

        Debug.Log($"[NPCController] {npcID} force-departed (sanctum already cleared).");
    }
}