using UnityEngine;

public enum EnemyDifficultyCategory { Beginner, Intermediate, Advanced, Boss }

// ----------------------------------------------------------------------------
// BALANCE PASS
// Player: 75 base HP, +bonusHP set in the Inspector per sanctum scene.
//   S1 = 75 HP (bonusHP 0) | S2 = 100 (25) | S3 = 125 (50) | S4 = 150 (75)
// Player deals ~20-23 damage per correct answer (base 20 + small tier bonus
// in EncounterManager.CalculatePlayerDamage).
// Each tier is tuned so:
//   - a regular fight lasts 3-7 correct answers to win (Boss ~10),
//   - the player survives ~6 wrong answers at their current-sanctum HP,
//   - one enemy hit = ~12-18% of that sanctum's player max HP.
// Sanity check (player dmg / enemy HP = rounds to win):
//   Beginner     20 /  60 ~= 3
//   Intermediate 21 / 100 ~= 5
//   Advanced     22 / 150 ~= 7
//   Boss         23 / 220 ~= 10
// Survivability (sanctum player HP / avg enemy dmg = misses until death):
//   Beginner      75 /  9 ~= 8   (escalates late, caps at 1.3x)
//   Intermediate 100 / 15 ~= 7
//   Advanced     125 / 22 ~= 6
//   Boss         150 / 25 ~= 6   (escalates late, capped so round 8+ ~ 37 max)
// ----------------------------------------------------------------------------

[System.Serializable]
public class EnemyData
{
    public string enemyName;
    public EnemyDifficultyCategory category;
    public int maxHP;
    public int baseAttack;
    public float dodgeChance;       // 0.0 to 1.0
    public int escalationStartRound; // round after which enemy starts escalating
    public float escalationPerRound; // attack multiplier increase per round
    public float escalationCap;      // maximum attack multiplier

    public static EnemyData CreateForCategory(EnemyDifficultyCategory category)
    {
        EnemyData data = new EnemyData();
        data.dodgeChance = 0.15f;
        data.escalationStartRound = 4;
        data.escalationPerRound = 0.10f;
        data.escalationCap = 1.40f;

        switch (category)
        {
            case EnemyDifficultyCategory.Beginner:
                data.enemyName = "Beginner Enemy";
                data.category = category;
                data.maxHP = 60;
                data.baseAttack = Random.Range(8, 12);       // 8-11
                data.dodgeChance = 0.10f;
                data.escalationStartRound = 5;
                data.escalationCap = 1.30f;
                break;

            case EnemyDifficultyCategory.Intermediate:
                data.enemyName = "Intermediate Enemy";
                data.category = category;
                data.maxHP = 100;
                data.baseAttack = Random.Range(13, 18);      // 13-17
                break;

            case EnemyDifficultyCategory.Advanced:
                data.enemyName = "Advanced Enemy";
                data.category = category;
                data.maxHP = 150;
                data.baseAttack = Random.Range(20, 26);      // 20-25
                data.dodgeChance = 0.20f;
                data.escalationPerRound = 0.12f;
                data.escalationCap = 1.50f;
                break;
            case EnemyDifficultyCategory.Boss:
                data.enemyName = "Null Wraith";
                data.category = category;
                data.maxHP = 220;
                data.baseAttack = Random.Range(22, 29);      // 22-28
                data.dodgeChance = 0.10f;
                data.escalationStartRound = 5;
                data.escalationPerRound = 0.10f;
                data.escalationCap = 1.50f;
                break;
        }
        return data;
    }
}

[System.Serializable]
public class PlayerCombatStats
{
    public int maxHP = 75;
    public int currentHP;
    public int baseAttack = 20;
    public int bonusHP = 0;
    public int bonusAttack = 0;

    public void Initialize()
    {
        currentHP = maxHP + bonusHP;
    }

    public int GetTotalAttack()
    {
        return baseAttack + bonusAttack;
    }
}

[System.Serializable]
public class EncounterResult
{
    public bool playerWon;
    public int roundsPlayed;
    public int correctAnswers;
    public int incorrectAnswers;
    public string knowledgeComponent;
}
