using UnityEngine;
using UnityEngine.SceneManagement;
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
    public int xpBoss = 100;
    [Header("Boss Unlock Thresholds (cumulative total XP)")]
    public int thresholdPrintConsole = 150;
    public int thresholdVarsVault = 550;
    public int thresholdInputMists = 900;
    public int thresholdElifLabyrinth = 1500;
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

    /// <summary>
    /// A sanctum stops paying out XP once global CurrentXP reaches its own
    /// unlock threshold. Reuses the existing threshold fields rather than
    /// separate cap values, so a misconfigured cap can never sit below the
    /// XP a player needs to unlock that sanctum's own boss.
    /// </summary>
    public bool CanGainXPInSanctum(string sanctumID)
    {
        return CurrentXP < GetThreshold(sanctumID);
    }
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

        // This was never logged before, only SanctumManager's separate
        // boss-bonus path called StudentLogManager. Every regular
        // enemy-defeat XP grant (the majority of XP in the game) was
        // silently absent from the log. Fixed here, at the source,
        // instead of asking every caller to remember to log it.
        StudentLogManager.Instance?.LogXPGained(
            amount, enemyType.ToString(), SceneManager.GetActiveScene().name);
    }
    /// <summary>
    /// Same tier-based lookup as AwardXP, but clamps the amount added so
    /// CurrentXP can never overshoot the sanctum's cap. A kill that would
    /// push past the cap still grants the remaining headroom, not the
    /// full tier amount, so total XP earned from a sanctum lands exactly
    /// at its cap rather than jumping past it.
    /// </summary>
    public void AwardXPCapped(EnemyType enemyType, string sanctumID)
    {
        int amount = enemyType switch
        {
            EnemyType.Beginner => xpBeginner,
            EnemyType.Intermediate => xpIntermediate,
            EnemyType.Difficult => xpDifficult,
            EnemyType.Boss => xpBoss,
            _ => 0
        };

        int headroom = GetThreshold(sanctumID) - CurrentXP;
        if (headroom <= 0) return;

        int clampedAmount = Mathf.Min(amount, headroom);

        CurrentXP += clampedAmount;
        Debug.Log($"[XPManager] +{clampedAmount} XP ({enemyType}, capped from {amount}). Total: {CurrentXP}");
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
    ///
    /// Now logs internally so every caller gets it for free, instead of
    /// each call site having to remember its own separate
    /// StudentLogManager call (which is exactly how the boss-bonus path
    /// worked before, and exactly why nothing else did). If you already
    /// have a call site that logs its own AddXP separately (SanctumManager
    /// currently does), remove that separate call, this would now
    /// double-log it.
    /// </summary>
    public void AddXP(int amount, string source = "bonus", string sanctumID = null)
    {
        CurrentXP += amount;
        Debug.Log($"[XPManager] +{amount} bonus XP ({source}). Total: {CurrentXP}");
        OnXPChanged?.Invoke(CurrentXP);

        string resolvedSanctumID = string.IsNullOrEmpty(sanctumID)
            ? SceneManager.GetActiveScene().name
            : sanctumID;
        StudentLogManager.Instance?.LogXPGained(amount, source, resolvedSanctumID);
    }
}