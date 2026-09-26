using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reflects world-corruption state in MainMap: hides corrupted meshes and
/// shows their restored counterparts once the epilogue has played, matching
/// the Null Wraith's corruption being lifted from all of Aethelscript.
///
/// CAMERAS + DESTRUCTION FX (new): pairs with destructWithCameraArrival
/// enabled (default) do not swap instantly during the live epilogue. When
/// OnEpilogueCompleted fires they are parked as "pending", and the moment
/// the epilogue camera finishes panning to a showcase point aimed at them
/// (matched by the pair's destructionTriggerPoint reference, or by distance
/// within showcaseMatchRadius when no trigger is assigned) the corrupted
/// pair plays its destruction sequence: the pair root is a big container
/// whose visible crystals are children, so each crystal CHILD shakes and
/// scales down individually — the parent itself never shrinks and only
/// disappears once every child has finished scaling down. The shared
/// explosionVFX then bursts at EVERY crystal child's own position — each
/// burst scaled to that crystal's original pre-shrink size — the shared
/// explosionSound plays once at the crystals' visual center, and the
/// restored counterpart appears in its place.
/// Pending pairs the camera never visits are force-restored when
/// OnEpilogueSequenceFullyComplete fires, so the world is always fully
/// restored by the time the end screen takes over.
///
/// State is DERIVED, not stored. It reads
/// StoryProgressionManager.IsQuestComplete("epilogue_return_to_mainmap") every time it
/// runs and drives BOTH branches explicitly: corrupted meshes are actively
/// re-shown when the epilogue has NOT been played, not just hidden when it
/// has. This is what prevents world-restoration bleeding from a completed
/// save into a New Game. MainMenuController.OnNewGameClicked() already calls
/// StoryProgressionManager.ResetProgression(), which clears the same
/// completedQuestIDs ledger "epilogue_return_to_mainmap" lives in. So on a fresh
/// session the flag is false, and this script re-shows corruption instead
/// of trusting whatever active/inactive state the scene happened to be
/// left in. ApplyState() (every scene load, fresh game, continue, load slot)
/// always swaps instantly with no FX — the staged camera-synced destruction
/// only happens during the live epilogue moment.
///
/// No changes to SaveSlotData or SaveLoadManager are needed. "epilogue_return_to_mainmap"
/// already rides through the existing completedQuestIDs export/import, so
/// this state survives manual saves, autosave, and load exactly like every
/// other quest flag already does.
///
/// Place one instance in the MainMap scene.
/// </summary>
public class WorldRestorationController : MonoBehaviour
{
    public static WorldRestorationController Instance { get; private set; }

    [Tooltip("Must match EpilogueSequenceController.epilogueCompletedQuestID exactly.")]
    public string epilogueCompletedQuestID = "epilogue_return_to_mainmap";

    [Header("Corrupted Crystal Destruction FX")]
    [Tooltip("ONE shared explosion VFX prefab, instantiated at EVERY corrupted crystal child mesh's position when they finish shaking and shrinking. Each instance is scaled to that crystal's original size (before it scaled down). Leave empty to skip the VFX.")]
    public GameObject explosionVFX;

    [Tooltip("Multiplier on each per-crystal explosion burst size. 1 = each VFX spawns at the same scale its crystal mesh had before scaling down; raise it to make the bursts larger than the crystals were.")]
    [Min(0.01f)] public float explosionScaleMultiplier = 1f;

    [Tooltip("ONE shared main explosion sound effect, played at the crystal's position when the explosion VFX appears. Leave empty to skip the SFX.")]
    public AudioClip explosionSound;

    [Range(0f, 1f), Tooltip("Volume of the explosion sound effect.")]
    public float explosionSoundVolume = 1f;

    [Range(0f, 1f), Tooltip("0 = fully 2D (always audible at full volume, best for cinematic camera), 1 = fully 3D positional. Default leans 2D so the explosion is clearly heard from the showcase vantage point.")]
    public float explosionSoundSpatialBlend = 0.3f;

    [Header("Epilogue Music")]
    [Tooltip("The scene's default music source (the AudioSource playing the normal MainMap track). It is disabled the moment the epilogue starts. Leave empty to auto-find a playing looping AudioSource in the scene.")]
    public AudioSource defaultMusicSource;

    [Tooltip("Music played when the player initiates the epilogue sequence (fires when the epilogue dialogue completes and the showcase begins). Loops until the destruction music takes over. Leave empty to keep the previous music.")]
    public AudioClip epilogueMusic;

    [Tooltip("Music played the moment the first corrupted crystal starts being destroyed. Loops until the restored music takes over.")]
    public AudioClip destructionMusic;

    [Tooltip("Music that replaces the default scene music once the player presses Continue Playing on the epilogue end screen. WorldRestorationController.RequestRestoredMusic() must be called from the end screen's continue handler (one line). Leave empty to keep the destruction music.")]
    public AudioClip restoredMusic;

    [Range(0f, 1f), Tooltip("Volume of the epilogue music tracks.")]
    public float musicVolume = 0.8f;

    [Tooltip("Skybox swapped in when the player presses Continue Playing on the epilogue end screen, and reapplied automatically when loading a save in which the epilogue already played. The original scene skybox is remembered and put back on New Game / corrupted worlds. Leave empty to keep the current skybox.")]
    public Material restoredSkybox;

    /// <summary>The scene's original skybox, captured on Awake so a New Game / corrupted world can put it back.</summary>
    private Material originalSkybox;

    /// <summary>Internal 2D looping source for the epilogue music states.</summary>
    private AudioSource musicSource;

    /// <summary>True once the destruction music has taken over (so it only switches once).</summary>
    private bool destructionMusicStarted;

    /// <summary>The default music source we disabled, so a New Game can re-enable it.</summary>
    private AudioSource disabledDefaultMusic;

    /// <summary>True while a restored world is actively guarding against the default music restarting.</summary>
    private bool defaultMusicGuardActive;

    [Tooltip("How long after loading a restored world the controller keeps forcing the default scene music source to stay silent (covers sources that start playing after Start).")]
    public float defaultMusicGuardSeconds = 5f;

    [Tooltip("How long the crystal shakes before it starts shrinking.")]
    public float shakeDuration = 0.6f;

    [Tooltip("Local-space jitter amplitude while the crystals shake (fades out over shakeDuration). Applied per crystal CHILD; the parent container never moves.")]
    public float shakeIntensity = 0.15f;

    [Tooltip("How long the crystal takes to scale down to nothing after the shake.")]
    public float scaleDownDuration = 0.8f;

    [Tooltip("Extra time the epilogue camera dwells on the point after the explosion, so the burst is seen before panning on.")]
    public float postExplosionHoldTime = 0.6f;

    [Tooltip("Fallback matching: when a pair has no destructionTriggerPoint assigned, it detonates when the camera arrives at a showcase point within this many world units of the corrupted crystal.")]
    public float showcaseMatchRadius = 8f;

    [System.Serializable]
    public class CorruptionPair
    {
        [Tooltip("The corrupted version of this piece of the map. Hidden once the epilogue has played. Usually a big container parent whose children are the crystal meshes — the destruction FX shakes/scales each child individually and only hides the parent after every child has finished shrinking.")]
        public GameObject corrupted;

        [Tooltip("Optional. The restored version to show in its place. Leave empty if there is no separate restored mesh, corrupted will just be hidden with nothing swapped in.")]
        public GameObject restored;

        [Tooltip("During the LIVE epilogue, hold this pair back until the showcase camera arrives at its location, then shake -> shrink -> explosion VFX/SFX -> restored swap. Untick for corrupted pieces that are not crystals (rocks, terrain...) so they restore instantly like before.")]
        public bool destructWithCameraArrival = true;

        [Tooltip("Optional. The epilogue showcase point aimed at this crystal — when the camera arrives at THIS exact point, the crystal detonates. Leave empty to instead match any showcase point within WorldRestorationController's showcaseMatchRadius of the corrupted crystal's position.")]
        public Transform destructionTriggerPoint;

        [System.NonSerialized] public bool destructionPlayed;
    }

    [Tooltip("Every corrupted mesh in MainMap, paired with its optional restored counterpart. Assign in Inspector.")]
    public List<CorruptionPair> corruptionPairs = new List<CorruptionPair>();

    /// <summary>Pairs waiting for the epilogue camera to aim at them before detonating.</summary>
    private readonly List<CorruptionPair> pendingDestruction = new List<CorruptionPair>();

    private void Awake()
    {
        Instance = this;
        originalSkybox = RenderSettings.skybox;
    }

    /// <summary>Creates the internal looping music source on this object's GameObject, 2D so the cinematic camera never affects it.</summary>
    private AudioSource EnsureMusicSource()
    {
        if (musicSource != null) return musicSource;
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;
        return musicSource;
    }

    private void PlayMusic(AudioClip clip)
    {
        if (clip == null) return; // Keep whatever is playing when nothing is assigned.
        AudioSource source = EnsureMusicSource();
        source.volume = musicVolume;
        if (source.clip == clip && source.isPlaying) return;
        source.clip = clip;
        source.loop = true;
        source.Play();
        Debug.Log($"[WorldRestorationController] Music switched to '{clip.name}'.");
    }

    private void DisableDefaultMusic()
    {
        if (defaultMusicSource == null)
        {
            // Fallback: the scene's default music is usually the only looping
            // AudioSource that is actually playing right now.
            foreach (AudioSource source in FindObjectsOfType<AudioSource>())
            {
                if (source == null || source == musicSource || !source.isPlaying || !source.loop) continue;
                defaultMusicSource = source;
                break;
            }
        }

        if (defaultMusicSource == null)
        {
            Debug.LogWarning("[WorldRestorationController] No default scene music source found or assigned - skipping its disable.");
            return;
        }

        defaultMusicSource.Stop();
        defaultMusicSource.enabled = false;
        disabledDefaultMusic = defaultMusicSource;
        Debug.Log($"[WorldRestorationController] Default scene music '{defaultMusicSource.name}' disabled for the epilogue.");
    }

    /// <summary>
    /// After loading a restored world, keeps the default scene music source
    /// silenced for a few seconds. Scene music controllers can start (or
    /// restart) their track after our Start() runs — e.g. on sanctum re-entry
    /// — so this re-asserts silence until any late starter has had its chance.
    /// </summary>
    private void StartDefaultMusicGuard()
    {
        if (!defaultMusicGuardActive)
        {
            defaultMusicGuardActive = true;
            StartCoroutine(DefaultMusicGuardCoroutine());
        }
    }

    private System.Collections.IEnumerator DefaultMusicGuardCoroutine()
    {
        float guardEnd = Time.unscaledTime + Mathf.Max(0.1f, defaultMusicGuardSeconds);
        while (Time.unscaledTime < guardEnd)
        {
            // Re-find the source in case the auto-discovery picked nothing at Start.
            if (defaultMusicSource == null)
            {
                FindDefaultMusicSourceQuiet();
            }
            if (defaultMusicSource != null)
            {
                if (defaultMusicSource.enabled && defaultMusicSource.isPlaying)
                {
                    defaultMusicSource.Stop();
                    Debug.Log($"[WorldRestorationController] Guard stopped late-playing default scene music '{defaultMusicSource.name}'.");
                }
                defaultMusicSource.enabled = false;
                disabledDefaultMusic = defaultMusicSource;
            }
            yield return null;
        }
        defaultMusicGuardActive = false;
    }

    /// <summary>Auto-discovery for the guard that stays silent when nothing matches.</summary>
    private void FindDefaultMusicSourceQuiet()
    {
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (AudioSource source in sources)
        {
            if (source == null || source == musicSource || !source.isPlaying || !source.loop) continue;
            defaultMusicSource = source;
            break;
        }
    }

    private void StartEpilogueMusic()
    {
        if (epilogueMusic == null)
        {
            Debug.LogWarning("[WorldRestorationController] epilogueMusic is not assigned - default scene music is still silenced.");
        }

        DisableDefaultMusic();
        PlayMusic(epilogueMusic);
    }

    /// <summary>
    /// Call this from the epilogue end screen's Continue Playing button handler
    /// (e.g. "GetComponent&lt;WorldRestorationController&gt;().RequestRestoredMusic();").
    /// Replaces the epilogue music with the restored-world track that becomes
    /// the new default scene music.
    /// </summary>
    public void RequestRestoredMusic()
    {
        if (restoredMusic == null)
        {
            Debug.LogWarning("[WorldRestorationController] restoredMusic is not assigned - keeping the current epilogue music.");
        }
        else
        {
            PlayMusic(restoredMusic);
        }

        ApplyRestoredSkybox();
    }

    /// <summary>Swaps the scene skybox for the fillable restoredSkybox material.</summary>
    private void ApplyRestoredSkybox()
    {
        if (restoredSkybox == null)
        {
            Debug.LogWarning("[WorldRestorationController] restoredSkybox is not assigned - skybox unchanged.");
            return;
        }
        if (RenderSettings.skybox == restoredSkybox) return;
        RenderSettings.skybox = restoredSkybox;
        Debug.Log($"[WorldRestorationController] Skybox swapped to '{restoredSkybox.name}'.");
    }

    /// <summary>Puts the original scene skybox back (New Game / corrupted worlds).</summary>
    private void RestoreOriginalSkybox()
    {
        if (originalSkybox != null && RenderSettings.skybox != originalSkybox)
        {
            RenderSettings.skybox = originalSkybox;
            Debug.Log($"[WorldRestorationController] Skybox reverted to '{originalSkybox.name}'.");
        }
    }

    [ContextMenu("Preview: Restored Music")]
    private void DebugPreviewRestoredMusic() => RequestRestoredMusic();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        // Runs on every MainMap load (fresh game, continue, load slot,
        // returning from a sanctum) and re-derives the correct visual
        // state from the quest ledger each time. Does not assume any
        // particular Start() order relative to EpilogueSequenceController,
        // both read the same StoryProgressionManager state independently.
        ApplyState();
    }

    private void OnEnable()
    {
        EpilogueSequenceController.OnEpilogueCompleted += HandleEpilogueCompleted;
        EpilogueSequenceController.OnEpilogueSequenceFullyComplete += HandleEpilogueFullyComplete;
        EpilogueSequenceController.OnEpilogueCameraArrivedAtPoint += HandleCameraArrivedAtPoint;
    }

    private void OnDisable()
    {
        EpilogueSequenceController.OnEpilogueCompleted -= HandleEpilogueCompleted;
        EpilogueSequenceController.OnEpilogueSequenceFullyComplete -= HandleEpilogueFullyComplete;
        EpilogueSequenceController.OnEpilogueCameraArrivedAtPoint -= HandleCameraArrivedAtPoint;
    }

    private void HandleEpilogueCompleted()
    {
        // Fires mid-session, the moment the epilogue quest is marked
        // complete. Pairs flagged for camera-synced destruction stay
        // corrupted (visible) here and are detonated by HandleCameraArrivedAtPoint
        // as the post-dialogue showcase pans to them; everything else
        // restores immediately, matching the pre-existing behavior.
        bool worldRestored = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.IsQuestComplete(epilogueCompletedQuestID);

        if (!worldRestored)
        {
            ApplyState();
            return;
        }

        // The player has initiated the epilogue: silence the default scene
        // music and switch to the epilogue track.
        StartEpilogueMusic();

        int deferred = 0;
        foreach (var pair in corruptionPairs)
        {
            if (pair.corrupted == null) continue;

            if (pair.destructWithCameraArrival && !pair.destructionPlayed)
            {
                if (!pendingDestruction.Contains(pair))
                {
                    pendingDestruction.Add(pair);
                    deferred++;
                }
                // Corrupted stays visible and restored stays hidden until
                // the camera arrives and the destruction sequence completes.
            }
            else
            {
                if (pair.corrupted != null) pair.corrupted.SetActive(false);
                if (pair.restored != null) pair.restored.SetActive(true);
            }
        }

        Debug.Log($"[WorldRestorationController] Epilogue completed. Pairs restored instantly: {corruptionPairs.Count - deferred}. Pairs awaiting camera-synced destruction: {deferred}.");
    }

    private void HandleEpilogueFullyComplete()
    {
        // Safety net: if the showcase never panned to a pending pair (no
        // showcase points, or none near it), restore it instantly so the
        // world is fully clean before the end screen / control handback.
        ForceCompletePendingDestruction();
    }

    private void HandleCameraArrivedAtPoint(Transform point)
    {
        TriggerDestructionForPoint(point);
    }

    /// <summary>
    /// Reapplies corruption/restoration state from scratch based on current
    /// StoryProgressionManager state. Safe to call at any time, including
    /// repeatedly, since it always sets both branches explicitly rather
    /// than only ever hiding. Always swaps instantly with no FX — this is
    /// the derived-state path for scene loads (fresh game, continue, load
    /// slot), not the live epilogue moment.
    /// </summary>
    public void ApplyState()
    {
        bool worldRestored = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.IsQuestComplete(epilogueCompletedQuestID);

        pendingDestruction.Clear();

        foreach (var pair in corruptionPairs)
        {
            if (pair.corrupted != null) pair.corrupted.SetActive(!worldRestored);
            if (pair.restored != null) pair.restored.SetActive(worldRestored);

            // New Game / corruption returns: allow the destruction FX to
            // play again on a future epilogue in this same session.
            if (!worldRestored) pair.destructionPlayed = false;
        }

        destructionMusicStarted = false;

        if (!worldRestored)
        {
            // Fresh game / corruption returns: bring the default scene music
            // back and stop any epilogue track still playing.
            if (disabledDefaultMusic != null)
            {
                disabledDefaultMusic.enabled = true;
                disabledDefaultMusic.Play();
                disabledDefaultMusic = null;
            }
            if (musicSource != null && musicSource.isPlaying) musicSource.Stop();
            RestoreOriginalSkybox();
        }
        else
        {
            // Loading into a world where the epilogue already played: the
            // restored track IS the new default scene music, and the restored
            // skybox is reapplied. Both derive from the same saved epilogue
            // quest flag, so a finished save always comes back fully restored.
            // The default scene music is ALWAYS silenced here — even when
            // restoredMusic is unassigned — so returning from a sanctum can
            // never bring the pre-epilogue track back.
            DisableDefaultMusic();
            if (restoredMusic != null)
            {
                PlayMusic(restoredMusic);
            }
            else
            {
                Debug.LogWarning("[WorldRestorationController] restoredMusic is not assigned - the world is restored but stays silent instead of playing the default track.");
            }
            StartDefaultMusicGuard();
            ApplyRestoredSkybox();
        }

        Debug.Log($"[WorldRestorationController] Applied state. World restored: {worldRestored}. Pairs affected: {corruptionPairs.Count}");
    }

    /// <summary>
    /// How long the epilogue camera should hold on this showcase point so a
    /// pending crystal can finish shake + shrink + explosion there. Returns
    /// 0 when no pending pair matches this point. The arrival event must be
    /// fired AFTER this query, since arriving clears the pending list.
    /// </summary>
    public float GetPendingDestructionDurationForPoint(Transform point)
    {
        if (point == null || pendingDestruction.Count == 0) return 0f;

        float longest = 0f;
        foreach (var pair in pendingDestruction)
        {
            if (!MatchesArrival(pair, point)) continue;
            longest = Mathf.Max(longest, shakeDuration + scaleDownDuration + postExplosionHoldTime);
        }
        return longest;
    }

    private void TriggerDestructionForPoint(Transform point)
    {
        if (point == null || pendingDestruction.Count == 0) return;

        for (int i = pendingDestruction.Count - 1; i >= 0; i--)
        {
            var pair = pendingDestruction[i];
            if (!MatchesArrival(pair, point)) continue;

            pendingDestruction.RemoveAt(i);
            pair.destructionPlayed = true;

            if (pair.corrupted == null || !pair.corrupted.activeSelf)
            {
                // Nothing left to detonate — just guarantee the restored side is up.
                if (pair.restored != null) pair.restored.SetActive(true);
                continue;
            }

            // The crystals start being destroyed: switch to the destruction
            // music once, the first time any pair actually detonates.
            if (!destructionMusicStarted)
            {
                destructionMusicStarted = true;
                PlayMusic(destructionMusic);
            }

            StartCoroutine(DestructSequence(pair));
        }
    }

    private bool MatchesArrival(CorruptionPair pair, Transform point)
    {
        // Explicit trigger point: match by reference only.
        if (pair.destructionTriggerPoint != null)
            return pair.destructionTriggerPoint == point;

        // No trigger point assigned: proximity fallback.
        if (pair.corrupted == null) return false;
        // Match against the crystals' visual center, not the container pivot,
        // which can sit far away on a big corrupted pair parent.
        return Vector3.Distance(GetCrystalCenter(pair.corrupted.transform), point.position) <= showcaseMatchRadius;
    }

    private IEnumerator DestructSequence(CorruptionPair pair)
    {
        GameObject parent = pair.corrupted;
        if (parent == null) yield break;

        // The corrupted pair root is a big container whose visible crystal
        // meshes are children: shake + scale-down target those children
        // INDIVIDUALLY (each keeps its own original scale), the parent
        // itself never shrinks, and it is only deactivated once every
        // child has finished scaling down.
        Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true);
        List<CrystalSnapshot> crystals = new List<CrystalSnapshot>(renderers.Length);
        CollectCrystalTargets(parent, crystals);

        // SFX origin: the crystals' visual center (a big container's pivot
        // can sit far from the visible meshes), captured before the FX.
        // The VFX itself does NOT burst here - each crystal child gets its
        // own burst at its own position (step 4).
        Vector3 explosionPosition = ComputeCrystalCenter(parent.transform, renderers);

        Debug.Log($"[WorldRestorationController] Detonating corrupted pair '{parent.name}' ({crystals.Count} crystal mesh(es); shake {shakeDuration:0.00}s -> shrink {scaleDownDuration:0.00}s -> explosion).");

        // 1) Shake: each crystal child jitters around its own original local
        //    position, easing out. The parent container never moves.
        float elapsed = 0f;
        while (elapsed < shakeDuration && parent.activeSelf)
        {
            elapsed += Time.deltaTime;
            float damp = 1f - Mathf.Clamp01(elapsed / shakeDuration);
            for (int i = 0; i < crystals.Count; i++)
            {
                Transform crystal = crystals[i].transform;
                if (crystal == null) continue;
                crystal.localPosition = crystals[i].localPosition + Random.insideUnitSphere * (shakeIntensity * damp);
            }
            yield return null;
        }
        for (int i = 0; i < crystals.Count; i++)
        {
            if (crystals[i].transform != null)
                crystals[i].transform.localPosition = crystals[i].localPosition;
        }

        // 2) Scale smaller until each crystal disappears (parent scale untouched).
        elapsed = 0f;
        while (elapsed < scaleDownDuration && parent.activeSelf)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(elapsed / scaleDownDuration));
            for (int i = 0; i < crystals.Count; i++)
            {
                Transform crystal = crystals[i].transform;
                if (crystal == null) continue;
                crystal.localScale = crystals[i].localScale * k;
            }
            yield return null;
        }

        // 3) The crystals are gone — NOW the parent disappears. Every child's
        //    original pose is restored while hidden so a future New Game
        //    re-shows the corrupted meshes intact.
        parent.SetActive(false);
        foreach (var crystal in crystals)
        {
            if (crystal.transform == null) continue;
            crystal.transform.localPosition = crystal.localPosition;
            crystal.transform.localScale = crystal.localScale;
        }

        // 4) Explosion VFX + main SFX, making the disappearance read as the
        //    crystals exploding: ONE burst PER crystal child mesh, at that
        //    mesh's own captured world position, scaled to at least the size
        //    that mesh had before it scaled down. The sound stays a single
        //    main boom at the cluster's visual center.
        if (explosionVFX != null)
        {
            for (int i = 0; i < crystals.Count; i++)
            {
                CrystalSnapshot crystal = crystals[i];
                if (crystal.transform == null) continue;

                GameObject burst = Instantiate(explosionVFX, crystal.worldPosition, Quaternion.identity);
                // Uniform scale from the largest original dimension keeps the
                // particle systems undistorted while still covering (or
                // exceeding) the crystal's original size on every axis.
                float crystalSize = Mathf.Max(crystal.worldScale.x, Mathf.Max(crystal.worldScale.y, crystal.worldScale.z));
                burst.transform.localScale = Vector3.one * Mathf.Max(0.01f, crystalSize * explosionScaleMultiplier);
                Destroy(burst, EstimateVfxLifetime(burst));
            }
        }
        if (explosionSound != null)
            PlayExplosionSound(explosionPosition);

        // 5) The restored world appears in its place with the burst.
        if (pair.restored != null)
            pair.restored.SetActive(true);
    }

    private void ForceCompletePendingDestruction()
    {
        if (pendingDestruction.Count == 0) return;

        int count = pendingDestruction.Count;
        foreach (var pair in pendingDestruction)
        {
            if (pair.corrupted != null) pair.corrupted.SetActive(false);
            if (pair.restored != null) pair.restored.SetActive(true);
            pair.destructionPlayed = true;
        }
        pendingDestruction.Clear();

        Debug.Log($"[WorldRestorationController] Epilogue fully complete — force-restored {count} pair(s) the camera never detonated.");
    }

    private void PlayExplosionSound(Vector3 position)
    {
        GameObject soundGo = new GameObject("EpilogueExplosionSFX");
        soundGo.transform.position = position;

        AudioSource source = soundGo.AddComponent<AudioSource>();
        source.clip = explosionSound;
        source.volume = explosionSoundVolume;
        source.spatialBlend = explosionSoundSpatialBlend;
        source.Play();

        Destroy(soundGo, explosionSound.length + 0.1f);
    }

    private static float EstimateVfxLifetime(GameObject vfxInstance)
    {
        float longest = 0f;
        var systems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
        }
        return longest > 0f ? longest + 0.5f : 5f;
    }

    // ------------------------------------------------------------------
    // Crystal-child targeting — the corrupted pair root is a big container
    // parent; the visible crystal meshes are its children. All destruction
    // FX (shake, shrink, per-child explosion position and size) operate on
    // those children, and the parent is only hidden after every child has
    // finished shrinking.
    // ------------------------------------------------------------------

    /// <summary>
    /// Original pose of one crystal child: the local pose restored after it
    /// vanishes, plus its pre-shake WORLD position and scale, which is where
    /// — and how big — its explosion burst plays (captured before any FX
    /// move or shrink it).
    /// </summary>
    private sealed class CrystalSnapshot
    {
        public Transform transform;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector3 worldPosition;
        public Vector3 worldScale;
    }

    /// <summary>
    /// Collects one snapshot per crystal: the TOP-MOST mesh child of each
    /// crystal branch, so nested mesh parts of the same crystal shrink as a
    /// single unit and the parent container itself is never scaled. Falls
    /// back to the root itself when a pair has no child meshes (legacy/flat
    /// setups), preserving the old whole-object behavior for those.
    /// </summary>
    private static void CollectCrystalTargets(GameObject corruptedRoot, List<CrystalSnapshot> crystals)
    {
        crystals.Clear();

        Renderer[] renderers = corruptedRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

            Transform child = renderer.transform;
            if (child == corruptedRoot.transform) continue; // never scale the parent container itself

            bool covered = false;
            for (int i = 0; i < crystals.Count; i++)
            {
                if (child.IsChildOf(crystals[i].transform)) { covered = true; break; }
            }
            if (covered) continue;

            crystals.Add(new CrystalSnapshot
            {
                transform = child,
                localPosition = child.localPosition,
                localScale = child.localScale,
                worldPosition = child.position,
                worldScale = child.lossyScale
            });
        }

        if (crystals.Count == 0)
        {
            Transform rootT = corruptedRoot.transform;
            crystals.Add(new CrystalSnapshot
            {
                transform = rootT,
                localPosition = rootT.localPosition,
                localScale = rootT.localScale,
                worldPosition = rootT.position,
                worldScale = rootT.lossyScale
            });
        }
    }

    /// <summary>
    /// Visual center of the corrupted crystals (bounds center of their mesh
    /// renderers). A big container's pivot can sit far from the visible
    /// meshes, so the explosion originates where the crystals actually are.
    /// Falls back to the root position when no usable renderer bounds exist.
    /// </summary>
    private static Vector3 ComputeCrystalCenter(Transform corruptedRoot, Renderer[] renderers)
    {
        bool hasBounds = false;
        Bounds combined = default;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            if (!hasBounds) { combined = renderer.bounds; hasBounds = true; }
            else combined.Encapsulate(renderer.bounds);
        }
        return hasBounds ? combined.center : corruptedRoot.position;
    }

    /// <summary>Visual center of a corrupted pair's crystal children, for arrival matching.</summary>
    private static Vector3 GetCrystalCenter(Transform corruptedRoot)
    {
        return ComputeCrystalCenter(corruptedRoot, corruptedRoot.GetComponentsInChildren<Renderer>(true));
    }
}
