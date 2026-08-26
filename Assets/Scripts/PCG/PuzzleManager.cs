using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance;

    [Header("UI Component Bindings")]
    public GameObject trueOrFalseCanvasPanel;
    public Text codeDisplayTextField_ToF;
    public GameObject pairACodeCanvasPanel;
    public GameObject fillInTheBlankCanvasPanel;
    public GameObject predictTheOutputCanvasPanel;
    public GameObject spotTheBugCanvasPanel;
    public GameObject lineScrambleCanvasPanel;

    private GameObject currentPuzzleCanvasPanel;
    private string currentActiveComponent;
    private PuzzleData currentPuzzle;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        Debug.Log("[PuzzleManager] Awakened");
    }

    /// <summary>
    /// Exploration mode: generates a puzzle using live BKT mastery for tier selection.
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

        ActivatePuzzleCanvas();
    }

    /// <summary>
    /// Encounter mode: generates a puzzle using a locked difficulty tier
    /// so mid-encounter BKT updates do not shift the puzzle difficulty.
    /// </summary>
    public void OnZoneEntered(string zone, PuzzleType puzzleType, DifficultyTier forcedTier)
    {
        currentActiveComponent = zone;
        Debug.Log($"[PuzzleManager] Zone: {zone} | PuzzleType: {puzzleType} | ForcedTier: {forcedTier}");

        currentPuzzle = PCGEngine.Instance.GeneratePuzzle(zone, puzzleType, forcedTier);

        Debug.Log($"[PuzzleManager] currentPuzzle: {currentPuzzle} | formatHandler: {currentPuzzle?.formatHandler} | FormatType: {currentPuzzle?.formatHandler?.FormatType}");

        if (currentPuzzle == null || currentPuzzle.formatHandler == null)
        {
            Debug.LogError("[PuzzleManager] Failed to generate puzzle for zone: " + zone);
            return;
        }

        ActivatePuzzleCanvas();
    }

    /// <summary>
    /// Shared canvas activation logic for both exploration and encounter modes.
    /// </summary>
    private void ActivatePuzzleCanvas()
    {
        switch (currentPuzzle.formatHandler.FormatType)
        {
            case PuzzleType.TrueOrFalse:
                currentPuzzleCanvasPanel = trueOrFalseCanvasPanel;
                currentPuzzle.formatHandler.RenderPuzzle(codeDisplayTextField_ToF);
                break;

            case PuzzleType.PairACode:
                currentPuzzleCanvasPanel = pairACodeCanvasPanel;
                PairACodeUIController pairUI = pairACodeCanvasPanel
                    .GetComponent<PairACodeUIController>();
                if (pairUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(pairUI);
                else
                    Debug.LogError("[PuzzleManager] PairACodeUIController not found");
                break;

            case PuzzleType.FillInTheBlank:
                currentPuzzleCanvasPanel = fillInTheBlankCanvasPanel;
                FillInTheBlankUIController fitbUI = fillInTheBlankCanvasPanel
                    .GetComponent<FillInTheBlankUIController>();
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

            case PuzzleType.SpotTheBug:
                currentPuzzleCanvasPanel = spotTheBugCanvasPanel;
                SpotTheBugUIController stbUI = spotTheBugCanvasPanel
                    .GetComponent<SpotTheBugUIController>();
                if (stbUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(stbUI);
                else
                    Debug.LogError("[PuzzleManager] SpotTheBugUIController not found");
                break;

            case PuzzleType.LineScramble:
                currentPuzzleCanvasPanel = lineScrambleCanvasPanel;
                LineScrambleUIController lsUI = lineScrambleCanvasPanel
                    .GetComponent<LineScrambleUIController>();
                if (lsUI != null)
                    currentPuzzle.formatHandler.RenderPuzzle(lsUI);
                else
                    Debug.LogError("[PuzzleManager] LineScrambleUIController not found");
                break;

            default:
                Debug.LogError("[PuzzleManager] No canvas for puzzle type: " +
                               currentPuzzle.formatHandler.FormatType);
                return;
        }

        currentPuzzleCanvasPanel.SetActive(true);
    }

    /// <summary>
    /// Input collection endpoint hooked directly up to Canvas buttons.
    /// Routes result to BKT (exploration) or EncounterManager (encounter).
    /// </summary>
    public void UserSubmission(object playerAnswerChoice)
    {
        if (currentPuzzle == null)
        {
            Debug.LogError("[PuzzleManager] No active puzzle to evaluate");
            return;
        }

        bool isCorrect = currentPuzzle.IsAnswerCorrect(playerAnswerChoice);
        int optionCount = currentPuzzle.formatHandler.GetOptionCount();
        float pGuessOverride = optionCount > 0 ? 1f / optionCount : 0f;

        Debug.Log($"[PuzzleManager] Player answered: {playerAnswerChoice} | Correct: {isCorrect}");

        currentPuzzleCanvasPanel.SetActive(false);
        currentPuzzle = null;

        // === TABLET MISSION PATH ===
        if (_tabletMissionCallback != null)
        {
            var callback = _tabletMissionCallback;
            _tabletMissionCallback = null;
            callback.Invoke(isCorrect);
            currentPuzzle = null;
            currentActiveComponent = null;
            return;
        }


        if (EncounterManager.Instance != null &&
            EncounterManager.Instance.IsEncounterActive())
        {
            // Encounter mode: pass result and p_guess to EncounterManager
            // BKT update is batched and applied after the encounter ends
            EncounterManager.Instance.OnPuzzleResolved(isCorrect, pGuessOverride);
            currentActiveComponent = null;
        }
        else
        {
            // Exploration mode: update BKT immediately
            BKTEngine.Instance.UpdateMastery(currentActiveComponent, isCorrect, pGuessOverride);
            currentActiveComponent = null;
        }
    }

    /// <summary>
    /// Bool overload for SpotTheBug and LineScramble which submit bool directly.
    /// </summary>
    public void UserSubmission(bool isCorrect)
    {
        UserSubmission((object)isCorrect);
    }
    // ========== TABLET MISSION PUZZLE PIPELINE ==========

    private System.Action<bool> _tabletMissionCallback;

    public void StartTabletMissionPuzzle(string knowledgeComponent, PuzzleType puzzleType, System.Action<bool> onComplete)
    {
        currentActiveComponent = knowledgeComponent;
        _tabletMissionCallback = onComplete;

        currentPuzzle = PCGEngine.Instance.GeneratePuzzle(knowledgeComponent, puzzleType);

        if (currentPuzzle == null || currentPuzzle.formatHandler == null)
        {
            Debug.LogError("[PuzzleManager] Failed to generate tablet mission puzzle.");
            _tabletMissionCallback?.Invoke(false);
            _tabletMissionCallback = null;
            return;
        }

        ActivatePuzzleCanvas();
    }
}