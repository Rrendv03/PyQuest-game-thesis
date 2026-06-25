using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class FillInTheBlankToken : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [HideInInspector] public string tokenText;
    [HideInInspector] public FillInTheBlankUIController parentController;

    private Image tokenImage;
    private Text tokenLabel;

    // Animation-ready state flags
    private bool isSelected = false;
    private bool isHovered = false;

    // Color states — replace with animations later
    //private Color defaultColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    //private Color hoverColor = new Color(0.4f, 0.4f, 0.8f, 1f);
    //private Color selectedColor = new Color(0.2f, 0.7f, 0.4f, 1f);
    //private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    public void Setup(string text, FillInTheBlankUIController controller)
    {
        tokenText = text;
        parentController = controller;

        // Get references here instead of Awake to guarantee they exist
        tokenImage = GetComponent<Image>();
        tokenLabel = GetComponentInChildren<Text>();

        if (tokenLabel != null)
            tokenLabel.text = text;

        SetState_Default();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isSelected)
        {
            parentController.OnTokenSelected(this);
        }
        else
        {
            // Clicking selected token deselects it
            parentController.OnTokenDeselected(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSelected)
        {
            isHovered = true;
            SetState_Hover();
            // Hook: play hover animation here in future
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSelected)
        {
            isHovered = false;
            SetState_Default();
            // Hook: play exit animation here in future
        }
    }

    // --- State setters (animation hooks) ---

    public void SetState_Default()
    {
        isSelected = false;
        //if (tokenImage != null) tokenImage.color = defaultColor;
        // Hook: play idle animation here in future
    }

    public void SetState_Hover()
    {
        //if (tokenImage != null) tokenImage.color = hoverColor;
        // Hook: play hover animation here in future
    }

    public void SetState_Selected()
    {
        isSelected = true;
        //if (tokenImage != null) tokenImage.color = selectedColor;
        // Hook: play selected animation here in future
    }

    public void SetState_Disabled()
    {
        isSelected = false;
        //if (tokenImage != null) tokenImage.color = disabledColor;
        Button btn = GetComponent<Button>();
        if (btn != null) btn.interactable = false;
        // Hook: play disabled animation here in future
    }
}