using UnityEngine;

public class BossGate : MonoBehaviour
{
    [Header("Sanctum")]
    public string sanctumID;

    [Header("Effects")]
    public GameObject lockedEffect;
    public GameObject unlockedEffect;

    void Start()
    {
        EvaluateState();
    }

    void OnEnable()
    {
        if (XPManager.Instance != null)
            XPManager.Instance.OnXPChanged += HandleXPChanged;
    }

    void OnDisable()
    {
        if (XPManager.Instance != null)
            XPManager.Instance.OnXPChanged -= HandleXPChanged;
    }

    private void HandleXPChanged(int _)
    {
        EvaluateState();
    }

    public void Refresh()
    {
        EvaluateState();
    }

    void EvaluateState()
    {
        bool defeated = SaveLoadManager.Instance?.IsSanctumBossDefeated(sanctumID) ?? false;
        bool unlocked = MissionTabletManager.Instance?.IsBossUnlockReady(sanctumID) ?? false;

        if (defeated || unlocked)
            OpenGate();
        else
            CloseGate();
    }

    public void OpenGate()
    {
        gameObject.SetActive(false);
        if (lockedEffect != null) lockedEffect.SetActive(false);
        if (unlockedEffect != null) unlockedEffect.SetActive(true);
    }

    public void CloseGate()
    {
        gameObject.SetActive(true);
        if (lockedEffect != null) lockedEffect.SetActive(true);
        if (unlockedEffect != null) unlockedEffect.SetActive(false);
    }
}