using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic reusable HUD compass arrow. Points toward any Transform target
/// regardless of what that target represents, exit gates, next-objective
/// markers, a returning NPC, anything. Not tied to any single feature.
///
/// Usage from anywhere in the codebase:
///     HUDCompassController.Instance.ShowCompassTo(someTransform, "Exit");
///     HUDCompassController.Instance.Hide();
///
/// Attach to a UI GameObject under the main Canvas. Assign compassRoot
/// (the object to show/hide), compassArrow (the Image that rotates), and
/// optionally compassLabelText.
/// </summary>
public class HUDCompassController : MonoBehaviour
{
    public static HUDCompassController Instance;

    [Header("UI References")]
    public GameObject compassRoot;
    public RectTransform compassArrow;
    public Text compassLabelText;

    [Header("Behavior")]
    [Tooltip("If the player gets this close to the target (world units), the compass auto-hides.")]
    public float autoHideDistance = 3f;
    [Tooltip("If true, autoHideDistance triggers Hide() automatically. Set false for targets you want to manage manually.")]
    public bool autoHideOnArrival = true;

    private Transform currentTarget;
    private Transform playerTransform;
    private Camera activeCamera;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (compassRoot != null)
            compassRoot.SetActive(false);
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;

        activeCamera = Camera.main;
    }

    void Update()
    {
        if (currentTarget == null || playerTransform == null) return;

        if (autoHideOnArrival)
        {
            float dist = Vector3.Distance(playerTransform.position, currentTarget.position);
            if (dist <= autoHideDistance)
            {
                Hide();
                return;
            }
        }

        UpdateArrowRotation();
    }

    private void UpdateArrowRotation()
    {
        if (compassArrow == null) return;

        Vector3 toTarget = currentTarget.position - playerTransform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // Reference forward: camera forward flattened to the ground plane,
        // since the player moves with directional buttons relative to the
        // camera, not a fixed world axis.
        Vector3 referenceForward = playerTransform.forward;
        if (activeCamera != null)
        {
            Vector3 camForward = activeCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 0.0001f)
                referenceForward = camForward;
        }
        referenceForward.y = 0f;

        float signedAngle = Vector3.SignedAngle(referenceForward, toTarget, Vector3.up);
        compassArrow.localRotation = Quaternion.Euler(0f, 0f, -signedAngle);
    }

    /// <summary>
    /// Points the compass at any target. Call this from anywhere,
    /// NPCController on dialogue complete, quest triggers, whatever needs it.
    /// </summary>
    public void ShowCompassTo(Transform target, string label = "")
    {
        if (target == null)
        {
            Debug.LogWarning("[HUDCompassController] ShowCompassTo called with a null target.");
            return;
        }

        currentTarget = target;

        if (activeCamera == null)
            activeCamera = Camera.main;

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                playerTransform = playerObj.transform;
        }

        if (compassLabelText != null)
            compassLabelText.text = label;

        if (compassRoot != null)
            compassRoot.SetActive(true);
    }

    public void Hide()
    {
        currentTarget = null;
        if (compassRoot != null)
            compassRoot.SetActive(false);
    }

    public bool IsShowing() => currentTarget != null;
}