using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        GameCompletionState.MarkGameCompleted();

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
        ResultsExportHelper.ExportAndShare(this, androidShareTitle, SetStatus);
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[EpilogueEndScreenController] {message}");
    }
}