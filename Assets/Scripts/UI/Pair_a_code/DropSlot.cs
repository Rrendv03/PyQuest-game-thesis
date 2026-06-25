using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Text slotLabel;

    [HideInInspector] public string currentAnswer = "";
    [HideInInspector] public DraggableOption currentOccupant = null;

    private Image slotImage;
    private Color defaultColor;
    private Color hoverColor = new Color(0.6f, 0.9f, 0.6f, 1f);
    private Color filledColor = new Color(0.4f, 0.7f, 1f, 1f);

    private void Awake()
    {
        slotImage = GetComponent<Image>();
        if (slotImage != null)
            defaultColor = slotImage.color;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (slotImage != null && currentOccupant == null)
            slotImage.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (slotImage != null && currentOccupant == null)
            slotImage.color = defaultColor;
    }

    public void OnDrop(PointerEventData eventData)
    {
        DraggableOption dropped = eventData.pointerDrag.GetComponent<DraggableOption>();
        if (dropped == null) return;

        // If slot already has an occupant, send it back
        if (currentOccupant != null)
        {
            currentOccupant.ResetToOriginal();
            currentOccupant = null;
        }

        // Snap dropped option into this slot
        dropped.transform.SetParent(transform);
        dropped.GetComponent<RectTransform>().localPosition = Vector3.zero;

        currentOccupant = dropped;
        currentAnswer = dropped.optionText;

        if (slotImage != null)
            slotImage.color = filledColor;

        if (slotLabel != null)
            slotLabel.text = dropped.optionText;

        Debug.Log($"[DropSlot] Received: {currentAnswer}");

        // Auto-submit on drop
        PuzzleManager.Instance.UserSubmission(currentAnswer);
    }

    public void ClearSlot()
    {
        if (currentOccupant != null)
        {
            currentOccupant.ResetToOriginal();
            currentOccupant = null;
        }
        currentAnswer = "";
        if (slotImage != null)
            slotImage.color = defaultColor;
        if (slotLabel != null)
            slotLabel.text = "[ ? ]";
    }
}