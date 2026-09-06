using UnityEngine;

/// <summary>
/// Per-scene music hook — this is how you give ANY scene its own music.
///
/// SETUP (per scene):
///   1. Create an empty GameObject in the scene (e.g. "SceneMusic").
///   2. Add this component.
///   3. Drag that scene's music AudioClip into the "Track" field.
///
/// That's it. On scene Start() it tells the global MusicManager to play
/// the track — the manager crossfades automatically, so:
///   - Scenes with no SceneMusic keep whatever was playing.
///   - Two scenes sharing the same AudioClip transition seamlessly.
///   - Two scenes with different tracks get a smooth crossfade.
///
/// Note: the component itself holds no AudioSource — everything routes
/// through the persistent MusicManager, so music never restarts or
/// double-plays when scenes load.
/// </summary>
public class SceneMusic : MonoBehaviour
{
    [Header("Scene Track")]
    [Tooltip("Music clip for this scene. Leave empty to keep whatever is currently playing.")]
    public AudioClip track;

    [Tooltip("Crossfade time into this track.")]
    [Range(0f, 10f)] public float fadeIn = 1.5f;

    [Tooltip("If true and no Track is assigned, fade the current music out when this scene starts.")]
    public bool stopMusicIfNoTrack = false;

    private void Start()
    {
        if (track != null)
        {
            MusicManager.Instance.PlayTrack(track, fadeIn);
        }
        else if (stopMusicIfNoTrack)
        {
            MusicManager.Instance.StopMusic(fadeIn);
        }
    }
}
