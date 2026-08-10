using System.Collections;
using UnityEngine;

public class ZoneTrigger : MonoBehaviour
{
    public string zoneName;
    public string knowledgeComponent;   // BKT knowledge component, separate from zoneName

    public PuzzleType forcedPuzzleType = PuzzleType.TrueOrFalse;
    public bool randomizePuzzleType = false;
    public bool isEncounterZone = false;
    public bool isBossZone = false;     // if true: skips BKT update, awards boss XP only

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

    void Awake()
    {
        zoneCollider = GetComponent<Collider>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;

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
            // Award XP only on first clear for regular zones
            if (awardXP && !hasAwardedXP && XPManager.Instance != null)
            {
                hasAwardedXP = true;
                XPManager.EnemyType xpType = isBossZone
                    ? XPManager.EnemyType.Boss
                    : DifficultyToXPType(encounterDifficulty);
                XPManager.Instance.AwardXP(xpType);

                // Refresh mission tablet if open
                if (MissionTabletUI.Instance != null)
                    MissionTabletUI.Instance.Refresh();
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