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

    private SpotTheBugLineButton selectedLine = null;
    private int correctLineIndex = -1;
    private string correctFix = "";
    private List<List<string>> allLineFixOptions;

    public void PopulateUI(List<string> codeLines, int bugLineIndex,
                           string correctFixOption,
                           List<List<string>> lineFixOptions)
    {
        correctLineIndex = bugLineIndex;
        correctFix = correctFixOption;
        allLineFixOptions = lineFixOptions;
        selectedLine = null;

        if (linePanelRoot != null) linePanelRoot.SetActive(true);
        if (fixOptionsPanelRoot != null) fixOptionsPanelRoot.SetActive(false);

        if (instructionText != null)
            instructionText.text = "Click the line that contains the bug.";

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackPressed);
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
                  $"Fix: {option.optionText} ({(fixCorrect ? "correct" : "wrong")})");

        PuzzleManager.Instance.UserSubmission(isCorrect);
    }

    private void ShowFixOptionsForLine(int lineIndex)
    {
        if (fixOptionsPanelRoot != null)
            fixOptionsPanelRoot.SetActive(true);

        if (instructionText != null)
            instructionText.text = "Select the correct fix for this line.";

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

        if (instructionText != null)
            instructionText.text = "Click the line that contains the bug.";

        Debug.Log("[SpotTheBugUIController] Back pressed");
    }
}