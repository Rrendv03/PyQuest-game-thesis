using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Place one instance in every scene a player can arrive into
/// (MainMap, Room, any Sanctum scene). Do NOT place in IntroScene.
///
/// Awake() covers the screen immediately. By the time Start() runs,
/// PlayerMovement.Awake() has already placed the player at RespawnPoint,
/// so the yield return null is no longer needed and is removed.
/// The fade-out in Start() reveals an already-correctly-placed player.
/// </summary>
public class SceneEntrance : MonoBehaviour
{
    public Image fadeOverlay;
    public float fadeDuration = 0.5f;

    void Awake()
    {
        if (fadeOverlay == null) return;

        fadeOverlay.gameObject.SetActive(true);
        Color c = fadeOverlay.color;
        c.a = 1f;
        fadeOverlay.color = c;
    }

    void Start()
    {
        if (fadeOverlay == null) return;
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        Color c = fadeOverlay.color;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Clamp01(1f - (elapsed / fadeDuration));
            fadeOverlay.color = c;
            yield return null;
        }

        c.a = 0f;
        fadeOverlay.color = c;
        fadeOverlay.gameObject.SetActive(false);
    }
}