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

    [Header("Panels")]
    public GameObject mainMenuPanel;
    public GameObject settingsPanel;
    [Tooltip("Drag your existing Save/Load Screen GameObject here")]
    public GameObject saveLoadPanel;

    private void Start()
    {
        // Hook up the button clicks
        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // Hide the save/load screen on startup so only the main buttons are showing
        if (saveLoadPanel != null)
            saveLoadPanel.SetActive(false);

        CheckForSaveData();
    }

    private void CheckForSaveData()
    {
        if (continueButton == null) return;

        // We still hide the continue button if no saves exist at all,
        // so the player doesn't open an empty screen.
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
        Debug.Log("[MainMenu] Starting New Game...");
        SceneManager.LoadScene(newGameSceneName);
    }

    public void OnContinueClicked()
    {
        Debug.Log("[MainMenu] Opening Save/Load Screen...");

        // Simply swap the panels: hide main menu, show save/load screen
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        if (saveLoadPanel != null)
            saveLoadPanel.SetActive(true);
    }

    public void OnSettingsClicked()
    {
        Debug.Log("[MainMenu] Opening Settings...");
        if (settingsPanel != null)
        {
            mainMenuPanel.SetActive(false);
            settingsPanel.SetActive(true);
        }
    }

    public void OnQuitClicked()
    {
        Debug.Log("[MainMenu] Quitting Game...");
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // --- Panel Navigation Methods ---

    // Hook this up to the "Back" button on your Settings panel
    public void OnBackToMainClicked()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    // Hook this up to the "Back" button on your Save/Load panel!
    public void OnBackFromSaveLoadClicked()
    {
        if (saveLoadPanel != null) saveLoadPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }
}