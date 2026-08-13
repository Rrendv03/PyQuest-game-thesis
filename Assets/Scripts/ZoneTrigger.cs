using System.Collections;
using UnityEngine;

public class ZoneTrigger : MonoBehaviour
{
    public string zoneName;
    public string knowledgeComponent;

    public PuzzleType forcedPuzzleType = PuzzleType.TrueOrFalse;
    public bool randomizePuzzleType = false;
    public bool isEncounterZone = false;
    public bool isBossZone = false;

    // ADD at the top with other fields:
    [Header("Save/Load Identity")]
    [Tooltip("Unique ID. Format: SCENE_Zone_01. Required for boss tracking.")]
    public string zoneID;

    public EnemyDifficultyCategory encounterDifficulty = EnemyDifficultyCategory.Beginner;

    [Header("Enemy Spawn")]
    public Vector3 enemySpawnOffset;
    public Vector3 enemyRotationOffset;

    [Header("Player Combat Position")]
    public Vector3 playerCombatOffset;
    public Vector3 playerCombatRotation;

    [Header("Respawn")]
    public bool respawns = true;
    public float respawnCooldownMin = 60f;
    public float respawnCooldownMax = 300f;
    public GameObject spawnerParent;

    private bool triggered = false;
    private bool hasAwardedXP = false;
    private Collider zoneCollider;

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
    }

    void Start()
    {
        zoneCollider = GetComponent<Collider>();

        // ADD THIS BLOCK:
        if (isBossZone && !string.IsNullOrEmpty(zoneID))
        {
            if (SaveLoadManager.Instance != null &&
                SaveLoadManager.Instance.IsZoneDefeated(zoneID, true))
            {
                Debug.Log($"[ZoneTrigger] Boss zone {zoneID} already defeated. Destroying.");
                Destroy(gameObject);
                return;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;

        triggered = true;

        if (isEncounterZone)
        {
            // === BOSS UNLOCK GATE ===
            if (isBossZone)
            {
                string sanctumID = GetSanctumIDFromScene();

                // Check BOTH XP and tablet missions
                if (MissionTabletManager.Instance != null &&
                    !MissionTabletManager.Instance.IsBossUnlockReady(sanctumID))
                {
                    Debug.Log("[ZoneTrigger] Boss not unlocked. Need more XP or tablet missions.");
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

            EncounterManager.Instance.StartEncounter(
                encounterDifficulty,
                kc,
                isBossZone,
                transform.position + enemySpawnOffset,
                Quaternion.Euler(enemyRotationOffset),
                playerMove,
                transform.position + playerCombatOffset,
                Quaternion.Euler(playerCombatRotation),
                this);
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
            // Award XP
            if (awardXP && !hasAwardedXP && XPManager.Instance != null)
            {
                hasAwardedXP = true;
                XPManager.EnemyType xpType = isBossZone
                    ? XPManager.EnemyType.Boss
                    : DifficultyToXPType(encounterDifficulty);
                XPManager.Instance.AwardXP(xpType);

                if (MissionTabletUI.Instance != null)
                    MissionTabletUI.Instance.Refresh();
            }

            // === SAVE/LOAD: Register defeat ===
            if (SaveLoadManager.Instance != null && !string.IsNullOrEmpty(zoneID))
            {
                string sanctum = isBossZone ? GetSanctumIDFromScene() : null;
                SaveLoadManager.Instance.RegisterZoneDefeated(zoneID, isBossZone, sanctum);
            }

            if (zoneCollider != null)
                zoneCollider.enabled = false;

            // === BOSS DEFEATED FLOW ===
            if (isBossZone)
            {
                HandleBossDefeated();
                Destroy(gameObject, 0.1f);
            }
            else if (respawns)
            {
                if (spawnerParent != null)
                {
                    EnemySpawner spawner = spawnerParent.GetComponent<EnemySpawner>();
                    if (spawner != null)
                        spawner.ScheduleRespawn(Random.Range(respawnCooldownMin, respawnCooldownMax));
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

    /// <summary>
    /// Called when a boss is defeated. Spawns crystal, refreshes UI, opens any gates.
    /// </summary>
    private void HandleBossDefeated()
    {
        string sanctumID = GetSanctumIDFromScene();
        Debug.Log($"[ZoneTrigger] Boss defeated in {sanctumID}!");

        // 1. Refresh tablet UI to show DEFEATED state
        if (MissionTabletUI.Instance != null)
            MissionTabletUI.Instance.Refresh();

        // 2. Spawn rune crystal
        RuneCrystal[] crystals = FindObjectsOfType<RuneCrystal>(true);
        foreach (var crystal in crystals)
        {
            if (crystal.sanctumID == sanctumID)
                crystal.OnBossDefeated();
        }
            

        // REMOVED: gate opening — gate was already open when requirements were met
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
            "EchoingAtrium" => "echoing_atrium",
            "VaultOfEssence" => "vault_of_essence",
            "WhitewakeMist" => "whitewake_mist",
            "LabyrinthOfLogic" => "labyrinth_of_logic",
            _ => "echoing_atrium"
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