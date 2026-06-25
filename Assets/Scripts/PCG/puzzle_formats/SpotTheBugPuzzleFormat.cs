using UnityEngine;
using UnityEngine.UI;

public class SpotTheBugPuzzleFormat : IPuzzleFormat
{
    public PuzzleType FormatType => PuzzleType.SpotTheBug;
    private PuzzleTemplate template;
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }

    public void RenderPuzzle(PredictTheOutputUIController uiController) { }
    public void Initialize(PuzzleTemplate template) => this.template = template;
    public void RenderPuzzle(Text displayField) { }
    public bool EvaluateAnswer(object playerAnswer) => false;
    public object GetCorrectAnswer() => template?.correctAnswer ?? "";

    public void RenderPuzzle(PairACodeUIController uiController) { }
}