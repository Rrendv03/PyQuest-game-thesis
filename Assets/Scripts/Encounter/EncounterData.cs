using UnityEngine;

public enum EnemyDifficultyCategory { Beginner, Intermediate, Advanced }

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
        data.dodgeChance = 0.25f;
        data.escalationStartRound = 4;
        data.escalationPerRound = 0.10f;
        data.escalationCap = 1.40f;

        switch (category)
        {
            case EnemyDifficultyCategory.Beginner:
                data.enemyName = "Beginner Enemy";
                data.category = category;
                data.maxHP = 50;
                data.baseAttack = Random.Range(8, 13);
                break;

            case EnemyDifficultyCategory.Intermediate:
                data.enemyName = "Intermediate Enemy";
                data.category = category;
                data.maxHP = 150;
                data.baseAttack = Random.Range(15, 21);
                break;

            case EnemyDifficultyCategory.Advanced:
                data.enemyName = "Advanced Enemy";
                data.category = category;
                data.maxHP = 225;
                data.baseAttack = Random.Range(25, 31);
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