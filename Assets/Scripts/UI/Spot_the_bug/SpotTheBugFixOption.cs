using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SpotTheBugFixOption : MonoBehaviour, IPointerClickHandler,
                                                  IPointerEnterHandler,
                                                  IPointerExitHandler
{
    [HideInInspector] public string optionText;
    [HideInInspector] public SpotTheBugUIController parentController;

    private Image backgroundImage;
    private Text optionLabel;

    private Color defaultColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private Color hoverColor = new Color(0.30f, 0.60f, 0.30f, 1f);
    private Color selectedColor = new Color(0.20f, 0.70f, 0.40f, 1f);

    public void Setup(string text, SpotTheBugUIController controller)
    {
        optionText = text;
        parentController = controller;

        backgroundImage = GetComponent<Image>();
        optionLabel = GetComponentInChildren<Text>();

        if (optionLabel != null)
            optionLabel.text = text;

        SetState_Default();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        parentController.OnFixOptionSelected(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetState_Hover();
        // Hook: play hover animation here in future
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetState_Default();
        // Hook: play exit animation here in future
    }

    public void SetState_Default()
    {
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
        if (backgroundImage != null) backgroundImage.color = selectedColor;
        // Hook: play selected animation here in future
    }
}