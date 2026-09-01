using System.IO;
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

    [Header("Panels & Background")]
    public GameObject menuBackground; // <-- DRAG YOUR BACKGROUND IMAGE HERE
    public GameObject mainMenuPanel;
    public GameObject settingsPanel;
    public GameObject saveLoadPanel;

    private void Start()
    {
        SaveRestrictionEnforcer.Instance?.AddBlocker("main_menu");

        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // Ensure sub-panels start hidden
        if (saveLoadPanel != null) saveLoadPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

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

    public void OnNewGameClicked()
    {
        // Bug-003 FIX: fully reset all DontDestroyOnLoad singletons before
        // starting a new game, so state from a previously-loaded save does
        // not bleed into the new playthrough.
        ResetCrossSceneSingletons();

        SceneManager.LoadScene(newGameSceneName);
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