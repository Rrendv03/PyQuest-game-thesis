using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Attach to any zone trigger collider in the scene.
/// When the player enters, fades to black then loads the target scene.
/// spawnPosition is passed via the static RespawnPoint so PlayerMovement
/// can read it in Start() on the other side, which is the existing behavior
/// and does not change.
///
/// fadeOverlay must be a full-screen black Image on a Canvas that sits in
/// this scene. Assign it in the Inspector. If left null the scene loads
/// instantly with no fade, matching the old behavior.
/// </summary>
public class SceneTransition : MonoBehaviour
{
    public string sceneToLoad;
    public Vector3 spawnPosition;
    public Image fadeOverlay;
    public float fadeDuration = 0.5f;

    public static Vector3 RespawnPoint;

    private bool isTransitioning = false;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || isTransitioning) return;
        isTransitioning = true;
        RespawnPoint = spawnPosition;
        StartCoroutine(FadeAndLoad());
    }

    private IEnumerator FadeAndLoad()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            float elapsed = 0f;
            Color c = fadeOverlay.color;
            c.a = 0f;
            fadeOverlay.color = c;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                c.a = Mathf.Clamp01(elapsed / fadeDuration);
                fadeOverlay.color = c;
                yield return null;
            }

            c.a = 1f;
            fadeOverlay.color = c;
        }

        SceneManager.LoadScene(sceneToLoad);
    }
}