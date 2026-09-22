using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LineScrambleUIController : MonoBehaviour
{
    [Header("Slot Container")]
    public RectTransform slotContainer;
    public List<GameObject> slotObjects;

    [Header("UI")]
    public Text instructionText;
    public Button checkButton;

    [Header("Interaction Sounds")]
    [Tooltip("Played when a slot is pressed down (pointer press).")]
    public AudioClip slotPressSound;
    [Tooltip("Looped while a slot is being dragged; stops when the drag ends.")]
    public AudioClip slotDragSound;
    [Tooltip("Played when a drop commits a swap between the dragged slot and the slot under it.")]
    public AudioClip slotSwapSound;
    [Tooltip("Played when a released slot animates back to the position it was dragged from (drag ended without a swap).")]
    public AudioClip slotReturnSound;
    [Tooltip("Played when the Check/execute button is pressed.")]
    public AudioClip executeButtonSound;
    [Tooltip("Log every sound trigger to the Console. Turn on to diagnose missing audio.")]
    public bool debugSoundEvents = false;

    // All audio plays through a dedicated "hub" created at the SCENE ROOT,
    // deliberately NOT parented under the canvas/panel: it can never be
    // deactivated along with the puzzle panel (a correct Check submission
    // deactivates it the same frame the execute sound starts) and nothing in
    // the slot container hierarchy can affect it. The drag loop and the
    // one-shots use SEPARATE AudioSources so stopping the loop can never cut
    // off a one-shot that started in the same frame (OnDrop plays the swap
    // sound, OnEndDrag stops the loop right after).
    private AudioSource oneShotSource;
    private AudioSource loopSource;

    // Warn once per unassigned clip instead of failing silently.
    private readonly HashSet<string> warnedMissingClips = new HashSet<string>();

    // Single source of truth: ordered list of slots as they currently
    // appear visually, top to bottom. Index in this list = visual position.
    private List<LineScrambleSlot> orderedSlots = new List<LineScrambleSlot>();

    // While a swap animation is running, sibling indices are NOT yet updated:
    // updating them mid-animation would let the VerticalLayoutGroup snap
    // every slot instantly and kill the animation. They are applied once all
    // slides finish (SyncLayoutAfterSwaps), or immediately when interrupted
    // (FlushPendingLayoutSync). The animated end positions already match what
    // the layout group computes for the new order, so the refresh is a no-op
    // visually.
    private bool layoutSyncPending;
    private Coroutine layoutSyncCoroutine;

    // --- Interaction sounds (clips assigned in the Inspector) ---

    /// <summary>Plays the slot press sound. Called by LineScrambleSlot.OnPointerDown.</summary>
    public void PlaySlotPressSound() => PlayOneShot(slotPressSound, "slot press");

    /// <summary>
    /// Starts looping the drag sound. Called by LineScrambleSlot.OnBeginDrag.
    /// The loop uses its own AudioSource on the hub, so stopping it later can
    /// never cut off the swap one-shot that starts in OnDrop the same frame.
    /// </summary>
    public void StartSlotDragSound()
    {
        if (slotDragSound == null)
        {
            WarnMissingClip("slot drag");
            return;
        }

        EnsureSfxHub();
        loopSource.clip = slotDragSound;
        loopSource.Play();

        if (debugSoundEvents)
            Debug.Log("[LineScrambleSfx] Drag loop started.");
    }

    /// <summary>Stops the looping drag sound. Called by LineScrambleSlot.OnEndDrag and PopulateUI.</summary>
    public void StopSlotDragSound()
    {
        if (loopSource == null || !loopSource.isPlaying)
            return;

        loopSource.Stop();

        if (debugSoundEvents)
            Debug.Log("[LineScrambleSfx] Drag loop stopped.");
    }

    /// <summary>Plays the swap sound. Called by SwapByReference when a swap commits.</summary>
    public void PlaySlotSwapSound() => PlayOneShot(slotSwapSound, "slot swap");

    /// <summary>Plays the return sound. Called by LineScrambleSlot.OnEndDrag when a drag ends with no swap and the slot glides back home.</summary>
    public void PlaySlotReturnSound() => PlayOneShot(slotReturnSound, "slot return");

    /// <summary>Plays the execute button sound. Called by OnCheckPressed.</summary>
    public void PlayExecuteSound() => PlayOneShot(executeButtonSound, "execute button");

    /// <summary>
    /// All one-shots play through the hub's dedicated AudioSource via
    /// PlayOneShot, which allows overlapping voices — a sound can never be
    /// cut off by another sound starting or stopping in the same frame.
    /// </summary>
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
            Debug.Log($"[LineScrambleSfx] Played '{soundName}' ({clip.name}).");
    }

    private void EnsureSfxHub()
    {
        if (oneShotSource != null) return;

        GameObject hub = new GameObject("LineScrambleSfxHub");
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
        if (warnedMissingClips.Contains(soundName)) return;
        warnedMissingClips.Add(soundName);
        Debug.LogWarning(
            $"[LineScrambleSfx] '{soundName}' sound was triggered but no AudioClip is assigned for it in the Inspector. " +
            "Assign it under Interaction Sounds on the LineScrambleUIController.");
    }

    public void PopulateUI(List<string> shuffledLines, List<int> shuffledRowNumbers)
    {
        // A fresh populate invalidates any in-flight swap animation and any
        // deferred layout sync from the previous puzzle. It also ends any
        // drag loop still running from a drag that never saw OnEndDrag.
        StopSlotDragSound();
        layoutSyncPending = false;
        if (layoutSyncCoroutine != null)
        {
            StopCoroutine(layoutSyncCoroutine);
            layoutSyncCoroutine = null;
        }

        orderedSlots = new List<LineScrambleSlot>();

        if (instructionText != null)
            instructionText.text = "Drag the code to other code to reorder them, then press Execute.";

        foreach (var obj in slotObjects)
            obj.SetActive(false);

        for (int i = 0; i < shuffledLines.Count && i < slotObjects.Count; i++)
        {
            slotObjects[i].SetActive(true);
            slotObjects[i].transform.SetParent(slotContainer, false); // parenting happens once, here only
            slotObjects[i].transform.SetSiblingIndex(i);


            LineScrambleSlot slot = slotObjects[i].GetComponent<LineScrambleSlot>();
            if (slot != null)
            {
                slot.Setup(shuffledRowNumbers[i], shuffledLines[i], this);
                orderedSlots.Add(slot);
            }
        }

        AdjustSpacing(shuffledLines.Count);
        RefreshLayoutPositions();

        if (checkButton != null)
        {
            checkButton.onClick.RemoveAllListeners();
            checkButton.onClick.AddListener(OnCheckPressed);
        }

        Debug.Log($"[LineScrambleUIController] Populated {shuffledLines.Count} lines | " +
                  $"Order: {string.Join(", ", GetCurrentRowNumbers())}");
    }

    /// <summary>
    /// Swaps two slots' positions in the orderedSlots list, then plays the
    /// swap as an animation instead of snapping:
    ///   - the dragged slot (a) glides from where the player released it
    ///     into the target slot's (b) position,
    ///   - the replaced target slot (b) slides to where the dragged slot was
    ///     sitting BEFORE the player started dragging it.
    /// Sibling indices are refreshed only after every slide has finished
    /// (SyncLayoutAfterSwaps), because changing them while animating lets
    /// the VerticalLayoutGroup instantly snap all slots back.
    /// This is the ONLY place that changes visual order — no sibling
    /// index tricks, no reparenting during the swap itself.
    /// </summary>
    public void SwapByReference(LineScrambleSlot a, LineScrambleSlot b)
    {
        int idxA = orderedSlots.IndexOf(a);
        int idxB = orderedSlots.IndexOf(b);

        if (idxA < 0 || idxB < 0)
        {
            Debug.LogError($"[LineScrambleUIController] Swap failed, slot not found in orderedSlots. idxA={idxA}, idxB={idxB}");
            return;
        }

        // Capture BEFORE the logical swap:
        //   draggedOrigin  : where the dragged slot (a) was sitting before the
        //                    player grabbed it -> the replaced slot's (b) destination.
        //   targetPosition : the target slot's (b) current position -> the
        //                    dragged slot's (a) destination.
        Vector3 draggedOrigin = a.DragStartWorldPosition;
        Vector3 targetPosition = b.CurrentWorldPosition;

        orderedSlots[idxA] = b;
        orderedSlots[idxB] = a;

        // The drop committed a swap: the dragged slot's OnEndDrag (which fires
        // right after OnDrop in the same frame) must not snap it back home.
        a.NotifySwapAnimationStarted();

        a.SlideTo(targetPosition);   // dragged slot -> the slot it dropped onto
        b.SlideTo(draggedOrigin);    // replaced slot -> dragged slot's original position

        PlaySlotSwapSound();

        // Defer the sibling-index/layout refresh until both slides finish.
        layoutSyncPending = true;
        if (layoutSyncCoroutine != null)
            StopCoroutine(layoutSyncCoroutine);
        layoutSyncCoroutine = StartCoroutine(SyncLayoutAfterSwaps());

        Debug.Log($"[LineScrambleUIController] Swapped positions {idxA} and {idxB} | " +
                  $"New order: {string.Join(", ", GetCurrentRowNumbers())}");
    }

    /// <summary>
    /// Waits until no slot is sliding anymore, then applies the deferred
    /// RefreshLayoutPositions so the VerticalLayoutGroup's computed layout
    /// matches the positions the animations just landed on.
    /// </summary>
    private IEnumerator SyncLayoutAfterSwaps()
    {
        while (AnySlotAnimating())
            yield return null;

        layoutSyncCoroutine = null;

        if (layoutSyncPending)
        {
            layoutSyncPending = false;
            RefreshLayoutPositions();
        }
    }

    private bool AnySlotAnimating()
    {
        foreach (var slot in orderedSlots)
        {
            if (slot != null && slot.IsAnimating)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Immediately applies any deferred sibling-index refresh. Called when a
    /// player grabs a slot mid-animation: the interrupted slide would never
    /// run to completion, so the layout must be re-synced right away to keep
    /// sibling order, orderedSlots and on-screen positions consistent.
    /// The layout is forced to rebuild synchronously so the caller (which is
    /// about to snapshot its drag origin) sees the final corrected positions
    /// immediately instead of one frame later.
    /// </summary>
    public void FlushPendingLayoutSync()
    {
        if (!layoutSyncPending) return;

        layoutSyncPending = false;
        if (layoutSyncCoroutine != null)
        {
            StopCoroutine(layoutSyncCoroutine);
            layoutSyncCoroutine = null;
        }

        RefreshLayoutPositions();

        if (slotContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(slotContainer);
    }

    /// <summary>
    /// Repositions every slot's transform.SetSiblingIndex to match
    /// its index in orderedSlots. This is the single point of truth for
    /// translating logical order into visual order via the container's
    /// VerticalLayoutGroup. All slots share the same parent (slotContainer)
    /// at all times, they are never reparented during drag/drop.
    /// NOTE: this snaps positions instantly, so during a swap animation it
    /// is deferred until the slides finish — see SwapByReference.
    /// </summary>
    private void RefreshLayoutPositions()
    {
        for (int i = 0; i < orderedSlots.Count; i++)
        {
            // Only reorder sibling index, never reparent.
            // All slots are already permanent children of slotContainer.
            orderedSlots[i].transform.SetSiblingIndex(i);
        }
    }

    private List<int> GetCurrentRowNumbers()
    {
        List<int> result = new List<int>();
        foreach (var slot in orderedSlots)
            result.Add(slot.originalRowNumber);
        return result;
    }

    /// <summary>
    /// CHANGED: used to compute correctness locally as "does position i
    /// hold original row i for every i", which is exact-match-to-one-order
    /// checking and rejects any valid alternate ordering (e.g. two
    /// independent variable inits swapped). Now submits the player's
    /// actual arranged order and lets LineScramblePuzzleFormat's
    /// dependency-graph check decide, via PuzzleManager. CheckAnswerCorrect
    /// is called first purely so the green "correct" highlight can still
    /// be shown before UserSubmission deactivates the canvas panel, same
    /// sequencing as before, just backed by the real check now.
    /// </summary>
    private void OnCheckPressed()
    {
        // Play regardless of correctness: the sound belongs to the button
        // press itself, not to the result.
        PlayExecuteSound();

        List<int> proposedOrder = GetCurrentRowNumbers();

        bool isCorrect = PuzzleManager.Instance.CheckAnswerCorrect(proposedOrder);

        Debug.Log($"[LineScrambleUIController] Check pressed | Correct: {isCorrect} | " +
                  $"Current order: {string.Join(", ", proposedOrder)}");

        if (isCorrect)
            foreach (var slot in orderedSlots)
                slot.SetState_Correct();

        PuzzleManager.Instance.UserSubmission(proposedOrder);
    }

    private void AdjustSpacing(int count)
    {
        if (slotContainer == null) return;

        VerticalLayoutGroup layout = slotContainer.GetComponent<VerticalLayoutGroup>();
        if (layout == null) return;

        layout.spacing = GetSpacingForCount(count);
    }

    private float GetSpacingForCount(int count)
    {
        switch (count)
        {
            case 2: return 40f;
            case 3: return 30f;
            case 4: return 20f;
            case 5: return 15f;
            case 6: return 10f;
            case 7: return 5f;
            default: return 20f;
        }
    }
}
