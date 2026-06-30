using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LineScrambleUIController : MonoBehaviour
{
    [Header("Slot Container")]
    public RectTransform slotContainer;
    public List<GameObject> slotObjects;

    [Header("UI")]
    public Text instructionText;
    public Button checkButton;

    // Single source of truth: ordered list of slots as they currently
    // appear visually, top to bottom. Index in this list = visual position.
    private List<LineScrambleSlot> orderedSlots = new List<LineScrambleSlot>();

    public void PopulateUI(List<string> shuffledLines, List<int> shuffledRowNumbers)
    {
        orderedSlots = new List<LineScrambleSlot>();

        if (instructionText != null)
            instructionText.text = "Drag the lines to reorder them, then press Check.";

        foreach (var obj in slotObjects)
            obj.SetActive(false);

        for (int i = 0; i < shuffledLines.Count && i < slotObjects.Count; i++)
        {
            slotObjects[i].SetActive(true);
            slotObjects[i].transform.SetParent(slotContainer, false); // parenting happens once, here only
            slotObjects[i].transform.SetSiblingIndex(i);


            LineScrambleSlot slot = slotObjects[i].GetComponent<LineScrambleSlot>();
            if (slot != null)
            {
                slot.Setup(shuffledRowNumbers[i], shuffledLines[i], this);
                orderedSlots.Add(slot);
            }
        }

        AdjustSpacing(shuffledLines.Count);
        RefreshLayoutPositions();

        if (checkButton != null)
        {
            checkButton.onClick.RemoveAllListeners();
            checkButton.onClick.AddListener(OnCheckPressed);
        }

        Debug.Log($"[LineScrambleUIController] Populated {shuffledLines.Count} lines | " +
                  $"Order: {string.Join(", ", GetCurrentRowNumbers())}");
    }

    /// <summary>
    /// Swaps two slots' positions in the orderedSlots list, then
    /// repositions every slot's transform to match the list order.
    /// This is the ONLY place that changes visual order, no sibling
    /// index tricks, no reparenting during the swap itself.
    /// </summary>
    public void SwapByReference(LineScrambleSlot a, LineScrambleSlot b)
    {
        int idxA = orderedSlots.IndexOf(a);
        int idxB = orderedSlots.IndexOf(b);

        if (idxA < 0 || idxB < 0)
        {
            Debug.LogError($"[LineScrambleUIController] Swap failed, slot not found in orderedSlots. idxA={idxA}, idxB={idxB}");
            return;
        }

        orderedSlots[idxA] = b;
        orderedSlots[idxB] = a;

        RefreshLayoutPositions();

        Debug.Log($"[LineScrambleUIController] Swapped positions {idxA} and {idxB} | " +
                  $"New order: {string.Join(", ", GetCurrentRowNumbers())}");
    }

    /// <summary>
    /// Repositions every slot's transform.SetSiblingIndex to match
    /// its index in orderedSlots. This runs after every swap and is
    /// the single point of truth for translating logical order into
    /// visual order. All slots share the same parent (slotContainer)
    /// at all times, they are never reparented during drag/drop.
    /// </summary>
    private void RefreshLayoutPositions()
    {
        for (int i = 0; i < orderedSlots.Count; i++)
        {
            // Only reorder sibling index, never reparent.
            // All slots are already permanent children of slotContainer.
            orderedSlots[i].transform.SetSiblingIndex(i);
        }
    }

    private List<int> GetCurrentRowNumbers()
    {
        List<int> result = new List<int>();
        foreach (var slot in orderedSlots)
            result.Add(slot.originalRowNumber);
        return result;
    }

    private void OnCheckPressed()
    {
        bool isCorrect = true;

        for (int i = 0; i < orderedSlots.Count; i++)
        {
            if (orderedSlots[i].originalRowNumber != i)
            {
                isCorrect = false;
                break;
            }
        }

        Debug.Log($"[LineScrambleUIController] Check pressed | Correct: {isCorrect} | " +
                  $"Current order: {string.Join(", ", GetCurrentRowNumbers())}");

        if (isCorrect)
            foreach (var slot in orderedSlots)
                slot.SetState_Correct();

        PuzzleManager.Instance.UserSubmission(isCorrect);
    }

    private void AdjustSpacing(int count)
    {
        if (slotContainer == null) return;

        VerticalLayoutGroup layout = slotContainer.GetComponent<VerticalLayoutGroup>();
        if (layout == null) return;

        layout.spacing = GetSpacingForCount(count);
    }

    private float GetSpacingForCount(int count)
    {
        switch (count)
        {
            case 2: return 40f;
            case 3: return 30f;
            case 4: return 20f;
            case 5: return 15f;
            case 6: return 10f;
            case 7: return 5f;
            default: return 20f;
        }
    }
}