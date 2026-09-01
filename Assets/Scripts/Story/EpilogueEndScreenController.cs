using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

/// <summary>
/// "Thanks for playing" screen shown after the epilogue sequence, including
/// the post-dialogue camera showcase, has fully finished. Two actions: send
/// the results CSV, and continue playing / roaming MainMap.
///
/// Subscribes to EpilogueSequenceController.OnEpilogueSequenceFullyComplete,
/// not OnEpilogueCompleted, see that script for why the timing matters.
///
/// If EpilogueSequenceController.externalSystemHandlesPlayerHandback is
/// true, it defers BOTH player movement AND HUD visibility to whatever
/// handles the final handback. This script is that handler: it locks the
/// player and hides the HUD again in Show() (mirroring what
/// EpilogueSequenceController already did during the cutscene, in case any
/// system briefly restored either in between), and only actually restores
/// them in OnContinueClicked().
///
/// Uses TMPro.TextMeshProUGUI. This is the only script in the project that
/// does, every other UI script uses legacy UnityEngine.UI.Text.
///
/// Place one instance in the MainMap scene, panel inactive by default.
/// </summary>
public class EpilogueEndScreenController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject endScreenPanel;
    public TextMeshProUGUI sendResultsButtonLabel;
    public Button sendResultsButton;
    public Button continueButton;
    public TextMeshProUGUI statusText;

    [Header("Config")]
    [Tooltip("Chooser title shown above the Android share sheet.")]
    public string androidShareTitle = "Send your PyQuest results";
    [Tooltip("Label used for the send-results button on Android.")]
    public string androidButtonLabel = "Send My Results";
    [Tooltip("Label used for the send-results button on Windows (Explorer reveal, for testing).")]
    public string windowsButtonLabel = "Show Me The File";
    [Tooltip("Message shown in Status Text the moment the screen appears.")]
    public string defaultStatusMessage = "Thanks for playing PyQuest!";

    private PlayerMovement playerMovement;

#if UNITY_STANDALONE_WIN
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

    private const int SW_MINIMIZE = 6;
#endif

    private void OnEnable()
    {
        EpilogueSequenceController.OnEpilogueSequenceFullyComplete += HandleEpilogueSequenceFullyComplete;
    }

    private void OnDisable()
    {
        EpilogueSequenceController.OnEpilogueSequenceFullyComplete -= HandleEpilogueSequenceFullyComplete;
    }

    private void Awake()
    {
        if (endScreenPanel != null) endScreenPanel.SetActive(false);

        if (sendResultsButton != null) sendResultsButton.onClick.AddListener(OnSendResultsClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);

        if (sendResultsButtonLabel != null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            sendResultsButtonLabel.text = androidButtonLabel;
#else
            sendResultsButtonLabel.text = windowsButtonLabel;
#endif
        }
    }

    private void HandleEpilogueSequenceFullyComplete()
    {
        Show();
    }

    private void Show()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerMovement = player.GetComponent<PlayerMovement>();
            if (playerMovement != null) playerMovement.enabled = false;
        }

        HUDController.Instance?.SetVisible(false);

        SaveRestrictionEnforcer.Instance?.AddBlocker("epilogue_end_screen");

        if (statusText != null) statusText.text = defaultStatusMessage;
        if (endScreenPanel != null) endScreenPanel.SetActive(true);
    }

    private void OnContinueClicked()
    {
        if (endScreenPanel != null) endScreenPanel.SetActive(false);

        if (playerMovement != null) playerMovement.enabled = true;

        HUDController.Instance?.SetVisible(true);

        SaveRestrictionEnforcer.Instance?.RemoveBlocker("epilogue_end_screen");
    }

    private void OnSendResultsClicked()
    {
        if (StudentLogManager.Instance == null)
        {
            SetStatus("Could not find results, StudentLogManager missing.");
            return;
        }

        string path = StudentLogManager.Instance.ExportCanonicalCsv();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            SetStatus("Could not create the results file. Try again, or tell your evaluator.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool launched = AndroidNativeShare.ShareFile(path, "text/csv", androidShareTitle);
        SetStatus(launched
            ? "Opening the share menu..."
            : "Could not open the share menu. Try again, or tell your evaluator.");
#elif UNITY_STANDALONE_WIN
        StartCoroutine(RevealInExplorerRoutine(path));
#else
        SetStatus("Your results file is at: " + path);
#endif
    }

#if UNITY_STANDALONE_WIN
    private IEnumerator RevealInExplorerRoutine(string path)
    {
        string windowsPath = path.Replace('/', '\\');

        Debug.Log($"[EpilogueEndScreenController] Current FullScreenMode: {Screen.fullScreenMode}");

        if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            yield return new WaitForSecondsRealtime(0.2f);
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + windowsPath + "\"");
            Debug.Log("[EpilogueEndScreenController] Process.Start for explorer.exe returned without throwing.");

            System.IntPtr gameWindow = GetForegroundWindow();
            if (gameWindow != System.IntPtr.Zero)
            {
                bool minimized = ShowWindow(gameWindow, SW_MINIMIZE);
                Debug.Log($"[EpilogueEndScreenController] ShowWindow(SW_MINIMIZE) returned: {minimized}");
            }
            else
            {
                Debug.LogWarning("[EpilogueEndScreenController] GetForegroundWindow() returned zero, could not minimize.");
            }

            SetStatus("Opening file location, check your taskbar.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[EpilogueEndScreenController] Failed to open Explorer: {e.Message}\n{e.StackTrace}");
            SetStatus("Could not open the folder. The file is at: " + path);
        }
    }
#endif

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[EpilogueEndScreenController] {message}");
    }
}