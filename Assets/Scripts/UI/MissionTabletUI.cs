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

    private const string Mission1QuestID = "echoing_atrium_mission1_complete";
    private const string Mission2QuestID = "echoing_atrium_mission2_complete";
    private const string Mission3QuestID = "echoing_atrium_mission3_complete";
    private const string SanctumID = "echoing_atrium";

    private readonly string Mission1Desc =
        "Mission I: Restore the Gate of First Words.";
    private readonly string Mission2Desc =
        "Mission II: Silence the Murmur Shades in the East Wing.";
    private readonly string Mission3Desc =
        "Mission III: Find and restore the erased final inscription.";

    void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
    }

    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HideTablet);

        if (mission1Desc != null) mission1Desc.text = Mission1Desc;
        if (mission2Desc != null) mission2Desc.text = Mission2Desc;
        if (mission3Desc != null) mission3Desc.text = Mission3Desc;
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
        if (StoryProgressionManager.Instance == null) return;

        bool m1 = StoryProgressionManager.Instance.IsQuestComplete(Mission1QuestID);
        bool m2 = StoryProgressionManager.Instance.IsQuestComplete(Mission2QuestID);
        bool m3 = StoryProgressionManager.Instance.IsQuestComplete(Mission3QuestID);

        SetMissionRow(mission1Check, m1);
        SetMissionRow(mission2Check, m2);
        SetMissionRow(mission3Check, m3);

        bool bossUnlocked = XPManager.Instance != null
            && XPManager.Instance.IsBossUnlocked(SanctumID);

        if (bossLockedText != null) bossLockedText.SetActive(!bossUnlocked);
        if (bossUnlockedText != null) bossUnlockedText.SetActive(bossUnlocked);
    }

    private void SetMissionRow(Text checkText, bool complete)
    {
        if (checkText == null) return;
        checkText.text = complete ? "?" : "?";
        checkText.color = complete
            ? new Color(0.2f, 0.8f, 0.2f)
            : new Color(0.8f, 0.8f, 0.8f);
    }
}