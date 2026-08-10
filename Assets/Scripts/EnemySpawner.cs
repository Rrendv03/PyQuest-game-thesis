using System.Collections;
using UnityEngine;

/// <summary>
/// Sits on a persistent empty GameObject in the scene.
/// Holds the ZoneTrigger prefab for one encounter zone and
/// respawns it after a cooldown when the zone is cleared.
///
/// Setup: create an empty GameObject, attach EnemySpawner,
/// assign the ZoneTrigger prefab to zonePrefab, set spawnPoint
/// to where the zone collider should reappear, then assign
/// this GameObject as spawnerParent on the ZoneTrigger prefab.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("Zone")]
    public GameObject zonePrefab;
    public Transform spawnPoint;

    private GameObject activeZone;
    private bool isOnCooldown = false;

    void Start()
    {
        SpawnZone();
    }

    public void ScheduleRespawn(float delaySeconds)
    {
        if (isOnCooldown) return;
        isOnCooldown = true;
        StartCoroutine(RespawnAfterDelay(delaySeconds));
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnZone();
        isOnCooldown = false;
    }

    private void SpawnZone()
    {
        if (zonePrefab == null || spawnPoint == null) return;

        activeZone = Instantiate(
            zonePrefab, spawnPoint.position, spawnPoint.rotation);

        // Link back so the zone can notify this spawner on clear
        ZoneTrigger zt = activeZone.GetComponent<ZoneTrigger>();
        if (zt != null)
            zt.spawnerParent = this.gameObject;
    }
}