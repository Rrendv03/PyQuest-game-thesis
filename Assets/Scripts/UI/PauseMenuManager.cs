using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the pause menu. Attach to a persistent GameObject in any
/// gameplay scene (MainMap, Sanctums, Room).
///
/// Pausing sets Time.timeScale to 0 and activates the fullscreen dark
/// overlay plus the pause panel. Resuming restores Time.timeScale to 1.
///
/// IsSafeToSave is set false while paused since the player is actively
/// in the menu. It restores on resume.
///
/// The pause button on your HUD calls TogglePause().
/// The Android back button also toggles pause.
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    public static PauseMenuManager Instance;

    [Header("Pause UI Root")]
    public GameObject pauseRoot;

    [Header("Overlay")]
    public Image darkOverlay;
    [Range(0f, 1f)]
    public float overlayAlpha = 0.65f;

    [Header("Pause Panel Buttons")]
    public Button resumeButton;
    public Button saveLoadButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Sub Panels")]
    public GameObject saveSlotPanel;
    public GameObject settingsPanel;

    [Header("Scene Names")]
    public string mainMenuSceneName = "MainMenu";

    private bool isPaused = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (pauseRoot != null) pauseRoot.SetActive(false);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (darkOverlay != null)
        {
            Color c = darkOverlay.color;
            c.a = overlayAlpha;
            darkOverlay.color = c;
        }

        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        if (saveLoadButton != null) saveLoadButton.onClick.AddListener(OpenSaveLoad);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (quitButton != null) quitButton.onClick.AddListener(QuitToMainMenu);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    public void TogglePause()
    {
        if (isPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (isPaused) return;

        isPaused = true;
        Time.timeScale = 0f;
        SaveLoadManager.IsSafeToSave = false;

        if (pauseRoot != null) pauseRoot.SetActive(true);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        Debug.Log("[PauseMenuManager] Game paused.");
    }

    public void Resume()
    {
        if (!isPaused) return;

        isPaused = false;
        Time.timeScale = 1f;
        SaveLoadManager.IsSafeToSave = true;

        if (pauseRoot != null) pauseRoot.SetActive(false);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        Debug.Log("[PauseMenuManager] Game resumed.");
    }

    private void OpenSaveLoad()
    {
        if (saveSlotPanel == null) return;

        bool isOpen = saveSlotPanel.activeSelf;
        saveSlotPanel.SetActive(!isOpen);

        if (!isOpen)
        {
            SaveSlotUI ui = saveSlotPanel.GetComponent<SaveSlotUI>();
            if (ui != null) ui.RefreshAllSlots();
        }

        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    private void OpenSettings()
    {
        if (settingsPanel == null)
        {
            Debug.Log("[PauseMenuManager] Settings panel not yet assigned.");
            return;
        }

        bool isOpen = settingsPanel.activeSelf;
        settingsPanel.SetActive(!isOpen);
        if (saveSlotPanel != null) saveSlotPanel.SetActive(false);
    }

    private void QuitToMainMenu()
    {
        isPaused = false;
        Time.timeScale = 1f;
        SaveLoadManager.IsSafeToSave = true;

        Debug.Log($"[PauseMenuManager] Quitting to: {mainMenuSceneName}");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public bool IsPaused => isPaused;
}