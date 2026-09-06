using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Failsafe "send results" button for the general Settings screen.
/// Mirrors EpilogueEndScreenController's send-results button, so a player
/// who accidentally dismissed the epilogue end screen (hit Continue
/// before sending) can still reach their results CSV later, from
/// Settings, in any scene.
///
/// Only visible once GameCompletionState.HasCompletedGame() is true. That
/// flag is set the moment the epilogue end screen first appears, not when
/// Continue is pressed, so it is already saved before a player could
/// possibly dismiss the screen. Place this on the button's own
/// GameObject (or a direct wrapper) inside the Settings panel; it hides
/// itself in OnEnable() if the game hasn't been finished yet.
/// </summary>
public class SettingsResultsButtonUI : MonoBehaviour
{
    public Button sendResultsButton;
    [Tooltip("Optional. Text label on the button, if you want the same platform-specific wording as the epilogue screen.")]
    public TextMeshProUGUI buttonLabel;
    public TextMeshProUGUI statusText;

    [Tooltip("Chooser title shown above the Android share sheet.")]
    public string androidShareTitle = "Send your PyQuest results";
    public string androidButtonLabel = "Send My Results";
    public string windowsButtonLabel = "Show Me The File";

    void Awake()
    {
        if (buttonLabel != null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            buttonLabel.text = androidButtonLabel;
#else
            buttonLabel.text = windowsButtonLabel;
#endif
        }
    }

    void OnEnable()
    {
        bool completed = GameCompletionState.HasCompletedGame();
        gameObject.SetActive(completed);
        if (!completed) return;

        if (sendResultsButton != null)
            sendResultsButton.onClick.AddListener(OnSendResultsClicked);
    }

    void OnDisable()
    {
        if (sendResultsButton != null)
            sendResultsButton.onClick.RemoveListener(OnSendResultsClicked);
    }

    private void OnSendResultsClicked()
    {
        ResultsExportHelper.ExportAndShare(this, androidShareTitle, SetStatus);
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[SettingsResultsButtonUI] {message}");
    }
}