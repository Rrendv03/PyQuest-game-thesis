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
    void RenderPuzzle(SpotTheBugUIController uiController);
    void RenderPuzzle(LineScrambleUIController uiController);

    int GetOptionCount();

    bool EvaluateAnswer(object playerAnswer);

    object GetCorrectAnswer();
}