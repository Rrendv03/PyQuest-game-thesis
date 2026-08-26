using System.Collections;
using UnityEngine;
public class ZoneTrigger : MonoBehaviour
{
    public string zoneName;
    public string knowledgeComponent;   // BKT knowledge component, separate from zoneName
    [Tooltip("Boss zones only. Leave empty for single-KC sanctums. If set, boss randomly tests this OR knowledgeComponent (50/50) each attempt.")]
    public string secondaryKnowledgeComponent;
    public PuzzleType forcedPuzzleType = PuzzleType.TrueOrFalse;
    public bool randomizePuzzleType = false;
    public bool isEncounterZone = false;
    public bool isBossZone = false;     // if true: uses inspector difficulty, ignores BKT scaling
    public EnemyDifficultyCategory encounterDifficulty = EnemyDifficultyCategory.Beginner;
    [Header("Enemy Spawn")]
    public Vector3 enemySpawnOffset;
    public Vector3 enemyRotationOffset;
    [Header("Player Combat Position")]
    public Vector3 playerCombatOffset;
    public Vector3 playerCombatRotation;
    [Header("Respawn")]
    public bool respawns = true;        // false for boss zones and mission-critical zones
    public float respawnCooldownMin = 60f;
    public float respawnCooldownMax = 300f;
    public GameObject spawnerParent;    // assign the EnemySpawner that owns this zone
    // Runtime state
    private bool triggered = false;
    private bool hasAwardedXP = false;  // first-clear-only XP gate
    private Collider zoneCollider;
    private EnemyDifficultyCategory _actualDifficultyUsed;
    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
    }

    void Start()
    {
        // Bug-004 FIX: if this is a boss zone and the sanctum boss has
        // already been defeated, disable immediately so the zone can't
        // be re-triggered after a save reload.
        if (isBossZone && StoryProgressionManager.Instance != null)
        {
            string sanctumID = GetSanctumIDFromScene();
            if (StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID))
            {
                Debug.Log($"[ZoneTrigger] Boss in sanctum '{sanctumID}' already " +
                          $"defeated; disabling boss zone permanently.");
                if (zoneCollider != null) zoneCollider.enabled = false;
                gameObject.SetActive(false);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;

        // Bug-004 FIX: secondary safety guard for boss zones - if the
        // boss was already defeated, bail before starting an encounter.
        // Covers edge cases where Start() runs but the collider stayed
        // active due to object pooling / inspector override.
        if (isBossZone && StoryProgressionManager.Instance != null)
        {
            string sanctumID = GetSanctumIDFromScene();
            if (StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID))
            {
                Debug.Log($"[ZoneTrigger] Boss already defeated in {sanctumID}; skipping.");
                return;
            }
        }

        triggered = true;
        if (isEncounterZone)
        {
            // Block entry if boss zone and XP threshold not met
            if (isBossZone && XPManager.Instance != null)
            {
                string sanctumID = GetSanctumIDFromScene();
                if (!XPManager.Instance.IsBossUnlocked(sanctumID))
                {
                    Debug.Log("[ZoneTrigger] Boss not yet unlocked. XP threshold not reached.");
                    triggered = false;
                    return;
                }
            }
            PlayerMovement playerMove = other.GetComponent<PlayerMovement>();
            if (playerMove == null)
            {
                Debug.LogError("[ZoneTrigger] PlayerMovement missing on Player.");
                triggered = false;
                return;
            }
            string kc = string.IsNullOrEmpty(knowledgeComponent) ? zoneName : knowledgeComponent;
            if (isBossZone && !string.IsNullOrEmpty(secondaryKnowledgeComponent) && Random.value < 0.5f)
            {
                kc = secondaryKnowledgeComponent;
            }
            // =========================================================
            // DYNAMIC DIFFICULTY VS BOSS LOCKING
            // =========================================================
            EnemyDifficultyCategory finalDifficulty;
            if (isBossZone)
            {
                // Bosses use the hardcoded difficulty set in the Unity Inspector
                finalDifficulty = EnemyDifficultyCategory.Boss;
            }
            else
            {
                // Normal enemies scale dynamically based on live BKT mastery score
                if (PCGEngine.Instance != null && BKTEngine.Instance != null)
                {
                    float mastery = BKTEngine.Instance.GetMastery(kc);
                    DifficultyTier bktTier = PCGEngine.Instance.GetTierForMasteryPublic(mastery);
                    // Safely map DifficultyTier (PCG) to EnemyDifficultyCategory (Encounter)
                    finalDifficulty = (EnemyDifficultyCategory)System.Enum.Parse(typeof(EnemyDifficultyCategory), bktTier.ToString());
                }
                else
                {
                    // Fallback if singletons are missing
                    finalDifficulty = encounterDifficulty;
                }
            }
            // =========================================================
            EncounterManager.Instance.StartEncounter(
                finalDifficulty,
                kc,
                isBossZone,
                transform.position + enemySpawnOffset,
                Quaternion.Euler(enemyRotationOffset),
                playerMove,
                transform.position + playerCombatOffset,
                Quaternion.Euler(playerCombatRotation),
                this);
            _actualDifficultyUsed = finalDifficulty;
        }
        else
        {
            PuzzleType selectedType = randomizePuzzleType
                ? GetRandomPuzzleType()
                : forcedPuzzleType;
            string kc = string.IsNullOrEmpty(knowledgeComponent) ? zoneName : knowledgeComponent;
            PuzzleManager.Instance.OnZoneEntered(kc, selectedType);
        }
    }
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            triggered = false;
    }
    public void OnEncounterCompleted(bool playerWon, bool awardXP)
    {
        if (playerWon)
        {
            string sanctumID = GetSanctumIDFromScene();

            // =========================================================
            // TRACK BOSS DEFEAT FOR RUNE CRYSTAL & SAVE SYSTEM
            // =========================================================
            if (isBossZone && StoryProgressionManager.Instance != null)
            {
                string bossQuestID = sanctumID + "_defeat_enemy";
                StoryProgressionManager.Instance.CompleteQuest(bossQuestID);
                Debug.Log($"[ZoneTrigger] Boss defeated! Quest '{bossQuestID}' marked complete.");
            }

            // Bug-005A FIX: mark "{sanctum}_enemy_encounter_cleared" quest
            // complete when player defeats a NON-BOSS encounter enemy.
            if (!isBossZone && StoryProgressionManager.Instance != null)
            {
                string defeatQuestID = $"{sanctumID}_enemy_encounter_cleared";
                if (!StoryProgressionManager.Instance.IsQuestComplete(defeatQuestID))
                {
                    StoryProgressionManager.Instance.CompleteQuest(defeatQuestID);
                    Debug.Log($"[ZoneTrigger] Step 3 complete: {defeatQuestID}");
                }
            }

            // Award XP only on first clear for regular zones, only if this
            // sanctum hasn't already produced enough cumulative XP to unlock its
            // own boss, and typed by the difficulty actually fought (BKT/PCG
            // scaled), not the Inspector default.
            if (awardXP && !hasAwardedXP && XPManager.Instance != null)
            {
                if (XPManager.Instance.CanGainXPInSanctum(sanctumID))
                {
                    hasAwardedXP = true;
                    XPManager.EnemyType xpType = isBossZone
                        ? XPManager.EnemyType.Boss
                        : DifficultyToXPType(_actualDifficultyUsed);
                    XPManager.Instance.AwardXPCapped(xpType, sanctumID);
                    if (MissionTabletUI.Instance != null)
                        MissionTabletUI.Instance.Refresh();
                }
            }
            if (zoneCollider != null)
                zoneCollider.enabled = false;
            if (respawns && !isBossZone)
            {
                // Notify spawner to schedule a respawn
                if (spawnerParent != null)
                {
                    EnemySpawner spawner = spawnerParent.GetComponent<EnemySpawner>();
                    if (spawner != null)
                        spawner.ScheduleRespawn(
                            Random.Range(respawnCooldownMin, respawnCooldownMax));
                }
                Destroy(gameObject, 0.1f);
            }
            else
            {
                Destroy(gameObject, 0.1f);
            }
        }
        else
        {
            triggered = false;
        }
    }
    private XPManager.EnemyType DifficultyToXPType(EnemyDifficultyCategory cat)
    {
        return cat switch
        {
            EnemyDifficultyCategory.Beginner => XPManager.EnemyType.Beginner,
            EnemyDifficultyCategory.Intermediate => XPManager.EnemyType.Intermediate,
            EnemyDifficultyCategory.Advanced => XPManager.EnemyType.Difficult,
            _ => XPManager.EnemyType.Beginner
        };
    }
    private string GetSanctumIDFromScene()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return scene switch
        {
            "PrintConsole" => "print_console",
            "VarsVault" => "vars_vault",
            "InputMists" => "input_mists",
            "ElifLabyrinth" => "elif_labyrinth",
            _ => "print_console"
        };
    }
    private PuzzleType GetRandomPuzzleType()
    {
        PuzzleType[] available = new PuzzleType[]
        {
            PuzzleType.TrueOrFalse, PuzzleType.PairACode,
            PuzzleType.FillInTheBlank, PuzzleType.PredictTheOutput,
            PuzzleType.SpotTheBug, PuzzleType.LineScramble
        };
        return available[Random.Range(0, available.Length)];
    }
}