using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EncounterManager : MonoBehaviour
{
    public static EncounterManager Instance;

    [Header("Combat Stats")]
    public PlayerCombatStats playerStats = new PlayerCombatStats();

    [Header("UI References")]
    public GameObject encounterPanel;
    public Slider playerHPBar;
    public Slider enemyHPBar;
    public Text playerHPText;
    public Text enemyHPText;
    public Text enemyNameText;
    public Text roundInfoText;
    public Text combatLogText;

    [Header("UI Transition Setup")]
    public Image spiralTransitionOverlay;

    [Header("3D Enemy Prefab Registry")]
    public GameObject beginnerEnemyPrefab;
    public GameObject intermediateEnemyPrefab;
    public GameObject advancedEnemyPrefab;

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
    private List<bool> roundResults = new List<bool>();
    private List<float> roundPGuessValues = new List<float>();
    private bool encounterActive = false;
    private bool standardVictoryOutcome = false;
    private bool isBossEncounter = false;
    private GameObject spawnedEnemyInstance;
    private DifficultyTier lockedEncounterTier;

    private PlayerMovement activePlayerMovement;
    private Vector3 playerInitialPosition;
    private Quaternion playerInitialRotation;

    private ZoneTrigger activeSourceZone;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
        roundResults = new List<bool>();
        roundPGuessValues = new List<float>();

        // Hide HUD and interact button during encounter
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);

        InteractButtonController interact = FindObjectOfType<InteractButtonController>();
        if (interact != null)
            interact.ForceHide();

        SaveLoadManager.IsSafeToSave = false;

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

        // --- CAMERA SWITCH LOGIC ---
        // Disables the main camera to completely stop follow scripts or Cinemachine overrides
        if (mainGameplayCamera != null)
        {
            mainGameplayCamera.gameObject.SetActive(false);
        }

        // Enables the script-free cinematic camera and snaps it to the Honkai offset
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

        playerStats.Initialize();

        if (spawnedEnemyInstance != null)
        {
            Destroy(spawnedEnemyInstance);
        }

        GameObject prefabToSpawn = null;
        switch (category)
        {
            case EnemyDifficultyCategory.Beginner: prefabToSpawn = beginnerEnemyPrefab; break;
            case EnemyDifficultyCategory.Intermediate: prefabToSpawn = intermediateEnemyPrefab; break;
            case EnemyDifficultyCategory.Advanced: prefabToSpawn = advancedEnemyPrefab; break;
        }

        if (prefabToSpawn != null)
        {
            spawnedEnemyInstance = Instantiate(prefabToSpawn, enemyPos, enemyRot);

            Animator enemyAnimator = spawnedEnemyInstance.GetComponent<Animator>();
            if (enemyAnimator != null)
            {
                enemyAnimator.SetTrigger("OnEncounterStart");
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
        PuzzleType randomFormat = GetRandomPuzzleFormat();
        PuzzleManager.Instance.OnZoneEntered(
            currentKnowledgeComponent, randomFormat, lockedEncounterTier);
    }

    public void OnPuzzleResolved(bool playerAnsweredCorrectly, float pGuessOverride)
    {
        if (!encounterActive) return;

        roundResults.Add(playerAnsweredCorrectly);
        roundPGuessValues.Add(pGuessOverride);

        Animator enemyAnimator = spawnedEnemyInstance != null
            ? spawnedEnemyInstance.GetComponent<Animator>() : null;

        if (playerAnsweredCorrectly)
        {
            int damage = CalculatePlayerDamage();
            currentEnemyHP = Mathf.Max(0, currentEnemyHP - damage);
            UpdateCombatLog($"Correct! You dealt {damage} damage to the enemy.");
            if (enemyAnimator != null) enemyAnimator.SetTrigger("TakeDamage");
        }
        else
        {
            bool enemyDodged = Random.value < currentEnemy.dodgeChance;
            if (!enemyDodged)
            {
                int enemyDamage = CalculateEnemyDamage();
                playerStats.currentHP = Mathf.Max(0, playerStats.currentHP - enemyDamage);
                UpdateCombatLog($"Wrong! Enemy dealt {enemyDamage} damage to you.");
                if (enemyAnimator != null) enemyAnimator.SetTrigger("Attack");
            }
            else
            {
                int playerDamage = 10;
                currentEnemyHP = Mathf.Max(0, currentEnemyHP - playerDamage);
                UpdateCombatLog($"Wrong! But you still dealt {playerDamage} damage.");
                if (enemyAnimator != null) enemyAnimator.SetTrigger("TakeDamage");
            }
        }

        if (currentRound >= currentEnemy.escalationStartRound)
        {
            currentEscalationMultiplier = Mathf.Min(
                currentEnemy.escalationCap,
                currentEscalationMultiplier + currentEnemy.escalationPerRound);
        }

        UpdateHPDisplay();

        if (currentEnemyHP <= 0) { EndEncounter(true); return; }
        if (playerStats.currentHP <= 0) { EndEncounter(false); return; }

        StartNextRound();
    }

    private int CalculatePlayerDamage()
    {
        float bonusMultiplier = 1.0f;
        switch (currentEnemy.category)
        {
            case EnemyDifficultyCategory.Beginner: bonusMultiplier = 1.02f; break;
            case EnemyDifficultyCategory.Intermediate: bonusMultiplier = 1.05f; break;
            case EnemyDifficultyCategory.Advanced: bonusMultiplier = 1.08f; break;
        }
        return Mathf.RoundToInt(playerStats.GetTotalAttack() * bonusMultiplier);
    }

    private int CalculateEnemyDamage() { return Mathf.RoundToInt(currentEnemy.baseAttack * currentEscalationMultiplier); }

    private void EndEncounter(bool playerWon)
    {
        encounterActive = false;
        standardVictoryOutcome = playerWon;

        // Boss encounters do not affect BKT scores
        if (!isBossEncounter)
        {
            for (int i = 0; i < roundResults.Count; i++)
            {
                BKTEngine.Instance.UpdateMastery(
                    currentKnowledgeComponent,
                    roundResults[i],
                    roundPGuessValues[i]);
            }
        }

        Animator enemyAnimator = spawnedEnemyInstance != null
            ? spawnedEnemyInstance.GetComponent<Animator>() : null;

        if (enemyAnimator != null)
        {
            string trigger = playerWon ? "Die" : "Victory";
            enemyAnimator.SetTrigger(trigger);
        }

        string outcomeMessage = playerWon
            ? $"Victory! You defeated the {currentEnemy.enemyName}!"
            : $"Defeated! The {currentEnemy.enemyName} won this round.";

        UpdateCombatLog(outcomeMessage);
        StartCoroutine(CleanUpEncounterAssets());
    }

    private IEnumerator CleanUpEncounterAssets()
    {
        yield return new WaitForSeconds(2.0f);

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

        // Restore HUD and autosave
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);
        SaveLoadManager.IsSafeToSave = true;

        if (activeSourceZone != null)
            activeSourceZone.OnEncounterCompleted(standardVictoryOutcome, true);

        if (activePlayerMovement != null)
        {
            activePlayerMovement.transform.position = playerInitialPosition;
            activePlayerMovement.transform.rotation = playerInitialRotation;
            activePlayerMovement.enabled = true;
        }

        // --- CAMERA RESTORATION ---
        // Disables the cinematic camera and re-enables the gameplay camera
        if (cinematicEncounterCamera != null)
        {
            cinematicEncounterCamera.gameObject.SetActive(false);
        }
        if (mainGameplayCamera != null)
        {
            mainGameplayCamera.gameObject.SetActive(true);
        }

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
        PuzzleType[] available = new PuzzleType[] { PuzzleType.TrueOrFalse, PuzzleType.PairACode, PuzzleType.FillInTheBlank, PuzzleType.PredictTheOutput, PuzzleType.SpotTheBug, PuzzleType.LineScramble };
        return available[Random.Range(0, available.Length)];
    }

    private void UpdateHPDisplay()
    {
        if (playerHPBar != null) { playerHPBar.maxValue = playerStats.maxHP + playerStats.bonusHP; playerHPBar.value = playerStats.currentHP; }
        if (enemyHPBar != null) { enemyHPBar.maxValue = currentEnemy.maxHP; enemyHPBar.value = currentEnemyHP; }
        if (playerHPText != null) playerHPText.text = $"{playerStats.currentHP} / {playerStats.maxHP + playerStats.bonusHP}";
        if (enemyHPText != null) enemyHPText.text = $"{currentEnemyHP} / {currentEnemy.maxHP}";
        if (enemyNameText != null) enemyNameText.text = currentEnemy.enemyName;
    }

    private void UpdateRoundInfo() { if (roundInfoText != null) roundInfoText.text = $"Round {currentRound}"; }
    private void UpdateCombatLog(string message) { if (combatLogText != null) combatLogText.text = message; }
}