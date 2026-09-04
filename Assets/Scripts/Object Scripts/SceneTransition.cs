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
///
/// Added: registers a "scene_transition" save blocker the moment a
/// transition starts, so autosave can't fire mid-load. The blocker is
/// cleared by SaveRestrictionEnforcer itself on the next scene load
/// (see SaveRestrictionManager.cs), not from here, since this component
/// and GameObject are destroyed as part of the scene unload and can't be
/// relied on to run cleanup code afterward.
/// </summary>
public class SceneTransition : MonoBehaviour
{
    public string sceneToLoad;
    public Vector3 spawnPosition;
    public Image fadeOverlay;
    public float fadeDuration = 0.5f;
    public static Vector3 RespawnPoint;
    public static float? RespawnYRotation = null;
    public static bool SkipSpawnPositioning = false;
    private bool isTransitioning = false;
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || isTransitioning) return;
        isTransitioning = true;
        SaveRestrictionEnforcer.Instance?.AddBlocker("scene_transition");
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