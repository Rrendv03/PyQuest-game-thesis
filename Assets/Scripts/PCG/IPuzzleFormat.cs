using UnityEngine;
using UnityEngine.UI;

public interface IPuzzleFormat
{
    PuzzleType FormatType { get; }

    void Initialize(PuzzleTemplate template);

    // Used by True/False and other text-based formats
    void RenderPuzzle(Text displayField);

    // Used by PairACode which needs its own UI controller
    void RenderPuzzle(PairACodeUIController uiController);

    void RenderPuzzle(FillInTheBlankUIController uiController);

    void RenderPuzzle(PredictTheOutputUIController uiController);

    bool EvaluateAnswer(object playerAnswer);

    object GetCorrectAnswer();
}