using System.Collections;
using UnityEngine;
using UnityEngine.UI;
public class TabletMissionObject : InteractableObject
{
    [Header("Mission Identity")]
    [Tooltip("Must exactly match a missionID in MissionTabletQuests.json")]
    public string missionID;
    [Header("Visual States")]
    public GameObject defaultMesh;
    public GameObject restoredMesh;
    [Header("HUD")]
    [Tooltip("Drag the root HUDCanvas here. Disabled during puzzle + transition.")]
    public GameObject hudCanvas;
    [Header("Completion Transition")]
    [Tooltip("Drag a full-screen black Image here (under a canvas). It will fade in/out on completion.")]
    public Image darkOverlay;
    public float fadeDuration = 0.6f;
    private bool isCompleted = false;
    private Collider triggerCollider;
    void Start()
    {
        triggerCollider = GetComponent<Collider>();
        if (MissionTabletManager.Instance != null &&
            MissionTabletManager.Instance.IsMissionComplete(missionID))
        {
            SetRestoredStateImmediate();
        }
        else
        {
            SetDefaultState();
            var data = MissionTabletManager.Instance?.GetMissionByID(missionID);
            if (data != null && !string.IsNullOrEmpty(data.promptText))
                promptText = data.promptText;
        }
    }
    public override void TriggerInteraction()
    {
        if (isCompleted) return;
        var data = MissionTabletManager.Instance?.GetMissionByID(missionID);
        if (data == null)
        {
            Debug.LogError($"[TabletMissionObject] missionID '{missionID}' not found.");
            return;
        }
        // Hide HUD and prompt
        if (hudCanvas != null) hudCanvas.SetActive(false);
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        // Disable player
        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = false;
        // Random puzzle
        PuzzleType randomType = GetRandomPuzzleType();
        Debug.Log($"[TabletMissionObject] Mission {missionID} | Randomized type: {randomType}");
        PuzzleManager.Instance?.StartTabletMissionPuzzle(
            data.knowledgeComponent,
            randomType,
            OnPuzzleResolved);
    }
    private void OnPuzzleResolved(bool correct)
    {
        if (correct)
        {
            MissionTabletManager.Instance?.CompleteMission(missionID);

            // This was the missing call. SanctumManager.RegisterTabletMissionComplete
            // is the only place that calls StudentLogManager.LogTabletMissionComplete,
            // and nothing was ever calling it. MissionTabletManager.CompleteMission
            // tracks completion for gameplay purposes (boss-unlock gating), but never
            // logged anything, those are two separate systems that needed to both
            // be told, and only one was.
            SanctumManager.Instance?.RegisterTabletMissionComplete(missionID);

            StartCoroutine(PlayCompletionTransition());
        }
        else
        {
            Debug.Log($"[TabletMissionObject] Mission {missionID} failed. Player can retry.");
            // Re-enable HUD immediately on failure (no transition needed)
            if (hudCanvas != null) hudCanvas.SetActive(true);
            PlayerMovement pm = FindObjectOfType<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }
    }
    /// <summary>
    /// Fades to black, swaps mesh during the black frame, fades back,
    /// then re-enables HUD and player movement.
    /// </summary>
    private IEnumerator PlayCompletionTransition()
    {
        // 1. Ensure overlay is ready
        if (darkOverlay != null)
        {
            darkOverlay.gameObject.SetActive(true);
            Color c = darkOverlay.color;
            c.a = 0f;
            darkOverlay.color = c;
        }
        // 2. Fade in to black
        if (darkOverlay != null)
            yield return StartCoroutine(FadeImageAlpha(darkOverlay, 0f, 1f, fadeDuration));
        else
            yield return new WaitForSeconds(fadeDuration); // fallback pause
        // 3. Swap mesh while screen is black
        SetRestoredStateImmediate();
        // Brief hold so the swap feels intentional
        yield return new WaitForSeconds(0.25f);
        // 4. Fade out from black
        if (darkOverlay != null)
            yield return StartCoroutine(FadeImageAlpha(darkOverlay, 1f, 0f, fadeDuration));
        else
            yield return new WaitForSeconds(fadeDuration);
        // 5. Disable overlay
        if (darkOverlay != null) darkOverlay.gameObject.SetActive(false);
        // 6. Re-enable player
        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
        // 7. Re-enable HUD only at the very end
        if (hudCanvas != null) hudCanvas.SetActive(true);
        // 8. Refresh dashboard
        MissionTabletUI.Instance?.Refresh();
    }
    private IEnumerator FadeImageAlpha(Image img, float from, float to, float duration)
    {
        float elapsed = 0f;
        Color c = img.color;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(from, to, elapsed / duration);
            img.color = c;
            yield return null;
        }
        c.a = to;
        img.color = c;
    }
    private void SetDefaultState()
    {
        isCompleted = false;
        if (defaultMesh != null) defaultMesh.SetActive(true);
        if (restoredMesh != null) restoredMesh.SetActive(false);
        if (triggerCollider != null) triggerCollider.enabled = true;
    }
    /// <summary>
    /// Instant swap with no fade. Used on Start() for save-loaded states.
    /// </summary>
    private void SetRestoredStateImmediate()
    {
        isCompleted = true;
        if (defaultMesh != null) defaultMesh.SetActive(false);
        if (restoredMesh != null) restoredMesh.SetActive(true);
        if (triggerCollider != null) triggerCollider.enabled = false;
        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearInteractable(this);
    }
    private PuzzleType GetRandomPuzzleType()
    {
        PuzzleType[] available = new PuzzleType[]
        {
            PuzzleType.TrueOrFalse,
            PuzzleType.PairACode,
            PuzzleType.FillInTheBlank,
            PuzzleType.PredictTheOutput,
            PuzzleType.SpotTheBug,
            PuzzleType.LineScramble
        };
        return available[Random.Range(0, available.Length)];
    }
    public bool IsRestored() => isCompleted;
}