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
    public int thresholdPrintConsole = 150;
    public int thresholdVarsVault = 350;
    public int thresholdInputMists = 600;
    public int thresholdElifLabyrinth = 900;

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
            "print_console" => CurrentXP >= thresholdPrintConsole,
            "vars_vault" => CurrentXP >= thresholdVarsVault,
            "input_mists" => CurrentXP >= thresholdInputMists,
            "elif_labyrinth" => CurrentXP >= thresholdElifLabyrinth,
            _ => false
        };
    }

    public int GetThreshold(string sanctumID)
    {
        return sanctumID switch
        {
            "print_console" => thresholdPrintConsole,
            "vars_vault" => thresholdVarsVault,
            "input_mists" => thresholdInputMists,
            "elif_labyrinth" => thresholdElifLabyrinth,
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

    /// <summary>
    /// Raw XP add, for bonuses that don't map to an EnemyType tier
    /// (e.g. SanctumManager's per-sanctum boss clear bonus).
    /// </summary>
    public void AddXP(int amount)
    {
        CurrentXP += amount;
        Debug.Log($"[XPManager] +{amount} bonus XP. Total: {CurrentXP}");
        OnXPChanged?.Invoke(CurrentXP);
    }
}