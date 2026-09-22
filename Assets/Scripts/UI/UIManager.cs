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
/// 5. Binding repair (Android "New Text" fix): a name-based re-bind can latch
///    onto a stray, never-configured object that merely shares the wired
///    name. If the re-bound notificationText is not a child of the re-bound
///    notificationRoot, the stray is hidden and the persistent fallback pair
///    is used, so a default "New Text" object can never sit visible on the
///    screen, uncontrolled. Re-bound fade overlays are likewise deactivated
///    while idle (Fade() re-activates them when a fade starts).
/// 6. Diagnostics: Awake and every scene load log the binding state with the
///    "[UIManager]" prefix (wired / RUNTIME FALLBACK / MISSING per field), so
///    wired-vs-fallback-vs-missing is provable in adb logcat on device.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    #region Static notification facade (zero scene setup)

    /// <summary>
    /// Returns the live UIManager, creating a bare one at runtime if no scene
    /// contains one. Lets any system fire a notification with no Inspector
    /// wiring at all: the auto-created copy immediately builds its persistent
    /// runtime toast canvas (EnsureUsableUI), so toasts work even in a scene
    /// with no UI set up. If a real UIManager already exists anywhere, it is
    /// reused untouched.
    /// </summary>
    public static UIManager EnsureInstance()
    {
        if (Instance == null)
        {
            // AddComponent runs Awake synchronously, which sets Instance,
            // DontDestroyOnLoad's the object and builds the fallback toast UI.
            new GameObject("UIManager (Auto-Created)").AddComponent<UIManager>();
            Debug.Log("[UIManager] No UIManager existed in the scene; auto-created one " +
                      "so notifications work without any scene-hierarchy wiring.");
        }
        return Instance;
    }

    /// <summary>
    /// One-line fire-and-forget toast, callable from anywhere:
    /// UIManager.Notify("Mission complete!");
    /// Never needs a scene reference - use this from world objects, managers
    /// and puzzle callbacks instead of dragging the UIManager into fields.
    /// </summary>
    public static void Notify(string message, float duration = 3f)
    {
        UIManager ui = EnsureInstance();
        if (ui != null)
            ui.ShowNotification(message, duration);
    }

    #endregion

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
        EnsureToastBindingConsistency();
        HideFadeOverlay();

        LogUiState("Awake (initial bindings)");

        SceneManager.sceneLoaded += OnSceneLoaded;
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

        // A name-based re-bind can latch onto a stray same-named object that
        // is not part of a real wired toast. Validate the pair, hide strays.
        EnsureToastBindingConsistency();
        HideFadeOverlay();

        LogUiState("Scene loaded: '" + scene.name + "' (" + mode + ")");
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
            {
                fadeImage = img;
                Debug.Log("[UIManager] Re-bound fadeImage -> '" + GetTransformPath(img.transform) +
                          "' in scene '" + scene.name + "'");
            }
            else
            {
                Debug.Log("[UIManager] No object named '" + fadeImageName +
                          "' in scene '" + scene.name + "' - fadeImage will use the runtime fallback.");
            }
        }

        if (rootDead)
        {
            Transform t = FindInScene(scene, notificationRootName);
            if (t != null)
            {
                notificationRoot = t.gameObject;
                Debug.Log("[UIManager] Re-bound notificationRoot -> '" + GetTransformPath(t) +
                          "' in scene '" + scene.name + "'");
            }
            else
            {
                Debug.Log("[UIManager] No object named '" + notificationRootName +
                          "' in scene '" + scene.name + "' - toast root will use the runtime fallback.");
            }
        }

        if (textDead)
        {
            TMP_Text txt = FindComponentInScene<TMP_Text>(scene, notificationTextName);
            if (txt != null)
            {
                notificationText = txt;
                Debug.Log("[UIManager] Re-bound notificationText -> '" + GetTransformPath(txt.transform) +
                          "' in scene '" + scene.name + "'");
            }
            else
            {
                Debug.Log("[UIManager] No object named '" + notificationTextName +
                          "' in scene '" + scene.name + "' - toast text will use the runtime fallback.");
            }
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
        try
        {
            TMP_FontAsset asset = TMP_Settings.defaultFontAsset;
            if (asset != null)
                return asset;
        }
        catch { }

        // TMP_Settings can come back empty in builds whose TMP Essentials were
        // never imported. Try the standard Essentials install location, which
        // lives inside a Resources folder and is therefore packaged into the
        // build (unlike editor-only asset lookups).
        string[] resourceCandidates =
        {
            "Fonts & Materials/LiberationSans SDF",
            "LiberationSans SDF"
        };

        foreach (string candidate in resourceCandidates)
        {
            TMP_FontAsset asset = Resources.Load<TMP_FontAsset>(candidate);
            if (asset != null)
                return asset;
        }

        return null;
    }

    #endregion

    #region Binding validation & diagnostics

    /// <summary>
    /// The name-based re-bind can latch onto ANY object in a newly loaded
    /// scene that merely shares the wired name - including a leftover default
    /// text ("New Text") that was never wired to anything. This method makes
    /// the toast binding self-consistent again:
    /// - a notificationText that is NOT a child of notificationRoot is a
    ///   stray: it gets hidden (it would otherwise sit on screen showing its
    ///   default text, uncontrolled) and the persistent fallback pair is used;
    /// - a notificationRoot with no notificationText under it would only ever
    ///   show an empty backdrop: it is hidden and the fallback pair is used.
    /// </summary>
    private void EnsureToastBindingConsistency()
    {
        if (notificationText != null && !IsFallbackToastText(notificationText) &&
            (notificationRoot == null || !notificationText.transform.IsChildOf(notificationRoot.transform)))
        {
            Debug.LogWarning("[UIManager] notificationText '" + GetTransformPath(notificationText.transform) +
                             "' is not under notificationRoot. Hiding the stray text; toasts will use the runtime fallback UI.");
            notificationText.gameObject.SetActive(false);
            notificationText = null;
        }

        if (notificationRoot != null && !IsFallbackToastRoot(notificationRoot) &&
            (notificationText == null || !notificationText.transform.IsChildOf(notificationRoot.transform)))
        {
            Debug.LogWarning("[UIManager] notificationRoot '" + GetTransformPath(notificationRoot.transform) +
                             "' has no matching notificationText under it. Hiding it; toasts will use the runtime fallback UI.");
            notificationRoot.SetActive(false);
            notificationRoot = null;
        }

        // Rebuild the persistent fallback pair if a stray binding was dropped.
        EnsureUsableUI();

        // The toast is hidden until something calls ShowNotification.
        if (notificationRoot != null)
            notificationRoot.SetActive(false);
    }

    private bool IsFallbackToastText(TMP_Text text)
    {
        return ReferenceEquals(text, fallbackNotificationText);
    }

    private bool IsFallbackToastRoot(GameObject root)
    {
        return ReferenceEquals(root, fallbackNotificationRoot);
    }

    /// <summary>
    /// A re-bound fade overlay must not sit visible while idle (a stray
    /// same-named Image would otherwise show as a white rectangle). Fade()
    /// re-activates the object every time a fade actually starts, so hiding
    /// it here is safe and matches the end-of-fade behaviour.
    /// </summary>
    private void HideFadeOverlay()
    {
        if (fadeImage != null && fadeImage.gameObject.activeSelf)
            fadeImage.gameObject.SetActive(false);
    }

    /// <summary>
    /// One-line binding report, logged on Awake and after every scene load.
    /// Filter adb logcat by "[UIManager]" to see whether the device build is
    /// using the wired UI, the runtime fallback, or nothing at all.
    /// </summary>
    private void LogUiState(string context)
    {
        Debug.Log("[UIManager] " + context +
                  " | scene '" + SceneManager.GetActiveScene().name + "'" +
                  " | fadeImage=" + DescribeUiRef(fadeImage, fallbackFadeImage) +
                  " | notificationRoot=" + DescribeUiRef(notificationRoot, fallbackNotificationRoot) +
                  " | notificationText=" + DescribeUiRef(notificationText, fallbackNotificationText));
    }

    private static string DescribeUiRef(Object wired, Object runtimeFallback)
    {
        if (wired != null)
            return "wired '" + wired.name + "'";

        if (runtimeFallback != null)
            return "RUNTIME FALLBACK";

        return "MISSING";
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
            return "<null>";

        string path = t.name;
        Transform parent = t.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
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
            // Loud, greppable signal: the device build has no toast UI bound
            // at all. Usually means the wired scene never made it into the
            // build (unsaved scene, wrong scene list) or the references were
            // never assigned on the built object.
            Debug.LogWarning("[UIManager] ShowNotification skipped - no toast UI is bound. " +
                             "scene '" + SceneManager.GetActiveScene().name + "', message was: " + message +
                             " (check earlier [UIManager] logs: wired references are missing in this build - " +
                             "was the scene saved and added to Build Settings before building?)");
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
            // routine is waiting - bail out instead of throwing.
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