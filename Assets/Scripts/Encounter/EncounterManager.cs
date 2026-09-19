using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// EncounterManager - combat state, encounter flow, UI and camera logic.
// All visual/audio beats (music, SFX, VFX, walk durations, facing axes,
// animation helpers) live on EncounterEffectsManager (one-way dep, timing unchanged).
public class EncounterManager : MonoBehaviour
{
    public static EncounterManager Instance;
    [Header("Combat Stats")]
    public PlayerCombatStats playerStats = new PlayerCombatStats();
    [Header("UI References")]
    public GameObject encounterPanel;
    [Tooltip("Image with Image Type = Filled, Fill Method = Horizontal (like the XP bar).")]
    public Image playerHPFill;
    [Tooltip("Image with Image Type = Filled, Fill Method = Horizontal (like the XP bar).")]
    public Image enemyHPFill;
    public Text playerHPText;
    public Text enemyHPText;
    public Text enemyNameText;
    public Text roundInfoText;
    public Text combatLogText;

    [Header("UI Transition Setup")]
    public Image spiralTransitionOverlay;
    // NOTE (split): the encounter music (fightMusic / bossFightMusic + the
    // switch/restore bookkeeping) AND the hit/transition SFX clips +
    // sfxVolume live on EncounterEffectsManager on this same GameObject.
    [Header("Hit Moment (NEW)")]
    [Tooltip("Root GameObject of the puzzle UI shown during a round (the panel PuzzleManager fills). Hidden for the hit moment after each answer and re-shown with the next round. Leave empty to skip hiding (functionality unaffected).")]
    public GameObject puzzleUIRoot;
    [Tooltip("Seconds the attacker takes to travel toward the target.")]
    [Min(0.05f)] public float lungeDuration = 0.18f;
    [Tooltip("Seconds the attacker takes to return to its combat position after the impact.")]
    [Min(0.05f)] public float lungeReturnDuration = 0.25f;
    [Tooltip("How far toward the target the attacker travels (1 = exactly on top of it).")]
    [Range(0.5f, 0.95f)] public float lungeReach = 0.8f;
    // (split) hitPauseAfterImpact, per-prefab walk durations, facing settings
    // and model facing axes live on EncounterEffectsManager; the shared
    // lunge/lungeReturn durations stay as the 0-value fallback (rule unchanged).
    [Header("3D Enemy Prefab Registry")]
    public GameObject beginnerEnemyPrefab;
    public GameObject intermediateEnemyPrefab;
    public GameObject advancedEnemyPrefab;
    [Header("3D Boss Prefab")]
    [Tooltip("Assign your unique Boss mesh/prefab here. It overrides standard enemies when isBossZone is true.")]
    public GameObject bossEnemyPrefab;
    [Tooltip("The name displayed in the UI during a boss fight.")]
    public string bossEnemyName = "Null Wraith";
    // (split) ModelFacingAxis enum + per-prefab axis fields live on
    // EncounterEffectsManager; read via Effects at spawn and in the
    // walk-home / end-of-fight turns.
    [Header("Cinematic Camera Framing")]
    public Camera mainGameplayCamera;
    public Camera cinematicEncounterCamera;
    public Vector3 cameraCombatOffset = new Vector3(0f, 0.5f, 2f);
    public Vector3 cameraCombatRotation = new Vector3(-10f, 8f, 10f);
    private EnemyData currentEnemy;
    private int currentEnemyHP;
    private int currentRound;
    private float currentEscalationMultiplier;
    private string currentKnowledgeComponent;
    private string currentSanctumID;
    private PuzzleType currentRoundFormat;
    private string currentEncounterID;
    private int puzzlesCorrectThisEncounter;
    private string currentPuzzleID;
    private List<string> roundPuzzleIDs = new List<string>();
    private List<bool> roundResults = new List<bool>();
    private List<float> roundPGuessValues = new List<float>();
    private bool encounterActive = false;
    private bool standardVictoryOutcome = false;
    private bool isBossEncounter = false;
    private GameObject spawnedEnemyInstance;
    // (split) the enemy's current facing axis is runtime state on
    // EncounterEffectsManager (set at spawn). True while the enemy is mid
    // walk-back: Update()'s facing is suppressed so it can 180-turn and walk away.
    private bool enemyWalkingHome = false;
    private DifficultyTier lockedEncounterTier;
    private PlayerMovement activePlayerMovement;
    private Vector3 playerInitialPosition;
    private Quaternion playerInitialRotation;
    private ZoneTrigger activeSourceZone;
    // --- Audio/animation runtime state ---
    // (split) music bookkeeping (ApplyEncounterMusic/RestoreEncounterMusic)
    // lives on EncounterEffectsManager; the manager only says WHEN.
    private bool resolvingRound = false; // guards double-resolution while the hit sequence plays
    // Tracks the last 2 puzzle formats served, so GetRandomPuzzleFormat can
    // avoid a 3rd consecutive repeat without needing a bigger history
    // system than that.
    private Queue<PuzzleType> recentFormats = new Queue<PuzzleType>();
    // --- NEW (split): the presentation component (SFX, VFX, animation
    // helpers). Lives on this same GameObject; added automatically if
    // missing so existing scenes keep working without any setup. ---
    public EncounterEffectsManager Effects { get; private set; }
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // (split) wire the effects component (SFX, VFX, animator helpers),
        // auto-added when absent. AddComponent runs its Awake immediately, so
        // its AudioSource is ready before the first PlaySfx.
        Effects = GetComponent<EncounterEffectsManager>();
        if (Effects == null) Effects = gameObject.AddComponent<EncounterEffectsManager>();
    }
    // ENEMY FACING - the enemy keeps facing the player all fight (covers its
    // walk-in too). (split) the Slerp math + settings live on
    // EncounterEffectsManager (UpdateEnemyFacing); this hook passes flow state only.
    private void Update()
    {
        if (!encounterActive) return;
        if (enemyWalkingHome) return;
        Effects.UpdateEnemyFacing(
            spawnedEnemyInstance != null ? spawnedEnemyInstance.transform : null,
            activePlayerMovement != null ? activePlayerMovement.transform : null);
    }
    // WALK DURATION RESOLVERS - per-prefab walk-out/back values live on
    // EncounterEffectsManager; shared lunge durations stay as the fallback
    // (0 on a per-prefab field = use shared, exactly as before).
    private float ActiveEnemyWalkDuration()
    {
        if (currentEnemy == null) return lungeDuration;
        float perPrefab = Effects.GetWalkDuration(currentEnemy.category); // -1 = tier not customized
        return perPrefab > 0f ? perPrefab : lungeDuration;
    }
    private float ActiveEnemyWalkReturnDuration()
    {
        // NOTE: evaluated ONCE, before the walk attack starts, so the
        // per-prefab value cannot be lost mid-sequence.
        if (currentEnemy == null) return lungeReturnDuration;
        float perPrefab = Effects.GetWalkReturnDuration(currentEnemy.category); // -1 = tier not customized
        return perPrefab > 0f ? perPrefab : lungeReturnDuration;
    }
    public bool IsEncounterActive()
    {
        return encounterActive;
    }
    public void StartEncounter(
        EnemyDifficultyCategory category,
        string knowledgeComponent,
        bool isBossZone,
        Vector3 enemyPos,
        Quaternion enemyRot,
        PlayerMovement playerMove,
        Vector3 playerCombatPos,
        Quaternion playerCombatRot,
        ZoneTrigger sourceZone)
    {
        StartCoroutine(EncounterSequence(category, knowledgeComponent, isBossZone, enemyPos, enemyRot, playerMove, playerCombatPos, playerCombatRot, sourceZone));
    }
    private void BringOverlayToAbsoluteFront()
    {
        if (spiralTransitionOverlay == null) return;
        spiralTransitionOverlay.gameObject.SetActive(true);
        Canvas overlayCanvas = spiralTransitionOverlay.GetComponent<Canvas>();
        if (overlayCanvas == null)
        {
            overlayCanvas = spiralTransitionOverlay.gameObject.AddComponent<Canvas>();
        }
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 32000;
    }
    private IEnumerator EncounterSequence(
        EnemyDifficultyCategory category,
        string knowledgeComponent,
        bool isBossZone,
        Vector3 enemyPos,
        Quaternion enemyRot,
        PlayerMovement playerMove,
        Vector3 playerCombatPos,
        Quaternion playerCombatRot,
        ZoneTrigger sourceZone)
    {
        activePlayerMovement = playerMove;
        activeSourceZone = sourceZone;
        standardVictoryOutcome = false;
        isBossEncounter = isBossZone;
        // Fix: was the raw scene name ("PrintConsole"), which didn't match the
        // "print_console" snake_case ID ZoneTrigger uses; both paths now go
        // through the one canonical mapper.
        currentSanctumID = ZoneTrigger.GetSanctumIDFromScene();
        roundResults = new List<bool>();
        roundPGuessValues = new List<float>();
        roundPuzzleIDs = new List<string>();
        // Remember the current track for restore-at-end, then switch to fight
        // (or boss) music while the spiral still covers the screen, so the change
        // lands with the transition. (split) clips + bookkeeping live on Effects.
        Effects.ApplyEncounterMusic(isBossEncounter);
        // Transition sound (start). One-shot, fire and forget.
        // (split) now lives on EncounterEffectsManager.
        Effects.PlaySfx(Effects.transitionSound);
        // Hide HUD and interact button during encounter
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);
        InteractButtonController interact = FindObjectOfType<InteractButtonController>();
        if (interact != null)
            interact.ForceHide();
        SaveLoadManager.IsSafeToSave = false;
        SaveRestrictionEnforcer.Instance?.AddBlocker("encounter");
        if (activePlayerMovement != null)
        {
            playerInitialPosition = activePlayerMovement.transform.position;
            playerInitialRotation = activePlayerMovement.transform.rotation;
            activePlayerMovement.enabled = false;
        }
        if (spiralTransitionOverlay != null)
        {
            BringOverlayToAbsoluteFront();
            spiralTransitionOverlay.transform.localScale = Vector3.zero;
            float duration = 1.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                spiralTransitionOverlay.transform.rotation = Quaternion.Euler(0, 0, t * 720f);
                spiralTransitionOverlay.transform.localScale = Vector3.Lerp(Vector3.zero, new Vector3(2.5f, 2.5f, 1f), t);
                yield return null;
            }
        }
        if (activePlayerMovement != null)
        {
            activePlayerMovement.transform.position = playerCombatPos;
            activePlayerMovement.transform.rotation = playerCombatRot;
        }
        // --- CAMERA SWITCH ---
        if (mainGameplayCamera != null)
            mainGameplayCamera.gameObject.SetActive(false);
        if (cinematicEncounterCamera != null)
        {
            cinematicEncounterCamera.gameObject.SetActive(true);
            Vector3 combatCenter = (playerCombatPos + enemyPos) * 0.5f;
            cinematicEncounterCamera.transform.position = combatCenter + cameraCombatOffset;
            cinematicEncounterCamera.transform.rotation = Quaternion.Euler(cameraCombatRotation);
        }
        currentEnemy = EnemyData.CreateForCategory(category);
        currentEnemyHP = currentEnemy.maxHP;
        currentRound = 0;
        currentEscalationMultiplier = 1.0f;
        currentKnowledgeComponent = knowledgeComponent;
        lockedEncounterTier = PCGEngine.Instance.GetTierForMasteryPublic(
            BKTEngine.Instance.GetMastery(knowledgeComponent));
        roundResults = new List<bool>();
        encounterActive = true;
        puzzlesCorrectThisEncounter = 0;
        playerStats.Initialize();
        if (spawnedEnemyInstance != null)
            Destroy(spawnedEnemyInstance);

        currentEncounterID = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        StudentLogManager.Instance?.StartEncounterTracking(currentEncounterID);

        if (isBossEncounter && SanctumManager.Instance != null)
            SanctumManager.Instance.OnBossEncounterStarted();

        // --- ENEMY PREFAB SELECTION ---
        GameObject prefabToSpawn = null;
        if (isBossEncounter)
        {
            prefabToSpawn = bossEnemyPrefab;
            currentEnemy.enemyName = bossEnemyName;
            if (bossEnemyPrefab == null)
                Debug.LogError("[EncounterManager] Boss triggered, but bossEnemyPrefab is not assigned in the Inspector!");
        }
        else
        {
            switch (category)
            {
                case EnemyDifficultyCategory.Beginner: prefabToSpawn = beginnerEnemyPrefab; break;
                case EnemyDifficultyCategory.Intermediate: prefabToSpawn = intermediateEnemyPrefab; break;
                case EnemyDifficultyCategory.Advanced: prefabToSpawn = advancedEnemyPrefab; break;
            }
            // Remember which way this tier's mesh visually faces so the facing
            // code (Slerp + spawn rotation) can correct for backwards/sideways
            // models. (split) the axis fields live on EncounterEffectsManager.
            Effects.activeEnemyFacingAxis = Effects.FacingAxisForCategory(category);
        }
        if (prefabToSpawn != null)
        {
            // Counter-rotate the spawn by the facing-axis offset so its VISUAL
            // front matches the zone enemy; a -Z goblin then spawns correctly
            // instead of spin-drifting around to face you.
            spawnedEnemyInstance = Instantiate(prefabToSpawn, enemyPos,
                enemyRot * Effects.FacingCorrection);
            // Use FindPlayableAnimator (the Animator that HAS a controller), not
            // a root wrapper that can silently swallow triggers.
            // (split) the helper is static on EncounterEffectsManager now.
            Animator enemyAnimator = EncounterEffectsManager.FindPlayableAnimator(spawnedEnemyInstance);
            if (enemyAnimator != null)
            {
                EncounterEffectsManager.ResetEnemyTriggers(enemyAnimator); // purge any stale triggers from a destroyed previous enemy
                // (split) the spawn trigger name lives on EncounterEffectsManager.
                enemyAnimator.SetTrigger(Effects.spawnTrigger);
            }
        }
        if (encounterPanel != null) encounterPanel.SetActive(true);
        UpdateHPDisplay();
        UpdateCombatLog($"A {currentEnemy.enemyName} appeared!");
        if (spiralTransitionOverlay != null)
        {
            float fadeDuration = 0.3f;
            float fadeElapsed = 0f;
            CanvasGroup group = spiralTransitionOverlay.GetComponent<CanvasGroup>();
            if (group != null)
            {
                while (fadeElapsed < fadeDuration)
                {
                    fadeElapsed += Time.deltaTime;
                    group.alpha = Mathf.Lerp(1f, 0f, fadeElapsed / fadeDuration);
                    yield return null;
                }
            }
            spiralTransitionOverlay.gameObject.SetActive(false);
            if (group != null) group.alpha = 1f;
        }
        StartNextRound();
    }
    private void StartNextRound()
    {
        if (!encounterActive) return;
        currentRound++;
        UpdateRoundInfo();
        currentRoundFormat = GetRandomPuzzleFormat();

        currentPuzzleID = $"{currentEncounterID}_r{currentRound}";
        StudentLogManager.Instance?.StartPuzzleTracking(currentPuzzleID);

        PuzzleManager.Instance.OnZoneEntered(
            currentKnowledgeComponent, currentRoundFormat, lockedEncounterTier);
    }
    // --- ROUND RESOLUTION WITH AUDIO + PHYSICAL HIT SEQUENCE ---
    /// <summary>Public signature unchanged (PuzzleManager calls this); work moved
    /// into a coroutine so the hit sequence plays out before the next puzzle.</summary>
    public void OnPuzzleResolved(bool playerAnsweredCorrectly, float pGuessOverride)
    {
        // Guard against a second resolve arriving while the hit sequence
        // is still playing (fast double submit, etc).
        if (!encounterActive || resolvingRound) return;
        resolvingRound = true;
        StartCoroutine(RoundResolution(playerAnsweredCorrectly, pGuessOverride));
    }
    private IEnumerator RoundResolution(bool playerAnsweredCorrectly, float pGuessOverride)
    {
        // --- Immediate bookkeeping (exactly as before, so research logging
        // and BKT inputs are not delayed by the animation) ---
        roundResults.Add(playerAnsweredCorrectly);
        roundPGuessValues.Add(pGuessOverride);
        roundPuzzleIDs.Add(currentPuzzleID);
        if (playerAnsweredCorrectly) puzzlesCorrectThisEncounter++;
        StudentLogManager.Instance?.LogPuzzleComplete(
            currentPuzzleID,
            currentRoundFormat.ToString(),
            currentKnowledgeComponent,
            lockedEncounterTier.ToString(),
            playerAnsweredCorrectly,
            "",
            "",
            currentSanctumID,
            wasTabletMission: false);

        // Hide the puzzle UI for the hit moment. Re-enabled right
        // before StartNextRound (or before EndEncounter it is simply left
        // hidden, because the encounter panel hides right after anyway).
        if (puzzleUIRoot != null) puzzleUIRoot.SetActive(false);

        if (playerAnsweredCorrectly)
        {
            // PLAYER STRIKES ENEMY: ice ball player->enemy, impact (hit sound +
            // enemy flinch + damage), then next round (replaces the old player
            // lunge). (split) ThrowIceBall lives on EncounterEffectsManager.
            int damage = 0;
            yield return StartCoroutine(Effects.ThrowIceBall(
                activePlayerMovement != null ? activePlayerMovement.transform : null,
                spawnedEnemyInstance != null ? spawnedEnemyInstance.transform : null,
                Effects.playerHitSound,
                EnemyAnimator(),
                Effects.takeDamageTrigger, // (split) hit animation names live on EncounterEffectsManager
                () =>
                {
                    damage = CalculatePlayerDamage();
                    currentEnemyHP = Mathf.Max(0, currentEnemyHP - damage);
                    UpdateCombatLog($"Correct! You dealt {damage} damage to the enemy.");
                    // (split) moved from inside ThrowIceBall: the HP bar
                    // refresh stays with the manager. Same frame as before.
                    UpdateHPDisplay();
                }));
        }
        else
        {
            bool enemyDodged = Random.value < currentEnemy.dodgeChance;
            if (!enemyDodged)
            {
                // ENEMY STRIKES PLAYER: enemy winds up (Attack trigger),
                // walks at the player, impact = enemy hit sound + damage.
                int enemyDamage = 0;
                // Walk-to-attack: the enemy walks up with its walk animation
                // (per-prefab durations), strikes, turns 180 and walks back.
                // Trigger names + playable-animator pick come from Effects.
                Animator enemyWalkAnimator = spawnedEnemyInstance != null
                    ? EncounterEffectsManager.FindPlayableAnimator(spawnedEnemyInstance) : null;
                yield return StartCoroutine(PerformWalkAttack(
                    spawnedEnemyInstance != null ? spawnedEnemyInstance.transform : null,
                    GetPlayerPosition(),
                    Effects.enemyHitSound,
                    PlayerAnimator(),
                    Effects.takeDamageTrigger, // (split) hit animation names live on EncounterEffectsManager
                    ActiveEnemyWalkDuration(),
                    ActiveEnemyWalkReturnDuration(),
                    enemyWalkAnimator,
                    Effects.enemyWalkTrigger,
                    Effects.enemyWalkEndTrigger,
                    () =>
                    {
                        enemyDamage = CalculateEnemyDamage();
                        playerStats.currentHP = Mathf.Max(0, playerStats.currentHP - enemyDamage);
                        UpdateCombatLog($"Wrong! Enemy dealt {enemyDamage} damage to you.");
                    }));
            }
            else
            {
                // Enemy dodged: the player still connects for reduced damage,
                // so the ice ball throw and hit sound still play.
                int playerDamage = 0;
                yield return StartCoroutine(Effects.ThrowIceBall(
                    activePlayerMovement != null ? activePlayerMovement.transform : null,
                    spawnedEnemyInstance != null ? spawnedEnemyInstance.transform : null,
                    Effects.playerHitSound,
                    EnemyAnimator(),
                    Effects.takeDamageTrigger, // (split) hit animation names live on EncounterEffectsManager
                    () =>
                    {
                        playerDamage = 10;
                        currentEnemyHP = Mathf.Max(0, currentEnemyHP - playerDamage);
                        UpdateCombatLog($"Wrong! But you still dealt {playerDamage} damage.");
                        UpdateHPDisplay();
                    }));
            }
        }
        UpdateHPDisplay();
        resolvingRound = false;

        if (currentEnemyHP <= 0) { EndEncounter(true); yield break; }
        if (playerStats.currentHP <= 0) { EndEncounter(false); yield break; }

        if (currentRound >= currentEnemy.escalationStartRound)
        {
            currentEscalationMultiplier = Mathf.Min(
                currentEnemy.escalationCap,
                currentEscalationMultiplier + currentEnemy.escalationPerRound);
        }

        // Bring the puzzle UI back just before the next round asks
        // PuzzleManager to show a puzzle, so whichever of the two owns the
        // panel's visibility, it ends up visible.
        if (puzzleUIRoot != null) puzzleUIRoot.SetActive(true);
        StartNextRound();
    }
    // ENEMY WALK-TO-ATTACK - dedicated coroutine (not a PerformLunge overload,
    // so parameter orders can't be confused): Walk -> approach -> Attack+impact
    // -> 180 turn -> Walk home -> Idle. Movement stays here; anim/SFX go to Effects.
    private IEnumerator PerformWalkAttack(Transform mover, Vector3 targetPos,
        AudioClip hitSound, Animator targetAnimator, string targetTrigger,
        float walkDuration, float walkReturnDuration,
        Animator walkAnimator, string walkTrigger,
        string walkEndTrigger, System.Action onImpact)
    {
        // Guard against a misconfigured per-prefab duration (0 would freeze
        // the walk mid-step): fall back to the shared lunge durations.
        float travelTime = walkDuration > 0.01f ? walkDuration : lungeDuration;
        float returnTime = walkReturnDuration > 0.01f ? walkReturnDuration : lungeReturnDuration;
        bool hasWalkAnim = walkAnimator != null &&
            (!string.IsNullOrEmpty(Effects.enemyWalkStateName) || !string.IsNullOrEmpty(walkTrigger));
        bool hasWalkEndAnim = walkAnimator != null &&
            (!string.IsNullOrEmpty(Effects.enemyIdleStateName) || !string.IsNullOrEmpty(walkEndTrigger));
        Vector3 homePos = mover != null ? mover.position : Vector3.zero;
        Vector3 strikePos = Vector3.Lerp(homePos, targetPos, lungeReach);

        // The attacker's own "Attack" trigger fires at the IMPACT, not wind-up
        // (at wind-up it would compete with "Walk" and cut the walk animation
        // off before the enemy has even started moving).

        // --- Start WALKING toward the target (walk animation starts
        // FIRST and INSTANTLY via CrossFade so an exit-time transition in
        // the controller can never delay the first round) ---
        if (hasWalkAnim) EncounterEffectsManager.PlayEnemyWalkState(walkAnimator, walkTrigger, Effects.enemyWalkStateName);
        if (mover != null)
        {
            Rigidbody moverBody = mover.GetComponent<Rigidbody>();
            float t = 0f;
            while (t < travelTime)
            {
                t += Time.deltaTime;
                Vector3 p = Vector3.Lerp(homePos, strikePos, Mathf.Clamp01(t / travelTime));
                SetMoverPosition(mover, moverBody, p);
                yield return null;
            }
            SetMoverPosition(mover, moverBody, strikePos);
        }

        // --- IMPACT: sound + attack animation + target reaction + damage,
        // all in one instant ---
        Effects.PlaySfx(hitSound);
        // The attacker's attack animation lands with the hit itself
        // (instant CrossFade to the Attack state when the state name is set).
        Animator moverAnimator = mover != null ? EncounterEffectsManager.FindPlayableAnimator(mover.gameObject) : null;
        if (moverAnimator != null)
            EncounterEffectsManager.PlayEnemyWalkState(moverAnimator, Effects.attackTrigger, Effects.enemyAttackStateName);
        // Target is the PLAYER: PlayPlayerState CrossFades the hurt state in
        // directly (SetTrigger could sit pending behind an exit-time transition
        // and never play - the bug that hid the player's Attack/Victory/Die).
        if (targetAnimator != null)
            EncounterEffectsManager.PlayPlayerState(targetAnimator, targetTrigger);
        if (onImpact != null) onImpact();
        UpdateHPDisplay();

        if (Effects.hitPauseAfterImpact > 0f)
            yield return new WaitForSeconds(Effects.hitPauseAfterImpact);

        // --- WALK home. Turn 180 so the model walks AWAY (a real exit, not a
        // moonwalk), restart the walk animation, keep the walk rotation locked
        // until home; enemyWalkingHome suppresses Update()'s facing meanwhile. ---
        enemyWalkingHome = true;
        try
        {
            if (hasWalkAnim) EncounterEffectsManager.PlayEnemyWalkState(walkAnimator, walkTrigger, Effects.enemyWalkStateName);
            if (mover != null)
            {
                Rigidbody moverBody = mover.GetComponent<Rigidbody>();
                Vector3 returnStartPos = moverBody != null ? moverBody.position : mover.position;
                // While walking home: look from the live position toward the home
                // spot, with the same facing-axis correction used everywhere else
                // so a -Z goblin still visually walks forward.
                Vector3 homeDir = homePos - mover.position;
                homeDir.y = 0f;
                Quaternion walkHomeRotation = homeDir.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(homeDir.normalized, Vector3.up)
                        * Effects.FacingCorrection
                    : mover.rotation;
                // Linear Lerp between FIXED points (strike pos -> home), like the
                // walk-out: re-lerping from the live position eased exponentially
                // and visually ignored walkReturnDuration.
                float t = 0f;
                while (t < returnTime)
                {
                    t += Time.deltaTime;
                    Vector3 p = Vector3.Lerp(returnStartPos, homePos, Mathf.Clamp01(t / returnTime));
                    SetMoverPosition(mover, moverBody, p);
                    SetMoverRotation(mover, moverBody,
                        Quaternion.Slerp(moverBody != null ? moverBody.rotation : mover.rotation,
                            // (split) turn speed lives on EncounterEffectsManager.
                            walkHomeRotation, Effects.enemyFacePlayerSpeed * Time.deltaTime));
                    yield return null;
                }
                SetMoverPosition(mover, moverBody, homePos);
                SetMoverRotation(mover, moverBody, walkHomeRotation);
            }
            if (hasWalkEndAnim) EncounterEffectsManager.PlayEnemyWalkState(walkAnimator, walkEndTrigger, Effects.enemyIdleStateName);
        }
        finally
        {
            // Always clear the flag (even if the enemy was destroyed
            // mid-walk) so the face-the-player code resumes next frame.
            enemyWalkingHome = false;
        }
    }

    /// <summary>Writes the position through the Rigidbody when one exists (keeps
    /// interpolation consistent); Transform fallback for non-physics movers.</summary>
    private static void SetMoverPosition(Transform mover, Rigidbody body, Vector3 position)
    {
        if (body != null) body.position = position;
        else mover.position = position;
    }
    /// <summary>Rotation counterpart of SetMoverPosition - writes through the
    /// Rigidbody when one exists so interpolation stays consistent.</summary>
    private static void SetMoverRotation(Transform mover, Rigidbody body, Quaternion rotation)
    {
        if (body != null) body.MoveRotation(rotation);
        else mover.rotation = rotation;
    }
    // Target animator for impact reactions: the enemy fires "TakeDamage" when
    // struck (as before); the player's animator is optional - null is fine, the
    // walk + hit sound carry the moment.
    private Animator EnemyAnimator()
    {
        return spawnedEnemyInstance != null
            ? EncounterEffectsManager.FindPlayableAnimator(spawnedEnemyInstance) : null;
    }

    private Animator PlayerAnimator()
    {
        // Skip the bare wrapper Animator on the player prefab root (no controller
        // -> swallows SetTrigger). FindPlayableAnimator prefers the animator with
        // the Attack/TakeDamage states, so the idle-only FBX controller can't win.
        return activePlayerMovement != null
            ? EncounterEffectsManager.FindPlayableAnimator(activePlayerMovement.gameObject) : null;
    }
    private Vector3 GetEnemyPosition()
    {
        return spawnedEnemyInstance != null
            ? spawnedEnemyInstance.transform.position : Vector3.zero;
    }
    private Vector3 GetPlayerPosition()
    {
        return activePlayerMovement != null
            ? activePlayerMovement.transform.position : Vector3.zero;
    }
    private int CalculatePlayerDamage()
    {
        float bonusMultiplier = 1.0f;
        switch (currentEnemy.category)
        {
            case EnemyDifficultyCategory.Beginner: bonusMultiplier = 1.02f; break;
            case EnemyDifficultyCategory.Intermediate: bonusMultiplier = 1.05f; break;
            case EnemyDifficultyCategory.Advanced: bonusMultiplier = 1.08f; break;
            case EnemyDifficultyCategory.Boss: bonusMultiplier = 1.15f; break;
        }
        return Mathf.RoundToInt(playerStats.GetTotalAttack() * bonusMultiplier);
    }
    private int CalculateEnemyDamage() { return Mathf.RoundToInt(currentEnemy.baseAttack * currentEscalationMultiplier); }
    private void EndEncounter(bool playerWon)
    {
        encounterActive = false;
        standardVictoryOutcome = playerWon;
        // Boss rounds still never move the LIVE BKT estimate (unchanged), but
        // used to be skipped entirely - no mastery_update row at all. Now every
        // round logs an opportunity row (for boss rounds the mastery is unchanged).
        {
            double runningMastery = BKTEngine.Instance.GetMastery(currentKnowledgeComponent);
            for (int i = 0; i < roundResults.Count; i++)
            {
                double previousMastery = runningMastery;
                if (!isBossEncounter)
                {
                    BKTEngine.Instance.UpdateMastery(
                        currentKnowledgeComponent,
                        roundResults[i],
                        roundPGuessValues[i]);
                    runningMastery = BKTEngine.Instance.GetMastery(currentKnowledgeComponent);
                }

                string pid = i < roundPuzzleIDs.Count ? roundPuzzleIDs[i] : "";
                StudentLogManager.Instance?.LogMasteryUpdate(
                    currentKnowledgeComponent, previousMastery, runningMastery, roundResults[i], pid);
            }
        }

        // xpAwarded stays 0: the real XP is computed downstream in
        // ZoneTrigger.OnEncounterCompleted / XPManager and never flows back here
        // (left as-is pending your confirmation to restructure that hand-off).
        List<string> kcsTested = new List<string> { currentKnowledgeComponent };
        StudentLogManager.Instance?.LogEncounterComplete(
            currentEncounterID,
            currentEnemy.enemyName,
            currentEnemy.category.ToString(),
            currentSanctumID,
            playerWon,
            roundResults.Count,
            puzzlesCorrectThisEncounter,
            0,
            kcsTested);
        string outcomeMessage = playerWon
            ? $"Victory! You defeated the {currentEnemy.enemyName}!"
            : $"Defeated! The {currentEnemy.enemyName} won this round.";
        UpdateCombatLog(outcomeMessage);
        // Die/Victory must wait out the walk-home leg + face-the-player turn
        // before firing (resolvingRound holds meanwhile; player pos cached NOW,
        // before the enemy-win path can disable it; encounterActive off after).
        Vector3 endPlayerPos = activePlayerMovement != null
            ? activePlayerMovement.transform.position
            : (spawnedEnemyInstance != null ? spawnedEnemyInstance.transform.position : Vector3.zero);
        resolvingRound = true;
        StartCoroutine(FireEndTriggerWhenFacing(playerWon, endPlayerPos));
    }

    /// <summary>Waits for the walk-home leg to finish, actively re-aligns the
    /// enemy with the player (timeout so it can never hang), then plays
    /// Die/Victory and ends the encounter.</summary>
    private IEnumerator FireEndTriggerWhenFacing(bool playerWon, Vector3 endPlayerPos)
    {
        // 1. Let any in-flight walk-home leg finish (flag is cleared in its
        //    finally block, so this is safe even if the enemy was destroyed).
        float guard = 10f;
        while (enemyWalkingHome && guard > 0f)
        {
            guard -= Time.deltaTime;
            yield return null;
        }

        // 2. ACTIVELY turn the enemy to face the player. Update()'s Slerp bails
        // out when the player object is disabled/destroyed (enemy-win path), so
        // the turn is done here with the cached position - works on both paths.
        float turnGuard = 2f;
        while (turnGuard > 0f && spawnedEnemyInstance != null)
        {
            Transform enemyT = spawnedEnemyInstance.transform;
            Vector3 direction = new Vector3(endPlayerPos.x, enemyT.position.y, endPlayerPos.z) - enemyT.position;
            if (direction.sqrMagnitude < 0.0001f) break;
            // Same facing-axis correction as the facing Slerp: aim the
            // model's VISUAL front at the player, not the raw +Z.
            // (split) correction + speed read from EncounterEffectsManager.
            Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Effects.FacingCorrection;
            if (Quaternion.Angle(enemyT.rotation, desired) < 3f) break;
            // Mirror the facing Slerp exactly (Rigidbody path keeps
            // interpolation consistent, Transform path otherwise).
            Rigidbody endBody = enemyT.GetComponent<Rigidbody>();
            if (endBody != null)
                endBody.MoveRotation(Quaternion.Slerp(
                    endBody.rotation, desired, Effects.enemyFacePlayerSpeed * Time.deltaTime));
            else
                enemyT.rotation = Quaternion.Slerp(
                    enemyT.rotation, desired, Effects.enemyFacePlayerSpeed * Time.deltaTime);
            turnGuard -= Time.deltaTime;
            yield return null;
        }

        // 3. Fire the end animation on the Animator that HAS the controller (a
        // bare wrapper Animator once silently swallowed the trigger). The enemy
        // plays Die/Victory, the player the counterpart; no animator = log only.
        if (activePlayerMovement != null)
        {
            Animator playerEndAnimator = PlayerAnimator();
            if (playerEndAnimator != null)
            {
                // (split) the victory/death trigger names live on
                // EncounterEffectsManager; same strings as before the move.
                EncounterEffectsManager.PlayPlayerState(playerEndAnimator,
                    playerWon ? Effects.victoryTrigger : Effects.deathTrigger);
            }
        }

        if (spawnedEnemyInstance != null)
        {
            Animator enemyAnimator = EncounterEffectsManager.FindPlayableAnimator(spawnedEnemyInstance);
            if (enemyAnimator != null)
            {
                EncounterEffectsManager.ResetEnemyTriggers(enemyAnimator);
                enemyAnimator.SetTrigger(playerWon ? Effects.deathTrigger : Effects.victoryTrigger);
            }
        }

        // 3b. Player victory/lose sound, fired with the end animations (after
        // the face-you turn); which clip plays depends only on who won. Clips
        // live on EncounterEffectsManager; an unassigned clip is silently skipped.
        Effects.PlayOutcomeSound(playerWon);

        // 4. Now end the encounter for real: re-enable answer input guards,
        //    switch the per-frame facing off, and run the cleanup chain.
        resolvingRound = false;
        encounterActive = false;
        StartCoroutine(CleanUpEncounterAssets());
    }
    private IEnumerator CleanUpEncounterAssets()
    {
        yield return new WaitForSeconds(2.0f);
        // Transition out sound (same clip as transition in, per spec),
        // then restore whatever music played before the fight.
        // (split) the SFX source lives on EncounterEffectsManager now.
        Effects.PlaySfx(Effects.transitionSound);
        // (split) music restore lives on EncounterEffectsManager - same
        // moment as before (right after the transition-out sound).
        Effects.RestoreEncounterMusic();
        if (spiralTransitionOverlay != null)
        {
            BringOverlayToAbsoluteFront();
            spiralTransitionOverlay.transform.localScale = Vector3.zero;
            CanvasGroup group = spiralTransitionOverlay.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            float duration = 1.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                spiralTransitionOverlay.transform.rotation = Quaternion.Euler(0, 0, t * 720f);
                spiralTransitionOverlay.transform.localScale = Vector3.Lerp(Vector3.zero, new Vector3(2.5f, 2.5f, 1f), t);
                yield return null;
            }
        }
        if (spawnedEnemyInstance != null) Destroy(spawnedEnemyInstance);
        if (encounterPanel != null) encounterPanel.SetActive(false);
        // Make sure the puzzle UI is not left hidden by the hit
        // sequence if the fight ended mid-sequence.
        resolvingRound = false;
        if (puzzleUIRoot != null) puzzleUIRoot.SetActive(true);
        // Restore HUD and autosave
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);
        SaveLoadManager.IsSafeToSave = true;
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("encounter");
        if (activeSourceZone != null)
            activeSourceZone.OnEncounterCompleted(standardVictoryOutcome, true);
        // Notify SanctumManager to handle boss rewards, crystal spawn, and state update
        if (standardVictoryOutcome && isBossEncounter && SanctumManager.Instance != null)
        {
            SanctumManager.Instance.HandleBossDefeated();
        }
        if (activePlayerMovement != null)
        {
            activePlayerMovement.transform.position = playerInitialPosition;
            activePlayerMovement.transform.rotation = playerInitialRotation;
            activePlayerMovement.enabled = true;
        }
        // --- CAMERA RESTORATION ---
        if (cinematicEncounterCamera != null)
            cinematicEncounterCamera.gameObject.SetActive(false);
        if (mainGameplayCamera != null)
            mainGameplayCamera.gameObject.SetActive(true);
        if (spiralTransitionOverlay != null)
        {
            float fadeDuration = 0.3f;
            float fadeElapsed = 0f;
            CanvasGroup group = spiralTransitionOverlay.GetComponent<CanvasGroup>();
            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                if (group != null) group.alpha = Mathf.Lerp(1f, 0f, fadeElapsed / fadeDuration);
                yield return null;
            }
            spiralTransitionOverlay.gameObject.SetActive(false);
        }
    }
    private PuzzleType GetRandomPuzzleFormat()
    {
        PuzzleType[] available = new PuzzleType[]
        {
            PuzzleType.TrueOrFalse, PuzzleType.PairACode,
            PuzzleType.FillInTheBlank, PuzzleType.PredictTheOutput,
            PuzzleType.SpotTheBug, PuzzleType.LineScramble
        };

        // If the last 2 rounds were the same format, exclude it so a 3rd
        // consecutive repeat can't happen; formats recurring a few rounds
        // apart are fine.
        List<PuzzleType> eligible = new List<PuzzleType>(available);
        if (recentFormats.Count >= 2)
        {
            PuzzleType[] lastTwo = recentFormats.ToArray();
            if (lastTwo[0] == lastTwo[1])
                eligible.RemoveAll(f => f == lastTwo[0]);
        }

        PuzzleType picked = eligible[Random.Range(0, eligible.Count)];

        recentFormats.Enqueue(picked);
        while (recentFormats.Count > 2)
            recentFormats.Dequeue();

        return picked;
    }
    private void UpdateHPDisplay()
    {
        int playerMaxHP = playerStats.maxHP + playerStats.bonusHP;
        if (playerHPFill != null)
            playerHPFill.fillAmount = playerMaxHP > 0
                ? Mathf.Clamp01((float)playerStats.currentHP / playerMaxHP) : 1f;
        if (enemyHPFill != null)
            enemyHPFill.fillAmount = currentEnemy != null && currentEnemy.maxHP > 0
                ? Mathf.Clamp01((float)currentEnemyHP / currentEnemy.maxHP) : 0f;
        if (playerHPText != null) playerHPText.text = $"{playerStats.currentHP} / {playerMaxHP}";
        if (enemyHPText != null && currentEnemy != null)
            enemyHPText.text = $"{currentEnemyHP} / {currentEnemy.maxHP}";
        if (enemyNameText != null && currentEnemy != null)
            enemyNameText.text = currentEnemy.enemyName;
    }

    private void UpdateRoundInfo() { if (roundInfoText != null) roundInfoText.text = $"Round {currentRound}"; }
    private void UpdateCombatLog(string message) { if (combatLogText != null) combatLogText.text = message; }
}
