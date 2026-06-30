using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SpotTheBugLineButton : MonoBehaviour, IPointerClickHandler,
                                                   IPointerEnterHandler,
                                                   IPointerExitHandler
{
    [HideInInspector] public int lineIndex;
    [HideInInspector] public string lineText;
    [HideInInspector] public SpotTheBugUIController parentController;

    private Image backgroundImage;
    private Text lineLabel;

    // Animation-ready state colors
    private Color defaultColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private Color hoverColor = new Color(0.30f, 0.30f, 0.60f, 1f);
    private Color selectedColor = new Color(0.60f, 0.30f, 0.10f, 1f);

    private bool isSelected = false;

    public void Setup(int index, string text, SpotTheBugUIController controller)
    {
        lineIndex = index;
        lineText = text;
        parentController = controller;

        backgroundImage = GetComponent<Image>();
        lineLabel = GetComponentInChildren<Text>();

        if (lineLabel != null)
            lineLabel.text = (index + 1) + ".  " + text;

        SetState_Default();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isSelected)
        {
            // Clicking selected line again deselects it
            parentController.OnLineDeselected(this);
        }
        else
        {
            parentController.OnLineSelected(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSelected)
        {
            SetState_Hover();
            // Hook: play hover animation here in future
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSelected)
        {
            SetState_Default();
            // Hook: play exit animation here in future
        }
    }

    public void SetState_Default()
    {
        isSelected = false;
        if (backgroundImage != null) backgroundImage.color = defaultColor;
        // Hook: play idle animation here in future
    }

    public void SetState_Hover()
    {
        if (backgroundImage != null) backgroundImage.color = hoverColor;
        // Hook: play hover animation here in future
    }

    public void SetState_Selected()
    {
        isSelected = true;
        if (backgroundImage != null) backgroundImage.color = selectedColor;
        // Hook: play selected animation here in future
    }
}