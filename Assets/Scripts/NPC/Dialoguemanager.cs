using System;
using System.Collections;
using System.Collections.Generic;
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

    // ?????????????????????????????????????????????????????????????????????????
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

    // ?? Load dialogue.json ????????????????????????????????????????????????????
    private IEnumerator LoadRegistry()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "dialogue.json");
        string json = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                json = req.downloadHandler.text;
            else
                Debug.LogError("[DialogueManager] Failed to load dialogue.json: " + req.error);
        }
#else
        if (System.IO.File.Exists(path))
            json = System.IO.File.ReadAllText(path);
        else
            Debug.LogError("[DialogueManager] dialogue.json not found at: " + path);
        yield return null;
#endif

        registry.Clear();

        if (!string.IsNullOrEmpty(json))
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
        }

        IsRegistryLoaded = true;
        Debug.Log($"[DialogueManager] Loaded {registry.Count} sequences.");
    }

    public bool HasSequence(string sequenceID)
    {
        return registry.ContainsKey(sequenceID);
    }

    // ?? Public Entry Points ??????????????????????????????????????????????????
    public void Play(string sequenceID)
    {
        if (!registry.TryGetValue(sequenceID, out var seq))
        {
            Debug.LogError($"[DialogueManager] Unknown sequenceID: {sequenceID}");
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

    // ?? Display Line ??????????????????????????????????????????????????????????
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

    // ?? Typewriter ????????????????????????????????????????????????????????????
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

    // ?? Advance Button ????????????????????????????????????????????????????????
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

    // ?? Mode Transition ???????????????????????????????????????????????????????
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
            fadeOverlay.color = c;
            yield return null;
        }

        Color final = fadeOverlay.color;
        final.a = targetAlpha;
        fadeOverlay.color = final;
    }

    // ?? End Sequence ??????????????????????????????????????????????????????????
    private void EndCurrentSequence()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        // Restore autosave and HUD now that dialogue is no longer active
        SaveLoadManager.IsSafeToSave = true;
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);

        DialogueSequence finished = currentSequence;
        currentSequence = null;

        OnSequenceComplete?.Invoke(finished);
    }
}

// ?? Data Structures ????????????????????????????????????????????????????????????
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