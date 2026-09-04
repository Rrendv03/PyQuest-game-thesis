using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent top-left HUD panel showing the active quest name.
/// Subscribes to QuestManager.OnQuestUpdated and refreshes the label.
/// Place on a GameObject in HUDCanvas alongside your movement buttons.
/// </summary>
public class QuestHUDDisplay : MonoBehaviour
{
    [Header("UI")]
    public Text questNameText;
    public GameObject questPanel;

    void Start()
    {
        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestUpdated += UpdateDisplay;

        // Show current quest immediately on scene load
        RefreshDisplay();
    }

    void OnDestroy()
    {
        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestUpdated -= UpdateDisplay;
    }

    private void UpdateDisplay(string displayName)
    {
        if (questNameText != null)
            questNameText.text = displayName;

        if (questPanel != null)
            questPanel.SetActive(!string.IsNullOrEmpty(displayName));
    }

    private void RefreshDisplay()
    {
        if (QuestManager.Instance == null) return;
        UpdateDisplay(QuestManager.Instance.GetActiveQuestDisplayName());
    }
}