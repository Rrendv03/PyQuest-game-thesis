using System.Collections;
using UnityEngine;

/// <summary>
/// Persistent music player. Auto-creates itself the first time anything
/// accesses MusicManager.Instance, so no scene setup is required to get
/// music playing.
///
/// HOW IT WORKS
///   - Lives on a DontDestroyOnLoad GameObject, so music keeps playing
///     seamlessly across scene loads (main menu -> gameplay, etc.).
///   - Owns TWO AudioSources so it can crossfade: the old track fades out
///     while the new one fades in. No hard cuts.
///   - Scenes request music via SceneMusic (per-scene component) or by
///     calling MusicManager.Instance.PlayTrack(clip, fadeIn) directly.
///
/// SETTINGS INTEGRATION (AudioSettingsManager)
///   Music volume is a plain 0..1 float on the AudioSource for now.
///   When AudioSettingsManager is re-attached, wire it up in two places:
///     1. In Awake(): assign the mixer Music group to both sources'
///        outputAudioMixerGroup (see TODO below).
///     2. Wherever settings are applied: call MusicManager.Instance
///        .SetVolume(sliderValue) so the manager follows the settings UI.
/// </summary>
public class MusicManager : MonoBehaviour
{
    private static MusicManager _instance;

    /// <summary>
    /// Lazily creates the manager if no instance exists in any loaded scene,
    /// so scripts can start music without any scene setup.
    /// </summary>
    public static MusicManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GameObject("MusicManager").AddComponent<MusicManager>();
            }
            return _instance;
        }
    }

    [Header("Volume")]
    [Range(0f, 1f)] public float musicVolume = 1f;

    [Header("Crossfade")]
    [Tooltip("Default fade length when a caller doesn't specify one.")]
    [Range(0f, 10f)] public float defaultFadeTime = 1.5f;

    private AudioSource _sourceA;
    private AudioSource _sourceB;
    private AudioSource _active;
    private Coroutine _fadeRoutine;

    private void Awake()
    {
        // A duplicate (e.g. one placed in a scene PLUS an auto-created one)
        // destroys itself and defers to the existing instance.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        _sourceA = gameObject.AddComponent<AudioSource>();
        _sourceB = gameObject.AddComponent<AudioSource>();

        foreach (var src in new[] { _sourceA, _sourceB })
        {
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            // TODO(AudioSettingsManager): once the settings scripts are
            // re-attached, set src.outputAudioMixerGroup to the Music/Audio
            // group exposed by AudioSettingsManager so volume sliders,
            // mute-on-focus, etc. apply here too.
        }

        _active = _sourceA;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>
    /// Plays (or crossfades to) the given track. Safe to call repeatedly:
    /// requesting the track that is already playing does nothing.
    /// Pass fadeIn < 0 to use defaultFadeTime.
    /// </summary>
    public void PlayTrack(AudioClip clip, float fadeIn = -1f)
    {
        if (clip == null) return;
        if (fadeIn < 0f) fadeIn = defaultFadeTime;

        // Already playing this exact track? Leave it alone (keeps position).
        if (_active != null && _active.clip == clip && _active.isPlaying) return;

        AudioSource from = _active;
        AudioSource to = (from == _sourceA) ? _sourceB : _sourceA;
        _active = to;

        to.clip = clip;
        to.volume = 0f;
        to.Play();

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(Crossfade(from, to, fadeIn));
    }

    /// <summary>
    /// Fades the current track out and stops it. Safe to call when nothing
    /// is playing.
    /// </summary>
    public void StopMusic(float fadeOut = -1f)
    {
        if (fadeOut < 0f) fadeOut = defaultFadeTime;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        if (_active == null || !_active.isPlaying) return;

        _fadeRoutine = StartCoroutine(FadeOutOnly(_active, fadeOut));
        _active = (_active == _sourceA) ? _sourceB : _sourceA;
    }

    /// <summary>
    /// Hook point for AudioSettingsManager: call this from the settings UI
    /// when the music slider changes.
    /// </summary>
    public void SetVolume(float normalizedVolume)
    {
        musicVolume = Mathf.Clamp01(normalizedVolume);
        if (_active != null && _fadeRoutine == null)
        {
            _active.volume = musicVolume;
        }
    }

    private IEnumerator Crossfade(AudioSource from, AudioSource to, float fade)
    {
        float startFromVolume = from != null ? from.volume : 0f;
        float t = 0f;

        while (t < fade)
        {
            // Unscaled time so fades still complete while the game is
            // paused (Time.timeScale == 0), e.g. in a pause menu.
            t += Time.unscaledDeltaTime;
            float k = fade <= 0f ? 1f : Mathf.Clamp01(t / fade);

            if (from != null && from != to)
                from.volume = Mathf.Lerp(startFromVolume, 0f, k);
            to.volume = Mathf.Lerp(0f, musicVolume, k);
            yield return null;
        }

        if (from != null && from != to) from.Stop();
        to.volume = musicVolume;
        _fadeRoutine = null;
    }

    private IEnumerator FadeOutOnly(AudioSource source, float fade)
    {
        float startVolume = source.volume;
        float t = 0f;

        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            float k = fade <= 0f ? 1f : Mathf.Clamp01(t / fade);
            source.volume = Mathf.Lerp(startVolume, 0f, k);
            yield return null;
        }

        source.Stop();
        source.volume = musicVolume;
        _fadeRoutine = null;
    }
}
