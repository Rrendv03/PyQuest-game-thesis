using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the PARENT empty GameObject that holds the rune crystal meshes.
/// Starts inactive. Activates when the sanctum's boss is defeated.
/// Contains visual swapping and corruption cleansing logic.
/// </summary>
public class RuneCrystal : MonoBehaviour
{
    [Header("Sanctum")]
    public string sanctumID;

    [Header("Spawn Effect")]
    public GameObject spawnEffect;
    public AudioClip spawnSound;

    [Header("Visual States")]
    [Tooltip("The corrupted/destroyed crystal mesh parent.")]
    public GameObject destroyedState;
    [Tooltip("The restored/glowing crystal mesh parent.")]
    public GameObject restoredState;

    [Header("Cleansing Effect")]
    [Tooltip("Tag assigned to corrupted props in the scene that should fade out.")]
    public string corruptedTag = "Corrupted";
    [Tooltip("How long it takes for corrupted objects to fade out.")]
    public float corruptionFadeDuration = 2f;

    private bool hasSpawned = false;
    private bool isRestored = false;

    void Start()
    {
        // If boss already defeated (loaded from save), show crystal immediately
        if (SaveLoadManager.Instance?.IsSanctumBossDefeated(sanctumID) ?? false)
        {
            gameObject.SetActive(true);
            hasSpawned = true;

            // If the crystal was ALREADY restored in a previous save, 
            // skip the destroyed state and just show the restored state.
            bool wasRestored = StoryProgressionManager.Instance != null &&
                               StoryProgressionManager.Instance.IsQuestComplete($"{sanctumID}_restore_crystal");

            if (wasRestored)
            {
                ForceRestoredState();
                isRestored = true;
            }
            else
            {
                // Boss is dead, but crystal hasn't been clicked yet
                if (destroyedState != null) destroyedState.SetActive(true);
                if (restoredState != null) restoredState.SetActive(false);
            }
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public void OnBossDefeated()
    {
        if (hasSpawned) return;
        hasSpawned = true;

        gameObject.SetActive(true);

        // Ensure we start in the destroyed state when boss dies
        if (destroyedState != null) destroyedState.SetActive(true);
        if (restoredState != null) restoredState.SetActive(false);

        if (spawnEffect != null)
            Instantiate(spawnEffect, transform.position, Quaternion.identity);

        if (spawnSound != null)
            AudioSource.PlayClipAtPoint(spawnSound, transform.position);
    }

    /// <summary>
    /// Called by InteractableObject when the player presses the interact button.
    /// </summary>
    public void Restore()
    {
        if (isRestored) return;
        isRestored = true;

        // 1. Swap visuals
        if (destroyedState != null) destroyedState.SetActive(false);
        if (restoredState != null) restoredState.SetActive(true);

        // 2. Mark quest complete so NPC (Echo) knows to change behavior
        if (StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.CompleteQuest($"{sanctumID}_restore_crystal");
        }

        // 3. Disable the collider. 
        // NOTE: Doing this automatically triggers InteractableObject's OnTriggerExit, 
        // which automatically hides the "Restore" prompt for you! No extra UI code needed.
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 4. Start cleansing corrupted map elements
        StartCoroutine(CleanseCorruption());

        Debug.Log($"[RuneCrystal] Crystal restored in {sanctumID}!");
    }

    private void ForceRestoredState()
    {
        // Used when loading a save where the crystal is already done
        if (destroyedState != null) destroyedState.SetActive(false);
        if (restoredState != null) restoredState.SetActive(true);

        // Instantly remove corruption without fading
        GameObject[] corruptedObjects = GameObject.FindGameObjectsWithTag(corruptedTag);
        foreach (GameObject obj in corruptedObjects)
        {
            obj.SetActive(false);
        }

        // Disable collider so it can't be interacted with again
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    private IEnumerator CleanseCorruption()
    {
        GameObject[] corruptedObjects = GameObject.FindGameObjectsWithTag(corruptedTag);

        if (corruptedObjects.Length == 0)
        {
            Debug.LogWarning($"[RuneCrystal] No objects found with tag '{corruptedTag}'.");
            yield break;
        }

        // Gather all renderers from all corrupted parent objects
        List<Renderer> allRenderers = new List<Renderer>();
        foreach (GameObject obj in corruptedObjects)
        {
            Renderer[] childRenderers = obj.GetComponentsInChildren<Renderer>();
            allRenderers.AddRange(childRenderers);
        }

        // Store original colors
        Color[] originalColors = new Color[allRenderers.Count];
        for (int i = 0; i < allRenderers.Count; i++)
        {
            if (allRenderers[i] != null)
                originalColors[i] = allRenderers[i].material.color;
        }

        // Fade them out over time
        float elapsed = 0f;
        while (elapsed < corruptionFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / corruptionFadeDuration);

            for (int i = 0; i < allRenderers.Count; i++)
            {
                if (allRenderers[i] != null)
                {
                    Color c = originalColors[i];
                    c.a = Mathf.Lerp(1f, 0f, t);
                    allRenderers[i].material.color = c;
                }
            }
            yield return null;
        }

        // Finally, turn the parent objects off completely
        foreach (GameObject obj in corruptedObjects)
        {
            obj.SetActive(false);
        }

        Debug.Log($"[RuneCrystal] Cleansed {corruptedObjects.Length} corrupted objects.");
    }
}