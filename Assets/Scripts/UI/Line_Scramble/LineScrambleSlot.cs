using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class LineScrambleSlot : MonoBehaviour, IPointerDownHandler, IBeginDragHandler,
                                               IDragHandler, IEndDragHandler, IDropHandler
{
    [HideInInspector] public int originalRowNumber;
    [HideInInspector] public string lineText;
    [HideInInspector] public LineScrambleUIController parentController;

    private Text lineLabel;
    private Image backgroundImage;
    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Canvas rootCanvas;

    private Vector3 dragStartWorldPosition;
    private Coroutine activeSlideCoroutine;

    // Set when the controller commits a swap for this slot while it is still
    // the dragged object. The swap slide animation started from this slot's
    // released position and now owns it, so OnEndDrag must NOT snap it back
    // to dragStartWorldPosition (that would kill the animation).
    private bool suppressEndDragSnap;

    [Header("Animation")]
    public float slideDuration = 0.25f;
    public AnimationCurve slideEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Color defaultColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private Color draggingColor = new Color(0.20f, 0.50f, 0.80f, 0.80f);
    private Color correctColor = new Color(0.20f, 0.70f, 0.30f, 1f);

    // --- Read access used by the controller to choreograph swap animations ---

    /// <summary>True while this slot's slide coroutine is running.</summary>
    public bool IsAnimating => activeSlideCoroutine != null;

    /// <summary>
    /// Where this slot was sitting when the current drag began — its original
    /// slot position before the player dragged it. The replaced (drop target)
    /// slot slides to this position during a swap.
    /// </summary>
    public Vector3 DragStartWorldPosition => dragStartWorldPosition;

    /// <summary>Current on-screen (world) position of this slot.</summary>
    public Vector3 CurrentWorldPosition =>
        rectTransform != null ? rectTransform.position : transform.position;

    public void Setup(int rowNumber, string text, LineScrambleUIController controller)
    {
        // A repopulate invalidates any slide animation still in flight.
        if (activeSlideCoroutine != null)
        {
            StopCoroutine(activeSlideCoroutine);
            activeSlideCoroutine = null;
        }
        suppressEndDragSnap = false;

        originalRowNumber = rowNumber;
        lineText = text;
        parentController = controller;

        lineLabel = GetComponentInChildren<Text>();
        backgroundImage = GetComponent<Image>();
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();
        rootCanvas = GetComponentInParent<Canvas>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Pointer/drag events reach the slot through its Image, no matter how
        // the container hierarchy is set up. Force the slot hittable and
        // clear a blocksRaycasts=false left over from a drag interrupted by a
        // repopulate (OnEndDrag may never have run to restore it).
        if (backgroundImage != null)
            backgroundImage.raycastTarget = true;
        canvasGroup.blocksRaycasts = true;

        if (lineLabel != null)
            lineLabel.text = text;

        SetState_Default();
    }

    /// <summary>
    /// Press feedback: fires on pointer press, even if the player releases
    /// without ever dragging. Routed to the controller so every puzzle sound
    /// (press, drag, swap, return, execute) is assigned in one place.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (parentController != null)
            parentController.PlaySlotPressSound();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        // Cancel any in-flight slide animation if the player grabs
        // a slot that's still animating from a previous swap
        if (activeSlideCoroutine != null)
        {
            StopCoroutine(activeSlideCoroutine);
            activeSlideCoroutine = null;
        }

        // That interrupted slide may have left the sibling-index refresh
        // pending (it only runs once every slide finishes). Flush it NOW so
        // the layout order and on-screen positions agree again before this
        // drag starts and before we snapshot the drag origin below.
        if (parentController != null)
            parentController.FlushPendingLayoutSync();

        // A fresh drag always owns its own end-drag behaviour.
        suppressEndDragSnap = false;

        dragStartWorldPosition = rectTransform.position;
        canvasGroup.blocksRaycasts = false;
        SetState_Dragging();

        if (parentController != null)
            parentController.StartSlotDragSound();
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;

        // The drag is over either way (swapped or snapped back home), so the
        // looping drag sound must stop in both branches.
        if (parentController != null)
            parentController.StopSlotDragSound();

        if (suppressEndDragSnap)
        {
            // A swap was committed on drop (OnDrop fires before OnEndDrag).
            // The slide animation started from this slot's released position,
            // so snapping back to dragStartWorldPosition here would teleport
            // it and kill the animation.
            suppressEndDragSnap = false;
        }
        else
        {
            // No swap happened (released over empty space): glide back to the
            // slot position this drag started from — the same smooth pullback
            // the PairACode options get, reusing the swap slide (same duration
            // and ease). Reusing the slide coroutine also means grabbing the
            // slot again mid-return cancels it cleanly, and a layout refresh
            // deferred by another swap waits for this slide to finish.
            SlideTo(dragStartWorldPosition);

            if (parentController != null)
                parentController.PlaySlotReturnSound();
        }

        SetState_Default();
    }

    public void OnDrop(PointerEventData eventData)
    {
        LineScrambleSlot draggedSlot = eventData.pointerDrag
            .GetComponent<LineScrambleSlot>();
        if (draggedSlot == null || draggedSlot == this) return;

        Debug.Log($"[LineScrambleSlot] OnDrop | dragged='{draggedSlot.lineText}' onto target='{lineText}'");

        parentController.SwapByReference(draggedSlot, this);
    }

    /// <summary>
    /// Called by the controller right after it commits a swap that involves
    /// this slot as the dragged object. The swap slide that was just started
    /// owns this slot's position from here on, so the upcoming OnEndDrag
    /// (which fires right after OnDrop in the same frame) must not snap it
    /// back to its pre-drag home.
    /// </summary>
    public void NotifySwapAnimationStarted()
    {
        suppressEndDragSnap = true;
    }

    /// <summary>
    /// Smoothly slides this slot from its current screen position
    /// to the given target world position. Used for both the
    /// dragged slot gliding into the target's old slot and the
    /// displaced slot sliding to where the dragged slot used to be.
    /// </summary>
    public void SlideTo(Vector3 targetWorldPosition)
    {
        if (activeSlideCoroutine != null)
            StopCoroutine(activeSlideCoroutine);

        activeSlideCoroutine = StartCoroutine(SlideRoutine(targetWorldPosition));
    }

    private IEnumerator SlideRoutine(Vector3 targetWorldPosition)
    {
        Vector3 startPosition = rectTransform.position;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            float eased = slideEase.Evaluate(t);

            rectTransform.position = Vector3.LerpUnclamped(
                startPosition, targetWorldPosition, eased);

            yield return null;
        }

        rectTransform.position = targetWorldPosition;
        activeSlideCoroutine = null;
    }

    public void SetState_Default()
    {
        if (backgroundImage != null) backgroundImage.color = defaultColor;
    }

    public void SetState_Dragging()
    {
        if (backgroundImage != null) backgroundImage.color = draggingColor;
    }

    public void SetState_Correct()
    {
        if (backgroundImage != null) backgroundImage.color = correctColor;
    }
}