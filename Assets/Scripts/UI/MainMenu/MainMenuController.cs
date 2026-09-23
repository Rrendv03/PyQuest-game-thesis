using System.IO;
using PyQuest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Scene Flow")]
    public string newGameSceneName = "IntroScene";

    [Header("UI References")]
    public Button newGameButton;
    public Button continueButton;
    public Button settingsButton;
    public Button quitButton;
    public Button howToPlayButton; // Optional: opens the How To Play screen (view-only mode)

    public Button backToMainButton; // Optional: button to return from settings to main menu

    [Header("Panels & Background")]
    public GameObject menuBackground; // <-- DRAG YOUR BACKGROUND IMAGE HERE
    public GameObject mainMenuPanel;
    public GameObject settingsPanel;
    public GameObject saveLoadPanel;

    [Header("How To Play")]
    [Tooltip("The How To Play PANEL GameObject - fill it exactly like the settings and save/load panels above. Hidden on Start, opened by the How To Play button. The panel root needs a HowToPlayScreen component (pages, Back, Skip Tutorial are configured there). Required for the New Game tutorial flow: New Game shows this panel first with Back disabled and Skip Tutorial enabled; Skip - or the pages themselves - then starts the actual new game.")]
    public GameObject howToPlayPanel;

    [Header("Audio")]
    [Tooltip("Music that plays in the main menu. Routed through the persistent MusicManager (auto-created at runtime).")]
    public AudioClip menuMusic;

    [Tooltip("Fade-in time when the menu music starts.")]
    [Range(0f, 10f)] public float musicFadeIn = 1.5f;

    private void Start()
    {
        SaveRestrictionEnforcer.Instance?.AddBlocker("main_menu");

        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);
        if (backToMainButton != null) backToMainButton.onClick.AddListener(OnBackToMainClicked);
        if (howToPlayButton != null) howToPlayButton.onClick.AddListener(OnHowToPlayClicked);

        // Ensure sub-panels start hidden
        if (saveLoadPanel != null) saveLoadPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);

        PlayMenuMusic();

        CheckForSaveData();
    }

    private void OnDestroy()
    {
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("main_menu");
    }

    private void CheckForSaveData()
    {
        if (continueButton == null) return;

        bool hasSave = false;
        if (SaveLoadManager.Instance != null)
        {
            for (int i = 0; i <= 3; i++)
            {
                if (SaveLoadManager.Instance.SlotExists(i))
                {
                    hasSave = true;
                    break;
                }
            }
        }
        continueButton.gameObject.SetActive(hasSave);
    }

    // ------------------------------------------------------------------
    // AUDIO
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts the menu music through the persistent MusicManager. The
    /// manager auto-creates itself, so no scene setup is needed — just
    /// assign the menuMusic clip in the Inspector.
    ///
    /// Because the MusicManager survives scene loads, this track keeps
    /// playing until some other scene's SceneMusic component (or a
    /// StopMusic call) replaces it.
    ///
    /// Button click sounds are handled globally by UISoundManager —
    /// no per-button wiring here.
    /// </summary>
    private void PlayMenuMusic()
    {
        if (menuMusic == null) return;
        MusicManager.Instance.PlayTrack(menuMusic, musicFadeIn);
    }

    // ------------------------------------------------------------------
    // MENU ACTIONS
    // ------------------------------------------------------------------

    public void OnNewGameClicked()
    {
        // How To Play exception: the very first thing a new player sees is
        // the How To Play screen in tutorial mode (Back disabled, Skip
        // Tutorial enabled). Skip Tutorial invokes StartNewGameNow() so the
        // normal new-game flow runs. If the panel is missing (or we somehow
        // re-entered mid-tutorial), fall through and start the game directly
        // so New Game never breaks.
        HowToPlayScreen screen = ResolveHowToPlayScreen();
        if (screen != null && !screen.IsInTutorialMode)
        {
            screen.OpenAsTutorial(StartNewGameNow);
            return;
        }

        StartNewGameNow();
    }

    /// <summary>
    /// The actual new-game flow (previously the body of OnNewGameClicked).
    /// Runs either immediately from New Game, or after the How To Play
    /// tutorial is skipped/finished.
    /// </summary>
    private void StartNewGameNow()
    {
        // Bug-003 FIX: fully reset all DontDestroyOnLoad singletons before
        // starting a new game, so state from a previously-loaded save does
        // not bleed into the new playthrough.
        ResetCrossSceneSingletons();
        Time.timeScale = 1f; // ensure time is unpaused in case we came from a paused scene
        SceneManager.LoadScene(newGameSceneName);
    }

    /// <summary>
    /// "How To Play" button: opens the screen in view-only mode.
    /// Back is enabled (returns to the main menu panel) and Skip Tutorial
    /// is hidden, because no game is starting.
    /// </summary>
    public void OnHowToPlayClicked()
    {
        if (howToPlayPanel == null)
        {
            Debug.LogWarning("[MainMenuController] howToPlayPanel is not assigned - cannot open the How To Play screen.");
            return;
        }

        HowToPlayScreen screen = howToPlayPanel.GetComponent<HowToPlayScreen>();
        if (screen != null)
        {
            // Same behaviour as the settings panel: the screen's own
            // controller hides the main menu panel/background (Open) and
            // restores them when Back is pressed (Close).
            screen.Open();
            return;
        }

        // No HowToPlayScreen component on the panel - fall back to plain
        // show/hide so the panel still opens exactly like the settings panel.
        if (menuBackground != null) menuBackground.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        howToPlayPanel.SetActive(true);
    }

    /// <summary>
    /// Finds the HowToPlayScreen controller living on the How To Play panel
    /// root. The panel GameObject is the only Inspector field here — same
    /// pattern as settingsPanel / saveLoadPanel.
    /// </summary>
    private HowToPlayScreen ResolveHowToPlayScreen()
    {
        if (howToPlayPanel == null)
        {
            Debug.LogWarning("[MainMenuController] howToPlayPanel is not assigned - New Game will skip the tutorial screen.");
            return null;
        }

        HowToPlayScreen screen = howToPlayPanel.GetComponent<HowToPlayScreen>();
        if (screen == null)
            Debug.LogWarning("[MainMenuController] howToPlayPanel has no HowToPlayScreen component - add it to the panel root.");
        return screen;
    }

    /// <summary>
    /// Resets every DontDestroyOnLoad gameplay singleton back to its
    /// blank initial state. Intentionally does NOT Destroy any
    /// GameObjects — Destroy()ing a DDOL mid-frame was causing
    /// MissingReferenceException on PuzzleManager and other singletons
    /// that are touched by other code in the same frame.
    ///
    /// Safe flow:
    ///   1. Call each singleton's typed Reset/Import API to blank state.
    ///   2. SceneManager.LoadScene(IntroScene) runs.
    ///   3. IntroScene's DDOL-prefab copies run Awake(), see
    ///      Instance != null (the existing, now-reset one), and
    ///      self-destruct via the "else Destroy(gameObject)" branch.
    ///   4. The existing reset Instance persists cleanly across loads.
    ///
    /// Scene-local singletons (PuzzleManager, EncounterManager etc. —
    /// no DontDestroyOnLoad in Awake) are not touched here — they die
    /// naturally when their current scene is unloaded.
    /// </summary>
    private void ResetCrossSceneSingletons()
    {
        if (StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.ResetProgression();
        if (QuestManager.Instance != null)
            QuestManager.Instance.EvaluateActiveQuest();
        if (MissionTabletManager.Instance != null)
            MissionTabletManager.Instance.ResetMissions();
        if (BKTEngine.Instance != null)
            BKTEngine.Instance.ResetAllMastery();
        if (XPManager.Instance != null)
            XPManager.Instance.ImportXP(0);
        if (StudentLogManager.Instance != null)
            StudentLogManager.Instance.ResetLogs();

        Debug.Log("[MainMenuController] Cross-scene singletons reset for New Game.");
    }

    public void OnContinueClicked()
    {
        // 1. Hide background and main menu buttons
        if (menuBackground != null) menuBackground.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        // 2. Open save screen, tell it to turn BOTH the background and buttons back on
        SaveSlotUI.Open(saveLoadPanel, () =>
        {
            if (menuBackground != null) menuBackground.SetActive(true);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        });
    }

    public void OnSettingsClicked()
    {
        if (menuBackground != null) menuBackground.SetActive(false); // Optional: hide background for settings too?
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void OnBackToMainClicked()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (menuBackground != null) menuBackground.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    public void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
