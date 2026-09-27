using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class TabletMissionObject : InteractableObject
{
    [Header("Mission Identity")]
    [Tooltip("Must exactly match a missionID in MissionTabletQuests.json")]
    public string missionID;
    [Header("Visual States")]
    [Tooltip("Corrupted tablet: big parent whose mesh children scale down individually during the live restore.")]
    public GameObject defaultMesh;
    [Tooltip("Restored tablet, activated in the restore burst's place.")]
    public GameObject restoredMesh;
    [Header("HUD")]
    [Tooltip("Drag the root HUDCanvas here. Disabled during puzzle + transition.")]
    public GameObject hudCanvas;
    [Header("Notifications (auto-wired, nothing to assign)")]
    [Tooltip("Seconds the success toast stays on screen.")]
    public float successToastDuration = 3.5f;
    [Tooltip("Seconds the failure toast stays on screen.")]
    public float failureToastDuration = 2.5f;
    [Header("Live Restore Effect (replaces the old fade-to-black)")]
    [Tooltip("Seconds the corrupted meshes take to scale down to nothing during the LIVE restore. Save-load restores (Start) still swap instantly, exactly like before.")]
    [Min(0.05f)] public float restoreShrinkDuration = 1.2f;
    [Tooltip("The restore VFX + SFX fire the moment the corrupted meshes have shrunk to this fraction of their original size (0.35 = burst starts while the meshes are still about a third visible, BEFORE they disappear completely). 0 = only at the very end.")]
    [Range(0f, 1f)] public float restoreVFXAtScale = 0.35f;
    [Tooltip("Optional repair VFX prefab: ONE burst at EVERY corrupted mesh's own position, each scaled to that mesh's pre-shrink size — the same per-mesh pattern as the epilogue. Leave empty to auto-play any ParticleSystem found under restoredMesh instead.")]
    public GameObject restoreVFX;
    [Tooltip("Multiplier on each per-mesh VFX burst size (only used when restoreVFX is assigned). 1 = each burst covers the mesh it replaces.")]
    [Min(0.01f)] public float restoreVFXScaleMultiplier = 1f;
    [Tooltip("Optional repair SFX played simultaneously with the VFX. An AudioSource is added at runtime if the tablet has none — nothing to assign in the hierarchy.")]
    public AudioClip restoreSound;
    [Range(0f, 1f), Tooltip("Volume of the restore sound effect.")]
    public float restoreSoundVolume = 1f;
    private bool isCompleted = false;
    private Collider triggerCollider;
    private AudioSource audioSource;
    private readonly List<Renderer> fxDisabledRenderers = new List<Renderer>();
    // MissionTabletQuests.json loads asynchronously (UnityWebRequest on Android), so
    // only the promptText lookup waits for it; the completion check reads the in-memory set.
    void Start()
    {
        triggerCollider = GetComponent<Collider>();
        if (MissionTabletManager.Instance != null &&
            MissionTabletManager.Instance.IsMissionComplete(missionID))
        {
            SetRestoredStateImmediate();
        }
        else
        {
            SetDefaultState();
            StartCoroutine(ApplyMissionDataWhenLoaded());
        }
    }
    // Waits for MissionTabletManager to finish loading, then applies the mission prompt; gives up after 10s.
    private IEnumerator ApplyMissionDataWhenLoaded()
    {
        float waited = 0f;
        while (MissionTabletManager.Instance == null || !MissionTabletManager.Instance.IsLoaded)
        {
            waited += Time.unscaledDeltaTime;
            if (waited > 10f)
            {
                Debug.LogWarning($"[TabletMissionObject] Gave up waiting for MissionTabletManager to load " +
                                 $"MissionTabletQuests.json (missionID '{missionID}'). " +
                                 "Look for its [MissionTabletManager] load error earlier in logcat.");
                yield break;
            }
            yield return null;
        }

        var data = MissionTabletManager.Instance.GetMissionByID(missionID);
        if (data != null && !string.IsNullOrEmpty(data.promptText))
            promptText = data.promptText;
        // One-line wiring report at launch: a tablet that refuses to do its sequence is diagnosable from this alone.
        Debug.Log($"[TabletMissionObject] '{missionID}' wiring: defaultMesh={(defaultMesh != null ? defaultMesh.name : "NONE")} ({CountMeshes(defaultMesh)} meshes) | " +
                  $"restoredMesh={(restoredMesh != null ? restoredMesh.name : "NONE")} ({CountMeshes(restoredMesh)} meshes) | " +
                  $"mission in MissionTabletQuests.json: {(data != null ? "yes" : "NO — the puzzle will refuse to open, fix the missionID or add the entry")}");
        if (data == null)
            Debug.LogError($"[TabletMissionObject] missionID '{missionID}' not found in MissionTabletQuests.json " +
                           "(the file loaded successfully, so check the ID's spelling/casing in the JSON).");
    }
    private static int CountMeshes(GameObject root)
    {
        if (root == null) return 0;
        int count = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) count++;
        return count;
    }
    public override void TriggerInteraction()
    {
        if (isCompleted)
        {
            Debug.Log($"[TabletMissionObject] '{missionID}' interaction ignored — already restored (completed by an earlier session's autosave?).");
            return;
        }
        if (MissionTabletManager.Instance == null)
        {
            Debug.LogError("[TabletMissionObject] No MissionTabletManager exists in this scene.");
            return;
        }
        if (!MissionTabletManager.Instance.IsLoaded)
        {
            // Only possible in the first moments after launch; not an error.
            Debug.Log("[TabletMissionObject] Missions are still loading; interact again in a moment.");
            return;
        }
        var data = MissionTabletManager.Instance.GetMissionByID(missionID);
        if (data == null)
        {
            Debug.LogError($"[TabletMissionObject] missionID '{missionID}' not found in MissionTabletQuests.json — the puzzle cannot open. Fix the missionID or add the entry.");
            return;
        }
        // Hide HUD and prompt
        if (hudCanvas != null) hudCanvas.SetActive(false);
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        // Disable player
        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = false;
        // Random puzzle
        PuzzleType randomType = GetRandomPuzzleType();
        Debug.Log($"[TabletMissionObject] Mission {missionID} | Randomized type: {randomType}");
        PuzzleManager.Instance?.StartTabletMissionPuzzle(
            data.knowledgeComponent,
            randomType,
            OnPuzzleResolved);
    }
    private void OnPuzzleResolved(bool correct)
    {
        if (correct)
        {
            MissionTabletManager.Instance?.CompleteMission(missionID);
            // SanctumManager is the only path that logs the completion to StudentLogManager; it must be told separately.
            SanctumManager.Instance?.RegisterTabletMissionComplete(missionID);

            Debug.Log($"[TabletMissionObject] '{missionID}' solved — starting restore sequence.");
            RunRestoreSequence();
        }
        else
        {
            Debug.Log($"[TabletMissionObject] Mission {missionID} failed. Player can retry.");
            // UIManager.Notify auto-creates UIManager (with its fallback toast UI) if no scene contains one.
            UIManager.Notify("Mission failed - try again.", failureToastDuration);

            // Re-enable HUD immediately on failure (no transition needed)
            if (hudCanvas != null) hudCanvas.SetActive(true);
            PlayerMovement pm = FindObjectOfType<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }
    }
    /// <summary>
    /// Staged live restore — same pattern as RuneCrystal.RestoreAnimated / the
    /// epilogue's corrupted-mesh destruction. The corrupted meshes (children of
    /// the defaultMesh visual-state parent) scale down INDIVIDUALLY, the repair
    /// VFX + SFX fire together while they are still partially visible, and only
    /// then does the corrupted state hide and the restored state appear. The
    /// old fade-to-black is gone; the effect itself replaces it.
    /// Runs on a dedicated host object so deactivating the tablet (or any of its
    /// parents) mid-effect can no longer kill the sequence and soft-lock the player.
    /// </summary>
    private void RunRestoreSequence()
    {
        List<ShrinkTarget> targets = new List<ShrinkTarget>();
        List<GameObject> rootsToHide = new List<GameObject>();
        BuildTargets(targets, rootsToHide);
        FxHost.StartCoroutine(RestoreRoutine(targets, rootsToHide));
    }
    private void BuildTargets(List<ShrinkTarget> targets, List<GameObject> rootsToHide)
    {
        if (defaultMesh != null)
        {
            CollectShrinkTargets(defaultMesh.transform, targets);
            rootsToHide.Add(defaultMesh);
        }
        // Surrounding corruption meshes (InteractableObject.corruptionMeshes) melt away with the tablet.
        if (corruptionMeshes != null)
        {
            foreach (GameObject root in corruptionMeshes)
            {
                if (root == null || !root.activeInHierarchy) continue;
                CollectShrinkTargets(root.transform, targets);
                rootsToHide.Add(root);
            }
        }
        if (targets.Count == 0)
        {
            // The assigned defaultMesh held no mesh children (or was NONE) — target whatever is actually visible on the tablet itself.
            Debug.LogWarning($"[TabletMissionObject] '{missionID}': defaultMesh {(defaultMesh == null ? "is NONE" : "'" + defaultMesh.name + "' has no mesh renderer children")} — falling back to the tablet's own visible renderers. Check the Visual States wiring in the Inspector.");
            CollectFallbackTargets(targets);
        }
        if (targets.Count == 0)
        {
            // Nothing renderable at all: still burst VFX + SFX at the tablet's position so the repair is never silent.
            Vector3 size = triggerCollider != null ? triggerCollider.bounds.size : Vector3.one;
            targets.Add(new ShrinkTarget { transform = transform, localScale = transform.localScale, worldPosition = transform.position, worldScale = size });
            Debug.LogWarning($"[TabletMissionObject] '{missionID}': nothing renderable found — playing VFX/SFX only.");
        }
    }
    // Last-resort targeting: any active renderer on the tablet itself, excluding the restored state; falls back to the whole root.
    private void CollectFallbackTargets(List<ShrinkTarget> targets)
    {
        foreach (Renderer renderer in transform.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is ParticleSystemRenderer) continue;
            if (restoredMesh != null && renderer.transform.IsChildOf(restoredMesh.transform)) continue;
            if (IsCovered(renderer.transform, targets)) continue;
            targets.Add(new ShrinkTarget
            {
                transform = renderer.transform,
                renderer = renderer,
                localScale = renderer.transform.localScale,
                worldPosition = renderer.transform.position,
                worldScale = renderer.transform.lossyScale
            });
        }
    }
    private IEnumerator RestoreRoutine(List<ShrinkTarget> targets, List<GameObject> rootsToHide)
    {
        Debug.Log($"[TabletMissionObject] Restoring {missionID}: {targets.Count} corrupted mesh(es) over {restoreShrinkDuration:0.00}s, VFX/SFX fire at {restoreVFXAtScale:P0} remaining scale.");
        bool fxTriggered = targets.Count == 0;
        float elapsed = 0f;
        while (elapsed < restoreShrinkDuration)
        {
            // Unscaled time so a paused Time.timeScale can never stall the effect.
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(elapsed / restoreShrinkDuration));
            for (int i = 0; i < targets.Count; i++)
                targets[i].ApplyScale(k);
            // VFX + SFX fire together while the meshes are still partially visible.
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
        // Meshes gone — restore original scales (so a New Game replay re-shows corruption intact), then hide the corrupted state.
        foreach (ShrinkTarget target in targets)
            target.Restore();
        foreach (GameObject root in rootsToHide)
        {
            // Never deactivate the tablet itself (or any of its ancestors) — that would hide the restored state too.
            if (root != null && !transform.IsChildOf(root.transform)) root.SetActive(false);
        }
        // Visuals that live directly on the tablet root (or outside every hidden root) are switched off renderer-by-renderer instead.
        HideUncoveredRenderers(targets, rootsToHide);
        // Swap to the restored state (also disables the collider + clears the interact HUD entry).
        SetRestoredStateImmediate();
        // Self-heal fallback: with no restoreVFX prefab, auto-play any particles living under the restored state.
        if (restoreVFX == null && restoredMesh != null)
        {
            foreach (ParticleSystem ps in restoredMesh.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play();
        }
        // Free the player, HUD, dashboard and raise the success toast.
        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
        if (hudCanvas != null) hudCanvas.SetActive(true);
        MissionTabletUI.Instance?.Refresh();
        UIManager.Notify($"Mission complete: {GetMissionLabel()}", successToastDuration);
    }
    // Disables renderers that no hidden root covers (flat setups where the corrupted mesh sits on the tablet root itself).
    private void HideUncoveredRenderers(List<ShrinkTarget> targets, List<GameObject> rootsToHide)
    {
        foreach (ShrinkTarget target in targets)
        {
            if (target.renderer == null) continue;
            Transform tr = target.renderer.transform;
            bool covered = false;
            foreach (GameObject root in rootsToHide)
                if (root != null && !transform.IsChildOf(root.transform) && tr.IsChildOf(root.transform)) { covered = true; break; }
            if (covered) continue;
            if (restoredMesh != null && tr.IsChildOf(restoredMesh.transform)) continue;
            target.renderer.enabled = false;
            if (!fxDisabledRenderers.Contains(target.renderer)) fxDisabledRenderers.Add(target.renderer);
        }
    }
    /// <summary>Fires the repair VFX and SFX simultaneously (one burst per corrupted mesh).</summary>
    private void TriggerRestoreFX(List<ShrinkTarget> targets)
    {
        if (restoreVFX != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ShrinkTarget target = targets[i];
                if (target.transform == null) continue;

                GameObject burst = Instantiate(restoreVFX, target.worldPosition, Quaternion.identity);
                // Uniform scale from the largest dimension keeps particle systems undistorted.
                float size = Mathf.Max(target.worldScale.x, Mathf.Max(target.worldScale.y, target.worldScale.z));
                burst.transform.localScale = Vector3.one * Mathf.Max(0.01f, size * restoreVFXScaleMultiplier);
                Destroy(burst, EstimateVfxLifetime(burst));
            }
        }
        else
        {
            Debug.Log("[TabletMissionObject] No restoreVFX prefab assigned — restored-state particles (if any) auto-play instead.");
        }
        if (restoreSound != null)
            EnsureAudioSource().PlayOneShot(restoreSound, restoreSoundVolume);
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
    // Corrupted-mesh targeting: visual states are big parents whose visible meshes are children —
    // the scale-down operates on the top-most mesh per branch, the parents are only hidden at the end.
    /// <summary>Original pose of one corrupted mesh: local scale to restore + pre-shrink world pose for its VFX burst.</summary>
    private sealed class ShrinkTarget
    {
        public Transform transform;
        public Renderer renderer;
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
    /// <summary>One target per top-most mesh child of the root (deduped across roots); falls back to the root itself for flat setups.</summary>
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
                renderer = renderer,
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
                renderer = root.GetComponent<MeshRenderer>(),
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
    private void SetDefaultState()
    {
        isCompleted = false;
        // Re-enable any renderers the fallback path switched off during a previous restore in this session.
        foreach (Renderer renderer in fxDisabledRenderers)
            if (renderer != null) renderer.enabled = true;
        fxDisabledRenderers.Clear();
        if (defaultMesh != null) defaultMesh.SetActive(true);
        if (restoredMesh != null) restoredMesh.SetActive(false);
        if (triggerCollider != null) triggerCollider.enabled = true;
    }
    /// <summary>Instant swap with no FX — load-time restores only (Start / save loads).</summary>
    private void SetRestoredStateImmediate()
    {
        isCompleted = true;
        if (defaultMesh != null) defaultMesh.SetActive(false);
        if (restoredMesh != null) restoredMesh.SetActive(true);
        if (triggerCollider != null) triggerCollider.enabled = false;
        InteractButtonController hud = FindObjectOfType<InteractButtonController>();
        if (hud != null) hud.ClearInteractable(this);
    }
    /// <summary>Toast label: the mission's description from MissionTabletQuests.json, falling back to the raw missionID.</summary>
    private string GetMissionLabel()
    {
        var data = MissionTabletManager.Instance != null
            ? MissionTabletManager.Instance.GetMissionByID(missionID)
            : null;
        return data != null && !string.IsNullOrEmpty(data.description)
            ? data.description
            : missionID;
    }
    private PuzzleType GetRandomPuzzleType()
    {
        PuzzleType[] available = new PuzzleType[]
        {
            PuzzleType.TrueOrFalse,
            PuzzleType.PairACode,
            PuzzleType.FillInTheBlank,
            PuzzleType.PredictTheOutput,
            PuzzleType.SpotTheBug,
            PuzzleType.LineScramble
        };
        return available[Random.Range(0, available.Length)];
    }
    public bool IsRestored() => isCompleted;
    // Dedicated host so the effect survives the tablet (or its parents) being deactivated/destroyed mid-sequence.
    private sealed class TabletFxHost : MonoBehaviour { }
    private static TabletFxHost _fxHost;
    private static TabletFxHost FxHost
    {
        get
        {
            if (_fxHost == null) _fxHost = new GameObject("TabletFxHost").AddComponent<TabletFxHost>();
            return _fxHost;
        }
    }
}