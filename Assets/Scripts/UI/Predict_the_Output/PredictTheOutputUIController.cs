using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PredictTheOutputUIController : MonoBehaviour, IDraggableOptionSounds
{
    [Header("Left - Code Display")]
    public Text codeDisplayText;

    [Header("Right - Draggable Options (exactly 3)")]
    public List<GameObject> optionCards;

    [Header("Drop Zone")]
    public DropSlot dropSlot;

    [Header("Interaction Sounds")]
    [Tooltip("Played when an option card is pressed down (pointer press).")]
    public AudioClip optionClickSound;
    [Tooltip("Looped while an option card is being held/dragged; stops when the drag ends (same pattern as Line Scramble).")]
    public AudioClip optionHoldSound;
    [Tooltip("Played while the card glides back to its original position after being released off the drop slot (same as Line Scramble's return sound).")]
    public AudioClip optionReturnSound;
    [Tooltip("Log every sound trigger to the Console. Turn on to diagnose missing audio.")]
    public bool debugSoundEvents = false;

    // Same hub pattern as LineScrambleUIController: the hub lives at the
    // SCENE ROOT (never under the canvas/panel) so it can't be deactivated
    // with the puzzle panel, and the hold loop uses its own AudioSource so
    // stopping it can never cut off a click one-shot in the same frame.
    private AudioSource oneShotSource;
    private AudioSource loopSource;

    private readonly HashSet<string> warnedMissingClips = new HashSet<string>();

    private void Awake()
    {
        // DraggableOption has no Inspector-wired reference to this controller,
        // so each card discovers it (works whether cards are nested anywhere
        // under this controller or siblings in the scene).
        foreach (var card in optionCards)
        {
            if (card == null) continue;
            DraggableOption draggable = card.GetComponent<DraggableOption>();
            if (draggable != null)
                draggable.parentController = this;
        }
    }

    public void PopulateUI(string codeSnippet, List<string> options)
    {
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Deactivate all option cards first
        foreach (var card in optionCards)
            card.SetActive(false);

        // Shuffle options
        List<string> shuffled = new List<string>(options);
        ShuffleList(shuffled);

        // Populate exactly 3 cards
        for (int i = 0; i < shuffled.Count && i < optionCards.Count; i++)
        {
            optionCards[i].SetActive(true);

            Text label = optionCards[i].GetComponentInChildren<Text>();
            if (label != null)
                label.text = shuffled[i];

            DraggableOption draggable = optionCards[i].GetComponent<DraggableOption>();
            if (draggable != null)
            {
                draggable.optionText = shuffled[i];
                draggable.parentController = this; // re-assert after repopulate
            }
        }

        // Clear drop slot
        if (dropSlot != null)
            dropSlot.ClearSlot();

        Debug.Log("[PredictTheOutputUIController] UI populated");
    }

    // ADDED: this controller had no path to PuzzleManager.UserSubmission at
    // all. PairACodeUIController (same drag-into-DropSlot pattern) has an
    // OnSubmit() doing exactly this; this file didn't. If you already wire
    // a submit button to something else that reaches PuzzleManager, this is
    // redundant and safe to ignore, but if PredictTheOutput has been hard
    // or impossible to actually submit, this was very likely why. Wire a
    // submit Button's OnClick() to this in the Inspector if it isn't
    // already hitting an equivalent path.
    public void OnSubmit()
    {
        if (dropSlot == null || string.IsNullOrEmpty(dropSlot.currentAnswer))
        {
            Debug.LogWarning("[PredictTheOutputUIController] No answer dropped yet");
            return;
        }

        Debug.Log($"[PredictTheOutputUIController] Submitting answer: {dropSlot.currentAnswer}");
        PuzzleManager.Instance.UserSubmission(dropSlot.currentAnswer);
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
            Debug.Log("[PredictTheOutputSfx] Hold loop started.");
    }

    /// <summary>Plays the return sound while the card glides back home. Called by DraggableOption.OnEndDrag.</summary>
    public void PlayOptionReturnSound() => PlayOneShot(optionReturnSound, "option return");

    /// <summary>Stops the looping hold sound. Called by DraggableOption.OnEndDrag.</summary>
    public void StopOptionHoldSound()
    {
        if (loopSource == null || !loopSource.isPlaying)
            return;

        loopSource.Stop();

        if (debugSoundEvents)
            Debug.Log("[PredictTheOutputSfx] Hold loop stopped.");
    }

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
            Debug.Log($"[PredictTheOutputSfx] Played '{soundName}' ({clip.name}).");
    }

    private void EnsureSfxHub()
    {
        if (oneShotSource != null) return;

        GameObject hub = new GameObject("PredictTheOutputSfxHub");
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
                $"[PredictTheOutputSfx] '{soundName}' sound was triggered but no AudioClip is assigned for it in the Inspector. " +
                $"Assign it on {name} under 'Interaction Sounds'.");
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
}