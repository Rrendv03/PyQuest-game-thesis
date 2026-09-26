using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// EncounterEffectsManager - owns every VISUAL/AUDIO beat of an encounter (split from EncounterManager; strictly one-way
// dependency: manager -> effects; auto-added at Awake on the same GameObject). Owns: one-shot SFX (player/enemy hit,
// transition, victory/lose + sfxVolume + impact hit-pause), encounter music (fight/boss switch at start, restore at cleanup;
// playback stays on MusicManager), ice-ball VFX + ThrowIceBall, per-prefab enemy walk durations, per-prefab model facing axes
// + enemy facing, walk/attack/hit/end Animator names, and the Animator helpers (FindPlayableAnimator, PlayPlayerState,
// PlayEnemyWalkState, ResetEnemyTriggers). NO combat math, UI, BKT logging or encounter flow.
// MIGRATION: after replacing scripts, re-drag onto THIS component: playerHitSound, enemyHitSound, transitionSound,
// iceBallEffect, fightMusic, bossFightMusic (+ re-set only if changed: sfxVolume, hitPauseAfterImpact, per-prefab walk
// durations, per-prefab facing axes - goblin = Negative Z - enemyFacePlayer/Speed); animation name fields keep old defaults.
public class EncounterEffectsManager : MonoBehaviour
{
    [Header("Encounter Audio (moved from EncounterManager)")]
    [Tooltip("Played when the PLAYER strikes the enemy (correct answer, or wrong-but-enemy-dodged).")]
    public AudioClip playerHitSound;
    [Tooltip("Played when the ENEMY strikes the player (wrong answer, enemy did not dodge).")]
    public AudioClip enemyHitSound;
    [Tooltip("Played when the encounter transition starts AND again when it ends (same clip, as requested).")]
    public AudioClip transitionSound;
    [Tooltip("Played when the PLAYER WINS the encounter, at the same moment the end animation starts (after the enemy has turned to face the player). Leave empty to skip.")]
    public AudioClip victorySound;
    [Tooltip("Played when the PLAYER LOSES the encounter, at the same moment the end animation starts (after the enemy has turned to face the player). Leave empty to skip.")]
    public AudioClip loseSound;
    [Tooltip("Optional SECOND win clip layered over victorySound for a richer win moment. Leave empty to skip.")]
    public AudioClip victorySoundSecondary;
    [Tooltip("Optional SECOND loss clip layered over loseSound for a richer loss moment. Leave empty to skip.")]
    public AudioClip loseSoundSecondary;

    [Header("Answer Feedback (NEW)")]
    [Tooltip("Played the moment a correct answer is confirmed, right before the ice-ball strike plays. Leave empty to skip.")]
    public AudioClip correctAnswerSound;
    [Tooltip("Played the moment a wrong answer is confirmed, right before the enemy's walk-up strike plays. Leave empty to skip.")]
    public AudioClip wrongAnswerSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Impact Timing (moved from EncounterManager)")]
    [Tooltip("Pause on the impact moment so the hit sound/animation reads clearly.")]
    [Min(0f)] public float hitPauseAfterImpact = 0.35f;

    [Header("Encounter Music (moved from EncounterManager)")]
    [Tooltip("Music played for the whole fight. Switched to via MusicManager on encounter start, and the pre-encounter track is restored when the fight ends.")]
    public AudioClip fightMusic;
    [Tooltip("Optional boss-specific fight music. When the encounter is a Sanctum boss (isBossZone), this plays INSTEAD of fightMusic. Leave empty to reuse fightMusic for bosses.")]
    public AudioClip bossFightMusic;
    // Music switch bookkeeping (moved from EncounterManager): what was
    // playing before the fight, and whether we actually switched, so the
    // pre-encounter track can be restored exactly once at cleanup.
    private AudioClip musicBeforeEncounter;
    private bool switchedToFightMusic = false;

    [Header("Ice Ball Attack (moved from EncounterManager)")]
    [Tooltip("The player's ranged attack visual: a small sphere with a ParticleSystem.\nSETUP: create the sphere+particles anywhere in the scene and assign it here\n(preferably INACTIVE - it is only a template). At throw time an active copy is\ninstantiated at the player's model, flies point A to B into the enemy, then is\ndestroyed. The manager keeps a protected hidden copy of this template under\nitself, so the encounter flow (camera/HUD/zone switching) can never delete it.\nLeave empty to fall back to a hit with no visual (combat never stalls).")]
    public GameObject iceBallEffect;
    [Tooltip("Ice ball travel speed in units per second.")]
    [Min(1f)] public float iceBallSpeed = 10f;
    [Tooltip("Where the ball spawns relative to the player model, in the player's local space.\n(0, 1, 0.3) = roughly chest height, slightly forward, so it looks thrown from the body.")]
    public Vector3 iceBallSpawnOffset = new Vector3(0f, 1f, 0.3f);
    [Tooltip("Vertical aim offset added to the enemy's position so the ball flies at torso height instead of the floor pivot.")]
    public float iceBallAimHeight = 0.8f;
    [Header("Projectile Impact VFX (NEW)")]
    [Tooltip("VFX spawned AT THE ENEMY the exact moment the ice ball lands (normal hits). Create the burst/particles anywhere in the scene and assign it here (preferably INACTIVE - it is only a template; the manager keeps a protected hidden copy under itself, so the encounter flow can never delete it). Leave empty to skip.")]
    public GameObject iceBallImpactEffect;
    [Tooltip("Bigger VFX spawned at the enemy when a CRIT lands. Leave empty to reuse iceBallImpactEffect.")]
    public GameObject critImpactEffect;
    [Tooltip("Vertical offset for the impact VFX spawn point, measured from the enemy's ground pivot (the enemy transform's Y). 0 = burst at ground level at the enemy's feet; nudge in the Inspector if a specific enemy model's pivot sits above/below the ground. The projectile still flies to torso height (iceBallAimHeight) - only the burst position moves.")]
    public float impactVfxYOffset = -0.1f;
    [Header("Combo Crit Audio (NEW)")]
    [Tooltip("Heavier hit sound played INSTEAD of playerHitSound when a crit lands. Leave empty to reuse playerHitSound.")]
    public AudioClip critHitSound;
    [Tooltip("Crit narration / announcer clip played with the crit hit sound. Leave empty to skip.")]
    public AudioClip critNarrationSound;
    [Range(0f, 1f)] public float critVolume = 1f;

    // --- PROTECTED VFX SOURCES (impact-VFX disappearance fix) ---
    // The Inspector template GameObjects can live ANYWHERE in the hierarchy, but
    // the encounter flow deactivates/destroys large parts of the scene (gameplay
    // camera off, HUD hidden, zone teardown) and one-shot burst prefabs often
    // carry ParticleSystem 'Stop Action = Destroy'. Any of those used to delete
    // the template itself - the reference went null and SpawnImpactVfx silently
    // spawned nothing for the rest of the session ('the VFX disappears when the
    // encounter initializes'). Fix: at Awake the manager keeps its own dormant
    // copy of every template parented under ITSELF, and all runtime spawning uses
    // those protected sources. The originals are never touched afterwards.
    private GameObject iceBallSource;
    private GameObject normalImpactSource;
    private GameObject critImpactSource;

    /// <summary>Prepares the protected VFX sources. Cheap and idempotent (each
    /// line no-ops once its source exists), so it is safe to call in Awake and
    /// again at every throw/spawn to catch templates assigned late.</summary>
    private void PrepareVfxSources()
    {
        iceBallSource = MakeInternalSource(iceBallEffect, iceBallSource, "EncounterIceBallSource");
        normalImpactSource = MakeInternalSource(iceBallImpactEffect, normalImpactSource, "EncounterImpactVfxSource");
        critImpactSource = MakeInternalSource(critImpactEffect, critImpactSource, "EncounterCritImpactVfxSource");
    }
    /// <summary>Returns a dormant template source that is guaranteed to survive the
    /// whole session: the template itself when it is already parked under this
    /// manager, otherwise a private hidden clone created under it. Once a source
    /// exists it is always preferred - even if the original template is later
    /// destroyed by the encounter flow or by its own Stop Action.</summary>
    private GameObject MakeInternalSource(GameObject template, GameObject existingSource, string sourceName)
    {
        if (template == null) return existingSource; // unassigned (or destroyed since last prepare): keep what we have
        if (template.transform.IsChildOf(transform))
        {
            // Already under this manager - it survives the encounter flow here.
            // Just make sure it is dormant (a one-shot burst left active would
            // play at scene load and can self-destruct via Stop Action).
            NeutralizeTemplatePlayback(template);
            template.SetActive(false);
            return template;
        }
        if (existingSource != null) return existingSource; // protected copy already exists
        GameObject src = Instantiate(template, transform);
        src.name = sourceName;
        NeutralizeTemplatePlayback(src);
        src.SetActive(false);
        return src;
    }
    /// <summary>Stops and clears every ParticleSystem under a template copy, so a
    /// Play-On-Awake burst triggered by Instantiate's Awake can neither emit here
    /// nor destroy the template via 'Stop Action = Destroy'.</summary>
    private static void NeutralizeTemplatePlayback(GameObject src)
    {
        if (src == null) return;
        foreach (ParticleSystem ps in src.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // ENEMY WALK DURATIONS (moved) - per-prefab walk-out/walk-back seconds, because a skeleton and a goblin don't move at
    // the same speed. 0 = use EncounterManager's shared lungeDuration / lungeReturnDuration (the manager owns the
    // fallback), so existing setups keep working unchanged.
    [Header("Enemy Walk Durations (moved from EncounterManager - 0 = use shared values)")]
    [Tooltip("Seconds the Beginner enemy takes to WALK from its spot to the player. 0 = use EncounterManager's shared lungeDuration.")]
    [Min(0f)] public float beginnerEnemyWalkDuration = 0f;
    [Tooltip("Seconds the Beginner enemy takes to WALK back to its spot after the hit. 0 = use the shared lungeReturnDuration.")]
    [Min(0f)] public float beginnerEnemyWalkReturnDuration = 0f;
    [Tooltip("Seconds the Intermediate enemy takes to WALK to the player. 0 = use the shared lungeDuration.")]
    [Min(0f)] public float intermediateEnemyWalkDuration = 0f;
    [Tooltip("Seconds the Intermediate enemy takes to WALK back to its spot. 0 = use the shared lungeReturnDuration.")]
    [Min(0f)] public float intermediateEnemyWalkReturnDuration = 0f;
    [Tooltip("Seconds the Advanced enemy takes to WALK to the player. 0 = use the shared lungeDuration.")]
    [Min(0f)] public float advancedEnemyWalkDuration = 0f;
    [Tooltip("Seconds the Advanced enemy takes to WALK back to its spot. 0 = use the shared lungeReturnDuration.")]
    [Min(0f)] public float advancedEnemyWalkReturnDuration = 0f;
    [Tooltip("Seconds the Boss takes to WALK to the player. 0 = use the shared lungeDuration.")]
    [Min(0f)] public float bossEnemyWalkDuration = 0f;
    [Tooltip("Seconds the Boss takes to WALK back to its spot. 0 = use the shared lungeReturnDuration.")]
    [Min(0f)] public float bossEnemyWalkReturnDuration = 0f;

    /// <summary>(moved) Per-tier walk-out duration, or -1 when the tier has no custom value; the manager applies the shared lungeDuration fallback.</summary>
    public float GetWalkDuration(EnemyDifficultyCategory category)
    {
        switch (category)
        {
            case EnemyDifficultyCategory.Beginner: return beginnerEnemyWalkDuration;
            case EnemyDifficultyCategory.Intermediate: return intermediateEnemyWalkDuration;
            case EnemyDifficultyCategory.Advanced: return advancedEnemyWalkDuration;
            case EnemyDifficultyCategory.Boss: return bossEnemyWalkDuration;
            default: return -1f;
        }
    }
    /// <summary>(moved from EncounterManager) Walk-home counterpart of GetWalkDuration.</summary>
    public float GetWalkReturnDuration(EnemyDifficultyCategory category)
    {
        switch (category)
        {
            case EnemyDifficultyCategory.Beginner: return beginnerEnemyWalkReturnDuration;
            case EnemyDifficultyCategory.Intermediate: return intermediateEnemyWalkReturnDuration;
            case EnemyDifficultyCategory.Advanced: return advancedEnemyWalkReturnDuration;
            case EnemyDifficultyCategory.Boss: return bossEnemyWalkReturnDuration;
            default: return -1f;
        }
    }

    [Header("Enemy Walk/Attack Animation Names (moved from EncounterManager)")]
    [Tooltip("Animator trigger fired on the enemy prefab when it starts walking (toward the player, and again for the walk home). Must match a trigger in each enemy's Animator Controller. Leave empty to skip the walk animation (movement still happens).")]
    public string enemyWalkTrigger = "Walk";
    [Tooltip("Animator trigger fired on the enemy when it is back at its spot and should stop walking (e.g. back to idle). Leave empty to skip.")]
    public string enemyWalkEndTrigger = "Idle";
    [Tooltip("The NAME of the Walk state in each enemy's Animator Controller. When set, the walk animation is started with CrossFade (plays INSTANTLY, bypassing exit-time transition settings that caused the delayed first-round animation). Leave empty to fall back to the trigger fields above.")]
    public string enemyWalkStateName = "Walk";
    [Tooltip("The NAME of the Idle state the enemy crossfades back to when it arrives home. Leave empty to fall back to the trigger field above.")]
    public string enemyIdleStateName = "Idle";
    [Tooltip("The NAME of the Attack state played at the moment of impact. Leave empty to fall back to the Attack trigger.")]
    public string enemyAttackStateName = "Attack";

    [Header("Hit / End Animation Names (moved from EncounterManager)")]
    [Tooltip("Trigger fired on the enemy prefab right after it spawns (encounter-start/idle pose). Must match a trigger in each enemy's Animator Controller.")]
    public string spawnTrigger = "OnEncounterStart";
    [Tooltip("Trigger fired on whichever character is STRUCK: the enemy flinch when your ice ball lands, the player hurt when the enemy strike lands. Both controllers use the same name by default.")]
    public string takeDamageTrigger = "TakeDamage";
    [Tooltip("Fallback trigger fired on the enemy at the strike impact when its controller has no state matching enemyAttackStateName (the state is preferred because it plays instantly).")]
    public string attackTrigger = "Attack";
    [Tooltip("Trigger fired on the WINNER at the end of the fight (the enemy when it wins, the player when you win).")]
    public string victoryTrigger = "Victory";
    [Tooltip("Trigger fired on the LOSER at the end of the fight (the enemy when it dies, the player when you lose).")]
    public string deathTrigger = "Die";

    // MODEL FACING AXIS (moved) - meshes are authored facing different ways (skeleton +Z, goblin -Z); set per prefab which
    // local axis is the model's VISUAL front so the facing code turns THAT toward the player. Pick in the prefab preview:
    // face along the blue +Z arrow = Positive Z (Unity default); opposite way = Negative Z (goblin); sideways = +/-X.
    public enum ModelFacingAxis { PositiveZ, NegativeZ, PositiveX, NegativeX }
    [Header("Enemy Model Facing Axis (moved from EncounterManager)")]
    [Tooltip("Which local axis the Beginner enemy prefab's MESH visually faces. Positive Z = the Unity default (face points along the blue arrow). Negative Z = mesh was authored backwards (e.g. your goblin).")]
    public ModelFacingAxis beginnerEnemyFacingAxis = ModelFacingAxis.PositiveZ;
    [Tooltip("Which local axis the Intermediate enemy prefab's mesh visually faces. See the Beginner tooltip.")]
    public ModelFacingAxis intermediateEnemyFacingAxis = ModelFacingAxis.PositiveZ;
    [Tooltip("Which local axis the Advanced enemy prefab's mesh visually faces. See the Beginner tooltip.")]
    public ModelFacingAxis advancedEnemyFacingAxis = ModelFacingAxis.PositiveZ;
    [Tooltip("Which local axis the Boss prefab's mesh visually faces. See the Beginner tooltip.")]
    public ModelFacingAxis bossEnemyFacingAxis = ModelFacingAxis.PositiveZ;
    // Visual front of the CURRENTLY spawned enemy, set by EncounterManager at spawn from that tier's per-prefab field and
    // read every frame by the facing code (so a -Z goblin still ends up looking at the player). Runtime state, not Inspector.
    [System.NonSerialized] public ModelFacingAxis activeEnemyFacingAxis = ModelFacingAxis.PositiveZ;

    // ENEMY FACING (moved) - while an encounter is active the enemy smoothly yaws to keep facing the player (Y-flattened
    // LookRotation + Slerp, same recipe as NPCController's interaction facing). Runs every frame, so it also covers the
    // enemy's own walk - it approaches you already facing you.
    [Header("Enemy Facing (moved from EncounterManager)")]
    [Tooltip("While an encounter is active, the enemy smoothly turns on the spot to keep facing the player (same pattern as NPCController's interaction facing: Y-flattened LookRotation + Slerp).")]
    public bool enemyFacePlayer = true;
    [Tooltip("Turn speed for enemy facing. 5 matches the NPCController interaction facing.")]
    [Min(0.1f)] public float enemyFacePlayerSpeed = 5f;

    /// <summary>(moved, verbatim) Pure YAW mapping the model's declared front axis onto the GameObject's +Z:
    /// PositiveZ identity, NegativeZ 180, PositiveX -90, NegativeX +90; LookRotation(dir) * this points the mesh's VISUAL
    /// front along dir. Yaw only, so models never tilt/roll, and Positive Z (the default) is an exact no-op.</summary>
    public static Quaternion FacingAxisCorrection(ModelFacingAxis axis)
    {
        switch (axis)
        {
            case ModelFacingAxis.NegativeZ: return Quaternion.Euler(0f, 180f, 0f);
            case ModelFacingAxis.PositiveX: return Quaternion.Euler(0f, -90f, 0f);
            case ModelFacingAxis.NegativeX: return Quaternion.Euler(0f, 90f, 0f);
            default: return Quaternion.identity; // PositiveZ: no correction
        }
    }
    /// <summary>Yaw correction for the CURRENTLY spawned enemy's declared front axis.</summary>
    public Quaternion FacingCorrection
    {
        get { return FacingAxisCorrection(activeEnemyFacingAxis); }
    }
    /// <summary>(moved from EncounterManager) Which Inspector facing field belongs to the tier being spawned.</summary>
    public ModelFacingAxis FacingAxisForCategory(EnemyDifficultyCategory category)
    {
        switch (category)
        {
            case EnemyDifficultyCategory.Beginner: return beginnerEnemyFacingAxis;
            case EnemyDifficultyCategory.Intermediate: return intermediateEnemyFacingAxis;
            case EnemyDifficultyCategory.Advanced: return advancedEnemyFacingAxis;
            default: return ModelFacingAxis.PositiveZ;
        }
    }

    /// <summary>(moved from EncounterManager's Update, every frame while active) Turns the enemy to keep facing the player,
    /// applying the facing-axis correction on top of the plain look-at: without it the code would aim the GameObject's +Z at
    /// the player, which is only the visual front for +Z meshes - the offset makes a -Z goblin face you, not away.</summary>
    public void UpdateEnemyFacing(Transform enemy, Transform player)
    {
        if (enemy == null || player == null) return;
        if (!enemyFacePlayer) return;

        // Flatten the Y axis so the enemy only yaws (no tilting up/down),
        // exactly like the NPCController interaction facing.
        Vector3 direction = new Vector3(player.position.x, enemy.position.y, player.position.z) - enemy.position;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
            * FacingCorrection;
        Rigidbody enemyBody = enemy.GetComponent<Rigidbody>();
        if (enemyBody != null)
        {
            // Rigidbody path keeps interpolation consistent (same treatment
            // as the walk movement).
            enemyBody.MoveRotation(Quaternion.Slerp(
                enemyBody.rotation, targetRotation, enemyFacePlayerSpeed * Time.deltaTime));
        }
        else
        {
            enemy.rotation = Quaternion.Slerp(
                enemy.rotation, targetRotation, enemyFacePlayerSpeed * Time.deltaTime);
        }
    }

    private AudioSource sfxSource;
    private AudioSource secondarySource;
    void Awake()
    {
        // One AudioSource on this component for all encounter one-shots
        // (hit sounds, transition sound). Music stays on MusicManager.
        sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        // Dedicated second source so a secondary win/lose jingle can never be
        // swallowed by (or collide with) one-shots on the main SFX source.
        secondarySource = gameObject.AddComponent<AudioSource>();
        secondarySource.playOnAwake = false;
        // Protect the VFX templates right at load, BEFORE anything in the scene
        // can deactivate/destroy them (camera switch, HUD hide, Stop Action).
        PrepareVfxSources();
    }
    public void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume);
    }
    void PlaySecondary(AudioClip clip)
    {
        if (clip == null || secondarySource == null) return;
        secondarySource.PlayOneShot(clip, sfxVolume);
    }

    /// <summary>Plays victorySound on a player win, loseSound on a loss; called at the exact moment the end-of-fight
    /// animation starts (after the enemy has turned to face the player) so the jingle lands with the Victory/Die pose.
    /// Unassigned clips are silently skipped.</summary>
    public void PlayOutcomeSound(bool playerWon)
    {
        PlaySfx(playerWon ? victorySound : loseSound);
        PlaySecondary(playerWon ? victorySoundSecondary : loseSoundSecondary);
    }

    /// <summary>Plays the correct/wrong answer feedback clip the instant a round's answer is confirmed — before
    /// the hit sequence starts, so it reads as "you got it right/wrong", not as the impact. Unassigned = skip.</summary>
    public void PlayAnswerSound(bool answeredCorrectly)
    {
        PlaySfx(answeredCorrectly ? correctAnswerSound : wrongAnswerSound);
    }

    /// <summary>(moved - same logic, same moment) Remember MusicManager's current clip, then switch to fightMusic, or to
    /// bossFightMusic when this is a boss zone and one is assigned. Called at encounter start so the switch lands while the
    /// spiral transition still covers the screen; playback itself stays on MusicManager.</summary>
    public void ApplyEncounterMusic(bool isBoss)
    {
        musicBeforeEncounter = MusicManager.Instance.CurrentClip;
        switchedToFightMusic = false;
        // Bosses get their own track when bossFightMusic is assigned; fall back to normal fightMusic.
        AudioClip trackForThisFight = fightMusic;
        if (isBoss && bossFightMusic != null) trackForThisFight = bossFightMusic;
        if (trackForThisFight != null)
        {
            MusicManager.Instance.PlayTrack(trackForThisFight, 0.75f);
            switchedToFightMusic = true;
        }
    }

    /// <summary>(moved - same logic) Called once at cleanup: restore the pre-fight track, or stop music if none was playing.</summary>
    public void RestoreEncounterMusic()
    {
        if (!switchedToFightMusic) return;
        if (musicBeforeEncounter != null)
            MusicManager.Instance.PlayTrack(musicBeforeEncounter, 1.0f);
        else
            MusicManager.Instance.StopMusic(1.0f);
        switchedToFightMusic = false;
    }

    /// <summary>Delayed-first-round fix: returns the first Animator that actually HAS a controller assigned, so a bare
    /// wrapper Animator on the prefab root can't silently swallow SetTrigger calls. Prefers the animator that contains the
    /// encounter states (Attack/TakeDamage) over FBX-imported child Animators that carry only an idle clip.</summary>
    public static Animator FindPlayableAnimator(GameObject root)
    {
        if (root == null) return null;

        Animator fallback = null;      // has a controller, but no fight states
        Animator bare = null;          // not even a controller

        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null) continue;
            if (a.runtimeAnimatorController == null)
            {
                if (bare == null) bare = a;
                continue;
            }

            // PREFER an animator containing the encounter states: FBX imports carry a child Animator with a default
            // idle-only controller - the OLD "first with any controller" rule picked it, which is why only Idle played.
            if (a.HasState(0, Animator.StringToHash("Attack")) ||
                a.HasState(0, Animator.StringToHash("TakeDamage")))
                return a;

            if (fallback == null) fallback = a;
        }

        Animator chosen = fallback != null ? fallback : bare;
        if (chosen != null && fallback == null)
            Debug.LogWarning("[EncounterEffectsManager] FindPlayableAnimator: no Animator with an Attack/TakeDamage state was found under '" + root.name + "'. Falling back to an Animator without encounter states - encounter animations will not play until Tools > PyQuest > Create Player Animator Controller is run and its controller is assigned.", root);
        return chosen;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        return t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
    }

    /// <summary>Clears every trigger the encounter scripts can set, so a pending trigger from a previous round (the cause
    /// of animations firing at the wrong moment, e.g. only after the enemy was dragged back) is never consumed late.</summary>
    public static void ResetEnemyTriggers(Animator animator)
    {
        if (animator == null) return;
        foreach (string trig in new[] {
            "OnEncounterStart", "Attack", "TakeDamage", "Die", "Victory", "Walk", "Idle" })
        {
            animator.ResetTrigger(trig);
        }
    }

    /// <summary>Plays an enemy animation IMMEDIATELY: if the controller has a state with this name (the Walk/Idle/Attack
    /// name fields), starts it with CrossFadeInFixedTime - bypassing transitions entirely, so a "Has Exit Time" left on can
    /// no longer delay the first round. Falls back to the classic trigger, purged of stale pending triggers first.</summary>
    public static void PlayEnemyWalkState(Animator animator, string triggerName, string stateName)
    {
        if (animator == null) return;
        if (!string.IsNullOrEmpty(stateName) &&
            animator.HasState(0, Animator.StringToHash(stateName)))
        {
            ResetEnemyTriggers(animator);
            animator.CrossFadeInFixedTime(stateName, 0.05f, 0);
        }
        else if (!string.IsNullOrEmpty(triggerName))
        {
            ResetEnemyTriggers(animator);
            animator.SetTrigger(triggerName);
        }
    }
    /// <summary>Player-side counterpart of PlayEnemyWalkState: if the controller HAS the named state, start it directly
    /// with CrossFadeInFixedTime (bypasses exit-time transitions and stale triggers - the cause of player Attack /
    /// TakeDamage / Victory / Die looking dead); otherwise fall back to the same-named trigger, purged of stale triggers.</summary>
    public static void PlayPlayerState(Animator animator, string stateName)
    {
        if (animator == null) return;

        // A disabled Animator swallows CrossFade AND SetTrigger with zero
        // errors - the #1 way "only idle plays" happens with everything wired.
        if (!animator.enabled)
        {
            animator.enabled = true;
            Debug.LogWarning($"[EncounterEffectsManager] Animator on '{GetPath(animator.transform)}' was DISABLED - re-enabled it so '{stateName}' can play.", animator);
        }
        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError($"[EncounterEffectsManager] Cannot play '{stateName}': Animator on '{GetPath(animator.transform)}' has NO controller assigned.", animator);
            return;
        }

        ResetEnemyTriggers(animator);
        if (!string.IsNullOrEmpty(stateName) &&
            animator.HasState(0, Animator.StringToHash(stateName)))
        {
            animator.CrossFadeInFixedTime(stateName, 0.05f, 0);
            return;
        }

        // No such state: can we at least fire the trigger parameter?
        bool hasParam = false;
        foreach (var p in animator.parameters)
            if (p.name == stateName) { hasParam = true; break; }
        if (hasParam)
        {
            animator.SetTrigger(stateName);
            return;
        }

        Debug.LogError($"[EncounterEffectsManager] Controller '{animator.runtimeAnimatorController.name}' has no state or parameter named '{stateName}'. Check the Clip assignment report from Tools > PyQuest > Create Player Animator Controller, or the walk/attack name fields on EncounterEffectsManager.", animator);
    }

    /// <summary>Spawns an active copy of an impact template (like the ice-ball copy) at the impact point and
    /// auto-destroys it after its longest ParticleSystem finishes, so impact copies never linger in the scene.
    /// Degrades safely: null/empty template = no-op.</summary>
    private void SpawnImpactVfx(GameObject source, Vector3 position)
    {
        if (source == null) return;
        GameObject vfx = Instantiate(source, position, Quaternion.identity);
        vfx.SetActive(true);
        float lifetime = 1.5f;
        foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            lifetime = Mathf.Max(lifetime, ps.main.duration + ps.main.startLifetime.constantMax);
        Destroy(vfx, lifetime);
    }
    /// <summary>One-shot SFX with an explicit volume (crit audio can punch above the shared sfxVolume).
    /// Falls back to the normal sfxVolume path when the clip or source is missing.</summary>
    private void PlaySfxAtVolume(AudioClip clip, float volume)
    {
        if (clip == null || sfxSource == null) { PlaySfx(clip); return; }
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>The player's ranged attack: spawns an active copy of the iceBallEffect template at the player, flies it point-A-to-B
    /// into the enemy's live position (aim re-read every frame; the player's Attack state fires as the throw wind-up), then on
    /// arrival plays the hit sound + target trigger + onImpact damage. DEGRADES SAFELY (no template/player/enemy still fires the
    /// impact after a short delay); only instantiated copies fly, never the template. The HP-bar refresh stayed with the manager -
    /// pass it via onImpact (same frame as before the split).</summary>
    public IEnumerator ThrowIceBall(Transform origin, Transform enemyTransform,
        AudioClip hitSound, Animator targetAnimator, string targetTrigger, System.Action onImpact,
        bool isCrit = false)
    {
        // Re-run is a no-op unless a template was assigned late; guarantees the
        // protected sources exist even if Awake ran before the fields were set.
        PrepareVfxSources();
        // Impact point for the hit VFX: torso height on the enemy (re-captured
        // from the ball's final position after flight, so the burst lands
        // exactly where the projectile hit).
        Vector3 impactPoint = enemyTransform != null
            ? enemyTransform.position + Vector3.up * iceBallAimHeight
            : Vector3.zero;

        // --- Throw wind-up: the player's ATTACK animation, if present ---
        // Two fixes for "Attack never played": FindPlayableAnimator instead of GetComponent<Animator> (the bare wrapper
        // Animator swallowed SetTrigger), and PlayPlayerState = CrossFadeInFixedTime into the Attack state (bypasses
        // exit-time transitions) - the same proven enemy-side pattern that fixed the delayed first-round animation.
        if (origin != null)
        {
            Animator playerAnimator = FindPlayableAnimator(origin.gameObject);
            PlayPlayerState(playerAnimator, attackTrigger);
        }

        GameObject ball = null;
        if (iceBallSource != null && origin != null && enemyTransform != null)
        {
            Vector3 startPos = origin.position
                + origin.TransformDirection(iceBallSpawnOffset);
            ball = Instantiate(iceBallSource, startPos, Quaternion.identity);
            ball.SetActive(true);
        }

        if (ball != null)
        {
            float t = 0f;
            while (t < 10f) // hard safety cap; impact fires first in practice
            {
                // Enemy may have died/been destroyed mid-flight - bail out
                // to the impact block so damage still lands.
                if (enemyTransform == null) break;

                Vector3 aimPoint = enemyTransform.position
                    + Vector3.up * iceBallAimHeight;
                Vector3 toTarget = aimPoint - ball.transform.position;
                float step = iceBallSpeed * Time.deltaTime;

                // Face the direction of travel so any trail/particles align.
                if (toTarget.sqrMagnitude > 0.0001f)
                    ball.transform.rotation = Quaternion.LookRotation(toTarget.normalized);

                if (toTarget.magnitude <= step)
                {
                    ball.transform.position = aimPoint;
                    break; // arrived
                }

                ball.transform.position += toTarget.normalized * step;
                t += Time.deltaTime;
                yield return null;
            }
            impactPoint = ball.transform.position; // VFX lands exactly where the ball hit
            Destroy(ball); // the flying copy; the inactive template stays
        }
        else
        {
            // No visuals available: brief beat so the flow still reads.
            yield return new WaitForSeconds(0.15f);
        }

        // --- IMPACT: VFX + sound + enemy reaction + damage, all in one instant ---
        // NOTE: the target here is ALWAYS the enemy - the player's hurt
        // animation goes through the manager's walk-attack sequence instead.
        // Projectile-hit VFX fires exactly when the ball reaches the enemy
        // (a crit gets its own bigger burst). The burst sits at GROUND level:
        // the ball's landing X/Z is kept (so it still reads as the same hit)
        // but the height comes from the enemy's ground pivot plus the
        // Inspector-tunable impactVfxYOffset, instead of the torso-height
        // aim point the projectile flies to.
        Vector3 burstPoint = impactPoint;
        burstPoint.y = (enemyTransform != null ? enemyTransform.position.y : impactPoint.y)
            + impactVfxYOffset;
        SpawnImpactVfx(isCrit ? critImpactSource : normalImpactSource, burstPoint);
        // Crit audio: heavier hit sound + narration layered on top.
        PlaySfxAtVolume(isCrit && critHitSound != null ? critHitSound : hitSound, isCrit ? critVolume : sfxVolume);
        if (isCrit && critNarrationSound != null) PlaySfxAtVolume(critNarrationSound, critVolume);
        if (targetAnimator != null) targetAnimator.SetTrigger(targetTrigger);
        if (onImpact != null) onImpact();

        if (hitPauseAfterImpact > 0f)
            yield return new WaitForSeconds(hitPauseAfterImpact);
    }
}