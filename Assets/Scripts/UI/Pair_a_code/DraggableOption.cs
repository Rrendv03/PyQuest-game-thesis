using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableOption : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public string optionText;
    [HideInInspector] public Transform originalParent;
    [HideInInspector] public Vector3 originalPosition;

    // Set automatically by the hosting UI controller (Awake/PopulateUI) —
    // any controller implementing IDraggableOptionSounds (PredictTheOutput,
    // PairACode, ...). Routes the click / hold / return sounds to one place,
    // same pattern as LineScrambleSlot -> LineScrambleUIController.
    [HideInInspector] public IDraggableOptionSounds parentController;

    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Canvas rootCanvas;

    [Header("Pullback Animation")]
    [Tooltip("Seconds the smooth pullback to the original position takes when released off a drop slot.")]
    public float pullbackDuration = 0.25f;
    [Tooltip("Easing curve for the pullback animation.")]
    public AnimationCurve pullbackEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Coroutine pullbackCoroutine;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        rootCanvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Click fires on press, even if the player releases without dragging.
        if (parentController != null)
            parentController.PlayOptionClickSound();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Grabbing a card that's still animating back cancels the pullback;
        // the drag takes ownership of the position from here.
        if (pullbackCoroutine != null)
        {
            StopCoroutine(pullbackCoroutine);
            pullbackCoroutine = null;
        }

        originalParent = transform.parent;
        originalPosition = rectTransform.localPosition;

        // Reparent to root canvas so it renders on top of everything
        transform.SetParent(rootCanvas.transform);

        // Allow raycasts to pass through while dragging
        canvasGroup.blocksRaycasts = false;

        if (parentController != null)
            parentController.StartOptionHoldSound();

        Debug.Log($"[DraggableOption] Begin drag: {optionText}");
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Follow finger/mouse position
        rectTransform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Re-enable raycasts
        canvasGroup.blocksRaycasts = true;

        // The drag is over either way (dropped on the slot or pulled back),
        // so the looping hold sound must stop in both branches.
        if (parentController != null)
            parentController.StopOptionHoldSound();

        // If not dropped on a valid target, animate smoothly back to the
        // position it was picked up from instead of teleporting.
        if (transform.parent == rootCanvas.transform)
        {
            if (pullbackCoroutine != null)
                StopCoroutine(pullbackCoroutine);
            pullbackCoroutine = StartCoroutine(PullbackToOriginal());

            // Same as Line Scramble: a one-shot plays while the card glides
            // back home. The hold loop was already stopped above.
            if (parentController != null)
                parentController.PlayOptionReturnSound();

            Debug.Log($"[DraggableOption] Pulling back: {optionText}");
        }
    }

    /// <summary>
    /// Reparents back under the original parent, then eases localPosition
    /// from wherever the card was released to originalPosition. The start
    /// position is converted through world space so the animation begins
    /// exactly at the release point (no 1-frame jump after reparenting).
    /// </summary>
    private IEnumerator PullbackToOriginal()
    {
        transform.SetParent(originalParent);

        Vector3 startLocal = originalParent != null
            ? originalParent.InverseTransformPoint(rectTransform.position)
            : rectTransform.localPosition;
        Vector3 endLocal = originalPosition;

        float elapsed = 0f;
        while (elapsed < pullbackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = pullbackDuration > 0f ? Mathf.Clamp01(elapsed / pullbackDuration) : 1f;
            rectTransform.localPosition = Vector3.LerpUnclamped(startLocal, endLocal, pullbackEase.Evaluate(t));
            yield return null;
        }

        rectTransform.localPosition = endLocal;
        pullbackCoroutine = null;
    }

    // Safety net: when a drop auto-submits, PuzzleManager can deactivate the
    // card in the SAME frame as OnDrop, which means OnEndDrag never fires and
    // the looping hold sound would keep playing forever. Stopping it here
    // guarantees the loop dies whenever this card is disabled mid-drag.
    private void OnDisable()
    {
        if (parentController != null)
            parentController.StopOptionHoldSound();
    }

    public void ResetToOriginal()
    {
        // A repopulate/reset invalidates any in-flight pullback.
        if (pullbackCoroutine != null)
        {
            StopCoroutine(pullbackCoroutine);
            pullbackCoroutine = null;
        }

        transform.SetParent(originalParent);
        rectTransform.localPosition = originalPosition;
        canvasGroup.blocksRaycasts = true;
    }
}