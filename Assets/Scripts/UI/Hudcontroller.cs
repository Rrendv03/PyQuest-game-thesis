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
    //
    // WHO asked to hide the HUD is tracked in two separate flags. The gameplay code
    // (enemy encounters, cutscenes) goes through SetVisible; the minimap goes
    // through SetHiddenByMap. The net SetActive is the AND of both, so:
    //   - a caller that hid the HUD is never "overwritten" by someone else's
    //     SetVisible(true) while it still needs it hidden,
    //   - the minimap can tell "the encounter hid the HUD" apart from "I hid the
    //     HUD", even when both flags are down at once.
    bool hiddenByGameplay;
    bool hiddenByMinimap;

    public void SetVisible(bool visible)
    {
        hiddenByGameplay = !visible;
        ApplyCombinedVisibility();
    }

    /// <summary>
    /// The minimap's own hide state, so its fullscreen close cannot resurrect a HUD
    /// an encounter still needs hidden, and so its visibility poll can listen to
    /// gameplay-side hides only.
    /// </summary>
    public void SetHiddenByMap(bool hidden)
    {
        hiddenByMinimap = hidden;
        ApplyCombinedVisibility();
    }

    /// <summary>True while the last non-minimap SetVisible call asked for hidden.</summary>
    public bool GameplayWantsHudHidden { get { return hiddenByGameplay; } }

    /// <summary>The combined truth of the gameplay HUD root GameObject.</summary>
    public bool IsVisible { get { return !hiddenByGameplay && !hiddenByMinimap; } }

    void ApplyCombinedVisibility()
    {
        bool visible = !hiddenByGameplay && !hiddenByMinimap;
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

        // Movement buttons are hold-controls, not clicks: keep them out of
        // the universal click-sound system so pressing them stays silent
        // and their input path stays exactly as it was before the audio
        // manager existed.
        UISoundManager.MarkSilent(button);

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