using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FillInTheBlankUIController : MonoBehaviour
{
    [Header("Left - Code Display")]
    public Text codeDisplayText;

    [Header("Right - Token Buttons")]
    public List<GameObject> tokenObjects;

    private FillInTheBlankToken selectedToken = null;

    public void PopulateUI(string codeSnippet, List<string> tokens)
    {
        // Display code snippet with blank
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Reset all tokens
        foreach (var obj in tokenObjects)
            obj.SetActive(false);

        // Shuffle tokens
        List<string> shuffled = new List<string>(tokens);
        ShuffleList(shuffled);

        // Populate token buttons
        for (int i = 0; i < shuffled.Count && i < tokenObjects.Count; i++)
        {
            tokenObjects[i].SetActive(true);

            FillInTheBlankToken token = tokenObjects[i].GetComponent<FillInTheBlankToken>();
            if (token != null)
                token.Setup(shuffled[i], this);
        }

        selectedToken = null;
        Debug.Log("[FillInTheBlankUIController] UI populated");
    }

    public void OnTokenSelected(FillInTheBlankToken token)
    {
        if (selectedToken != null)
            selectedToken.SetState_Default();

        selectedToken = token;
        token.SetState_Selected();

        if (codeDisplayText != null)
            codeDisplayText.text = codeDisplayText.text.Replace("____", token.tokenText);

        Debug.Log($"[FillInTheBlankUIController] Token selected: {token.tokenText}");

        PuzzleManager.Instance.UserSubmission(token.tokenText);
    }

    public void OnTokenDeselected(FillInTheBlankToken token)
    {
        if (selectedToken == token)
        {
            selectedToken = null;
            token.SetState_Default();

            if (codeDisplayText != null)
                codeDisplayText.text = codeDisplayText.text.Replace(token.tokenText, "____");

            Debug.Log($"[FillInTheBlankUIController] Token deselected: {token.tokenText}");
        }
    }

    private void ShuffleList(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}