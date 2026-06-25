using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableOption : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public string optionText;
    [HideInInspector] public Transform originalParent;
    [HideInInspector] public Vector3 originalPosition;

    private CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Canvas rootCanvas;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        rootCanvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = transform.parent;
        originalPosition = rectTransform.localPosition;

        // Reparent to root canvas so it renders on top of everything
        transform.SetParent(rootCanvas.transform);

        // Allow raycasts to pass through while dragging
        canvasGroup.blocksRaycasts = false;

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

        // If not dropped on a valid target, snap back
        if (transform.parent == rootCanvas.transform)
        {
            transform.SetParent(originalParent);
            rectTransform.localPosition = originalPosition;
            Debug.Log($"[DraggableOption] Snapped back: {optionText}");
        }
    }

    public void ResetToOriginal()
    {
        transform.SetParent(originalParent);
        rectTransform.localPosition = originalPosition;
        canvasGroup.blocksRaycasts = true;
    }
}