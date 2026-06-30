using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class LineScrambleSlot : MonoBehaviour, IBeginDragHandler, IDragHandler,
                                               IEndDragHandler, IDropHandler
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

    [Header("Animation")]
    public float slideDuration = 0.25f;
    public AnimationCurve slideEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Color defaultColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private Color draggingColor = new Color(0.20f, 0.50f, 0.80f, 0.80f);
    private Color correctColor = new Color(0.20f, 0.70f, 0.30f, 1f);

    public void Setup(int rowNumber, string text, LineScrambleUIController controller)
    {
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

        if (lineLabel != null)
            lineLabel.text = text;

        SetState_Default();
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

        dragStartWorldPosition = rectTransform.position;
        canvasGroup.blocksRaycasts = false;
        SetState_Dragging();
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;
        rectTransform.position = dragStartWorldPosition;
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
    /// Smoothly slides this slot from its current screen position
    /// to the given target world position. Used for both the
    /// dragged slot snapping into place and the displaced slot
    /// sliding to where the dragged slot used to be.
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