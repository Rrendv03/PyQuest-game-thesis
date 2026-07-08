using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class NPCDialogueManager : MonoBehaviour
{
    public static NPCDialogueManager Instance;

    [Header("Dialogue UI")]
    public GameObject dialoguePanel;
    public Text speakerNameText;
    public Text dialogueBodyText;
    public Button advanceButton;
    public Text advanceButtonLabel;

    [Header("Typewriter Settings")]
    public float typewriterSpeed = 0.04f;

    [Header("Auto-Advance Settings")]
    public float autoAdvanceDelay = 15f;

    [Header("Camera References")]
    public Camera mainGameplayCamera;
    public Camera dialogueCamera;

    private DialogueSequence currentSequence;
    private NPCController currentNPC;
    private int currentLineIndex = 0;
    private bool isTyping = false;
    private bool dialogueActive = false;
    private Coroutine typewriterCoroutine;
    private Coroutine autoAdvanceCoroutine;
    private Coroutine cameraPanCoroutine;
    private ThirdPersonCamera thirdPersonCamera;

    // Store camera origin for restoration
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (advanceButton != null)
            advanceButton.onClick.AddListener(OnAdvancePressed);
    }

    /// <summary>
    /// Entry point called by NPCController to begin a dialogue sequence.
    /// </summary>
    public void StartDialogue(
        DialogueSequence sequence,
        NPCController npc,
        string npcName,
        Vector3 cameraPanOffset,
        Vector3 cameraPanRotation,
        float panDuration)
    {
        currentSequence = sequence;
        currentNPC = npc;
        currentLineIndex = 0;
        dialogueActive = true;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        // Pan camera
        if (cameraPanCoroutine != null)
            StopCoroutine(cameraPanCoroutine);

        cameraPanCoroutine = StartCoroutine(PanCamera(
            cameraPanOffset, cameraPanRotation, panDuration, npc.transform));

        DisplayCurrentLine();

        Debug.Log($"[NPCDialogueManager] Started dialogue with {npcName}");
    }

    /// <summary>
    /// Displays the current line using the typewriter effect.
    /// </summary>
    private void DisplayCurrentLine()
    {
        if (currentSequence == null ||
            currentLineIndex >= currentSequence.lines.Count)
        {
            EndDialogue();
            return;
        }

        DialogueLine line = currentSequence.lines[currentLineIndex];

        if (speakerNameText != null)
            speakerNameText.text = line.speakerName;

        if (dialogueBodyText != null)
            dialogueBodyText.text = "";

        // Cancel existing coroutines before starting new ones
        if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
        if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);

        typewriterCoroutine = StartCoroutine(TypewriterEffect(line.dialogueText));
    }

    private IEnumerator TypewriterEffect(string fullText)
    {
        isTyping = true;

        if (advanceButtonLabel != null)
            advanceButtonLabel.text = "Skip";

        for (int i = 0; i <= fullText.Length; i++)
        {
            if (dialogueBodyText != null)
                dialogueBodyText.text = fullText.Substring(0, i);

            yield return new WaitForSeconds(typewriterSpeed);
        }

        isTyping = false;

        if (advanceButtonLabel != null)
            advanceButtonLabel.text = "Next";

        // Start auto-advance timer after typewriter finishes
        autoAdvanceCoroutine = StartCoroutine(AutoAdvanceTimer());
    }

    private IEnumerator AutoAdvanceTimer()
    {
        yield return new WaitForSeconds(autoAdvanceDelay);

        if (dialogueActive)
            AdvanceLine();
    }

    /// <summary>
    /// Called by the advance button.
    /// If still typing: skip to full text.
    /// If done typing: advance to next line.
    /// </summary>
    private void OnAdvancePressed()
    {
        if (isTyping)
        {
            // Skip typewriter: show full text immediately
            if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);

            DialogueLine line = currentSequence.lines[currentLineIndex];
            if (dialogueBodyText != null)
                dialogueBodyText.text = line.dialogueText;

            isTyping = false;

            if (advanceButtonLabel != null)
                advanceButtonLabel.text = "Next";

            if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);
            autoAdvanceCoroutine = StartCoroutine(AutoAdvanceTimer());
        }
        else
        {
            AdvanceLine();
        }
    }

    private void AdvanceLine()
    {
        if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);

        currentLineIndex++;

        if (currentLineIndex >= currentSequence.lines.Count)
        {
            EndDialogue();
        }
        else
        {
            DisplayCurrentLine();
        }
    }

    private void EndDialogue()
    {
        dialogueActive = false;

        if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
        if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        // Restore camera
        if (cameraPanCoroutine != null) StopCoroutine(cameraPanCoroutine);
        cameraPanCoroutine = StartCoroutine(RestoreCamera(0.6f));

        string completedID = currentSequence?.sequenceID ?? "";
        currentNPC?.OnDialogueEnded(completedID);

        Debug.Log($"[NPCDialogueManager] Dialogue ended: {completedID}");
    }

    /// <summary>
    /// Smoothly pans the main gameplay camera to frame
    /// the player on the left and the NPC on the right.
    /// </summary>
    private IEnumerator PanCamera(
    Vector3 panOffset,
    Vector3 panRotation,
    float duration,
    Transform npcTransform)
    {
        if (mainGameplayCamera == null) yield break;

        // Disable ThirdPersonCamera so LateUpdate doesn't override the pan
        if (thirdPersonCamera == null)
            thirdPersonCamera = mainGameplayCamera.GetComponent<ThirdPersonCamera>();
        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = false;

        originalCameraPosition = mainGameplayCamera.transform.position;
        originalCameraRotation = mainGameplayCamera.transform.rotation;

        Vector3 targetPosition = npcTransform.position + panOffset;
        Quaternion targetRotation = Quaternion.Euler(panRotation);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            mainGameplayCamera.transform.position = Vector3.Lerp(
                originalCameraPosition, targetPosition, t);

            mainGameplayCamera.transform.rotation = Quaternion.Slerp(
                originalCameraRotation, targetRotation, t);

            yield return null;
        }

        mainGameplayCamera.transform.position = targetPosition;
        mainGameplayCamera.transform.rotation = targetRotation;
    }

    private IEnumerator RestoreCamera(float duration)
    {
        if (mainGameplayCamera == null) yield break;

        Vector3 currentPos = mainGameplayCamera.transform.position;
        Quaternion currentRot = mainGameplayCamera.transform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            mainGameplayCamera.transform.position = Vector3.Lerp(
                currentPos, originalCameraPosition, t);

            mainGameplayCamera.transform.rotation = Quaternion.Slerp(
                currentRot, originalCameraRotation, t);

            yield return null;
        }

        mainGameplayCamera.transform.position = originalCameraPosition;
        mainGameplayCamera.transform.rotation = originalCameraRotation;

        // Re-enable ThirdPersonCamera after pan fully restores
        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = true;
    }
}