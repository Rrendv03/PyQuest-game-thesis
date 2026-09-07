using System;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    // Degrees per second. 120 is a natural feeling turn rate.
    // Increase for snappier turns, decrease for tank-like turning.
    public float turnSpeed = 120f;

    private Rigidbody rb;

    void Awake()
    {
        // PHYSICS SETUP FIRST.
        // The save-restore block used to run above this line. When it threw
        // ("Nullable object must have a value"), rb was never assigned, so
        // FixedUpdate threw NullReferenceException every physics frame and
        // the player could not move. Rigidbody init must never be skipped,
        // so it goes first.
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.freezeRotation = true;

        TryApplyTransitionRestore();
    }

    void Start() { }

    void FixedUpdate()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        if (HUDController.MoveForward) v = 1f;
        if (HUDController.MoveBackward) v = -1f;
        if (HUDController.MoveLeft) h = -1f;
        if (HUDController.MoveRight) h = 1f;

        // Degrees this physics step
        float turnDelta = h * turnSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turnDelta, 0f));

        Vector3 move = transform.forward * v * speed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
    }

    /// <summary>
    /// Restores the position/rotation staged by SaveLoadManager.ApplySaveData
    /// (load-a-save) or by SceneTransition (walking through a door).
    ///
    /// CONFIRMED API (SceneTransition.cs, cross-checked against the real file):
    ///   public static Vector3 RespawnPoint;      // plain Vector3: zero = nothing staged
    ///   public static float?  RespawnYRotation;  // nullable: null = nothing staged
    ///   public static bool    SkipSpawnPositioning; (consumed by SanctumManager, untouched here)
    ///
    /// BUG FIX: the previous version read RespawnYRotation as if it were a
    /// plain float. It is float?, and on a fresh New Game / first scene
    /// entry it is still null (only a save load or door transition stages
    /// it beforehand), so reading it threw "Nullable object must have a
    /// value" at Awake() line 32. Awake aborted, rb was never assigned,
    /// and FixedUpdate threw NullReferenceException every physics frame -
    /// the frozen player. Loading a save populated the value first, which
    /// is exactly why save-loads worked while first entry did not.
    ///
    /// Wrapped in try/catch so no future restore problem can ever kill
    /// movement: worst case the player spawns at the scene default and a
    /// warning is logged.
    /// </summary>
    private void TryApplyTransitionRestore()
    {
        try
        {
            // RespawnPoint is a plain Vector3 (zero = nothing staged), so it
            // uses the zero sentinel - it has no HasValue. Clearing after
            // use matters: the statics are never reset by SceneTransition or
            // SaveLoadManager, so a stale value would otherwise teleport the
            // player to an old position on a New Game after a save session.
            if (SceneTransition.RespawnPoint != Vector3.zero)
            {
                Vector3 savedPos = SceneTransition.RespawnPoint;
                transform.position = savedPos;
                rb.position = savedPos;
                SceneTransition.RespawnPoint = Vector3.zero;
            }

            // RespawnYRotation IS nullable (float?) - guard with HasValue.
            if (SceneTransition.RespawnYRotation.HasValue)
            {
                float yaw = SceneTransition.RespawnYRotation.Value;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                rb.rotation = transform.rotation;
                SceneTransition.RespawnYRotation = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Save/transition restore skipped: " + e);
        }
    }
}
