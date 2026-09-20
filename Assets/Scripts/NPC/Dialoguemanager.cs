using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generalized dialogue engine. Owns only line sequencing, typewriter text,
/// the advance/skip button, and the standard-vs-cinematic display swap.
/// Knows nothing about cameras, NPCs, scene loading, or quest state.
///
/// Callers (NPCController, IntroSequenceController) call Play(sequenceID)
/// and subscribe to OnSequenceComplete to layer their own behavior
/// (camera pans, fades, scene loads, quest hooks) on top.
///
/// Scene-local, not a cross-scene singleton. Each scene that needs dialogue
/// (IntroScene, MainMap) places its own DialogueManager with its own UI
/// wiring and its own Instance. dialogue.json is reloaded fresh each time
/// a DialogueManager starts. The file is small enough that this cost is
/// negligible, and it avoids DontDestroyOnLoad conflicts between scenes
/// that have structurally different dialogue UI.
///
/// ANDROID LOADING NOTES (kept deliberately verbose in logs):
/// - StreamingAssets on Android live inside the APK (jar:file://...), so the
///   file must be read via UnityWebRequest, not System.IO.File.
/// - File.ReadAllText (editor) strips a UTF-8 BOM automatically, but
///   downloadHandler.text (Android) keeps it as U+FEFF, which JsonUtility
///   rejects. We strip it explicitly so both platforms behave identically.
/// - A JsonUtility exception used to abort this coroutine before
///   IsRegistryLoaded was set; parse failures are now caught so callers
///   never hang waiting on IsRegistryLoaded.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance;

    [Header("Dialogue Root")]
    public GameObject dialoguePanel;

    [Header("Standard Dialogue Box")]
    public GameObject dialogueBoxGroup;
    public Text speakerNameText;
    public Text dialogueBodyText;

    [Header("Cinematic Mode")]
    // cinematicGroup must contain its own opaque black background image
    // plus cinematicText as children. fadeOverlay below is only a
    // transient mask during the swap, not the cinematic backdrop itself.
    public GameObject cinematicGroup;
    public Text cinematicText;
    public float cinematicTransitionDuration = 0.5f;

    [Header("Shared Controls")]
    public Button advanceButton;
    public Text advanceButtonLabel;

    [Header("Typewriter")]
    public float typewriterSpeed = 0.03f;

    [Header("Transient Fade Mask (shared by swaps and callers' own end-fades)")]
    public Image fadeOverlay;

    public event Action<DialogueSequence> OnSequenceStart;
    public event Action<DialogueLine, int> OnLineChanged;
    public event Action<DialogueSequence> OnSequenceComplete;

    public bool IsRegistryLoaded { get; private set; } = false;

    private Dictionary<string, DialogueSequence> registry = new Dictionary<string, DialogueSequence>();
    private DialogueSequence currentSequence;
    private int currentLineIndex;
    private bool isTyping;
    private bool isInCinematicMode;
    private Coroutine typewriterCoroutine;
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (dialogueBoxGroup != null) dialogueBoxGroup.SetActive(true);
        if (cinematicGroup != null) cinematicGroup.SetActive(false);
        if (cinematicText != null) cinematicText.alignment = TextAnchor.MiddleCenter;

        isInCinematicMode = false;

        if (advanceButton != null)
            advanceButton.onClick.AddListener(OnAdvancePressed);

        if (fadeOverlay != null)
        {
            Color c = fadeOverlay.color;
            c.a = 0f;
            fadeOverlay.color = c;
        }

        StartCoroutine(LoadRegistry());
    }

    // === Load dialogue.json ====================================================================
    private IEnumerator LoadRegistry()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Dialogue.json");
        string json = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        // Diagnostic logging: this is the exact URL being requested and the
        // exact HTTP status that comes back. If dialogue.json truly is
        // packaged at this path inside the APK, this request cannot 404.
        // A 404 here means the build does not contain the file at this
        // path (wrong folder, different casing, or a stale APK), not a bug
        // in how this script reads it, since puzzle_templates.json and
        // bkt_params.json use this identical UnityWebRequest pattern and
        // succeed. Use these log lines together with an APK-as-zip
        // inspection to confirm packaging.
        Debug.Log("[DialogueManager] Requesting dialogue.json from: " + path);
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            Debug.Log($"[DialogueManager] dialogue.json request finished | " +
                      $"result={req.result} | responseCode={req.responseCode} | " +
                      $"url={req.url} | error={req.error}");

            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text ?? "";

                // Byte-level probe: a UTF-8 BOM is EF BB BF. Logging the raw
                // first bytes makes a BOM (or a non-UTF-8 file, e.g. saved as
                // UTF-16 or ANSI in Notepad) visible in logcat instead of only
                // suspected. File.ReadAllText in the editor silently strips a
                // BOM, which is why the same file can pass in-editor and fail
                // on device.
                byte[] bytes = req.downloadHandler.data;
                if (bytes != null && bytes.Length >= 3)
                    Debug.Log($"[DialogueManager] dialogue.json first bytes: " +
                              $"{bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} " +
                              $"(UTF-8 BOM would be EF BB BF; UTF-16 LE would be FF FE)");
            }
            else
            {
                Debug.LogError("[DialogueManager] Failed to load dialogue.json: " + req.error +
                               " | responseCode=" + req.responseCode +
                               " | If responseCode=404, open the APK as a zip and confirm " +
                               "assets/dialogue.json exists with this exact casing.");
            }
        }
#else
        if (System.IO.File.Exists(path))
            json = System.IO.File.ReadAllText(path); // note: this strips a BOM automatically
        else
            Debug.LogError("[DialogueManager] dialogue.json not found at: " + path);
        yield return null;
#endif

        // File.ReadAllText (editor) consumes a leading UTF-8 BOM, but
        // downloadHandler.text (Android) keeps it as U+FEFF and JsonUtility
        // then throws "Invalid value" before a single sequence loads. Trim it
        // here so both platforms parse the exact same string. Harmless when
        // the file has no BOM.
        json = (json ?? "").TrimStart('\uFEFF');

        Debug.Log($"[DialogueManager] dialogue.json text length: {json.Length}");
        if (json.Length > 0)
            Debug.Log($"[DialogueManager] dialogue.json first char: U+{(int)json[0]:X4}" +
                  $" ('{(char.IsControl(json[0]) ? '?' : json[0])}')  (normal JSON starts with U+007B '{{')");

        registry.Clear();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[DialogueManager] dialogue.json was empty or missing; no dialogue can play.");
        }
        else
        {
            // try/catch so a malformed/BOM'd file logs a clear error and still
            // reaches IsRegistryLoaded = true, instead of killing this
            // coroutine and leaving callers waiting on it forever.
            try
            {
                DialogueRoot root = JsonUtility.FromJson<DialogueRoot>(json);
                if (root != null && root.sequences != null)
                {
                    foreach (var seq in root.sequences)
                    {
                        if (string.IsNullOrEmpty(seq.sequenceID))
                        {
                            Debug.LogWarning("[DialogueManager] Sequence with empty sequenceID skipped.");
                            continue;
                        }
                        if (registry.ContainsKey(seq.sequenceID))
                        {
                            Debug.LogWarning($"[DialogueManager] Duplicate sequenceID '{seq.sequenceID}', keeping first.");
                            continue;
                        }
                        registry.Add(seq.sequenceID, seq);
                    }
                }
                else
                {
                    // Loaded fine but nothing deserialized: on IL2CPP Android
                    // builds with Managed Stripping Level above Minimal, the
                    // fields of [Serializable] classes can be stripped, so
                    // JsonUtility silently yields null. Fix with link.xml or
                    // Player Settings > stripping level = Minimal.
                    Debug.LogError("[DialogueManager] dialogue.json parsed but root.sequences is null. " +
                                   "If this only happens on device, check Managed Stripping Level " +
                                   "(Player Settings) and add a link.xml preserving the Dialogue* classes.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[DialogueManager] JSON parse failed. Read the 'first bytes' / 'first char' " +
                               "log above: U+FEFF or bytes EF BB BF mean the file was saved as UTF-8 WITH BOM " +
                               "(re-save as plain UTF-8); U+0000/garbage means it is not UTF-8 at all. " +
                               "Exception: " + e);
            }
        }

        IsRegistryLoaded = true;
        Debug.Log($"[DialogueManager] Loaded {registry.Count} sequences.");
    }

    public bool HasSequence(string sequenceID)
    {
        return registry.ContainsKey(sequenceID);
    }

    // === Public Entry Points ===================================================================
    public void Play(string sequenceID)
    {
        if (!IsRegistryLoaded)
            Debug.LogWarning($"[DialogueManager] Play('{sequenceID}') called before the registry finished loading.");

        if (!registry.TryGetValue(sequenceID, out var seq))
        {
            Debug.LogError($"[DialogueManager] Unknown sequenceID: {sequenceID}" +
                           (IsRegistryLoaded ? "" : " (registry was not loaded yet)"));
            return;
        }
        Play(seq);
    }

    public void Play(DialogueSequence sequence)
    {
        if (sequence == null || sequence.lines == null || sequence.lines.Count == 0)
        {
            Debug.LogWarning("[DialogueManager] Tried to play an empty sequence.");
            return;
        }

        currentSequence = sequence;
        currentLineIndex = 0;

        // Block autosave and hide HUD while dialogue is active
        SaveLoadManager.IsSafeToSave = false;
        SaveRestrictionEnforcer.Instance?.AddBlocker("dialogue");
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);

        InteractButtonController interact = FindObjectOfType<InteractButtonController>();
        if (interact != null)
            interact.ForceHide();

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        OnSequenceStart?.Invoke(sequence);
        DisplayCurrentLine();
    }

    // === Display Line =========================================================================
    private void DisplayCurrentLine()
    {
        if (currentLineIndex >= currentSequence.lines.Count)
        {
            EndCurrentSequence();
            return;
        }

        DialogueLine line = currentSequence.lines[currentLineIndex];

        if (line.isCinematic != isInCinematicMode)
        {
            StartCoroutine(SwitchModeThenShowLine(line));
            return;
        }

        ShowLineText(line);
    }

    private IEnumerator SwitchModeThenShowLine(DialogueLine line)
    {
        yield return StartCoroutine(TransitionMode(line.isCinematic));
        ShowLineText(line);
    }

    private void ShowLineText(DialogueLine line)
    {
        if (typewriterCoroutine != null)
            StopCoroutine(typewriterCoroutine);

        if (line.isCinematic)
        {
            if (cinematicText != null) cinematicText.text = "";
            typewriterCoroutine = StartCoroutine(TypewriterEffect(line.dialogueText, cinematicText));
        }
        else
        {
            if (speakerNameText != null)
            {
                bool hasSpeaker = !string.IsNullOrEmpty(line.speakerName);
                speakerNameText.gameObject.SetActive(hasSpeaker);
                speakerNameText.text = line.speakerName;
            }
            if (dialogueBodyText != null) dialogueBodyText.text = "";
            typewriterCoroutine = StartCoroutine(TypewriterEffect(line.dialogueText, dialogueBodyText));
        }

        OnLineChanged?.Invoke(line, currentLineIndex);
    }

    // === Typewriter ===========================================================================
    private IEnumerator TypewriterEffect(string fullText, Text target)
    {
        isTyping = true;
        if (advanceButtonLabel != null) advanceButtonLabel.text = "Skip";

        for (int i = 0; i <= fullText.Length; i++)
        {
            if (target != null) target.text = fullText.Substring(0, i);
            yield return new WaitForSeconds(typewriterSpeed);
        }

        isTyping = false;
        if (advanceButtonLabel != null) advanceButtonLabel.text = "Next";
    }

    // === Advance Button =======================================================================
    private void OnAdvancePressed()
    {
        if (currentSequence == null) return;

        if (isTyping)
        {
            if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);

            DialogueLine line = currentSequence.lines[currentLineIndex];
            Text target = line.isCinematic ? cinematicText : dialogueBodyText;
            if (target != null) target.text = line.dialogueText;

            isTyping = false;
            if (advanceButtonLabel != null) advanceButtonLabel.text = "Next";
        }
        else
        {
            currentLineIndex++;
            DisplayCurrentLine();
        }
    }

    // === Mode Transition ======================================================================
    private IEnumerator TransitionMode(bool toCinematic)
    {
        yield return StartCoroutine(FadeOverlayTo(1f, cinematicTransitionDuration));

        if (toCinematic)
        {
            if (dialogueBoxGroup != null) dialogueBoxGroup.SetActive(false);
            if (cinematicGroup != null) cinematicGroup.SetActive(true);
        }
        else
        {
            if (cinematicGroup != null) cinematicGroup.SetActive(false);
            if (dialogueBoxGroup != null) dialogueBoxGroup.SetActive(true);
        }

        isInCinematicMode = toCinematic;

        yield return StartCoroutine(FadeOverlayTo(0f, cinematicTransitionDuration));
    }

    private IEnumerator FadeOverlayTo(float targetAlpha, float duration)
    {
        if (fadeOverlay == null) yield break;

        float startAlpha = fadeOverlay.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            Color c = fadeOverlay.color;
            c.a = a;
            yield return null;
        }

        Color final = fadeOverlay.color;
        final.a = targetAlpha;
        fadeOverlay.color = final;
    }

    // === End Sequence =========================================================================
    private void EndCurrentSequence()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        // Restore autosave and HUD now that dialogue is no longer active
        SaveLoadManager.IsSafeToSave = true;
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("dialogue");
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);

        DialogueSequence finished = currentSequence;
        currentSequence = null;

        OnSequenceComplete?.Invoke(finished);
    }
}

// === Data Structures ======================================================================
[System.Serializable]
public class DialogueLine
{
    public string speakerName;
    [TextArea(2, 5)]
    public string dialogueText;
    public bool isCinematic = false;
}

[System.Serializable]
public class DialogueSequence
{
    public string sequenceID;
    public List<DialogueLine> lines = new List<DialogueLine>();

    // "none" | "stay" | "depart"
    // Parsed by the caller (NPCController, IntroSequenceController), not by
    // DialogueManager itself, since only the caller knows what "depart"
    // should visually mean in its own scene.
    public string endBehavior = "none";
    public string nextSequenceIfStay = "";
    public string questIDToComplete = "";
}

[System.Serializable]
public class DialogueRoot
{
    public List<DialogueSequence> sequences = new List<DialogueSequence>();
}
