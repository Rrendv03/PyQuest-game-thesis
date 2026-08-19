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
            // If loading a save where it's already done, show it restored
            Restore();
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
        if (isActivated) return;
        ActivateCrystal();
    }

    private void ActivateCrystal()
    {
        isActivated = true;

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