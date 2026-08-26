using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Save slot selection screen.
/// Shows three manual slots (1-3) plus one autosave slot (0).
/// Each slot panel displays: slot number, mission name, location,
/// playtime, date saved, and Save/Load/Delete buttons.
/// Wire one SlotPanel entry per slot in the Inspector.
/// This panel is opened via the static Open() method, which automatically
/// handles the Back button navigation to whatever panel called it.
/// </summary>
public class SaveSlotUI : MonoBehaviour
{
    [System.Serializable]
    public class SlotPanel
    {
        public int slotNumber;            // 1, 2, 3, or 0 for autosave
        public GameObject panel;
        public Text slotLabel;
        public Text missionText;
        public Text locationText;
        public Text playtimeText;
        public Text dateSavedText;
        public Button saveButton;         // null on autosave slot (autosave is automatic)
        public Button loadButton;
        public Button deleteButton;
    }

    [Header("Slot Panels")]
    public SlotPanel[] slots;             // assign 4 entries: slots 1, 2, 3, autosave (0)

    [Header("Navigation")]
    [Tooltip("Drag your Back Button here. Its OnClick is wired automatically in code.")]
    public Button backButton;

    [Header("Confirm Panel")]
    public GameObject confirmPanel;
    public Text confirmMessageText;
    public Button confirmYesButton;
    public Button confirmNoButton;

    private int pendingSlot = -1;
    private enum PendingAction { None, Save, Load, Delete }
    private PendingAction pendingAction = PendingAction.None;

    // Stores the instruction on how to go back
    private Action onBackCallback;

    void OnEnable()
    {
        RefreshAllSlots();
        WireBackButton();
    }

    // -------------------------------------------------------------------
    // AUTOMATIC OPEN/CLOSE SYSTEM
    // Other menus call this to open the SaveLoad screen.
    // -------------------------------------------------------------------

    /// <summary>
    /// Call this from MainMenu or PauseMenu to open the screen.
    /// Example: SaveSlotUI.Open(mySaveLoadPanel, () => myMainMenuPanel.SetActive(true));
    /// </summary>
    public static void Open(GameObject saveLoadPanelObject, Action backAction)
    {
        if (saveLoadPanelObject == null) return;

        SaveSlotUI ui = saveLoadPanelObject.GetComponent<SaveSlotUI>();
        if (ui != null)
        {
            ui.onBackCallback = backAction;
            saveLoadPanelObject.SetActive(true);
        }
        else
        {
            Debug.LogError("[SaveSlotUI] Open() called on an object without a SaveSlotUI component!");
        }
    }

    private void WireBackButton()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        else
        {
            Debug.LogWarning("[SaveSlotUI] Back button is not assigned in the inspector!");
        }
    }

    private void OnBackButtonClicked()
    {
        // Hide this save/load screen
        gameObject.SetActive(false);

        // Execute whatever instruction was given by the menu that opened this
        onBackCallback?.Invoke();

        // Clear the callback to free up memory
        onBackCallback = null;
    }

    public void RefreshAllSlots()
    {
        if (slots == null) return;

        foreach (var slot in slots)
        {
            RefreshSlot(slot);
        }
    }

    private void RefreshSlot(SlotPanel slot)
    {
        if (slot == null || slot.panel == null) return;

        bool exists = SaveLoadManager.Instance != null
                   && SaveLoadManager.Instance.SlotExists(slot.slotNumber);

        if (slot.slotLabel != null)
            slot.slotLabel.text = slot.slotNumber == 0 ? "Autosave" : $"Slot {slot.slotNumber}";

        if (exists)
        {
            SaveSlotData data = SaveLoadManager.Instance.LoadFromSlot(slot.slotNumber);

            if (data != null)
            {
                if (slot.missionText != null)
                    slot.missionText.text = data.missionDisplayName;

                if (slot.locationText != null)
                    slot.locationText.text = data.locationDisplayName;

                if (slot.playtimeText != null)
                    slot.playtimeText.text = FormatPlaytime(data.playTimeSeconds);

                if (slot.dateSavedText != null)
                    slot.dateSavedText.text = data.dateSavedISO;
            }

            if (slot.loadButton != null) slot.loadButton.interactable = true;
            if (slot.deleteButton != null) slot.deleteButton.interactable = true;
        }
        else
        {
            if (slot.missionText != null) slot.missionText.text = "Empty";
            if (slot.locationText != null) slot.locationText.text = "";
            if (slot.playtimeText != null) slot.playtimeText.text = "";
            if (slot.dateSavedText != null) slot.dateSavedText.text = "";

            if (slot.loadButton != null) slot.loadButton.interactable = false;
            if (slot.deleteButton != null) slot.deleteButton.interactable = false;
        }

        // Autosave slot has no manual Save button
        if (slot.saveButton != null)
        {
            slot.saveButton.interactable = slot.slotNumber != 0;
            slot.saveButton.onClick.RemoveAllListeners();
            int capturedSlot = slot.slotNumber;
            slot.saveButton.onClick.AddListener(() => RequestSave(capturedSlot));
        }

        if (slot.loadButton != null)
        {
            slot.loadButton.onClick.RemoveAllListeners();
            int capturedSlot = slot.slotNumber;
            slot.loadButton.onClick.AddListener(() => RequestLoad(capturedSlot));
        }

        if (slot.deleteButton != null)
        {
            slot.deleteButton.onClick.RemoveAllListeners();
            int capturedSlot = slot.slotNumber;
            slot.deleteButton.onClick.AddListener(() => RequestDelete(capturedSlot));
        }
    }

    // ?? Request Handlers ?????????????????????????????????????????????????????
    private void RequestSave(int slot)
    {
        pendingSlot = slot;
        pendingAction = PendingAction.Save;

        bool exists = SaveLoadManager.Instance.SlotExists(slot);
        ShowConfirm(exists
            ? $"Overwrite Slot {slot}?"
            : $"Save to Slot {slot}?");
    }

    private void RequestLoad(int slot)
    {
        pendingSlot = slot;
        pendingAction = PendingAction.Load;
        string label = slot == 0 ? "Autosave" : $"Slot {slot}";
        ShowConfirm($"Load {label}? Unsaved progress will be lost.");
    }

    private void RequestDelete(int slot)
    {
        pendingSlot = slot;
        pendingAction = PendingAction.Delete;
        string label = slot == 0 ? "Autosave" : $"Slot {slot}";
        ShowConfirm($"Delete {label}? This cannot be undone.");
    }

    // ?? Confirm Panel ?????????????????????????????????????????????????????????
    private void ShowConfirm(string message)
    {
        if (confirmPanel == null) return;

        if (confirmMessageText != null)
            confirmMessageText.text = message;

        confirmPanel.SetActive(true);

        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.RemoveAllListeners();
            confirmYesButton.onClick.AddListener(OnConfirmYes);
        }

        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.RemoveAllListeners();
            confirmNoButton.onClick.AddListener(OnConfirmNo);
        }
    }

    private void OnConfirmYes()
    {
        if (confirmPanel != null) confirmPanel.SetActive(false);

        if (SaveLoadManager.Instance == null) return;

        switch (pendingAction)
        {
            case PendingAction.Save:
                SaveLoadManager.Instance.SaveToSlot(pendingSlot);
                RefreshAllSlots();
                break;

            case PendingAction.Load:
                SaveSlotData data = SaveLoadManager.Instance.LoadFromSlot(pendingSlot);
                if (data != null)
                    SaveLoadManager.Instance.ApplySaveData(data);
                else
                    Debug.LogWarning($"[SaveSlotUI] Load failed: slot {pendingSlot} returned null.");
                break;

            case PendingAction.Delete:
                SaveLoadManager.Instance.DeleteSlot(pendingSlot);
                RefreshAllSlots();
                break;
        }

        pendingSlot = -1;
        pendingAction = PendingAction.None;
    }

    private void OnConfirmNo()
    {
        if (confirmPanel != null) confirmPanel.SetActive(false);
        pendingSlot = -1;
        pendingAction = PendingAction.None;
    }

    // ?? Utility ???????????????????????????????????????????????????????????????
    private string FormatPlaytime(float seconds)
    {
        int h = Mathf.FloorToInt(seconds / 3600f);
        int m = Mathf.FloorToInt((seconds % 3600f) / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{h:00}:{m:00}:{s:00}";
    }
}