using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Mobile HUD. Four directional buttons for movement, pause button.
/// Swipe zone removed entirely. Camera rotation is handled by
/// ThirdPersonCamera following the player directly.
/// </summary>
public class HUDController : MonoBehaviour
{
    public static HUDController Instance;

    [Header("Movement Buttons")]
    public Button buttonUp;
    public Button buttonDown;
    public Button buttonLeft;
    public Button buttonRight;

    [Header("Pause Button")]
    public Button pauseButton;

    public static bool MoveForward;
    public static bool MoveBackward;
    public static bool MoveLeft;
    public static bool MoveRight;

    void Awake()
    {
        Instance = this;
    }

    [Header("HUD Canvas Root")]
    public GameObject hudCanvas; // drag the HUDCanvas root GameObject here

    // SetVisible never touches the interact button.
    // InteractButtonController owns its own visibility based on NPC range.
    public void SetVisible(bool visible)
    {
        if (hudCanvas != null)
            hudCanvas.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }

    void Start()
    {
        AddHoldListeners(buttonUp, () => MoveForward = true, () => MoveForward = false);
        AddHoldListeners(buttonDown, () => MoveBackward = true, () => MoveBackward = false);
        AddHoldListeners(buttonLeft, () => MoveLeft = true, () => MoveLeft = false);
        AddHoldListeners(buttonRight, () => MoveRight = true, () => MoveRight = false);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(() =>
            {
                if (PauseMenuManager.Instance != null)
                    PauseMenuManager.Instance.TogglePause();
            });
    }

    void OnDestroy()
    {
        MoveForward = MoveBackward = MoveLeft = MoveRight = false;
    }

    private void AddHoldListeners(Button button,
        System.Action onDown, System.Action onUp)
    {
        if (button == null) return;

        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = button.gameObject.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry
        { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => onDown());
        trigger.triggers.Add(downEntry);

        var upEntry = new EventTrigger.Entry
        { eventID = EventTriggerType.PointerUp };
        upEntry.callback.AddListener(_ => onUp());
        trigger.triggers.Add(upEntry);

        var exitEntry = new EventTrigger.Entry
        { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => onUp());
        trigger.triggers.Add(exitEntry);
    }
}