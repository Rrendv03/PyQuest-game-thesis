using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Global UI coordinator for non-HUD UI: screen fades and transient
/// notification toasts. HUDController remains separately responsible
/// for the mobile movement/pause HUD.
///
/// DontDestroyOnLoad singleton. Requires a Canvas with:
/// - fadeImage: a full-screen Image, alpha driven at runtime
/// - notificationRoot: a GameObject holding notificationText
/// - notificationText: a TextMeshPro (TMP_Text) component for toast messages
///
/// Scene-change resilience (no hierarchy changes required):
/// 1. Any running toast is killed the moment a scene loads, so its coroutine
///    can never touch objects that were destroyed with the previous scene
///    (this is what fixes the MissingReferenceException in NotificationRoutine).
/// 2. The names of the wired fadeImage / notificationRoot / notificationText
///    are remembered; after every scene load, any reference that died with the
///    old scene is re-bound to a same-named object in the new one, so a
///    per-scene UI canvas keeps working automatically.
/// 3. Anything still missing is replaced by a persistent runtime fallback
///    (full-screen fade Image + toast TextMeshPro text on a private canvas
///    that is a child of this DontDestroyOnLoad object), so fades and notifications keep
///    working in every scene, including scenes with no wired UI at all.
/// 4. All coroutines re-validate their targets after every yield and bail out
///    safely instead of throwing MissingReferenceException.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Fade Overlay")]
    public Image fadeImage;

    [Header("Notification Toast")]
    public GameObject notificationRoot;
    public TMP_Text notificationText;

    private Coroutine notificationRoutine;

    // Names of the originally wired objects, used to re-bind after scene loads.
    private string fadeImageName;
    private string notificationRootName;
    private string notificationTextName;

    // Runtime-built fallback UI. Lives as a child of this DontDestroyOnLoad
    // object, so it survives scene changes exactly like the manager does.
    private Canvas fallbackCanvas;
    private Image fallbackFadeImage;
    private GameObject fallbackNotificationRoot;
    private TextMeshProUGUI fallbackNotificationText;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheWiredNames();
        EnsureUsableUI();

        SceneManager.sceneLoaded += OnSceneLoaded;

        if (notificationRoot != null)
            notificationRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    #region Scene-change resilience

    private void CacheWiredNames()
    {
        fadeImageName = fadeImage != null ? fadeImage.gameObject.name : null;
        notificationRootName = notificationRoot != null ? notificationRoot.gameObject.name : null;
        notificationTextName = notificationText != null ? notificationText.gameObject.name : null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A toast that started in the previous scene must never touch objects
        // that were destroyed with it. This prevents the
        // MissingReferenceException from NotificationRoutine.
        if (mode == LoadSceneMode.Single)
            KillNotification();

        // Re-bind any reference that died with the previous scene to a
        // same-named object in the newly loaded one.
        RebindDiedReferences(scene);

        // Whatever is still missing gets the persistent runtime fallback.
        EnsureUsableUI();

        if (notificationRoot != null)
            notificationRoot.SetActive(false);
    }

    private void KillNotification()
    {
        if (notificationRoutine != null)
        {
            StopCoroutine(notificationRoutine);
            notificationRoutine = null;
        }

        // Unity's fake-null check: true for destroyed objects too.
        if (notificationRoot != null)
            notificationRoot.SetActive(false);
    }

    private void RebindDiedReferences(Scene scene)
    {
        bool fadeDead = fadeImage == null;   // destroyed-with-scene or never wired
        bool rootDead = notificationRoot == null;
        bool textDead = notificationText == null;

        // Everything survived (e.g. children of this DontDestroyOnLoad object).
        if (!fadeDead && !rootDead && !textDead)
            return;

        if (fadeDead)
        {
            Image img = FindComponentInScene<Image>(scene, fadeImageName);
            if (img != null)
                fadeImage = img;
        }

        if (rootDead)
        {
            Transform t = FindInScene(scene, notificationRootName);
            if (t != null)
                notificationRoot = t.gameObject;
        }

        if (textDead)
        {
            TMP_Text txt = FindComponentInScene<TMP_Text>(scene, notificationTextName);
            if (txt != null)
                notificationText = txt;
        }
    }

    private T FindComponentInScene<T>(Scene scene, string objectName) where T : Component
    {
        Transform t = FindInScene(scene, objectName);
        return t != null ? t.GetComponent<T>() : null;
    }

    /// <summary>
    /// Depth-first search of the freshly loaded scene (includes inactive
    /// objects, which GameObject.Find would miss). Never returns objects that
    /// belong to another UIManager copy: scene-loaded duplicates destroy
    /// themselves at the end of the frame, so anything parented under them is
    /// about to die and must not be re-bound to.
    /// </summary>
    private Transform FindInScene(Scene scene, string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (IsForeignUIManagerRoot(root.transform))
                continue;

            Transform t = FindChildRecursive(root.transform, objectName);
            if (t != null)
                return t;
        }
        return null;
    }

    private Transform FindChildRecursive(Transform parent, string objectName)
    {
        if (parent.name == objectName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (IsForeignUIManagerRoot(child))
                continue;

            Transform t = FindChildRecursive(child, objectName);
            if (t != null)
                return t;
        }
        return null;
    }

    private bool IsForeignUIManagerRoot(Transform node)
    {
        return node != transform && node.GetComponent<UIManager>() != null;
    }

    /// <summary>
    /// Guarantees fadeImage / notificationRoot / notificationText are all live.
    /// Anything missing is swapped for the persistent runtime fallback UI.
    /// </summary>
    private void EnsureUsableUI()
    {
        // --- Fade overlay ---
        if (fadeImage == null)
        {
            if (fallbackFadeImage == null)
            {
                GameObject go = new GameObject("FadeImage (Runtime)");
                go.transform.SetParent(GetFallbackCanvas().transform, false);

                fallbackFadeImage = go.AddComponent<Image>();
                fallbackFadeImage.color = new Color(0f, 0f, 0f, 0f);
                fallbackFadeImage.raycastTarget = true; // blocks clicks while a fade is on screen

                RectTransform rt = fallbackFadeImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                go.SetActive(false);
            }

            fadeImage = fallbackFadeImage;
        }

        // --- Notification toast ---
        if (notificationRoot == null || notificationText == null)
        {
            if (fallbackNotificationRoot == null)
            {
                GameObject root = new GameObject("NotificationRoot (Runtime)");
                root.transform.SetParent(GetFallbackCanvas().transform, false);

                RectTransform rootRt = root.AddComponent<RectTransform>();
                rootRt.anchorMin = new Vector2(0.5f, 0.9f);
                rootRt.anchorMax = new Vector2(0.5f, 0.9f);
                rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.sizeDelta = new Vector2(820f, 90f);

                Image backdrop = root.AddComponent<Image>();
                backdrop.color = new Color(0f, 0f, 0f, 0.65f);
                backdrop.raycastTarget = false;

                GameObject textGo = new GameObject("NotificationText (Runtime)");
                textGo.transform.SetParent(root.transform, false);

                RectTransform textRt = textGo.AddComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(16f, 10f);
                textRt.offsetMax = new Vector2(-16f, -10f);

                fallbackNotificationText = textGo.AddComponent<TextMeshProUGUI>();
                fallbackNotificationText.alignment = TextAlignmentOptions.Center;
                fallbackNotificationText.color = Color.white;
                fallbackNotificationText.fontSize = 30f;
                fallbackNotificationText.raycastTarget = false;
                fallbackNotificationText.text = string.Empty;

                TMP_FontAsset font = LoadDefaultTmpFont();
                if (font != null)
                    fallbackNotificationText.font = font;

                fallbackNotificationRoot = root;
                root.SetActive(false);
            }

            if (notificationRoot == null)
                notificationRoot = fallbackNotificationRoot;

            if (notificationText == null)
                notificationText = fallbackNotificationText;
        }
    }

    private Canvas GetFallbackCanvas()
    {
        if (fallbackCanvas == null)
        {
            GameObject go = new GameObject("UIManagerRuntimeCanvas");
            go.transform.SetParent(transform, false); // DontDestroyOnLoad with us

            fallbackCanvas = go.AddComponent<Canvas>();
            fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fallbackCanvas.sortingOrder = 300; // above gameplay canvases

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
        }
        return fallbackCanvas;
    }

    private static TMP_FontAsset LoadDefaultTmpFont()
    {
        // The TMP Essentials default font asset (usually LiberationSans SDF).
        // Only relevant for the runtime fallback text; a scene-wired TMP text
        // already carries its own font. Guarded so a project that hasn't
        // imported TMP Essentials still runs (text just renders unstyled).
        try { return TMP_Settings.defaultFontAsset; }
        catch { return null; }
    }

    #endregion

    #region Fade

    public IEnumerator FadeToBlack(float duration)
    {
        yield return StartCoroutine(Fade(0f, 1f, duration));
    }

    public IEnumerator FadeFromBlack(float duration)
    {
        yield return StartCoroutine(Fade(1f, 0f, duration));
    }

    private IEnumerator Fade(float fromAlpha, float toAlpha, float duration)
    {
        // Capture once: a scene change may re-bind the fadeImage field mid-fade,
        // and a destroyed image must never be written to.
        Image img = fadeImage;

        if (img == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        img.gameObject.SetActive(true);

        float elapsed = 0f;
        Color c = img.color;
        c.a = fromAlpha;
        img.color = c;

        while (elapsed < duration)
        {
            yield return null;

            if (img == null)
                yield break; // destroyed mid-fade by a scene change

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            c.a = Mathf.Lerp(fromAlpha, toAlpha, t);
            img.color = c;
        }

        if (img == null)
            yield break;

        c.a = toAlpha;
        img.color = c;

        if (Mathf.Approximately(toAlpha, 0f))
            img.gameObject.SetActive(false);
    }

    #endregion

    #region Notifications

    public void ShowNotification(string message, float duration = 3f)
    {
        if (notificationRoot == null || notificationText == null)
        {
            Debug.Log("[UIManager] Notification: " + message);
            return;
        }

        if (notificationRoutine != null)
            StopCoroutine(notificationRoutine);

        notificationRoutine = StartCoroutine(NotificationRoutine(message, duration));
    }

    private IEnumerator NotificationRoutine(string message, float duration)
    {
        if (notificationText == null || notificationRoot == null)
            yield break;

        notificationText.text = message;
        notificationRoot.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;

            // The wired UI can be destroyed by a scene change while this
            // routine is waiting — bail out instead of throwing.
            if (notificationRoot == null || notificationText == null)
            {
                notificationRoutine = null;
                yield break;
            }

            // Unscaled so toasts still finish while the game is paused.
            elapsed += Time.unscaledDeltaTime;
        }

        notificationRoot.SetActive(false);
        notificationRoutine = null;
    }

    #endregion
}
