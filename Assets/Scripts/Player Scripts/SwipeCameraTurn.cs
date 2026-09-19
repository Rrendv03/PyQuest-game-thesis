using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class SwipeCameraTurn : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Turning")]
    [Tooltip("Camera degrees per pixel of horizontal drag. Raise for faster turns.")]
    public float degreesPerPixel = 0.2f;
    [Tooltip("Camera degrees per pixel of vertical drag. 0 disables pitch (keep 0 if the camera is a locked rig).")]
    public float pitchDegPerPixel = 0.05f;
    [Tooltip("Clamped camera pitch (X angle) range so you can't flip the camera over.")]
    public Vector2 pitchLimits = new Vector2(10f, 90f);

    [Header("Objects (leave empty for auto-find)")]
    [Tooltip("The camera to rotate. Empty = Camera.main (your gameplay camera).")]
    public Transform cameraOverride;
    [Tooltip("The pivot to orbit around. Empty = the PlayerMovement object in the scene.")]
    public Transform playerOverride;

    [Header("Desktop Keys (testing)")]
    [Tooltip("Degrees per second rotated while Q/E are held (legacy input only).")]
    public float keyboardTurnSpeed = 120f;

    private Transform player;

    public void OnBeginDrag(PointerEventData eventData) { ResolveReferences(); }

    public void OnDrag(PointerEventData eventData)
    {
        try
        {
            if (eventData == null) return;
            float deltaYaw = eventData.delta.x * degreesPerPixel;
            float deltaPitch = eventData.delta.y * pitchDegPerPixel;
            ApplyCameraTurn(deltaYaw, deltaPitch);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SwipeCameraTurn] Drag turn skipped: " + e.Message);
        }
    }

    public void OnEndDrag(PointerEventData eventData) { }

    void Update()
    {
        // Q / E turn for desktop playtesting (uses the legacy input manager
        // if enabled; silently skipped otherwise).
#if ENABLE_LEGACY_INPUT_MANAGER
        float keyTurn = 0f;
        if (Input.GetKey(KeyCode.Q)) keyTurn -= keyboardTurnSpeed;
        if (Input.GetKey(KeyCode.E)) keyTurn += keyboardTurnSpeed;
        if (keyTurn != 0f) ApplyCameraTurn(keyTurn * Time.deltaTime, 0f);
#endif
    }

    private void ApplyCameraTurn(float deltaYaw, float deltaPitch)
    {
        ResolveReferences();

        Transform cam = cameraOverride;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam == null || player == null) return;

        // Orbit the camera position around the player pivot. RotateAround
        // moves the position AND the orientation together, so after the
        // turn the camera keeps looking at the same framing relative to
        // the player - no manual LookRotation math needed.
        if (deltaYaw != 0f)
            cam.RotateAround(player.position, Vector3.up, deltaYaw);
        if (deltaPitch != 0f)
        {
            cam.RotateAround(player.position, cam.right, -deltaPitch);
            // Clamp pitch so the camera cannot be dragged under the ground
            // or flipped upside down.
            Vector3 e = cam.rotation.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);
            float yaw = e.y;
            float roll = e.z;
            cam.rotation = Quaternion.Euler(pitch, yaw, roll);
        }
    }

    private void ResolveReferences()
    {
        if (player != null) return;
        player = playerOverride;
        if (player != null) return;
        try
        {
            PlayerMovement mover = FindObjectOfType<PlayerMovement>();
            if (mover != null) player = mover.transform;
        }
        catch (Exception) { /* leave player null; drag becomes a no-op */ }
    }
}
