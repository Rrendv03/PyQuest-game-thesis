using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gatekeeper for the Main Menu scene. Shows a one-time name-entry
/// welcome screen before the Main Menu panel becomes visible, and swaps
/// to the Main Menu once StudentLogManager.TrySetStudentID() succeeds.
///
/// Lives on its own always-active GameObject (NOT on the Main Menu panel
/// itself), so it reliably runs on scene load regardless of which panel
/// starts active. WelcomePanel should start ACTIVE and MainMenuPanel
/// should start INACTIVE in the Inspector, this script only needs to
/// override that for a respondent who already has a saved ID.
///
/// If your project uses legacy UI.Text/InputField instead of
/// TextMeshPro, swap TMP_InputField -> InputField and TMP_Text -> Text.
/// </summary>
public class StudentWelcomeUI : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("The welcome/name-entry panel. Should start ACTIVE in the Inspector.")]
    [SerializeField] private GameObject welcomePanel;

    [Tooltip("The actual Main Menu panel (Play/Continue/Settings/etc). Should start INACTIVE in the Inspector.")]
    [SerializeField] private GameObject mainMenuPanel;

    [Header("Welcome Panel Controls")]
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text errorText;

    private void Start()
    {
        if (errorText != null)
            errorText.text = "";

        // Respondent already has an ID on this device (they've launched
        // before), skip straight to the Main Menu instead of asking again.
        if (StudentLogManager.Instance != null && StudentLogManager.Instance.HasStudentID())
        {
            ShowMainMenu();
            return;
        }

        ShowWelcomeScreen();

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmPressed);

        if (nameInputField != null)
        {
            // Live-strip numbers/symbols as they type, instead of only
            // rejecting on submit, so the player sees immediately that
            // a character didn't go through.
            nameInputField.onValueChanged.AddListener(FilterInput);
            // Let Enter/Return submit the field instead of requiring a click.
            nameInputField.onSubmit.AddListener(_ => OnConfirmPressed());
        }
    }

    private void FilterInput(string newText)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in newText)
        {
            if (char.IsLetter(c) || c == ' ' || c == '-' || c == '\'')
                sb.Append(c);
        }
        string filtered = sb.ToString();
        if (filtered != newText)
        {
            nameInputField.SetTextWithoutNotify(filtered);
            nameInputField.caretPosition = filtered.Length;
        }
    }

    private void OnConfirmPressed()
    {
        if (StudentLogManager.Instance == null)
        {
            if (errorText != null)
                errorText.text = "Logging system not ready, please restart the app.";
            return;
        }

        bool ok = StudentLogManager.Instance.TrySetStudentID(
            nameInputField != null ? nameInputField.text : null,
            out string finalID,
            out string error);

        if (!ok)
        {
            if (errorText != null)
                errorText.text = error;
            return;
        }

        ShowMainMenu();
    }

    private void ShowWelcomeScreen()
    {
        if (welcomePanel != null) welcomePanel.SetActive(true);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    }

    private void ShowMainMenu()
    {
        if (welcomePanel != null) welcomePanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }
}