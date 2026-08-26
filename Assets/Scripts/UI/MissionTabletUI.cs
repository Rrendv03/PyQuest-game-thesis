using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MissionTabletUI : MonoBehaviour
{
    public static MissionTabletUI Instance;

    [Header("Panel")]
    public GameObject panel;

    [Header("HUD to hide while tablet is open")]
    public GameObject hudCanvas;

    [Header("Mission Rows")]
    public Text mission1Check;
    public Text mission1Desc;
    public Text mission2Check;
    public Text mission2Desc;
    public Text mission3Check;
    public Text mission3Desc;

    [Header("Boss Status")]
    public GameObject bossLockedText;
    public GameObject bossUnlockedText;

    [Header("Close Button")]
    public Button closeButton;

    [Header("Current Sanctum")]
    public string currentSanctumID = "echoing_atrium";

    void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HideTablet);
    }

    public void ShowTablet()
    {
        if (panel != null) panel.SetActive(true);
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);

        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        SaveLoadManager.IsSafeToSave = false;

        Refresh();
    }

    public void HideTablet()
    {
        if (panel != null) panel.SetActive(false);
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);

        PlayerMovement pm = FindObjectOfType<PlayerMovement>();
        if (pm != null) pm.enabled = true;

        SaveLoadManager.IsSafeToSave = true;
    }

    public void Refresh()
    {
        if (MissionTabletManager.Instance == null) return;

        List<MissionTabletData> missions =
            MissionTabletManager.Instance.GetMissionsForSanctum(currentSanctumID);

        UpdateRow(0, missions, mission1Check, mission1Desc);
        UpdateRow(1, missions, mission2Check, mission2Desc);
        UpdateRow(2, missions, mission3Check, mission3Desc);

        bool bossUnlocked = MissionTabletManager.Instance.IsBossUnlockReady(currentSanctumID);

        if (bossLockedText != null) bossLockedText.SetActive(!bossUnlocked);
        if (bossUnlockedText != null) bossUnlockedText.SetActive(bossUnlocked);
    }

    private void UpdateRow(int index, List<MissionTabletData> missions, Text check, Text desc)
    {
        if (index >= missions.Count)
        {
            if (check != null) check.text = "";
            if (desc != null) desc.text = "";
            return;
        }

        MissionTabletData m = missions[index];
        bool done = MissionTabletManager.Instance.IsMissionComplete(m.missionID);

        if (check != null)
        {
            check.text = "?";
            check.color = done
                ? new Color(0.2f, 0.8f, 0.2f)
                : new Color(0.8f, 0.8f, 0.8f);
        }

        if (desc != null)
            desc.text = m.description;
    }
}