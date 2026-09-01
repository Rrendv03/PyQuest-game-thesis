using UnityEngine;

/// <summary>
/// Attach to the SAME GameObject as SceneTransition on each sanctum
/// entrance in MainMap. Locks the entrance until the required previous
/// sanctum's boss has been defeated, enforcing linear sanctum progression.
///
/// Leave requiredPreviousSanctumID empty on the first sanctum in the
/// progression (Print Console) since it has no prerequisite.
///
/// This disables the shared Collider itself (not just the SceneTransition
/// script) when locked. A disabled Collider cannot send trigger events to
/// ANY script on the GameObject, which removes any dependency on which
/// scripts are present or their execution order.
///
/// Optional: assign proximityTrigger to a second, slightly larger trigger
/// Collider (on a child object, always enabled) if you want a "locked"
/// message to show when the player approaches a sealed entrance. If left
/// null, a locked entrance just silently blocks with no feedback.
///
/// Does not modify SceneTransition.cs or StoryProgressionManager.cs.
/// Re-checks in Start() each time MainMap loads, which covers the unlock
/// case automatically since SanctumManager.ExitSequence() already reloads
/// MainMap on every sanctum exit.
/// </summary>
[RequireComponent(typeof(SceneTransition))]
public class SanctumEntranceLock : MonoBehaviour
{
    [Tooltip("sanctumID (matches MissionTabletQuests.json / SanctumManager.sanctumID) " +
             "that must be cleared before this entrance opens. Leave empty for the first sanctum.")]
    public string requiredPreviousSanctumID;

    [Header("Locked Feedback (optional)")]
    public GameObject lockedVisual;
    public GameObject unlockedVisual;
    public string lockedMessage = "This sanctum is sealed. Clear the previous sanctum first.";

    [Tooltip("Optional second trigger Collider (child object, always enabled, isTrigger = true, " +
             "sized slightly larger than the gate collider) used only to show the locked message. " +
             "Leave null to block silently with no feedback.")]
    public Collider proximityTrigger;

    private Collider gateCollider;
    private bool unlocked;

    void Awake()
    {
        // The gate collider is whichever Collider sits on THIS GameObject,
        // shared with SceneTransition. proximityTrigger, if assigned, is a
        // separate Collider (typically on a child) and is untouched here.
        gateCollider = GetComponent<Collider>();
    }

    void Start()
    {
        RefreshLockState();
    }

    private void RefreshLockState()
    {
        unlocked = string.IsNullOrEmpty(requiredPreviousSanctumID)
            || (StoryProgressionManager.Instance != null
                && StoryProgressionManager.Instance.HasDefeatedBoss(requiredPreviousSanctumID));

        // Disabling the Collider (not the script) means no script on this
        // GameObject, SceneTransition or otherwise, can receive a trigger
        // event through it while locked.
        if (gateCollider != null) gateCollider.enabled = unlocked;

        if (lockedVisual != null) lockedVisual.SetActive(!unlocked);
        if (unlockedVisual != null) unlockedVisual.SetActive(unlocked);

        Debug.Log($"[SanctumEntranceLock] {gameObject.name} | Requires: " +
                  $"'{requiredPreviousSanctumID}' | Unlocked: {unlocked} | " +
                  $"GateCollider.enabled: {(gateCollider != null ? gateCollider.enabled.ToString() : "NULL")}");
    }

    // Only wired to proximityTrigger, a SEPARATE collider from the gate,
    // in the Inspector. Never wire this to the gate collider itself, since
    // that collider is disabled while locked and won't call this anyway.
    void OnTriggerEnter(Collider other)
    {
        if (unlocked) return;
        if (!other.CompareTag("Player")) return;

        UIManager.Instance?.ShowNotification(lockedMessage, 3f);
        Debug.Log($"[SanctumEntranceLock] Player approached locked entrance " +
                  $"{gameObject.name}: requires '{requiredPreviousSanctumID}' boss defeated.");
    }
}