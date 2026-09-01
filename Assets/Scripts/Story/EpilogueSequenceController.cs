using System.Collections;
using UnityEngine;

/// <summary>
/// Detects arrival in MainMap after the final sanctum (Elif Labyrinth) boss
/// has been defeated, and plays the one-time epilogue cutscene.
///
/// Camera flow, in order:
///   1. Player is teleported to epilogueStandPoint and locked (LockPlayerAndPosition).
///   2. If dialogueFocusPoint is assigned, ThirdPersonCamera is disabled and
///      the camera pans to it BEFORE dialogue starts.
///   3. Dialogue plays.
///   4. On completion, if showcasePoints are assigned, the camera continues
///      panning through them.
///   5. Camera is restored to its true pre-cutscene position/rotation and
///      ThirdPersonCamera re-enabled.
///
/// HUD visibility: DialogueManager.Play() hides the HUD for its own
/// duration and restores it the instant dialogue ends, with no awareness
/// that a camera showcase might still be running afterward, and no
/// coverage at all for the pre-dialogue pan, which happens before Play()
/// is ever called. This script owns HUD visibility for its own full
/// duration instead:
///   - Hidden as soon as the trigger conditions are confirmed, before the
///     pre-dialogue pan or LockPlayerAndPosition even run.
///   - Re-hidden as the very first line of HandleEpilogueComplete(), which
///     fires synchronously from DialogueManager's OnSequenceComplete, right
///     after DialogueManager has already restored it. This closes that gap
///     in the same frame, no visible flicker.
///   - Only genuinely restored at the end of EndSequence(), after the
///     showcase and camera restore are done, and only if no external
///     system (EpilogueEndScreenController) is handling the final handback.
///
/// Two separate completion events, deliberately different timing:
///   - OnEpilogueCompleted fires EARLY, before the showcase, for
///     WorldRestorationController.
///   - OnEpilogueSequenceFullyComplete fires LATE, after EndSequence()
///     finishes entirely, for EpilogueEndScreenController.
///
/// Place one instance in the MainMap scene. Safe to leave active
/// permanently, does nothing if trigger conditions aren't met.
/// </summary>
public class EpilogueSequenceController : MonoBehaviour
{
    [Header("Trigger Condition")]
    [Tooltip("sanctumID that must be cleared to fire the epilogue. Must match SanctumManager.sanctumID / ZoneTrigger's GetSanctumIDFromScene mapping exactly.")]
    public string finalSanctumID = "elif_labyrinth";

    [Tooltip("questID used to mark the epilogue as already played. Also the terminal QuestManager chain entry (unlockedByQuestID = elif_labyrinth_restore_crystal).")]
    public string epilogueCompletedQuestID = "epilogue_return_to_mainmap";

    [Tooltip("dialogue.json sequenceID to play for the epilogue.")]
    public string epilogueSequenceID = "epilogue";

    [Header("Player Handling")]
    [Tooltip("Where the player is teleported to before the epilogue dialogue starts.")]
    public Transform epilogueStandPoint;

    [Tooltip("Enable if EpilogueEndScreenController (or something else) is present and will be the one to re-enable player movement AND restore the HUD when the player is ready to continue.")]
    public bool externalSystemHandlesPlayerHandback = false;

    [Header("Camera - Pre-Dialogue Focus")]
    [Tooltip("Camera position/rotation to pan to BEFORE dialogue starts. Leave empty to skip.")]
    public Transform dialogueFocusPoint;

    [Header("Camera - Post-Dialogue Showcase")]
    [Tooltip("Leave empty to skip the showcase entirely.")]
    public Camera mainMapCamera;
    public Transform[] showcasePoints;
    public float cameraPanSpeed = 2f;
    public float showcaseHoldTime = 1.2f;

    /// <summary>
    /// Fires EARLY: the instant the epilogue quest is marked complete,
    /// before the post-dialogue showcase runs. WorldRestorationController
    /// subscribes to this.
    /// </summary>
    public static event System.Action OnEpilogueCompleted;

    /// <summary>
    /// Fires LATE: after EndSequence() has fully finished, including the
    /// showcase and camera restore. EpilogueEndScreenController subscribes
    /// to this, not OnEpilogueCompleted.
    /// </summary>
    public static event System.Action OnEpilogueSequenceFullyComplete;

    private ThirdPersonCamera thirdPersonCamera;
    private PlayerMovement playerMovement;

    private Vector3 originalCameraPos;
    private Quaternion originalCameraRot;
    private bool cameraStateCaptured = false;

    private void Start()
    {
        StartCoroutine(CheckAndRun());
    }

    private IEnumerator CheckAndRun()
    {
        yield return null;

        if (StoryProgressionManager.Instance == null)
        {
            Debug.LogWarning("[EpilogueSequenceController] StoryProgressionManager missing, skipping epilogue check.");
            yield break;
        }

        bool finalSanctumCleared = StoryProgressionManager.Instance.HasDefeatedBoss(finalSanctumID);
        bool alreadyPlayed = StoryProgressionManager.Instance.IsQuestComplete(epilogueCompletedQuestID);

        if (!finalSanctumCleared || alreadyPlayed)
            yield break;

        // Hidden before anything else happens, covers the pre-dialogue pan,
        // not just DialogueManager's own narrower hide/show window.
        HUDController.Instance?.SetVisible(false);

        LockPlayerAndPosition();

        yield return StartCoroutine(WaitForDialogueManagerThenPlay());
    }

    private IEnumerator WaitForDialogueManagerThenPlay()
    {
        while (DialogueManager.Instance == null || !DialogueManager.Instance.IsRegistryLoaded)
            yield return null;

        if (!DialogueManager.Instance.HasSequence(epilogueSequenceID))
        {
            Debug.LogError($"[EpilogueSequenceController] Sequence '{epilogueSequenceID}' not found in dialogue.json. Unlocking player and aborting.");
            if (playerMovement != null) playerMovement.enabled = true;
            HUDController.Instance?.SetVisible(true);
            yield break;
        }

        if (dialogueFocusPoint != null && mainMapCamera != null)
        {
            CaptureOriginalCameraStateAndDisableFollow();
            yield return StartCoroutine(PanCameraTo(dialogueFocusPoint));
        }

        DialogueManager.Instance.OnSequenceComplete -= HandleEpilogueComplete;
        DialogueManager.Instance.OnSequenceComplete += HandleEpilogueComplete;
        DialogueManager.Instance.Play(epilogueSequenceID);

        Debug.Log("[EpilogueSequenceController] Playing epilogue sequence.");
    }

    private void LockPlayerAndPosition()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[EpilogueSequenceController] No Player found in scene.");
            return;
        }

        playerMovement = player.GetComponent<PlayerMovement>();
        if (playerMovement != null) playerMovement.enabled = false;

        if (epilogueStandPoint != null)
        {
            player.transform.position = epilogueStandPoint.position;
            player.transform.rotation = epilogueStandPoint.rotation;
        }
        else
        {
            Debug.LogWarning("[EpilogueSequenceController] No epilogueStandPoint assigned, player stays at MainMap default position.");
        }
    }

    private void HandleEpilogueComplete(DialogueSequence finished)
    {
        DialogueManager.Instance.OnSequenceComplete -= HandleEpilogueComplete;

        // DialogueManager has already restored the HUD by this point, since
        // this handler runs off its OnSequenceComplete. Re-hide immediately,
        // same frame, so there's no visible pop before the showcase runs.
        HUDController.Instance?.SetVisible(false);

        if (!string.IsNullOrEmpty(finished.questIDToComplete) && StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.CompleteQuest(finished.questIDToComplete);

        if (StoryProgressionManager.Instance != null
            && !StoryProgressionManager.Instance.IsQuestComplete(epilogueCompletedQuestID))
        {
            StoryProgressionManager.Instance.CompleteQuest(epilogueCompletedQuestID);
        }

        OnEpilogueCompleted?.Invoke();

        StartCoroutine(EndSequence());
    }

    private IEnumerator EndSequence()
    {
        if (showcasePoints != null && showcasePoints.Length > 0 && mainMapCamera != null)
            yield return StartCoroutine(RunCameraShowcase());

        RestoreCameraIfNeeded();

        if (!externalSystemHandlesPlayerHandback)
        {
            if (playerMovement != null) playerMovement.enabled = true;
            HUDController.Instance?.SetVisible(true);
        }

        SaveLoadManager.Instance?.AutoSave();

        OnEpilogueSequenceFullyComplete?.Invoke();

        Debug.Log("[EpilogueSequenceController] Epilogue sequence fully complete." +
            (externalSystemHandlesPlayerHandback ? " Player/HUD handback deferred to external system." : " Control returned to player."));
    }

    private void CaptureOriginalCameraStateAndDisableFollow()
    {
        if (mainMapCamera == null) return;

        if (!cameraStateCaptured)
        {
            originalCameraPos = mainMapCamera.transform.position;
            originalCameraRot = mainMapCamera.transform.rotation;
            cameraStateCaptured = true;
        }

        if (thirdPersonCamera == null)
            thirdPersonCamera = mainMapCamera.GetComponent<ThirdPersonCamera>();
        if (thirdPersonCamera != null) thirdPersonCamera.enabled = false;
    }

    private void RestoreCameraIfNeeded()
    {
        if (!cameraStateCaptured || mainMapCamera == null) return;

        mainMapCamera.transform.position = originalCameraPos;
        mainMapCamera.transform.rotation = originalCameraRot;
        if (thirdPersonCamera != null) thirdPersonCamera.enabled = true;

        cameraStateCaptured = false;
    }

    private IEnumerator RunCameraShowcase()
    {
        CaptureOriginalCameraStateAndDisableFollow();

        foreach (Transform point in showcasePoints)
        {
            if (point == null) continue;
            yield return StartCoroutine(PanCameraTo(point));
            yield return new WaitForSeconds(showcaseHoldTime);
        }
    }

    private IEnumerator PanCameraTo(Transform target)
    {
        Vector3 startPos = mainMapCamera.transform.position;
        Quaternion startRot = mainMapCamera.transform.rotation;
        float elapsed = 0f;
        float moveDuration = Vector3.Distance(startPos, target.position) / cameraPanSpeed;
        moveDuration = Mathf.Clamp(moveDuration, 0.5f, 4f);

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
            mainMapCamera.transform.position = Vector3.Lerp(startPos, target.position, t);
            mainMapCamera.transform.rotation = Quaternion.Slerp(startRot, target.rotation, t);
            yield return null;
        }

        mainMapCamera.transform.position = target.position;
        mainMapCamera.transform.rotation = target.rotation;
    }
}