using UnityEngine;
using UnityEngine.UI;

public class LineScramblePuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.LineScramble;
    private PuzzleTemplate template;

    public void Initialize(PuzzleTemplate template) => this.template = template;
    public void RenderPuzzle(Text displayField) { }
    public bool EvaluateAnswer(object playerAnswer) => false;
    public object GetCorrectAnswer() => template?.correctAnswer ?? "";
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }
    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void RenderPuzzle(PairACodeUIController uiController) { }
}