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

    [Header("Portal Sounds")]
    [Tooltip("Plays when Glyph reaches into the rift (case 3), and again at the final line (case 23).")]
    public AudioClip portalSound;
    public float portalVolume = 1f;

    [Header("End-of-Sequence Fade (device-safe)")]
    [Tooltip("If true, the controller creates its own topmost fade canvas instead of using DialogueManager.fadeOverlay.")]
    public bool useOwnedFadeOverlay = true;

    private Coroutine bgFadeCoroutine;
    private Canvas loadingCanvas;
    private Canvas endFadeCanvas;
    private Image endFadeImage;
    private AudioSource audioSource;

    private void Awake()
    {
        // Build a topmost black overlay BEFORE the first frame renders,
        // so nothing in the scene is ever visible while loading.
        CreateLoadingBlackScreen();

        // Audio source for portal sounds
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void CreateLoadingBlackScreen()
    {
        GameObject go = new GameObject("LoadingBlackScreen");
        loadingCanvas = go.AddComponent<Canvas>();
        loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        loadingCanvas.sortingOrder = 32767; // above every other canvas

        GameObject img = new GameObject("BlackImage");
        img.transform.SetParent(go.transform, false);
        Image black = img.AddComponent<Image>();
        black.color = Color.black;

        // Stretch to fill the whole screen
        RectTransform rt = black.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // OPTIONAL: survive into MainMap so the next scene is also covered
        // until you hide it there. Uncomment ONLY if you also add the
        // cleanup code in MainMap (see notes below the script).
        // DontDestroyOnLoad(go);

        // --- Device-safe end-of-sequence fade overlay ---
        // Topmost, fully transparent until EndSequence activates it.
        GameObject fgo = new GameObject("EndFadeCanvas");
        endFadeCanvas = fgo.AddComponent<Canvas>();
        endFadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        endFadeCanvas.sortingOrder = 32766; // just under the loading screen

        GameObject fimg = new GameObject("FadeImage");
        fimg.transform.SetParent(fgo.transform, false);
        endFadeImage = fimg.AddComponent<Image>();
        endFadeImage.color = new Color(0f, 0f, 0f, 0f); // fully transparent

        RectTransform frt = endFadeImage.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;

        fgo.SetActive(false); // hidden until the end sequence needs it
    }

    private void Start()
    {
        SaveRestrictionEnforcer.Instance?.AddBlocker("prologue");

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
        // Indices match the array order in dialogue.json for "intro_prologue"
        // (24 lines, indices 0-23). If you add/remove lines from the sequence,
        // these cases MUST be updated to match.
        switch (lineIndex)
        {
            case 0: // "On a quiet, rainy night."
                // The cinematic dialogue has started: the loading black screen
                // has served its purpose, so remove it.
                if (loadingCanvas != null)
                    Destroy(loadingCanvas.gameObject);
                break;

            case 1: // "Glyph sits at his desk seeking inspiration..."
                // Fade out black screen, show the room group
                StartBGFade(0f);
                SetMeshes(room: true, dimension: false, pyth: false);
                break;

            case 3: // "Startled, Glyph drops his pen and instinctively reaches out into the yielding darkness."
                // Portal opens: play the sound and pull Glyph through
                PlayPortalSound();
                SetMeshes(room: false, dimension: true, pyth: true);
                break;

            case 22: // Glyph: "Wait-"
                // Pythariel is fading: fade the background back to black
                StartBGFade(1f);
                SetMeshes(room: false, dimension: true, pyth: true);
                break;

            case 23: // Pythariel: "Don't fear getting it wrong..." (final line)
                // Back to cinematic mode. Hide everything to match the text,
                // and close with a portal sound.
                SetMeshes(room: false, dimension: true, pyth: true);
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
        // (Note: Because she is disabled at line 23, this will safely skip itself).
        if (pytharielRenderer != null && pytharielRenderer.enabled)
            yield return StartCoroutine(FadePythariel());

        yield return new WaitForSeconds(0.5f);

        if (showcasePoints != null && showcasePoints.Length > 0 && introCamera != null)
            yield return StartCoroutine(RunCameraShowcase());

        if (useOwnedFadeOverlay && endFadeImage != null)
        {
            // Activate the topmost overlay FIRST so it covers the despawn,
            // then fade in over it. unscaledDeltaTime: immune to Time.timeScale
            // and mobile frame pacing.
            endFadeCanvas.gameObject.SetActive(true);
            Color c = endFadeImage.color;
            float elapsed = 0f;
            while (elapsed < fadeToBlackDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(elapsed / fadeToBlackDuration);
                endFadeImage.color = c;
                yield return null;
            }
            c.a = 1f;
            endFadeImage.color = c;
        }
        else if (DialogueManager.Instance != null && DialogueManager.Instance.fadeOverlay != null)
        {
            // Editor fallback (old behavior)
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

        // Hold black for a beat so the scene load itself is fully hidden
        yield return new WaitForSecondsRealtime(0.25f);
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

    private void PlayPortalSound()
    {
        if (portalSound == null || audioSource == null) return;
        audioSource.PlayOneShot(portalSound, portalVolume);
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
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("prologue");
        Debug.Log($"[IntroSequenceController] Loading scene: {mainMapSceneName}");
        SceneManager.LoadScene(mainMapSceneName);
    }
}
