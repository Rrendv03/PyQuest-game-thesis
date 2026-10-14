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
    /// <summary>Scene singleton so QuestionnaireController can hand the player
    /// back here after a successful submission. Null-safe at call sites.</summary>
    public static EpilogueEndScreenController Instance { get; private set; }

    private bool questionnaireFinished;

    /// <summary>QuestionnaireController asks who the player is, and after
    /// its confirmation Continue it tells us to fully retire the end screen
    /// (it never comes back once the questionnaire is submitted).</summary>
    public GameObject GetPlayer()
    {
        var found = GameObject.FindGameObjectWithTag("Player");
        return found;
    }

    public void HideEndScreenPermanently()
    {
        questionnaireFinished = true;
        if (endScreenPanel != null) endScreenPanel.SetActive(false);
        if (sendResultsButton != null) sendResultsButton.gameObject.SetActive(false);
        if (questionnaireButton != null) questionnaireButton.gameObject.SetActive(false);
    }

    [Header("UI References")]
    public GameObject endScreenPanel;
    public TextMeshProUGUI sendResultsButtonLabel;
    public Button sendResultsButton;
    public Button continueButton;
    public TextMeshProUGUI statusText;
    [Tooltip("Optional. Shows the BKT engine's final mastery belief per knowledge component, labeled by tier (no raw percentages, so the post-game questionnaire isn't anchored by a number). Left empty, the block is skipped.")]
    public TextMeshProUGUI masterySummaryText;
    [Tooltip("Button that redirects to the post-game questionnaire. If left unassigned, Continue opens the questionnaire instead of returning to the game.")]
    public Button questionnaireButton;

    [Header("Config")]
    [Tooltip("Chooser title shown above the Android share sheet.")]
    public string androidShareTitle = "Send your PyQuest results";
    [Tooltip("Label used for the send-results button on Android.")]
    public string androidButtonLabel = "Send My Results";
    [Tooltip("Label used for the send-results button on Windows (Explorer reveal, for testing).")]
    public string windowsButtonLabel = "Show Me The File";
    [Tooltip("Message shown in Status Text the moment the screen appears.")]
    public string defaultStatusMessage = "Thanks for playing PyQuest!";
    [Tooltip("Full path to the results CSV to reveal in the file manager. Leave empty to auto-find the newest .csv in the app's persistent data folder.")]
    public string csvFilePath = "";

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
        Instance = this;

        if (endScreenPanel != null) endScreenPanel.SetActive(false);

        if (sendResultsButton != null) sendResultsButton.onClick.AddListener(OnSendResultsClicked);
        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (questionnaireButton != null) questionnaireButton.onClick.AddListener(OnQuestionnaireClicked);

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

        // Data-gathering flow: while the questionnaire is pending, the student
        // only gets the questionnaire button (plus the mastery summary). The
        // send-results button is hidden, NOT deleted, and both it and the
        // questionnaire button come back only if you reset
        // questionnaireFinished below.
        if (!questionnaireFinished && sendResultsButton != null)
            sendResultsButton.gameObject.SetActive(false);

        // BKT visibility (adviser request): surface the engine's own mastery
        // beliefs on the end screen. READ-ONLY: this only calls GetMastery /
        // GetTierForMasteryPublic, it never writes engine state, so the frozen
        // bkt_params.json (SHA-256 85314702...c26) and engine behavior are
        // untouched. Tier labels only, no raw percentages: the post-game
        // questionnaire follows this screen and a number could anchor it.
        if (masterySummaryText != null)
        {
            masterySummaryText.text = BuildMasterySummary();
        }

        if (endScreenPanel != null) endScreenPanel.SetActive(true);
    }

    /// <summary>
    /// One line per knowledge component: "Print Statements - Advanced".
    /// Reads the engine's live belief via its own public API. If the engine
    /// or a component is missing, that line is simply skipped, and the text
    /// stays empty when nothing can be read (fail-silent by design: this is
    /// informational UI, not data collection - the CSV carries the numbers).
    /// </summary>
    private string BuildMasterySummary()
    {
        var engine = BKTEngine.Instance;
        if (engine == null) return "";

        string[] components =
        {
            "print_statements", "variables", "input_handling",
            "conditionals", "loops", "basic_operations"
        };

        // Tier rule lives in PCGEngine, not BKTEngine (same call the
        // encounter spawner uses: PCGEngine.Instance.GetTierForMasteryPublic).
        var pcg = PCGEngine.Instance;
        if (pcg == null) return "";

        var lines = new System.Collections.Generic.List<string>();
        foreach (string kc in components)
        {
            // GetMastery returns 0f for a component this run never touched,
            // which reads honestly as "Beginner" - a zero belief IS zero.
            float mastery = engine.GetMastery(kc);
            DifficultyTier tier = pcg.GetTierForMasteryPublic(mastery);
            lines.Add($"{PrettifyKc(kc)} - {tier}");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "";
    }

    private static string PrettifyKc(string kc)
    {
        // "print_statements" -> "Print Statements"
        string[] parts = kc.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(" ", parts);
    }

    private void OnQuestionnaireClicked()
    {
        if (endScreenPanel != null) endScreenPanel.SetActive(false);
        QuestionnaireController.Instance?.Show();
    }

    /// <summary>
    /// Called by QuestionnaireController's confirmation Continue. Re-shows the
    /// end screen with ONLY the Continue button active: send-results stays
    /// hidden and the questionnaire button is dismissed, because the
    /// questionnaire is already submitted. World handback stays the end
    /// screen's own Continue (same blockers as before).
    /// </summary>
    public void ShowAfterQuestionnaire()
    {
        questionnaireFinished = true;

        if (sendResultsButton != null) sendResultsButton.gameObject.SetActive(false);
        if (questionnaireButton != null) questionnaireButton.gameObject.SetActive(false);

        // Do NOT re-lock or re-hide anything: the end screen's Show() already
        // locked the player and hid the HUD and nothing in between restored
        // them (the questionnaire never does). Only bring the panel back.
        if (statusText != null)
            statusText.text = defaultStatusMessage + "\nThank you for completing the questionnaire!";
        if (endScreenPanel != null) endScreenPanel.SetActive(true);
    }

    private void OnContinueClicked()
    {
        // The restored world is now the player's home: swap the epilogue
        // tracks out for the restored scene music (see WorldRestorationController).
        WorldRestorationController.Instance?.RequestRestoredMusic();

        if (endScreenPanel != null) endScreenPanel.SetActive(false);

        if (playerMovement != null) playerMovement.enabled = true;

        HUDController.Instance?.SetVisible(true);

        SaveRestrictionEnforcer.Instance?.RemoveBlocker("epilogue_end_screen");
    }

    private void OnSendResultsClicked()
    {
        // Exports the canonical CSV, then opens the platform file manager at
        // its location (Explorer-select on Windows, Finder reveal on macOS,
        // MediaStore + file manager on Android, etc). csvFilePath, when set,
        // points the reveal at a specific file instead of the canonical one.
        ResultsExportHelper.ExportAndShare(this, androidShareTitle, SetStatus, csvFilePath);
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[EpilogueEndScreenController] {message}");
    }
}