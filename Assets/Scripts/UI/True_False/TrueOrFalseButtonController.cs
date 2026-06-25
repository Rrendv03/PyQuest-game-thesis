using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the True and False buttons on the True_or_False_Screen canvas to PuzzleManager.
/// Attach this script to the True_or_False_Screen canvas.
/// </summary>
public class TrueOrFalseButtonController : MonoBehaviour
{
    public Button buttonTrue;
    public Button buttonFalse;

    private void Start()
    {
        if (buttonTrue == null || buttonFalse == null)
        {
            Debug.LogError("[TrueOrFalseButtonController] Button references are not assigned in inspector");
            return;
        }

        buttonTrue.onClick.AddListener(() => SubmitAnswer(true));
        buttonFalse.onClick.AddListener(() => SubmitAnswer(false));

        Debug.Log("[TrueOrFalseButtonController] Buttons wired successfully");
    }

    private void SubmitAnswer(bool playerAnswer)
    {
        Debug.Log($"[TrueOrFalseButtonController] Player clicked: {(playerAnswer ? "True" : "False")}");
        PuzzleManager.Instance.UserSubmission(playerAnswer);
    }
}