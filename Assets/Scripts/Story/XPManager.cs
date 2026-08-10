using UnityEngine;

/// <summary>
/// Tracks cumulative player XP across all Sanctums.
/// XP is gained from enemy defeats only. Boss XP does not affect BKT.
/// Thresholds are cumulative totals, not per-sanctum resets.
/// Place on the same DontDestroyOnLoad GameObject as StoryProgressionManager.
/// </summary>
public class XPManager : MonoBehaviour
{
    public static XPManager Instance;

    [Header("XP Values")]
    public int xpBeginner = 30;
    public int xpIntermediate = 60;
    public int xpDifficult = 100;
    public int xpBoss = 150;

    [Header("Boss Unlock Thresholds (cumulative total XP)")]
    public int thresholdEchoingAtrium = 150;
    public int thresholdVaultOfEssence = 350;
    public int thresholdWhitewakeMist = 600;
    public int thresholdLabyrinthOfLogic = 900;

    public int CurrentXP { get; private set; } = 0;

    public event System.Action<int> OnXPChanged;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public enum EnemyType { Beginner, Intermediate, Difficult, Boss }

    public void AwardXP(EnemyType enemyType)
    {
        int amount = enemyType switch
        {
            EnemyType.Beginner => xpBeginner,
            EnemyType.Intermediate => xpIntermediate,
            EnemyType.Difficult => xpDifficult,
            EnemyType.Boss => xpBoss,
            _ => 0
        };

        CurrentXP += amount;
        Debug.Log($"[XPManager] +{amount} XP ({enemyType}). Total: {CurrentXP}");
        OnXPChanged?.Invoke(CurrentXP);
    }

    public bool IsBossUnlocked(string sanctumID)
    {
        return sanctumID switch
        {
            "echoing_atrium" => CurrentXP >= thresholdEchoingAtrium,
            "vault_of_essence" => CurrentXP >= thresholdVaultOfEssence,
            "whitewake_mist" => CurrentXP >= thresholdWhitewakeMist,
            "labyrinth_of_logic" => CurrentXP >= thresholdLabyrinthOfLogic,
            _ => false
        };
    }

    public int GetThreshold(string sanctumID)
    {
        return sanctumID switch
        {
            "echoing_atrium" => thresholdEchoingAtrium,
            "vault_of_essence" => thresholdVaultOfEssence,
            "whitewake_mist" => thresholdWhitewakeMist,
            "labyrinth_of_logic" => thresholdLabyrinthOfLogic,
            _ => 0
        };
    }

    // Save/Load bridge
    public int ExportXP() => CurrentXP;
    public void ImportXP(int xp)
    {
        CurrentXP = xp;
        OnXPChanged?.Invoke(CurrentXP);
    }
}