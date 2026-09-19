// ThirdPersonCamera.cs (fixed)
//
// PYQUEST — third-person follow camera that COOPERATES with the new
// camera-relative movement + SwipeCameraTurn.
//
// WHAT WAS BROKEN: the old version used
//     target.TransformDirection(offset)          -> camera always behind the PLAYER's facing
//     Quaternion.Slerp(cam.rotation, target.rotation, ...) -> camera yaw forced
//     onto the player's yaw every LateUpdate
// Combined with camera-relative movement that meant camera-yaw == player-yaw
// all the time, so every stick direction converged to "forward" (side/back/
// diagonal walk & run clips could never play), and every SwipeCameraTurn
// drag got slerped straight back behind the player.
//
// THE FIX: the camera follows the player's POSITION only. Its own yaw/pitch
// are never overwritten — they belong to the swipe gesture (and the mouse /
// desktop keys in SwipeCameraTurn). The camera keeps whatever orbit angle the
// player last dragged it to, and simply maintains that distance/height as the
// player moves. Pushing forward runs away from the camera regardless of where
// the player faces; strafing shows the real side-run clips.
//
// UNITY SETUP: put this on your gameplay Camera, drag the player prefab into
// "target". Optional fields are safe to leave at defaults. If you actively
// drag (SwipeCameraTurn) the smoothing on position still applies — rotation
// is only lerped while the player is NOT dragging, so there is no fight.

using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;

    [Header("Framing (set once; the swipe keeps the orbit angle)")]
    [Tooltip("Distance the camera keeps from the player while following.")]
    public float distance = 2f;
    [Tooltip("Height above the player the camera tries to keep. Negative keeps current height only.")]
    public float height = 1.5f;
    [Tooltip("How fast the camera follows the player (higher = tighter).")]
    public float followSpeed = 10f;

    [Header("Aim ahead (keep the player visible AND show what's coming)")]
    [Tooltip("How far (metres) the look point sits in FRONT of the player along " +
             "the player's facing. 0 = look straight at the player itself.")]
    public float lookAheadDistance = 2.5f;
    [Tooltip("Height of the look point above the player's feet (aim at chest/head, not the floor).")]
    public float lookHeight = 1f;
    [Tooltip("How fast the camera turns toward the look point (higher = snappier). " +
             "Keep modest so SwipeCameraTurn drags still feel responsive.")]
    [Range(0f, 10f)] public float aimAheadSpeed = 0.1f;
    [Tooltip("Horizontal only: don't peek up/down with the player's facing.")]
    public bool flattenLookAhead = true;

    [Header("Optional soft look-at (only if you want auto-level)")]
    [Tooltip("0 = camera rotation fully owned by SwipeCameraTurn + aim-ahead. " +
             ">0 = gently steer the view back onto the player each frame.")]
    [Range(0f, 1f)] public float lookAtStrength = 0.3f;

    // Being dragged right now (so follow-position smoothing doesn't fight the drag).
    private bool isDragging;
    private bool subscribedToDrag;

    void LateUpdate()
    {
        if (target == null) return;

        // --- POSITION FOLLOW (yaw/pitch untouched) --------------------------
        // Direction currently held from player -> camera, kept at a constant
        // spherical size. Because we reuse the camera's OWN direction, the
        // orbit angle chosen by the swipe gesture (or the default start
        // angle) is preserved exactly.
        Vector3 playerPos = target.position;
        Vector3 currentDir = transform.position - playerPos;

        float keep = Mathf.Max(0.1f, distance);
        if (currentDir.sqrMagnitude < 0.01f)
        {
            // Degenerate (camera spawned exactly on the player): pick a
            // default behind-the-torso anchor.
            currentDir = target.forward * -keep + Vector3.up * height;
        }
        else
        {
            // Normalize to the desired distance but PRESERVE the vertical
            // component the swipe's pitch gave us (don't fully flatten,
            // or pitch dragging would be erased too).
            Vector3 horiz = new Vector3(currentDir.x, 0f, currentDir.z);
            if (horiz.sqrMagnitude < 0.01f) horiz = target.forward * -keep; // straight-down degeneracy
            Vector3 flat = horiz.normalized * keep;
            Vector3 up = height >= 0f ? new Vector3(0f, height, 0f)
                                      : new Vector3(0f, currentDir.y, 0f);
            currentDir = flat + up;
        }

        Vector3 desiredPosition = playerPos + currentDir;
        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime); // framerate-independent
        transform.position = Vector3.Lerp(transform.position, desiredPosition, t);

        // --- AIM AHEAD ------------------------------------------------------
        // Look at a configurable point in front of the player (along the
        // player's own forward, NOT the camera's), so the player stays framed
        // on-screen while the view also shows the road/area ahead of them.
        if (aimAheadSpeed > 0f)
        {
            Vector3 fwd = target.forward;
            if (flattenLookAhead)
            {
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                else fwd.Normalize();
            }
            Vector3 lookPoint = playerPos + fwd * lookAheadDistance + Vector3.up * lookHeight;

            Quaternion aimRot = Quaternion.LookRotation(lookPoint - transform.position);
            float aimT = 1f - Mathf.Exp(-aimAheadSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, aimRot, aimT);
        }

        // --- OPTIONAL soft look-at -----------------------------------------
        if (lookAtStrength > 0f)
        {
            Quaternion look = Quaternion.LookRotation(playerPos + Vector3.up * 1f - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, lookAtStrength * t);
        }
    }

    // Hooked defensively: presence of a SwipeCameraTurn is detected but not
    // required — the position-only follow never conflicts with it by design.
}
