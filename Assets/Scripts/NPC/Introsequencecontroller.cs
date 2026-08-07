using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Thin director for the prologue cutscene. Delegates all line sequencing,
/// typewriter text, and cinematic/standard swapping to DialogueManager.
/// Owns only what is unique to the intro: Pythariel's model fade, the
/// post-dialogue camera showcase, mesh/background choreography, and scene load.
/// </summary>
public class IntroSequenceController : MonoBehaviour
{
    [Header("Scene Loading")]
    public string mainMapSceneName = "MainMap";
    public string introSequenceID = "intro_prologue";

    [Header("Pythariel Fade (End of sequence)")]
    public Renderer pytharielRenderer;
    public float fadeDuration = 2.5f;

    [Header("Camera Showcase")]
    public Camera introCamera;
    public Transform[] showcasePoints;
    public float showcaseMoveSpeed = 2f;
    public float showcaseHoldTime = 1.2f;

    [Header("Final Fade to Main Map")]
    public float fadeToBlackDuration = 1.5f;

    [Header("Cinematic Background Control")]
    [Tooltip("Drag the solid black Image component that is a child of cinematicGroup here.")]
    public Image cinematicBlackBG;
    public float bgFadeDuration = 1.5f;

    [Header("3D Mesh Groups To Toggle")]
    [Tooltip("The parent GameObject of the room (includes the portal as part of the room).")]
    public GameObject roomMesh;
    [Tooltip("The parent GameObject of the inside dimension/space of the portal (the falling area).")]
    public GameObject insideDimensionMesh;
    [Tooltip("The parent GameObject of Pythariel's character model.")]
    public GameObject pytharielMesh;

    private Coroutine bgFadeCoroutine;

    private void Start()
    {
        // Ensure all mesh groups start hidden
        SetMeshes(false, false, false);

        // Ensure the cinematic background starts fully opaque (black)
        if (cinematicBlackBG != null)
        {
            Color c = cinematicBlackBG.color;
            c.a = 1f;
            cinematicBlackBG.color = c;
        }

        StartCoroutine(WaitForDialogueManagerThenPlay());
    }

    private IEnumerator WaitForDialogueManagerThenPlay()
    {
        while (DialogueManager.Instance == null || !DialogueManager.Instance.IsRegistryLoaded)
            yield return null;

        if (!DialogueManager.Instance.HasSequence(introSequenceID))
        {
            Debug.LogError($"[IntroSequenceController] Sequence '{introSequenceID}' not found.");
            LoadMainMap();
            yield break;
        }

        DialogueManager.Instance.OnLineChanged += HandleLineChanged;
        DialogueManager.Instance.OnSequenceComplete += HandleIntroComplete;
        DialogueManager.Instance.Play(introSequenceID);
    }

    private void HandleLineChanged(DialogueLine line, int lineIndex)
    {
        // Choreograph the 3D mesh groups and the black background image.
        // Indices match the array order in dialogue.json (starting at 0).
        switch (lineIndex)
        {
            case 3: // "Nothing about this night suggests..."
                // Fade out black screen, show the room group
                StartBGFade(0f);
                SetMeshes(room: true, dimension: false, pyth: false);
                break;

            case 6: // "There is no tunnel. No countdown..."
                // Hide room group, show the inside dimension group (falling through script)
                SetMeshes(room: false, dimension: true, pyth: false);
                break;

            case 10: // "She is kneeling at the edge of the portal..."
                // Show Pythariel inside the dimension
                SetMeshes(room: false, dimension: true, pyth: true);
                break;

            case 12: // "But her eyes, when they find Glyph, are completely clear."
                // Fade the background BACK to black before the UI switches
                StartBGFade(1f);
                break;

            case 13: // "You came through. Good." (Standard dialogue starts)
                // FIXED: Keep the dimension visible in the background! Only the room is false.
                SetMeshes(room: false, dimension: true, pyth: true);
                break;

            case 48: // "And then, like an inscription... she is gone."
                // Back to cinematic mode. NOW we hide everything to match the text.
                SetMeshes(false, false, false);
                if (cinematicBlackBG != null)
                {
                    Color c = cinematicBlackBG.color;
                    c.a = 1f;
                    cinematicBlackBG.color = c;
                }
                break;
        }
    }

    private void HandleIntroComplete(DialogueSequence finished)
    {
        DialogueManager.Instance.OnSequenceComplete -= HandleIntroComplete;
        DialogueManager.Instance.OnLineChanged -= HandleLineChanged;

        if (!string.IsNullOrEmpty(finished.questIDToComplete) && StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.CompleteQuest(finished.questIDToComplete);

        StartCoroutine(EndSequence());
    }

    private IEnumerator EndSequence()
    {
        // If pythariel's specific renderer is still somehow visible, fade her out.
        // (Note: Because she is disabled at line 48, this will safely skip itself).
        if (pytharielRenderer != null && pytharielRenderer.enabled)
            yield return StartCoroutine(FadePythariel());

        yield return new WaitForSeconds(0.5f);

        if (showcasePoints != null && showcasePoints.Length > 0 && introCamera != null)
            yield return StartCoroutine(RunCameraShowcase());

        if (DialogueManager.Instance != null && DialogueManager.Instance.fadeOverlay != null)
        {
            Image overlay = DialogueManager.Instance.fadeOverlay;
            float elapsed = 0f;
            while (elapsed < fadeToBlackDuration)
            {
                elapsed += Time.deltaTime;
                Color c = overlay.color;
                c.a = Mathf.Lerp(0f, 1f, elapsed / fadeToBlackDuration);
                overlay.color = c;
                yield return null;
            }
        }
        LoadMainMap();
    }

    // --- Helper Methods ---

    private void SetMeshes(bool room, bool dimension, bool pyth)
    {
        if (roomMesh != null) roomMesh.SetActive(room);
        if (insideDimensionMesh != null) insideDimensionMesh.SetActive(dimension);
        if (pytharielMesh != null) pytharielMesh.SetActive(pyth);
    }

    private void StartBGFade(float targetAlpha)
    {
        if (cinematicBlackBG == null) return;

        // Stop any existing fade so skipping dialogue doesn't cause flickering
        if (bgFadeCoroutine != null)
            StopCoroutine(bgFadeCoroutine);

        bgFadeCoroutine = StartCoroutine(FadeCinematicBG(targetAlpha));
    }

    private IEnumerator FadeCinematicBG(float targetAlpha)
    {
        float startAlpha = cinematicBlackBG.color.a;
        float elapsed = 0f;

        while (elapsed < bgFadeDuration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, targetAlpha, elapsed / bgFadeDuration);
            Color c = cinematicBlackBG.color;
            c.a = a;
            cinematicBlackBG.color = c;
            yield return null;
        }

        // Ensure it hits exact target
        Color final = cinematicBlackBG.color;
        final.a = targetAlpha;
        cinematicBlackBG.color = final;
    }
    // ----------------------

    private IEnumerator FadePythariel()
    {
        Material mat = pytharielRenderer.material;
        Color startColor = mat.color;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            mat.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }

        pytharielRenderer.gameObject.SetActive(false);
    }

    private IEnumerator RunCameraShowcase()
    {
        foreach (Transform point in showcasePoints)
        {
            if (point == null) continue;

            Vector3 startPos = introCamera.transform.position;
            Quaternion startRot = introCamera.transform.rotation;
            float elapsed = 0f;
            float moveDuration = Vector3.Distance(startPos, point.position) / showcaseMoveSpeed;
            moveDuration = Mathf.Clamp(moveDuration, 0.5f, 4f);

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / moveDuration);
                introCamera.transform.position = Vector3.Lerp(startPos, point.position, t);
                introCamera.transform.rotation = Quaternion.Slerp(startRot, point.rotation, t);
                yield return null;
            }

            introCamera.transform.position = point.position;
            introCamera.transform.rotation = point.rotation;

            yield return new WaitForSeconds(showcaseHoldTime);
        }
    }

    private void LoadMainMap()
    {
        Debug.Log($"[IntroSequenceController] Loading scene: {mainMapSceneName}");
        SceneManager.LoadScene(mainMapSceneName);
    }
}