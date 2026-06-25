using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PredictTheOutputUIController : MonoBehaviour
{
    [Header("Left - Code Display")]
    public Text codeDisplayText;

    [Header("Right - Draggable Options (exactly 3)")]
    public List<GameObject> optionCards;

    [Header("Drop Zone")]
    public DropSlot dropSlot;

    public void PopulateUI(string codeSnippet, List<string> options)
    {
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Deactivate all option cards first
        foreach (var card in optionCards)
            card.SetActive(false);

        // Shuffle options
        List<string> shuffled = new List<string>(options);
        ShuffleList(shuffled);

        // Populate exactly 3 cards
        for (int i = 0; i < shuffled.Count && i < optionCards.Count; i++)
        {
            optionCards[i].SetActive(true);

            Text label = optionCards[i].GetComponentInChildren<Text>();
            if (label != null)
                label.text = shuffled[i];

            DraggableOption draggable = optionCards[i].GetComponent<DraggableOption>();
            if (draggable != null)
                draggable.optionText = shuffled[i];
        }

        // Clear drop slot
        if (dropSlot != null)
            dropSlot.ClearSlot();

        Debug.Log("[PredictTheOutputUIController] UI populated");
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