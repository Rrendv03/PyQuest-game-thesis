using UnityEngine;

/// <summary>
/// Persistent, DontDestroyOnLoad singleton that owns the game's global
/// audio volume. Uses AudioListener.volume, a built-in Unity multiplier
/// that scales every AudioSource in every scene, so no AudioMixer asset
/// or per-scene wiring is required.
///
/// Place ONE instance of this in whichever scene loads first (MainMenu,
/// most likely, matching how your other persistent managers are set up).
/// It applies the saved volume immediately on Awake, before any other
/// scene's audio can play, and survives every scene load afterward.
///
/// Volume is saved to PlayerPrefs, NOT to the save-slot JSON files. This
/// is a player/device preference (like a settings menu option), not
/// game progress, so it should persist independently of which save slot
/// is loaded and should NOT reset on New Game.
/// </summary>
public class AudioSettingsManager : MonoBehaviour
{
    public static AudioSettingsManager Instance;

    private const string VolumePrefKey = "MasterVolume";
    private const float DefaultVolume = 1f;

    private float currentVolume;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        currentVolume = PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume);
        ApplyVolume();
    }

    /// <summary>
    /// Called by AudioSettingsUI whenever the slider moves. Applies
    /// immediately and saves immediately, no separate "confirm" step,
    /// matching how the rest of this project's settings behave.
    /// </summary>
    public void SetMasterVolume(float value)
    {
        currentVolume = Mathf.Clamp01(value);
        ApplyVolume();
        PlayerPrefs.SetFloat(VolumePrefKey, currentVolume);
        PlayerPrefs.Save();
    }

    public float GetMasterVolume() => currentVolume;

    private void ApplyVolume()
    {
        AudioListener.volume = currentVolume;
    }
}