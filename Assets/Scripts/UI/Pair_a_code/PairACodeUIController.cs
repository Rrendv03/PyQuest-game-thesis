using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PairACodeUIController : MonoBehaviour
{
    [Header("Left Column")]
    public Text codeDisplayText;
    public DropSlot dropSlot;

    [Header("Right Column - Option Cards")]
    public List<GameObject> optionCards;

    private void Start()
    {
    }

    /// <summary>
    /// Called by PairACodePuzzleFormat to populate the UI.
    /// codeSnippet: full code with blank line replaced by [ ? ]
    /// options: list of option strings (correct + distractors)
    /// </summary>
    public void PopulateUI(string codeSnippet, List<string> options)
    {
        // Display code snippet on left
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Clear all option cards first
        foreach (var card in optionCards)
            card.SetActive(false);

        // Populate option cards with shuffled options
        List<string> shuffled = new List<string>(options);
        ShuffleList(shuffled);

        for (int i = 0; i < shuffled.Count && i < optionCards.Count; i++)
        {
            optionCards[i].SetActive(true);

            // Set option text
            Text label = optionCards[i].GetComponentInChildren<Text>();
            if (label != null)
                label.text = shuffled[i];

            // Set draggable option text
            DraggableOption draggable = optionCards[i].GetComponent<DraggableOption>();
            if (draggable != null)
                draggable.optionText = shuffled[i];
        }

        // Clear the drop slot
        if (dropSlot != null)
            dropSlot.ClearSlot();

        Debug.Log("[PairACodeUIController] UI populated");
    }

    private void OnSubmit()
    {
        if (dropSlot == null || string.IsNullOrEmpty(dropSlot.currentAnswer))
        {
            Debug.LogWarning("[PairACodeUIController] No answer dropped yet");
            return;
        }

        Debug.Log($"[PairACodeUIController] Submitting answer: {dropSlot.currentAnswer}");
        PuzzleManager.Instance.UserSubmission(dropSlot.currentAnswer);
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