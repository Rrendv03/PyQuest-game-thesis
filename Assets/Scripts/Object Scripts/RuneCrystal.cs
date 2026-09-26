using System.Collections;
using System.Collections.Generic;
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

    [Header("Live Restore Effect")]
    [Tooltip("Seconds the corrupted meshes take to scale down to nothing during the LIVE restore (player interacts with the crystal). Load-time restores (RuneCrystal.Start / SanctumManager.CleanupCompletedSanctum) always swap instantly with no FX, exactly like before.")]
    [Min(0.05f)] public float restoreShrinkDuration = 1.2f;

    [Tooltip("The restore VFX + SFX fire the moment the corrupted meshes have shrunk to this fraction of their original size (0.35 = the burst starts while the meshes are still about a third visible, i.e. BEFORE they disappear completely). 0 = only at the very end.")]
    [Range(0f, 1f)] public float restoreVFXAtScale = 0.35f;

    [Tooltip("Optional VFX prefab for the restore burst. When assigned, ONE burst is spawned at EVERY corrupted mesh's own position, each scaled to that mesh's original pre-shrink size — the same per-mesh pattern as the epilogue's explosionVFX. Leave empty to reuse the existing spawnEffect instead (no new scene assignments needed).")]
    public GameObject restoreVFX;

    [Tooltip("Multiplier on each per-mesh VFX burst size (only used when restoreVFX is assigned). 1 = each burst covers the mesh it replaces; raise for larger bursts.")]
    [Min(0.01f)] public float restoreVFXScaleMultiplier = 1f;

    [Tooltip("Optional SFX played simultaneously with the VFX. Leave empty to reuse the existing spawnSound instead.")]
    public AudioClip restoreSound;

    [Range(0f, 1f), Tooltip("Volume of the restore sound effect.")]
    public float restoreSoundVolume = 1f;

    private AudioSource audioSource;
    private bool isActivated = false;
    private Coroutine restoreCoroutine;

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
    /// INSTANT restore (no FX). Load-time paths only: RuneCrystal.Start()
    /// (restore quest already complete on a save load) and
    /// SanctumManager.CleanupCompletedSanctum(). The meshes must never
    /// animate while a scene is loading — the staged scale-down/VFX/SFX is
    /// reserved for the LIVE interact moment (RestoreAnimated).
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

    /// <summary>
    /// LIVE restore, called by InteractableObject.HandleRuneCrystal() when the
    /// player actually interacts with the crystal. Same end state as Restore(),
    /// but staged like the epilogue's corrupted-crystal destruction: the
    /// corrupted meshes (children of the big visual-state parents) scale down
    /// individually, the restore VFX + SFX fire while they are still partially
    /// visible, and only then does the corrupted state hide and the restored
    /// state appear. The surrounding corruptionMeshes join the same sequence,
    /// so nothing corruption-related pops off instantly anymore.
    /// </summary>
    public void RestoreAnimated()
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

        // Nothing to animate (no default-state parent) — legacy setups fall
        // back to the instant swap + instant corruption clear, exactly as before.
        if (crystalDefaultState == null)
        {
            ActivateCrystal();
            DisableCorruptionMeshesNow();
            return;
        }

        isActivated = true;

        // Ensure parent is visible before the sequence runs
        if (crystalParent != null && !crystalParent.activeSelf)
            crystalParent.SetActive(true);

        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreSequence());
    }

    private IEnumerator RestoreSequence()
    {
        GameObject corruptedState = crystalDefaultState;

        // The visual states are big containers whose visible meshes are
        // children: the scale-down targets those children INDIVIDUALLY (each
        // keeps its own original scale), the parent itself never shrinks, and
        // it is only hidden once every child has finished scaling down — the
        // same rule as the epilogue's corrupted-pair destruction.
        List<ShrinkTarget> targets = new List<ShrinkTarget>();
        List<GameObject> rootsToHide = new List<GameObject>();

        CollectShrinkTargets(corruptedState.transform, targets);
        rootsToHide.Add(corruptedState);

        // Surrounding corruption meshes (InteractableObject.corruptionMeshes)
        // join the same staged sequence so they melt away with the crystal
        // instead of vanishing in the same frame. Entries that overlap the
        // crystal's own meshes are skipped by the dedupe inside
        // CollectShrinkTargets.
        if (corruptionMeshes != null)
        {
            foreach (GameObject root in corruptionMeshes)
            {
                if (root == null || !root.activeInHierarchy) continue;
                CollectShrinkTargets(root.transform, targets);
                rootsToHide.Add(root);
            }
        }

        Debug.Log($"[RuneCrystal] Live restore in {sanctumID}: {targets.Count} corrupted mesh(es) scale down over {restoreShrinkDuration:0.00}s, VFX/SFX fire at {restoreVFXAtScale:P0} remaining scale.");

        // 1) Scale smaller until each corrupted mesh disappears.
        bool fxTriggered = false;
        float elapsed = 0f;
        while (elapsed < restoreShrinkDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(elapsed / restoreShrinkDuration));
            for (int i = 0; i < targets.Count; i++)
                targets[i].ApplyScale(k);

            // 2) VFX + SFX fire while the meshes are still partially visible,
            //    i.e. BEFORE they disappear completely.
            if (!fxTriggered && k <= restoreVFXAtScale)
            {
                fxTriggered = true;
                TriggerRestoreFX(targets);
            }
            yield return null;
        }
        if (!fxTriggered)
        {
            fxTriggered = true;
            TriggerRestoreFX(targets);
        }

        // 3) The corrupted meshes are gone — NOW the roots disappear. Every
        //    target's original scale is restored while hidden so a future
        //    New Game / replay re-shows the corruption intact.
        foreach (ShrinkTarget target in targets)
            target.Restore();
        foreach (GameObject root in rootsToHide)
        {
            if (root != null) root.SetActive(false);
        }

        // 4) The restored crystal appears in the burst's place.
        if (crystalRestoredState != null)
            crystalRestoredState.SetActive(true);

        // Self-heal: if the fallback spawnEffect lives under the (previously
        // inactive) restored state it could not play at the VFX moment — start
        // it now that the state is visible. A dedicated restoreVFX prefab or an
        // already-playing spawnEffect is skipped by the isEmitting guard.
        if (restoreVFX == null && spawnEffect != null && crystalRestoredState != null
            && !spawnEffect.isEmitting && !spawnEffect.isPlaying
            && spawnEffect.gameObject.activeInHierarchy)
        {
            spawnEffect.Play();
        }

        restoreCoroutine = null;
        Debug.Log($"[RuneCrystal] Restored in sanctum: {sanctumID}");
    }

    /// <summary>
    /// Fires the restore VFX and SFX together, so they play simultaneously.
    /// VFX: one burst per corrupted mesh at its own pre-shrink world position
    /// (scaled to that mesh's original size) when a restoreVFX prefab is
    /// assigned; otherwise the existing scene spawnEffect plays once. SFX:
    /// restoreSound, falling back to the existing spawnSound.
    /// </summary>
    private void TriggerRestoreFX(List<ShrinkTarget> targets)
    {
        if (restoreVFX != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ShrinkTarget target = targets[i];
                if (target.transform == null) continue;

                GameObject burst = Instantiate(restoreVFX, target.worldPosition, Quaternion.identity);
                // Uniform scale from the largest original dimension keeps the
                // particle systems undistorted while still covering (or
                // exceeding) the mesh's original size on every axis.
                float size = Mathf.Max(target.worldScale.x, Mathf.Max(target.worldScale.y, target.worldScale.z));
                burst.transform.localScale = Vector3.one * Mathf.Max(0.01f, size * restoreVFXScaleMultiplier);
                Destroy(burst, EstimateVfxLifetime(burst));
            }
        }
        else if (spawnEffect != null && spawnEffect.gameObject.activeInHierarchy)
        {
            // No dedicated prefab — reuse the existing scene spawnEffect at the
            // same moment so VFX and SFX stay simultaneous.
            spawnEffect.Play();
        }
        else
        {
            Debug.Log("[RuneCrystal] No restore VFX available (restoreVFX prefab empty and spawnEffect missing/inactive) — meshes just scale down.");
        }

        AudioClip clip = restoreSound != null ? restoreSound : spawnSound;
        if (clip != null)
        {
            EnsureAudioSource().PlayOneShot(clip, restoreSoundVolume);
        }
    }

    private AudioSource EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }
        return audioSource;
    }

    private static float EstimateVfxLifetime(GameObject vfxInstance)
    {
        float longest = 0f;
        ParticleSystem[] systems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
        }
        return longest > 0f ? longest + 0.5f : 5f;
    }

    // ------------------------------------------------------------------
    // Corrupted-mesh targeting — the visual states are big parents whose
    // visible corrupted meshes are children. The scale-down operates on
    // those children (top-most mesh per branch, deduped across roots) and
    // the parents are only hidden after every child has finished shrinking.
    // ------------------------------------------------------------------

    /// <summary>
    /// Original pose of one corrupted mesh: the local scale restored after it
    /// vanishes, plus its pre-shrink WORLD position and scale, which is where
    /// — and how big — its VFX burst plays (captured before anything shrinks).
    /// </summary>
    private sealed class ShrinkTarget
    {
        public Transform transform;
        public Vector3 localScale;
        public Vector3 worldPosition;
        public Vector3 worldScale;

        public void ApplyScale(float k)
        {
            if (transform == null) return;
            transform.localScale = localScale * k;
        }

        public void Restore()
        {
            if (transform == null) return;
            transform.localScale = localScale;
        }
    }

    /// <summary>
    /// Collects one target per top-most mesh child of the root so nested mesh
    /// parts of the same piece shrink as a single unit and the parent itself
    /// is never scaled. Falls back to the root itself when it has no child
    /// meshes (legacy/flat setups). Already-covered candidates (same piece
    /// reached through another root, e.g. corruptionMeshes overlapping the
    /// crystal's own meshes) are skipped.
    /// </summary>
    private static void CollectShrinkTargets(Transform root, List<ShrinkTarget> targets)
    {
        if (root == null) return;

        bool addedAny = false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

            Transform child = renderer.transform;
            if (child == root) continue; // never scale the parent container itself
            if (IsCovered(child, targets)) continue;

            targets.Add(new ShrinkTarget
            {
                transform = child,
                localScale = child.localScale,
                worldPosition = child.position,
                worldScale = child.lossyScale
            });
            addedAny = true;
        }

        if (!addedAny && !IsCovered(root, targets))
        {
            // Legacy/flat setup: no mesh children, shrink the root itself.
            targets.Add(new ShrinkTarget
            {
                transform = root,
                localScale = root.localScale,
                worldPosition = root.position,
                worldScale = root.lossyScale
            });
        }
    }

    /// <summary>True when t is already covered by (or covers) an accepted target.</summary>
    private static bool IsCovered(Transform t, List<ShrinkTarget> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            Transform other = targets[i].transform;
            if (other == null) continue;
            if (t == other || t.IsChildOf(other) || other.IsChildOf(t)) return true;
        }
        return false;
    }

    /// <summary>
    /// INSTANT swap to the RESTORED state (no staged FX). Used by the
    /// legacy fallback inside RestoreAnimated and by every load-time
    /// Restore() call, where animating during a scene load would be wrong.
    /// </summary>
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