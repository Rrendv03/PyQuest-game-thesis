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
/// SKIP DIALOGUE: every sequence can be skipped through a Skip Dialogue
/// button and a per-sequence confirm panel. The panel shows THAT sequence's
/// summary (its skipSummary from dialogue.json, or an auto-built excerpt of
/// its lines when the field is empty). Confirming ends the sequence through
/// EndCurrentSequence() — the exact same completion path as reading it — so
/// OnSequenceComplete still fires and quest/scene flow stays intact.
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

    [Header("Skip Dialogue")]
    [Tooltip("Optional: your own Skip Dialogue button. Left empty, one is auto-built on the dialogue panel.")]
    public Button skipDialogueButton;
    [Tooltip("Optional: your own confirm panel. Left empty, one is auto-built (dim backdrop, summary text, Keep Talking / Skip buttons).")]
    public GameObject skipConfirmPanel;
    [Tooltip("Required when skipConfirmPanel is assigned: the Text that shows the sequence's summary.")]
    public Text skipConfirmSummaryText;
    [Tooltip("Required when skipConfirmPanel is assigned: the button that confirms the skip.")]
    public Button skipConfirmYesButton;
    [Tooltip("Required when skipConfirmPanel is assigned: the button that cancels and keeps talking.")]
    public Button skipConfirmNoButton;
    [Tooltip("When a sequence has no skipSummary in dialogue.json, build a short excerpt from its own lines instead.")]
    public bool autoSummarizeSkippedDialogue = true;

    [Header("Typewriter")]
    public float typewriterSpeed = 0.03f;

    // NOTE: The dialogue file name is deliberately NOT a serialized/Inspector
    // field. A public field on this component re-serialized every scene that
    // contains a DialogueManager, which kept altering the scene files. The
    // name is a constant below, and on Android the exact casing is
    // auto-discovered from the APK itself, so no editable field is needed.

    [Header("Transient Fade Mask (shared by swaps and callers' own end-fades)")]
    public Image fadeOverlay;

    [Header("NPC Audio")]
    [Tooltip("One entry per NPC speaker name. Plays once, the first time that NPC's dialogue starts (the 'encounter hello').")]
    public List<EncounterAudioEntry> encounterClips = new List<EncounterAudioEntry>();
    [Tooltip("Undertale-style voice blip played repeatedly while the typewriter reveals text.")]
    public AudioClip dialogueBlip;
    [Tooltip("Play the blip at most once every N revealed characters (1 = every character).")]
    public int blipEveryNChars = 4;
    [Range(0f, 1f)] public float encounterVolume = 1f;
    [Range(0f, 1f)] public float blipVolume = 1f;

    private AudioSource audioSource;
    private readonly HashSet<string> encounteredSpeakers = new HashSet<string>(StringComparer.Ordinal);
    private int charsSinceLastBlip;

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
    private Coroutine modeSwitchCoroutine;      // SwitchModeThenShowLine wrapper
    private Coroutine modeTransitionCoroutine;  // the inner TransitionMode fade
    private bool isSkipConfirmOpen;
    void Awake()
    {
        Instance = this;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
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

        // Skip Dialogue UI: auto-build whatever was left unassigned, then wire
        // the listeners exactly once (the builders never subscribe themselves).
        EnsureSkipDialogueUi();

        if (skipDialogueButton != null)
        {
            skipDialogueButton.onClick.AddListener(OnSkipDialoguePressed);
            skipDialogueButton.gameObject.SetActive(false);
        }
        if (skipConfirmYesButton != null)
            skipConfirmYesButton.onClick.AddListener(OnSkipDialogueConfirmed);
        if (skipConfirmNoButton != null)
            skipConfirmNoButton.onClick.AddListener(OnSkipDialogueCancelled);
        if (skipConfirmPanel != null)
            skipConfirmPanel.SetActive(false);

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
        string json = "";
        bool fileRead = false;
        string[] candidatePaths = BuildCandidatePaths();

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
        //
        // Candidate names are tried in order, but first Android reports the
        // EXACT name of the dialogue file inside the APK (zip lookups are
        // case-sensitive while the desktop project folder is not), so casing
        // drift can no longer 404 on device. If the file is missing entirely,
        // the listing log below proves it instead of leaving it to guesswork.
        List<string> attemptPaths = new List<string>(candidatePaths);
        string exactApkName = FindAndroidDialogueAsset();
        if (!string.IsNullOrEmpty(exactApkName))
            attemptPaths.Insert(0, Path.Combine(Application.streamingAssetsPath, exactApkName));

        foreach (string path in attemptPaths)
        {
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
                    fileRead = true;
                    break; // success — no need to try the next candidate name
                }
                else
                {
                    Debug.LogError("[DialogueManager] Failed to load dialogue.json: " + req.error +
                                   " | responseCode=" + req.responseCode +
                                   " | If responseCode=404, open the APK as a zip and confirm " +
                                   "assets/dialogue.json exists with this exact casing.");
                }
            }
        }
#else
        foreach (string path in candidatePaths)
        {
            if (System.IO.File.Exists(path))
            {
                json = System.IO.File.ReadAllText(path); // note: this strips a BOM automatically
                fileRead = true;
                break;
            }
        }
        if (!fileRead)
            Debug.LogError("[DialogueManager] dialogue.json not found. Tried: " + string.Join(", ", candidatePaths));
        yield return null;
#endif

        if (!fileRead)
            Debug.LogError("[DialogueManager] LOAD FAILED: dialogue.json was not read on this platform. " +
                           "No dialogue will play. On device this almost always means the APK does not " +
                           "contain the file (stale build) or the file lives in a subfolder of StreamingAssets.");

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
        if (registry.Count > 0)
            Debug.Log($"[DialogueManager] LOAD RESULT: OK — {registry.Count} sequences loaded from dialogue.json.");
        else
            Debug.LogError("[DialogueManager] LOAD RESULT: FAILED — 0 sequences. No dialogue will play. " +
                           "Read the errors above: 'not found' = packaging/path problem; 'parse failed' = file " +
                           "encoding/content problem; 'root.sequences is null' = IL2CPP code stripping problem; " +
                           "no error at all = the JSON's field names no longer match this script's data classes " +
                           "(sequenceID / lines / speakerName / dialogueText / isCinematic).");
    }

    // The file name is hardcoded (see note above Awake). The second entry is
    // a static safety net for packaging drift; on Android the exact name is
    // discovered at runtime and tried first anyway.
    private const string DialogueFileName = "dialogue.json";

    private static string[] BuildCandidatePaths()
    {
        string[] names = { DialogueFileName, "Dialogue.json" };
        var paths = new List<string>(names.Length);
        foreach (string name in names)
            paths.Add(Path.Combine(Application.streamingAssetsPath, name));
        return paths.ToArray();
    }

    // Asks Android's AssetManager for the exact name of the dialogue file in
    // the APK. Zip lookups are case-sensitive while the desktop project folder
    // is not, so "Dialogue.json" vs "dialogue.json" only ever breaks on device
    // — this removes that failure mode entirely. Returns null when the file is
    // not found (or listing failed), in which case the static candidate names
    // are used as before. The log also lists every .json in the APK, so "the
    // file is not in this build" becomes provable straight from logcat.
    private static string FindAndroidDialogueAsset()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var assets = activity.Call<AndroidJavaObject>("getAssets"))
            {
                string[] entries = assets.Call<string[]>("list", "");
                if (entries == null || entries.Length == 0)
                {
                    Debug.LogWarning("[DialogueManager] APK assets root listing was empty — StreamingAssets were not packaged into this build.");
                    return null;
                }

                string match = null;
                var jsonNames = new List<string>();
                foreach (string entry in entries)
                {
                    if (entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        jsonNames.Add(entry);
                    if (match == null &&
                        string.Equals(entry, DialogueFileName, StringComparison.OrdinalIgnoreCase))
                        match = entry;
                }

                Debug.Log("[DialogueManager] APK assets root contains: " + string.Join(", ", entries));
                if (match != null)
                    Debug.Log("[DialogueManager] Dialogue file packaged in APK as: \"" + match + "\" — requesting that exact name.");
                else
                    Debug.LogError("[DialogueManager] No dialogue.json (any casing) in the APK assets root. .json files present: " +
                                   (jsonNames.Count > 0 ? string.Join(", ", jsonNames) : "none") +
                                   " — rebuild the APK; if it still fails, the file is not directly inside Assets/StreamingAssets.");
                return match;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DialogueManager] Could not list APK assets (" + ex.Message + "); using candidate names instead.");
            return null;
        }
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

        // A fresh sequence always starts with the confirm panel closed and the
        // skip button available again.
        isSkipConfirmOpen = false;
        if (skipConfirmPanel != null) skipConfirmPanel.SetActive(false);
        if (skipDialogueButton != null) skipDialogueButton.gameObject.SetActive(true);

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

        PlayEncounterAudioIfNeeded(sequence);

        OnSequenceStart?.Invoke(sequence);
        DisplayCurrentLine();
    }

    // === NPC Audio ============================================================================
    [System.Serializable]
    public class EncounterAudioEntry
    {
        [Tooltip("Must match the speakerName used on the NPC's dialogue lines.")]
        public string speakerName;
        public AudioClip clip;
    }

    private void PlayEncounterAudioIfNeeded(DialogueSequence sequence)
    {
        string speaker = null;
        if (sequence.lines != null && sequence.lines.Count > 0)
            speaker = sequence.lines[0].speakerName;

        if (string.IsNullOrEmpty(speaker)) return;
        if (!encounteredSpeakers.Add(speaker)) return; // already met this NPC

        EncounterAudioEntry entry = encounterClips.Find(e =>
            string.Equals(e.speakerName, speaker, StringComparison.Ordinal));
        if (entry?.clip != null)
            audioSource.PlayOneShot(entry.clip, encounterVolume);
    }

    private void PlayBlip()
    {
        if (dialogueBlip == null) return;
        if (++charsSinceLastBlip < Mathf.Max(1, blipEveryNChars)) return;
        charsSinceLastBlip = 0;
        audioSource.PlayOneShot(dialogueBlip, blipVolume);
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
            // Tracked so a skip during a cinematic fade can stop the whole
            // chain, not just the text reveal.
            modeSwitchCoroutine = StartCoroutine(SwitchModeThenShowLine(line));
            return;
        }

        ShowLineText(line);
    }

    private IEnumerator SwitchModeThenShowLine(DialogueLine line)
    {
        // Tracked separately: stopping the outer coroutine does NOT stop a
        // nested one, so the fade keeps its own handle for skip/end cleanup.
        modeTransitionCoroutine = StartCoroutine(TransitionMode(line.isCinematic));
        yield return modeTransitionCoroutine;
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
        charsSinceLastBlip = 0;
        if (advanceButtonLabel != null) advanceButtonLabel.text = "Skip";

        for (int i = 0; i <= fullText.Length; i++)
        {
            // The skip confirm panel is modal: hold the typewriter (and its
            // blips) exactly where it is until the player answers it.
            while (isSkipConfirmOpen)
                yield return null;

            if (target != null) target.text = fullText.Substring(0, i);

            // Undertale-style voice blip: fire on each newly revealed letter.
            if (i > 0 && i <= fullText.Length && char.IsLetterOrDigit(fullText[i - 1]))
                PlayBlip();

            yield return new WaitForSeconds(typewriterSpeed);
        }

        isTyping = false;
        if (advanceButtonLabel != null) advanceButtonLabel.text = "Next";
    }

    // === Advance Button =======================================================================
    private void OnAdvancePressed()
    {
        if (currentSequence == null) return;
        if (isSkipConfirmOpen) return; // modal is up; ignore taps that leak through

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

    // === Skip Dialogue =========================================================================
    // Every sequence can be skipped. The Skip Dialogue button opens a small
    // confirm panel that shows a summary of the sequence being skipped (its
    // "skipSummary" from dialogue.json, or an auto-built excerpt from its
    // lines when that field is empty). Confirming ends the sequence through
    // EndCurrentSequence() — the same completion path as reading to the last
    // line — so OnSequenceComplete still fires and callers (NPCController,
    // IntroSequenceController) run their normal post-dialogue behavior:
    // quest completion, endBehavior, camera/scene hooks, HUD + autosave
    // restore. Nothing downstream can tell a skipped dialogue from a read one.

    private void OnSkipDialoguePressed()
    {
        if (currentSequence == null || isSkipConfirmOpen) return;
        OpenSkipConfirmPanel();
    }

    private void OpenSkipConfirmPanel()
    {
        isSkipConfirmOpen = true;

        if (skipConfirmSummaryText != null)
            skipConfirmSummaryText.text = ResolveSkipSummary(currentSequence);

        if (skipConfirmPanel != null)
            skipConfirmPanel.SetActive(true);
        else
            Debug.LogWarning("[DialogueManager] Skip confirm panel is missing — a skip needs confirmation, so nothing was skipped.");
    }

    private void OnSkipDialogueConfirmed()
    {
        if (!isSkipConfirmOpen) return;
        CloseSkipConfirmPanel();
        SkipCurrentSequence();
    }

    private void OnSkipDialogueCancelled()
    {
        CloseSkipConfirmPanel();
    }

    private void CloseSkipConfirmPanel()
    {
        isSkipConfirmOpen = false;
        if (skipConfirmPanel != null)
            skipConfirmPanel.SetActive(false);
    }

    // Ends the running sequence immediately, through the normal completion
    // path. The typewriter (paused behind the confirm panel) and any running
    // cinematic fade are stopped first.
    private void SkipCurrentSequence()
    {
        if (currentSequence == null) return;

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }
        if (modeSwitchCoroutine != null)
        {
            StopCoroutine(modeSwitchCoroutine);
            modeSwitchCoroutine = null;
        }
        if (modeTransitionCoroutine != null)
        {
            StopCoroutine(modeTransitionCoroutine);
            modeTransitionCoroutine = null;
        }

        isTyping = false;
        EndCurrentSequence();
    }

    // The summary shown in the confirm panel: the sequence's curated
    // skipSummary when dialogue.json provides one, otherwise a short excerpt
    // built from the sequence's own lines.
    private string ResolveSkipSummary(DialogueSequence sequence)
    {
        if (sequence == null) return "";
        if (!string.IsNullOrWhiteSpace(sequence.skipSummary))
            return sequence.skipSummary;

        if (!autoSummarizeSkippedDialogue) return "";

        // Who leads the scene, how long it runs, and how it opens.
        string speaker = "";
        for (int i = 0; i < sequence.lines.Count && speaker.Length == 0; i++)
            speaker = (sequence.lines[i].speakerName ?? "").Trim();
        if (speaker.Length == 0) speaker = "the story";

        string opening = "";
        for (int i = 0; i < sequence.lines.Count && opening.Length == 0; i++)
        {
            string candidate = (sequence.lines[i].dialogueText ?? "")
                .Replace("\r", " ").Replace("\n", " ").Trim();
            if (candidate.Length > 0) opening = candidate;
        }
        if (opening.Length > 160)
            opening = opening.Substring(0, 157) + "...";

        string lineWord = sequence.lines.Count == 1 ? "line" : "lines";
        return $"{sequence.lines.Count} {lineWord} with {speaker}.\n\n\"{opening}\"";
    }

    // --- Auto-built skip UI --------------------------------------------------------------------
    // Left unassigned in the Inspector, the button and the confirm panel are
    // built once at runtime, so every scene's DialogueManager gets the feature
    // with no scene edits. Assign your own references to restyle or localize
    // instead — the auto-build then never runs.
    private void EnsureSkipDialogueUi()
    {
        if (skipConfirmPanel == null)
            BuildSkipConfirmPanel();
        else if (skipConfirmSummaryText == null || skipConfirmYesButton == null || skipConfirmNoButton == null)
            Debug.LogError("[DialogueManager] skipConfirmPanel is assigned but its summary text / yes / no button references are not. Assign them, or leave skipConfirmPanel empty to use the auto-built panel.");

        if (skipDialogueButton == null)
            BuildSkipButton();
    }

    private void BuildSkipButton()
    {
        if (dialoguePanel == null)
        {
            Debug.LogError("[DialogueManager] Cannot auto-build the Skip Dialogue button: dialoguePanel is not assigned.");
            return;
        }

        GameObject buttonGo = new GameObject("SkipDialogueButton (auto)", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rt = buttonGo.GetComponent<RectTransform>();
        rt.SetParent(dialoguePanel.transform, false);
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(210f, 48f);

        Image background = buttonGo.GetComponent<Image>();
        background.color = new Color(0.09f, 0.09f, 0.12f, 0.85f);

        Text label = CreateUiText("Label", rt, "Skip Dialogue", 20, TextAnchor.MiddleCenter, Color.white);
        StretchRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-10f, -6f));

        skipDialogueButton = buttonGo.GetComponent<Button>();
        Debug.Log("[DialogueManager] No Skip Dialogue button was assigned — auto-built one on the dialogue panel.");
    }

    private void BuildSkipConfirmPanel()
    {
        if (dialoguePanel == null)
        {
            Debug.LogError("[DialogueManager] Cannot auto-build the skip confirm panel: dialoguePanel is not assigned.");
            return;
        }

        // Full-panel dim backdrop; it also blocks clicks from reaching the
        // advance button while the confirm is up.
        GameObject panelGo = new GameObject("SkipConfirmPanel (auto)", typeof(RectTransform), typeof(Image));
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.SetParent(dialoguePanel.transform, false);
        StretchRect(panelRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        GameObject cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.SetParent(panelRt, false);
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(680f, 400f);
        cardGo.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f, 0.98f);

        Text title = CreateUiText("Title", cardRt, "Skip this dialogue?", 30, TextAnchor.MiddleCenter, Color.white);
        StretchRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -84f), new Vector2(-28f, -30f));

        Text summary = CreateUiText("Summary", cardRt, "", 22, TextAnchor.UpperCenter, new Color(0.88f, 0.88f, 0.9f));
        StretchRect(summary.rectTransform, Vector2.zero, Vector2.one, new Vector2(32f, 118f), new Vector2(-32f, -96f));

        skipConfirmNoButton = CreatePanelButton("KeepTalkingButton", cardRt, "Keep Talking",
            new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(288f, 62f), new Color(0.30f, 0.55f, 0.85f));
        skipConfirmYesButton = CreatePanelButton("SkipButton", cardRt, "Skip",
            new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(288f, 62f), new Color(0.82f, 0.36f, 0.32f));

        skipConfirmSummaryText = summary;
        skipConfirmPanel = panelGo;
        panelGo.SetActive(false);

        Debug.Log("[DialogueManager] No skip confirm panel was assigned — auto-built one on the dialogue panel.");
    }

    private static Button CreatePanelButton(string buttonName, Transform parent, string label,
        Vector2 cornerAnchor, Vector2 anchoredPosition, Vector2 size, Color background)
    {
        GameObject buttonGo = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rt = buttonGo.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = cornerAnchor;
        rt.anchorMax = cornerAnchor;
        rt.pivot = cornerAnchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
        buttonGo.GetComponent<Image>().color = background;

        Text text = CreateUiText("Label", rt, label, 24, TextAnchor.MiddleCenter, Color.white);
        StretchRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));

        return buttonGo.GetComponent<Button>();
    }

    private static Text CreateUiText(string textName, Transform parent, string content, int fontSize, TextAnchor alignment, Color color)
    {
        GameObject textGo = new GameObject(textName, typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(parent, false);

        Text text = textGo.GetComponent<Text>();
        text.font = DefaultUiFont();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static void StretchRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static Font DefaultUiFont()
    {
        // The built-in font changed name in Unity 2022.2 (Arial.ttf ->
        // LegacyRuntime.ttf); try both so either version renders.
        try { Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (legacy != null) return legacy; } catch { }
        try { Font arial = Resources.GetBuiltinResource<Font>("Arial.ttf"); if (arial != null) return arial; } catch { }
        return null;
    }

    // === Mode Transition =======================================================================
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
        if (currentSequence == null) return; // already ended (a skip racing the last line, or a double end)

        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = null;
        }
        if (modeSwitchCoroutine != null)
        {
            StopCoroutine(modeSwitchCoroutine);
            modeSwitchCoroutine = null;
        }
        if (modeTransitionCoroutine != null)
        {
            StopCoroutine(modeTransitionCoroutine);
            modeTransitionCoroutine = null;
        }

        // If we ended in the middle of a cinematic fade, park the overlay back
        // at fully clear so callers' own end-fades start from a clean state.
        if (fadeOverlay != null)
        {
            Color clear = fadeOverlay.color;
            clear.a = 0f;
            fadeOverlay.color = clear;
        }

        isTyping = false;
        isSkipConfirmOpen = false;
        if (skipConfirmPanel != null) skipConfirmPanel.SetActive(false);
        if (skipDialogueButton != null) skipDialogueButton.gameObject.SetActive(false);

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

    // Optional one-paragraph summary shown in the "skip dialogue" confirm
    // panel. When empty, DialogueManager builds a short excerpt from the
    // sequence's own lines instead (see ResolveSkipSummary).
    public string skipSummary = "";

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
