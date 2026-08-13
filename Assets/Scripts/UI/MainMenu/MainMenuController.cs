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
        if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGameClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // Ensure sub-panels start hidden
        if (saveLoadPanel != null) saveLoadPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        CheckForSaveData();
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
        SceneManager.LoadScene(newGameSceneName);
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