using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hosts the PairACode puzzle UI: code snippet on the left, draggable option
/// cards on the right, one DropSlot for the answer. Implements
/// IDraggableOptionSounds so DraggableOption cards and DropSlot can play the
/// click / hold / return sounds here — identical wiring to
/// PredictTheOutputUIController (same hub pattern as Line Scramble).
/// </summary>
public class PairACodeUIController : MonoBehaviour, IDraggableOptionSounds
{
    [Header("Left Column")]
    public Text codeDisplayText;
    public DropSlot dropSlot;

    [Header("Right Column - Option Cards")]
    public List<GameObject> optionCards;

    [Header("Interaction Sounds")]
    [Tooltip("Played when an option card is pressed down (pointer press).")]
    public AudioClip optionClickSound;
    [Tooltip("Looped while an option card is being held/dragged; stops when the drag ends or the card lands in the drop slot.")]
    public AudioClip optionHoldSound;
    [Tooltip("Played while the card glides back to its original position after being released off the drop slot (same as Line Scramble's return sound).")]
    public AudioClip optionReturnSound;
    [Tooltip("Log every sound trigger to the Console. Turn on to diagnose missing audio.")]
    public bool debugSoundEvents = false;

    // Hub lives at the SCENE ROOT (never under the canvas/panel) so it can't
    // be deactivated with the puzzle panel, and the hold loop uses its own
    // AudioSource so stopping it can never cut off a click one-shot.
    private AudioSource oneShotSource;
    private AudioSource loopSource;

    private readonly HashSet<string> warnedMissingClips = new HashSet<string>();

    private void Awake()
    {
        InjectSoundController();
    }

    /// <summary>
    /// Called by PairACodePuzzleFormat to populate the UI.
    /// codeSnippet: full code with blank line replaced by [ ? ]
    /// options: list of option strings (correct + distractors)
    /// </summary>
    public void PopulateUI(string codeSnippet, List<string> options)
    {
        // Display code snippet on left
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Clear all option cards first
        foreach (var card in optionCards)
            card.SetActive(false);

        // Populate option cards with shuffled options
        List<string> shuffled = new List<string>(options);
        ShuffleList(shuffled);

        for (int i = 0; i < shuffled.Count && i < optionCards.Count; i++)
        {
            optionCards[i].SetActive(true);

            // Set option text
            Text label = optionCards[i].GetComponentInChildren<Text>();
            if (label != null)
                label.text = shuffled[i];

            // Set draggable option text
            DraggableOption draggable = optionCards[i].GetComponent<DraggableOption>();
            if (draggable != null)
                draggable.optionText = shuffled[i];
        }

        InjectSoundController();

        // Clear the drop slot
        if (dropSlot != null)
            dropSlot.ClearSlot();

        Debug.Log("[PairACodeUIController] UI populated");
    }

    // DraggableOption has no Inspector-wired reference to this controller,
    // so each card discovers it (works whether cards are nested anywhere
    // under this controller or siblings in the scene).
    private void InjectSoundController()
    {
        foreach (var card in optionCards)
        {
            if (card == null) continue;
            DraggableOption draggable = card.GetComponent<DraggableOption>();
            if (draggable != null)
                draggable.parentController = this;
        }
    }

    private void OnSubmit()
    {
        if (dropSlot == null || string.IsNullOrEmpty(dropSlot.currentAnswer))
        {
            Debug.LogWarning("[PairACodeUIController] No answer dropped yet");
            return;
        }

        Debug.Log($"[PairACodeUIController] Submitting answer: {dropSlot.currentAnswer}");
        PuzzleManager.Instance.UserSubmission(dropSlot.currentAnswer);
    }

    private void ShuffleList(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    // --- Interaction sounds (clips assigned in the Inspector) ---

    /// <summary>Plays the click sound. Called by DraggableOption.OnPointerDown.</summary>
    public void PlayOptionClickSound() => PlayOneShot(optionClickSound, "option click");

    /// <summary>
    /// Starts looping the hold sound. Called by DraggableOption.OnBeginDrag.
    /// The loop runs on its own AudioSource on the hub, so stopping it later
    /// can never cut off the click one-shot that started on press.
    /// </summary>
    public void StartOptionHoldSound()
    {
        if (optionHoldSound == null)
        {
            WarnMissingClip("option hold");
            return;
        }

        EnsureSfxHub();
        loopSource.clip = optionHoldSound;
        loopSource.Play();

        if (debugSoundEvents)
            Debug.Log("[PairACodeSfx] Hold loop started.");
    }

    /// <summary>Stops the looping hold sound. Called by DraggableOption.OnEndDrag, DropSlot.OnDrop and DraggableOption.OnDisable.</summary>
    public void StopOptionHoldSound()
    {
        if (loopSource == null || !loopSource.isPlaying)
            return;

        loopSource.Stop();

        if (debugSoundEvents)
            Debug.Log("[PairACodeSfx] Hold loop stopped.");
    }

    /// <summary>Plays the return sound while the card glides back home. Called by DraggableOption.OnEndDrag.</summary>
    public void PlayOptionReturnSound() => PlayOneShot(optionReturnSound, "option return");

    private void PlayOneShot(AudioClip clip, string soundName)
    {
        if (clip == null)
        {
            WarnMissingClip(soundName);
            return;
        }

        EnsureSfxHub();
        oneShotSource.PlayOneShot(clip);

        if (debugSoundEvents)
            Debug.Log($"[PairACodeSfx] Played '{soundName}' ({clip.name}).");
    }

    private void EnsureSfxHub()
    {
        if (oneShotSource != null) return;

        GameObject hub = new GameObject("PairACodeSfxHub");
        oneShotSource = hub.AddComponent<AudioSource>();
        oneShotSource.playOnAwake = false;
        oneShotSource.spatialBlend = 0f; // 2D UI sound, no positional panning

        loopSource = hub.AddComponent<AudioSource>();
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.spatialBlend = 0f;
    }

    // Warn once per unassigned clip so a missing Inspector assignment is
    // obvious in the Console instead of failing silently.
    private void WarnMissingClip(string soundName)
    {
        if (warnedMissingClips.Add(soundName))
            Debug.LogWarning(
                $"[PairACodeSfx] '{soundName}' sound was triggered but no AudioClip is assigned for it in the Inspector. " +
                $"Assign it on {name} under 'Interaction Sounds'.");
    }
}