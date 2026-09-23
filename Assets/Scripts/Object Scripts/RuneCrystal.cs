using UnityEngine;

/// <summary>
/// Activates when a sanctum boss is defeated. Uses inherited sanctumID from InteractableObject.
/// </summary>
public class RuneCrystal : InteractableObject
{
    [Header("Rune Crystal")]
    [Tooltip("Parent GameObject containing all crystal meshes (auto-found if null)")]
    public GameObject crystalParent;
    [Tooltip("Optional spawn effect when activated")]
    public ParticleSystem spawnEffect;
    [Tooltip("Optional sound when activated")]
    public AudioClip spawnSound;

    [Header("Visual States")]
    public GameObject crystalDefaultState;
    public GameObject crystalRestoredState;

    private AudioSource audioSource;
    private bool isActivated = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && spawnSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }
    }

    private void Start()
    {
        // Ensure correct visual state on startup
        crystalDefaultState?.SetActive(true);
        crystalRestoredState?.SetActive(false);

        if (crystalParent == null)
            crystalParent = transform.parent?.gameObject ?? gameObject;

        bool alreadyDefeated = false;
        if (SaveLoadManager.Instance != null && SanctumManager.Instance != null)
            alreadyDefeated = SanctumManager.Instance.IsBossDefeated();

        if (alreadyDefeated)
        {
            // Ensure parent is visible before restoring (handles race condition
            // where Start() ran before SanctumManager was ready)
            if (crystalParent != null)
                crystalParent.SetActive(true);

            // BUGFIX (save-load): "boss defeated" is true for the WHOLE rest of
            // the game, including the window where the mission is still
            // "restore the crystal". The old code called Restore() unconditionally
            // here, so loading a save taken in that window lit the RESTORED mesh
            // and set isActivated without the player ever touching the crystal —
            // the interact handler then saw "already restored" and silently
            // skipped the guide's departure sequence (and the exit compass).
            //
            // Only auto-restore when the player ACTUALLY completed the restore
            // (the {sanctumID}_restore_crystal quest is how the chain advances,
            // so it is the authoritative "crystal was used" signal). Otherwise
            // show the DEFAULT (destroyed) state — exactly what OnBossDefeated()
            // does live — leaving the crystal interactable so the departure
            // sequence still plays after a save load.
            bool restoreQuestComplete =
                StoryProgressionManager.Instance != null &&
                StoryProgressionManager.Instance.IsQuestComplete($"{sanctumID}_restore_crystal");

            if (restoreQuestComplete)
            {
                Restore();
            }
            else
            {
                if (StoryProgressionManager.Instance == null)
                    Debug.LogWarning("[RuneCrystal] StoryProgressionManager unavailable; assuming the crystal was NOT restored yet (default state).");
                Debug.Log($"[RuneCrystal] Boss defeated but '{sanctumID}_restore_crystal' is still open — showing DEFAULT state so the interact/departure sequence stays available.");
            }
        }
        else
        {
            // Hide completely until boss dies
            crystalParent.SetActive(false);
        }
    }

    /// <summary>
    /// Called by SanctumManager immediately on boss defeat.
    /// ONLY reveals the crystal in its DEFAULT (destroyed) state.
    /// </summary>
    public void OnBossDefeated()
    {
        // Just make the parent visible. Start() already ensured Default is ON and Restored is OFF.
        if (crystalParent != null)
        {
            crystalParent.SetActive(true);
            Debug.Log($"[RuneCrystal] Revealed in default state in sanctum: {sanctumID}");
        }
    }

    /// <summary>
    /// Called by InteractableObject.HandleRuneCrystal() when the player
    /// manually interacts with the crystal. Swaps to RESTORED state.
    /// </summary>
    public void Restore()
    {
        if (isActivated)
        {
            // Re-apply correct state in case parent was hidden by a race condition
            if (crystalParent != null && !crystalParent.activeSelf)
                crystalParent.SetActive(true);
            crystalDefaultState?.SetActive(false);
            crystalRestoredState?.SetActive(true);
            return;
        }
        ActivateCrystal();
    }

    private void ActivateCrystal()
    {
        isActivated = true;

        // Ensure parent is visible before swapping meshes
        if (crystalParent != null && !crystalParent.activeSelf)
            crystalParent.SetActive(true);

        // Swap the meshes
        crystalDefaultState?.SetActive(false);
        crystalRestoredState?.SetActive(true);

        if (spawnEffect != null)
            spawnEffect.Play();

        if (audioSource != null && spawnSound != null)
            audioSource.PlayOneShot(spawnSound);

        Debug.Log($"[RuneCrystal] Restored in sanctum: {sanctumID}");
    }
}
