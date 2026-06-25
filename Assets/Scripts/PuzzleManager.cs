using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance;

    [Header("UI Component Bindings")]
    public GameObject trueOrFalseCanvasPanel;
    public GameObject pairACodeCanvasPanel;
    public GameObject fillInTheBlankCanvasPanel;
    public GameObject predictTheOutputCanvasPanel;
    public Text codeDisplayTextField_ToF;

    private GameObject currentPuzzleCanvasPanel;
    private string currentActiveComponent;
    private PuzzleData currentPuzzle;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        Debug.Log("[PuzzleManager] Awakened");
    }

    /// <summary>
    /// Activated instantly when a player crosses a map boundary zone event.
    /// Generates an adaptive puzzle and displays it to the player.
    /// </summary>
    public void OnZoneEntered(string zone, PuzzleType puzzleType)
    {
        currentActiveComponent = zone;
        Debug.Log($"[PuzzleManager] Zone: {zone} | PuzzleType: {puzzleType}");
        Debug.Log($"[PuzzleManager] trueOrFalseCanvasPanel: {trueOrFalseCanvasPanel} | pairACodeCanvasPanel: {pairACodeCanvasPanel}");

        currentPuzzle = PCGEngine.Instance.GeneratePuzzle(zone, puzzleType);

        Debug.Log($"[PuzzleManager] currentPuzzle: {currentPuzzle} | formatHandler: {currentPuzzle?.formatHandler} | FormatType: {currentPuzzle?.formatHandler?.FormatType}");
        
        if (currentPuzzle == null || currentPuzzle.formatHandler == null)
        {
            Debug.LogError("[PuzzleManager] Failed to generate puzzle for zone: " + zone);
            return;
        }

        switch (currentPuzzle.formatHandler.FormatType)
        {
            case PuzzleType.TrueOrFalse:
                currentPuzzleCanvasPanel = trueOrFalseCanvasPanel;
                currentPuzzle.formatHandler.RenderPuzzle(codeDisplayTextField_ToF);
                break;

            case PuzzleType.PairACode:
                currentPuzzleCanvasPanel = pairACodeCanvasPanel;
                PairACodeUIController pairUI = pairACodeCanvasPanel.GetComponent<PairACodeUIController>();
                if (pairUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(pairUI);
                else
                    Debug.LogError("[PuzzleManager] PairACodeUIController not found");
                break;
            case PuzzleType.FillInTheBlank:
                currentPuzzleCanvasPanel = fillInTheBlankCanvasPanel;
                FillInTheBlankUIController fitbUI = fillInTheBlankCanvasPanel.GetComponent<FillInTheBlankUIController>();
                if (fitbUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(fitbUI);
                else
                    Debug.LogError("[PuzzleManager] FillInTheBlankUIController not found");
                break;
            case PuzzleType.PredictTheOutput:
                currentPuzzleCanvasPanel = predictTheOutputCanvasPanel;
                PredictTheOutputUIController ptoUI = predictTheOutputCanvasPanel
                    .GetComponent<PredictTheOutputUIController>();
                if (ptoUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(ptoUI);
                else
                    Debug.LogError("[PuzzleManager] PredictTheOutputUIController not found");
                break;
            default:
                Debug.LogError("[PuzzleManager] No canvas for puzzle type: " + currentPuzzle.formatHandler.FormatType);
                return;
        }

        currentPuzzleCanvasPanel.SetActive(true);
    }

    /// <summary>
    /// Input collection endpoint hooked directly up to your Canvas buttons.
    /// Evaluates the player's answer and updates the BKT mastery model.
    /// </summary>
    public void UserSubmission(object playerAnswerChoice)
{
    if (currentPuzzle == null)
    {
        Debug.LogError("[PuzzleManager] No active puzzle to evaluate");
        return;
    }

    bool isCorrect = currentPuzzle.IsAnswerCorrect(playerAnswerChoice);

    Debug.Log($"[PuzzleManager] Player answered: {playerAnswerChoice} | Correct: {isCorrect}");

    BKTEngine.Instance.UpdateMastery(currentActiveComponent, isCorrect);

    currentPuzzleCanvasPanel.SetActive(false);
    currentPuzzle = null;
    currentActiveComponent = null;
}
}