using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpotTheBugUIController : MonoBehaviour
{
    [Header("Line Buttons Panel")]
    public GameObject linePanelRoot;
    public List<GameObject> lineButtons;

    [Header("Fix Options Panel")]
    public GameObject fixOptionsPanelRoot;
    public List<GameObject> fixOptionButtons;

    [Header("UI")]
    public Text instructionText;
    public Button backButton;

    [Header("Interaction Sounds")]
    [Tooltip("Played when a line button or a fix option is pressed down (pointer press).")]
    public AudioClip optionClickSound;
    [Tooltip("Played when the back button on the fix options screen is clicked. Separate clip AND separate AudioSource so it never cuts off, or gets cut off by, an option click.")]
    public AudioClip backButtonClickSound;
    [Tooltip("Log every sound trigger to the Console. Turn on to diagnose missing audio.")]
    public bool debugSoundEvents = false;

    // Same hub pattern as PredictTheOutputUIController / LineScrambleUIController:
    // the hub lives at the SCENE ROOT (never under the canvas/panel) so it can't
    // be deactivated with the puzzle panel, and each sound gets its own
    // AudioSource so one click can never cut another off mid-playback.
    private AudioSource optionClickSource;
    private AudioSource backClickSource;

    private readonly HashSet<string> warnedMissingClips = new HashSet<string>();

    // Phase C/D round 2: the goal/supposed-output header the format built for
    // the SERVED puzzle. Cached here so intermediate screens (fix options, the
    // back-from-fix-options line view) can restore it instead of dropping the
    // player back on the scene's default text — previously the header was
    // written into instructionText exactly once and every later screen
    // overwrote it with a hardcoded fallback.
    private string contextHeaderText = null;

    private SpotTheBugLineButton selectedLine = null;
    private int correctLineIndex = -1;
    private string correctFix = "";
    private List<List<string>> allLineFixOptions;

    // Phase C/D (09 §7): contextHeader carries the machine-verified goal-fact
    // restatement built by SpotTheBugPuzzleFormat (null on the legacy path —
    // default keeps 4-argument legacy callers compiling unchanged).
    public void PopulateUI(List<string> codeLines, int bugLineIndex,
                           string correctFixOption,
                           List<List<string>> lineFixOptions,
                           string contextHeader = null)
    {
        correctLineIndex = bugLineIndex;
        correctFix = correctFixOption;
        allLineFixOptions = lineFixOptions;
        selectedLine = null;

        if (linePanelRoot != null) linePanelRoot.SetActive(true);
        if (fixOptionsPanelRoot != null) fixOptionsPanelRoot.SetActive(false);

        // Phase C/D (09 §7): when the generative path supplied goal facts,
        // the instruction line states those VERIFIED facts (const template);
        // legacy serves keep the original line verbatim.
        contextHeaderText = contextHeader;
        RestoreInstructionHeader();

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            // Wire through OnBackButtonPressed (not OnBackPressed directly) so
            // ONLY the back button plays the back sound. Deselecting a line by
            // re-clicking it reaches OnBackPressed via OnLineDeselected without
            // it -- that press already played the line click sound.
            backButton.onClick.AddListener(OnBackButtonPressed);
            backButton.gameObject.SetActive(false);
        }

        foreach (var btn in lineButtons)
            btn.SetActive(false);

        for (int i = 0; i < codeLines.Count && i < lineButtons.Count; i++)
        {
            lineButtons[i].SetActive(true);
            SpotTheBugLineButton lineBtn = lineButtons[i]
                .GetComponent<SpotTheBugLineButton>();
            if (lineBtn != null)
                lineBtn.Setup(i, codeLines[i], this);
        }

        Debug.Log($"[SpotTheBugUIController] Populated {codeLines.Count} lines");
    }

    public void OnLineSelected(SpotTheBugLineButton line)
    {
        if (selectedLine != null)
            selectedLine.SetState_Default();

        selectedLine = line;
        line.SetState_Selected();

        if (backButton != null)
            backButton.gameObject.SetActive(true);

        ShowFixOptionsForLine(line.lineIndex);

        Debug.Log($"[SpotTheBugUIController] Line selected: {line.lineIndex}");
    }

    public void OnLineDeselected(SpotTheBugLineButton line)
    {
        OnBackPressed();
    }

    public void OnFixOptionSelected(SpotTheBugFixOption option)
    {
        if (selectedLine == null) return;

        option.SetState_Selected();

        bool lineCorrect = selectedLine.lineIndex == correctLineIndex;
        bool fixCorrect = option.optionText.Trim() == correctFix.Trim();
        bool isCorrect = lineCorrect && fixCorrect;

        Debug.Log($"[SpotTheBugUIController] Line {selectedLine.lineIndex} " +
                  $"({(lineCorrect ? "correct" : "wrong")}) | " +
                  $"Fix: {option.optionText} ({(fixCorrect ? "correct" : "wrong")}) | " +
                  $"Pre-check result: {isCorrect}");

        var submission = new SpotTheBugPuzzleFormat.SubmissionData(
            selectedLine.lineIndex,
            option.optionText);

        PuzzleManager.Instance.UserSubmission(submission);
    }

    private void ShowFixOptionsForLine(int lineIndex)
    {
        if (fixOptionsPanelRoot != null)
            fixOptionsPanelRoot.SetActive(true);

        // Phase C/D round 2: keep the served header here too — the fix-options
        // screen only ADDS a hint under it rather than replacing the header.
        if (instructionText != null)
            instructionText.text = string.IsNullOrEmpty(contextHeaderText)
                ? "Select the correct fix for this line."
                : contextHeaderText + "\nSelect the correct fix for this line.";

        foreach (var btn in fixOptionButtons)
            btn.SetActive(false);

        if (allLineFixOptions == null || lineIndex >= allLineFixOptions.Count)
            return;

        List<string> options = allLineFixOptions[lineIndex];

        for (int i = 0; i < options.Count && i < fixOptionButtons.Count; i++)
        {
            fixOptionButtons[i].SetActive(true);
            SpotTheBugFixOption fixBtn = fixOptionButtons[i]
                .GetComponent<SpotTheBugFixOption>();
            if (fixBtn != null)
                fixBtn.Setup(options[i], this);
        }

        Debug.Log($"[SpotTheBugUIController] Fix options for line {lineIndex}: " +
                  $"{string.Join(", ", options)}");
    }

    private void OnBackButtonPressed()
    {
        PlayBackClickSound();
        OnBackPressed();
    }

    private void OnBackPressed()
    {
        if (selectedLine != null)
        {
            selectedLine.SetState_Default();
            selectedLine = null;
        }

        if (fixOptionsPanelRoot != null)
            fixOptionsPanelRoot.SetActive(false);

        if (backButton != null)
            backButton.gameObject.SetActive(false);

        // Phase C/D round 2: restore the served [Goal:...] / [Supposed
        // Output:...] header instead of the scene's default line — returning
        // from the fix options must never blank the goal facts.
        RestoreInstructionHeader();

        Debug.Log("[SpotTheBugUIController] Back pressed");
    }

    /// <summary>Writes the cached served header back into instructionText;
    /// falls back to the original default line only on the legacy path where
    /// no header was ever supplied.</summary>
    private void RestoreInstructionHeader()
    {
        if (instructionText != null)
            instructionText.text = string.IsNullOrEmpty(contextHeaderText)
                ? "Click the line that contains the bug."
                : contextHeaderText;
    }

    // --- Interaction sounds (clips assigned in the Inspector) ---

    /// <summary>
    /// Plays the option click sound. Called by SpotTheBugLineButton and
    /// SpotTheBugFixOption from OnPointerDown, so it fires on press.
    /// </summary>
    public void PlayOptionClickSound()
    {
        if (optionClickSound == null)
        {
            WarnMissingClip("option click");
            return;
        }

        EnsureSfxHub();
        optionClickSource.PlayOneShot(optionClickSound);

        if (debugSoundEvents)
            Debug.Log($"[SpotTheBugSfx] Played 'option click' ({optionClickSound.name}).");
    }

    /// <summary>
    /// Plays the back-button click sound on its own AudioSource on the hub,
    /// so it can never cut off an option click (or vice versa).
    /// </summary>
    public void PlayBackClickSound()
    {
        if (backButtonClickSound == null)
        {
            WarnMissingClip("back click");
            return;
        }

        EnsureSfxHub();
        backClickSource.PlayOneShot(backButtonClickSound);

        if (debugSoundEvents)
            Debug.Log($"[SpotTheBugSfx] Played 'back click' ({backButtonClickSound.name}).");
    }

    private void EnsureSfxHub()
    {
        if (optionClickSource != null) return;

        GameObject hub = new GameObject("SpotTheBugSfxHub");
        optionClickSource = hub.AddComponent<AudioSource>();
        optionClickSource.playOnAwake = false;
        optionClickSource.spatialBlend = 0f; // 2D UI sound, no positional panning

        backClickSource = hub.AddComponent<AudioSource>();
        backClickSource.playOnAwake = false;
        backClickSource.spatialBlend = 0f;
    }

    // Warn once per unassigned clip so a missing Inspector assignment is
    // obvious in the Console instead of failing silently.
    private void WarnMissingClip(string soundName)
    {
        if (warnedMissingClips.Add(soundName))
            Debug.LogWarning(
                $"[SpotTheBugSfx] '{soundName}' sound was triggered but no AudioClip is assigned for it in the Inspector. " +
                $"Assign it on {name} under 'Interaction Sounds'.");
    }
}