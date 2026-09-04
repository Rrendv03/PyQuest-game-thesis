using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the settings panel (or the slider itself) in EITHER
/// MainMenuController.settingsPanel or PauseMenuManager.settingsPanel.
/// Both use the same underlying AudioSettingsManager singleton, so the
/// slider reads/writes the same global volume no matter which panel
/// it's on.
///
/// Re-syncs the slider's displayed value in OnEnable(), since both
/// settings panels are toggled active/inactive rather than
/// instantiated fresh each time, so Start() alone would only run once
/// and the slider could drift out of sync with the real value after
/// the panel is reopened.
/// </summary>
public class AudioSettingsUI : MonoBehaviour
{
    public Slider volumeSlider;

    private bool isSyncing = false;

    void OnEnable()
    {
        SyncSliderToCurrentVolume();
        if (volumeSlider != null)
            volumeSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    void OnDisable()
    {
        if (volumeSlider != null)
            volumeSlider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void SyncSliderToCurrentVolume()
    {
        if (volumeSlider == null || AudioSettingsManager.Instance == null) return;

        // isSyncing guards against this programmatic .value set
        // re-triggering OnSliderChanged and writing the value right
        // back to PlayerPrefs on every panel open.
        isSyncing = true;
        volumeSlider.value = AudioSettingsManager.Instance.GetMasterVolume();
        isSyncing = false;
    }

    private void OnSliderChanged(float value)
    {
        if (isSyncing) return;
        AudioSettingsManager.Instance?.SetMasterVolume(value);
    }
}