// SwipeCameraTurn.cs
//
// PYQUEST — swipe-to-turn camera control.
//
// WHY THIS EXISTS: PlayerMovement was switched to camera-relative movement
// (pushing the stick up moves away from the camera, etc.) so the foot
// animations could line up with the camera, which removed the old
// "joystick-x rotates the player" tank controls. This script gives the
// turn ability back as a SCREEN GESTURE: drag anywhere on a dedicated
// swipe area to orbit the main gameplay camera around the player. The
// player body automatically turns to match the camera while walking
// (handled in PlayerMovement), so swiping IS turning.
//
// UNITY SETUP (2 minutes):
//   1. On your HUD Canvas:  GameObject > UI > Image, name it
//      "ScreenSwipeArea", set the sprite to None, Color alpha = 0
//      (invisible), and stretch it to fill the whole canvas
//      (Anchor Presets > stretch/stretch, offsets all 0).
//   2. Add this component (SwipeCameraTurn) to that Image.
//   3. If you use the Input System package, make sure the canvas
//      EventSystem has InputSystemUIInputModule (not StandaloneInput).
//   4. IMPORTANT — draw order: the swipe Image must come BEFORE (above in
//      the Hierarchy list = drawn earlier) the joystick. UI raycasts hit
//      the LAST matching raycast target in hierarchy order, so place the
//      Joystick (and any buttons) as SIBLINGS below/after it in the
//      hierarchy. That way dragging on the stick works normally and any
//      other drag goes to the swipe area.
//   5. Hide this image during encounters the same way the HUD is hidden
//      (HUDController.SetVisible already hides a whole HUD canvas).
//
// Desktop testing: hold Q and E to rotate the camera left/right.
//
// Wrapped in try/catch everywhere: the worst case is "swiping does
// nothing" — movement, combat, saving are never affected.
//
// STUTTER FIX (Android): OnDrag used to rotate the camera directly, at
// TOUCH-EVENT rate. Android touch sampling is not in step with the display,
// so some rendered frames got two corrections and others none — which felt
// like judder exactly and only WHILE swiping. OnDrag now only accumulates
// the pending angle (two float adds, effectively free) and LateUpdate
// applies the total once per rendered frame, after ThirdPersonCamera has
// run (DefaultExecutionOrder below).

using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Run LateUpdate AFTER ThirdPersonCamera (default order 0) so the buffered
// swipe rotation is the last word on the camera each frame.
[DefaultExecutionOrder(100)]
public class SwipeCameraTurn : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Turning")]
    [Tooltip("Camera degrees per pixel of horizontal drag. Raise for faster turns.")]
    public float degreesPerPixel = 0.35f;
    [Tooltip("Camera degrees per pixel of vertical drag. 0 disables pitch (keep 0 if the camera is a locked rig).")]
    public float pitchDegPerPixel = 0.15f;
    [Tooltip("Clamped camera pitch (X angle) range so you can't flip the camera over.")]
    public Vector2 pitchLimits = new Vector2(10f, 70f);

    [Header("Objects (leave empty for auto-find)")]
    [Tooltip("The camera to rotate. Empty = Camera.main (your gameplay camera).")]
    public Transform cameraOverride;
    [Tooltip("The pivot to orbit around. Empty = the PlayerMovement object in the scene.")]
    public Transform playerOverride;

    [Header("Desktop Keys (testing)")]
    [Tooltip("Degrees per second rotated while Q/E are held (legacy input only).")]
    public float keyboardTurnSpeed = 120f;

    private Transform player;

    // --- Buffered swipe input (see header: STUTTER FIX) -----------------
    private float pendingYaw;
    private float pendingPitch;
    private float nextPlayerSearchTime;
    private static float lastDragEventTime = -999f;

    // True while a swipe is actively steering the camera (short grace window
    // so a missed OnEndDrag — Android pointercancel — cannot leave it stuck
    // on). ThirdPersonCamera reads this and stands down while it is true,
    // so the two scripts never fight over the view.
    public static bool IsDragging =>
        Time.unscaledTime - lastDragEventTime < 0.15f;

    public void OnBeginDrag(PointerEventData eventData)
    {
        lastDragEventTime = Time.unscaledTime;
        ResolveReferences();
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Buffer only — no scene lookups, no camera writes, no allocations
        // per touch event. The camera itself moves in LateUpdate, once per
        // rendered frame.
        if (eventData == null) return;
        lastDragEventTime = Time.unscaledTime;
        pendingYaw += eventData.delta.x * degreesPerPixel;
        pendingPitch += eventData.delta.y * pitchDegPerPixel;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Nothing to do: any pending motion is still applied this frame in
        // LateUpdate, so the gesture never loses its final flick.
    }

    void Update()
    {
        // Q / E turn for desktop playtesting (uses the legacy input manager
        // if enabled; silently skipped otherwise).
#if ENABLE_LEGACY_INPUT_MANAGER
        float keyTurn = 0f;
        if (Input.GetKey(KeyCode.Q)) keyTurn -= keyboardTurnSpeed;
        if (Input.GetKey(KeyCode.E)) keyTurn += keyboardTurnSpeed;
        if (keyTurn != 0f) pendingYaw += keyTurn * Time.deltaTime;
#endif
    }

    void LateUpdate()
    {
        // Apply the buffered drag exactly once per rendered frame — this is
        // what makes the turn smooth on Android regardless of how unevenly
        // touch events arrive.
        if (pendingYaw == 0f && pendingPitch == 0f) return;
        float yaw = pendingYaw;
        float pitch = pendingPitch;
        pendingYaw = 0f;
        pendingPitch = 0f;
        try
        {
            ApplyCameraTurn(yaw, pitch);
        }
        catch (Exception e)
        {
            // Same defensive promise as before: the worst case is a skipped
            // turn, never a broken game.
            Debug.LogWarning("[SwipeCameraTurn] Turn skipped: " + e.Message);
        }
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
        // Throttle the scene scan: if the player cannot be found this used
        // to retry on EVERY drag event, which is expensive. Once per 0.5 s.
        float now = Time.unscaledTime;
        if (now < nextPlayerSearchTime) return;
        nextPlayerSearchTime = now + 0.5f;
        try
        {
#if UNITY_2023_1_OR_NEWER
            PlayerMovement mover = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
#else
            PlayerMovement mover = FindObjectOfType<PlayerMovement>();
#endif
            if (mover != null) player = mover.transform;
        }
        catch (Exception) { /* leave player null; drag becomes a no-op */ }
    }
}
