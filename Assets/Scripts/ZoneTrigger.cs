using UnityEngine;

public class ZoneTrigger : MonoBehaviour
{
    public string zoneName;
    public PuzzleType forcedPuzzleType = PuzzleType.TrueOrFalse;
    public bool randomizePuzzleType = false;
    public bool isEncounterZone = false;
    public EnemyDifficultyCategory encounterDifficulty = EnemyDifficultyCategory.Beginner;

    [Header("Enemy Spawn Adjustment")]
    public Vector3 enemySpawnOffset;
    public Vector3 enemyRotationOffset;

    [Header("Player Combat Adjustment")]
    public Vector3 playerCombatOffset;
    public Vector3 playerCombatRotation;

    private bool triggered = false;
    private Collider zoneCollider;

    void Awake()
    {
        // Caches the collider component to safely manage physics states
        zoneCollider = GetComponent<Collider>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;

        Debug.Log($"Player entered {zoneName}");
        triggered = true;

        if (isEncounterZone)
        {
            PlayerMovement playerMove = other.GetComponent<PlayerMovement>();
            if (playerMove != null)
            {
                Vector3 finalEnemyPosition = this.transform.position + enemySpawnOffset;
                Quaternion finalEnemyRotation = Quaternion.Euler(enemyRotationOffset);

                Vector3 finalPlayerPosition = this.transform.position + playerCombatOffset;
                Quaternion finalPlayerRotation = Quaternion.Euler(playerCombatRotation);

                // Passes 'this' to hand over control of this specific zone instance
                EncounterManager.Instance.StartEncounter(
                    encounterDifficulty,
                    zoneName,
                    finalEnemyPosition,
                    finalEnemyRotation,
                    playerMove,
                    finalPlayerPosition,
                    finalPlayerRotation,
                    this
                );
            }
            else
            {
                Debug.LogError("[ZoneTrigger] PlayerMovement component missing on the entering Player object.");
            }
        }
        else
        {
            PuzzleType selectedType = randomizePuzzleType
                ? GetRandomPuzzleType()
                : forcedPuzzleType;

            PuzzleManager.Instance.OnZoneEntered(zoneName, selectedType);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player exited {zoneName}");
            triggered = false;
        }
    }

    /// <summary>
    /// Handles combat resolution state. Disables physics interaction before player repositioning occurs.
    /// </summary>
    public void OnEncounterCompleted(bool playerWon)
    {
        if (playerWon)
        {
            if (zoneCollider != null)
            {
                zoneCollider.enabled = false;
            }

            // Designed for the upcoming spawning system: removes the object instance once its encounter is cleared
            Destroy(gameObject, 0.1f);
        }
        else
        {
            // If the player fails, allows the zone to re-evaluate on subsequent entries
            triggered = false;
        }
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
}