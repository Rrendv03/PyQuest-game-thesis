using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Universal UI sound player. Plays a click sound for EVERY
/// UnityEngine.UI.Button in every scene — no per-button wiring needed.
///
/// SETUP (recommended, one time):
///   1. In your first scene (main menu / boot), create an empty GameObject
///      named "UISoundManager".
///   2. Add this component and drag your click AudioClip into "Button Click".
///   It persists via DontDestroyOnLoad and keeps working in all scenes.
///
/// HOW IT WORKS
///   - On every scene load it finds all Buttons (active or inactive) and
///     attaches a click listener to them.
///   - A light periodic rescan also catches buttons created at runtime
///     (spawned menus, pooled popups) automatically.
///   - Because it hooks onClick, it also fires for keyboard/gamepad
///     "Submit" activation, not just mouse clicks.
///   - If no scene object exists, a manager is auto-created the first time
///     UISoundManager.Instance is used — then assign the clip from code:
///         UISoundManager.Instance.buttonClick = myClip;
///
/// SILENT BUTTONS (no click sound)
///   Some buttons should stay silent — hold-to-move buttons, sliders, etc.
///   Two ways to exclude a Button:
///     - From code:      UISoundManager.MarkSilent(theButton);
///     - From Inspector: add the NoClickSound component to the Button.
///   HUDController marks its four movement buttons automatically.
///
/// NOT PLAYING? Common causes:
///   - buttonClick clip is unassigned (assign in Inspector or from code).
///   - The button is marked silent (NoClickSound component / MarkSilent).
///   - The clickable thing is not a UnityEngine.UI.Button (e.g. a custom
///     IPointerClickHandler) — call UISoundManager.RegisterButton(...)
///     from its click path, or extend HookAllButtons to other Selectables.
///
/// SETTINGS INTEGRATION (AudioSettingsManager)
///   Same pattern as MusicManager: call
///     UISoundManager.Instance.SetVolume(sliderValue)
///   from the UI-sound slider once AudioSettingsManager is (re)attached.
/// </summary>
public class UISoundManager : MonoBehaviour
{
    private static UISoundManager _instance;

    // Buttons excluded from the universal click sound (see MarkSilent).
    private static readonly HashSet<Button> _silentButtons = new HashSet<Button>();

    /// <summary>
    /// Lazily creates the manager if no instance exists, so UI clicks get
    /// sound even in scenes that never set one up.
    /// </summary>
    public static UISoundManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GameObject("UISoundManager").AddComponent<UISoundManager>();
            }
            return _instance;
        }
    }

    [Header("Sounds")]
    [Tooltip("Played when any hooked Button is clicked.")]
    public AudioClip buttonClick;

    [Header("Volume")]
    [Range(0f, 1f)] public float uiVolume = 1f;

    [Header("Auto-Hooking")]
    [Tooltip("Rescan periodically so dynamically created buttons get the click sound too.")]
    public bool autoRescan = true;

    [Tooltip("Seconds between rescans when Auto Rescan is enabled.")]
    [Min(0.25f)] public float rescanInterval = 1f;

    private AudioSource _source;
    private readonly HashSet<Button> _hookedButtons = new HashSet<Button>();
    private float _nextRescanTime;

    private void Awake()
    {
        // A duplicate (scene object PLUS auto-created one) defers to the
        // existing instance and destroys itself.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        // TODO(AudioSettingsManager): set _source.outputAudioMixerGroup to
        // the UI/SFX group exposed by AudioSettingsManager, same as the
        // MusicManager TODO.

        HookAllButtons();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new scene (single, additive, or DDOL content) may contain new
        // buttons — hook them as soon as it loads.
        HookAllButtons();
    }

    private void Update()
    {
        if (!autoRescan) return;
        if (Time.unscaledTime < _nextRescanTime) return;

        _nextRescanTime = Time.unscaledTime + rescanInterval;
        HookAllButtons();
    }

    /// <summary>
    /// Optional manual hook for a button created at runtime, if you don't
    /// want to wait for the next rescan.
    /// </summary>
    public static void RegisterButton(Button button)
    {
        if (button == null) return;
        Instance.Hook(button);
    }

    /// <summary>
    /// Excludes a Button from the universal click sound (e.g. hold-to-move
    /// buttons) and removes the sound hook if it was already attached.
    /// Safe to call repeatedly, before or after the manager exists.
    /// </summary>
    public static void MarkSilent(Button button)
    {
        if (button == null) return;

        _silentButtons.Add(button);
        if (_instance != null) _instance.Unhook(button);
    }

    /// <summary>Convenience overload: marks whatever Button sits on this GameObject.</summary>
    public static void MarkSilent(GameObject owner)
    {
        if (owner == null) return;
        MarkSilent(owner.GetComponent<Button>());
    }

    /// <summary>Hook point for AudioSettingsManager (UI sound slider).</summary>
    public void SetVolume(float normalizedVolume)
    {
        uiVolume = Mathf.Clamp01(normalizedVolume);
    }

    private void HookAllButtons()
    {
        // Forget destroyed buttons so the sets can't grow forever.
        _hookedButtons.RemoveWhere(b => b == null);
        _silentButtons.RemoveWhere(b => b == null);

#if UNITY_2022_2_OR_NEWER
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Button[] buttons = FindObjectsOfType<Button>(true);
#endif
        for (int i = 0; i < buttons.Length; i++)
        {
            Hook(buttons[i]);
        }
    }

    private void Hook(Button button)
    {
        if (button == null) return;

        // Silent buttons (movement/hold controls) never get the click sound.
        // RemoveListener also covers the case where the scene hook ran before
        // the HUD script marked them silent (sceneLoaded fires before Start).
        if (_silentButtons.Contains(button) || button.GetComponent<NoClickSound>() != null)
        {
            button.onClick.RemoveListener(PlayClick);
            return;
        }

        // HashSet.Add returns false when already hooked — prevents
        // duplicate listeners from repeated rescans.
        if (!_hookedButtons.Add(button)) return;

        button.onClick.AddListener(PlayClick);
    }

    private void Unhook(Button button)
    {
        if (button == null || !_hookedButtons.Remove(button)) return;

        button.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick()
    {
        if (buttonClick == null || _source == null) return;

        // PlayOneShot so rapid clicking overlaps instead of cutting off.
        _source.PlayOneShot(buttonClick, uiVolume);
    }
}
