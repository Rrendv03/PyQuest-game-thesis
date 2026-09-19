using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    // Camera-relative movement (Genshin/Zelda style): stick is read in CAMERA
    // space, body faces where it moves. rotationSmoothTime = turn response (s).
    [Min(0.01f)] public float rotationSmoothTime = 0.12f;

    // Working state for Mathf.SmoothDampAngle (persists across frames so
    // the damping stays critically damped instead of re-accelerating).
    private float yawVelocity;

    [Header("Walk vs Run (NEW)")]
    [Tooltip("Full-pedal walk speed. Stick deflection below runThreshold scales from 0 up to this.")]
    public float walkSpeed = 5f;
    [Tooltip("Speed when the stick (or 'move' key) is pushed past runThreshold - the RUN.")]
    public float runSpeed = 9f;
    [Tooltip("Stick magnitude at/above which the player RUNS instead of walks (0.5-1.0).")]
    [Range(0.3f, 1f)] public float runThreshold = 0.7f;

    [Header("Footstep Audio (walk loop + run loop)")]
    [Tooltip("Looping clip played while WALKING (stick below runThreshold). Leave empty for silence.")]
    public AudioClip walkSound;
    [Tooltip("Looping clip played while RUNNING (stick at/above runThreshold). Leave empty for silence.")]
    public AudioClip runSound;
    [Tooltip("Playback SPEED of the walk loop (AudioSource.pitch): 1 = normal, 2 = twice as fast, 0.5 = half speed. Tune until steps match the legs. Can be changed live in Play mode.")]
    [Range(0.1f, 3f)] public float walkSoundSpeed = 1f;
    [Tooltip("Playback SPEED of the run loop, tuned independently from the walk loop.")]
    [Range(0.1f, 3f)] public float runSoundSpeed = 1f;
    [Tooltip("Loudness of each loop, 0-1.")]
    [Range(0f, 1f)] public float walkSoundVolume = 0.8f;
    [Range(0f, 1f)] public float runSoundVolume = 0.8f;
    [Tooltip("Seconds to fade between walk/run/stop so switches never click or pop.")]
    [Min(0.01f)] public float footstepFadeTime = 0.1f;

    private Rigidbody rb;

    // Locomotion animator (IsWalking etc.): we search the hierarchy and keep
    // the first Animator that HAS a controller - a bare wrapper Animator on
    // the prefab root silently swallows SetTrigger/SetBool.
    private Animator animator;

    // Set once so a broken gamepad read doesn't spam the Console every
    // physics frame (FixedUpdate runs ~50x per second).
    private static bool loggedGamepadWarning = false;

    // Footstep audio sources - created automatically in Awake (one child
    // GameObject each), so no manual setup beyond assigning the two clips.
    private AudioSource walkSource;
    private AudioSource runSource;

    void Awake()
    {
        // Rigidbody init must never be skipped (a throw above it once left rb
        // null and froze the player), so it goes first.
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.freezeRotation = true;

        // ANTI-JITTER: RB interpolation gives every rendered frame an
        // in-between transform between physics steps instead of a snap.
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // ANTI-JITTER (round 2): 60 Hz physics lines up with the display
        // cadence; frictionless colliders and the input deadzone (below)
        // kill the remaining shimmer sources.
        if (Time.fixedDeltaTime > 1f / 60f)
            Time.fixedDeltaTime = 1f / 60f;
        MakeCollidersFrictionless();

        TryApplyTransitionRestore();

        ConfigurePlayableAnimator();

        SetupFootstepAudio();
    }

    void Start() { }

    // ENCOUNTER ANIMATION FIX: EncounterManager disables this component in a
    // fight, freezing the last IsWalking=true. OnDisable is the guaranteed
    // hook to clear it (and the blend floats) so encounter triggers play.
    void OnDisable()
    {
        // Encounter hand-off: the player stops moving, so the footsteps must
        // too. Runs BEFORE the animator block because that can return early.
        StopFootsteps();
        try
        {
            if (animator == null) ConfigurePlayableAnimator();
            if (animator == null) return;
            animator.SetBool(IsWalkingHash, false);
            animator.SetBool(IsRunningHash, false);
            animator.SetFloat(MoveXHash, 0f);
            animator.SetFloat(MoveYHash, 0f);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Encounter hand-off animation reset skipped: " + e.Message);
            animator = null;
        }
    }

    void FixedUpdate()
    {
        // INPUT ORDER: OnScreenStick (virtual Gamepad) -> VirtualJoystick.cs ->
        // keyboard axes. HUDController button overrides were removed; re-add
        // them after the joystick reads below if the buttons are ever needed.

        float h = 0f;
        float v = 0f;

#if ENABLE_INPUT_SYSTEM
        // Route 1: virtual gamepad fed by the OnScreenStick component.
        Vector2 stick = ReadGamepadLeftStick();
        if (stick.sqrMagnitude > 0f)
        {
            h = stick.x;   // x axis turns the player
            v = stick.y;   // y axis walks forward / backward
        }
#endif

        // Route 2: self-contained on-screen joystick (VirtualJoystick.cs).
        if (h == 0f && v == 0f)
        {
            Vector2 joy = VirtualJoystick.InputVector;
            if (joy.sqrMagnitude > 0f)
            {
                h = joy.x;
                v = joy.y;
            }
        }

        // Route 3: keyboard (digital) - a held key pins magnitude past
        // runThreshold = RUN; touch/joystick gets the smooth ramp instead.
        if (h == 0f && v == 0f)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            h = Input.GetAxis("Horizontal");
            v = Input.GetAxis("Vertical");
            if (h != 0f || v != 0f)
            {
                float mag = Mathf.Clamp01(new Vector2(h, v).magnitude);
                if (mag < runThreshold)
                {
                    float sign;
                    Vector2 dir = new Vector2(h, v);
                    sign = mag > 0.0001f ? 1f : 0f;
                    dir = dir.normalized * runThreshold * sign;
                    h = dir.x; v = dir.y; // boost keyboard input into the RUN band
                }
            }
#endif
        }

        // ANTI-JITTER: released sticks never read exactly zero (sensor noise),
        // which caused creep and Idle/Walk flicker; the remap keeps direction
        // but removes the noisy inner core.
        const float Deadzone = 0.05f;
        {
            Vector2 raw = new Vector2(h, v);
            float rawMag = raw.magnitude;
            if (rawMag < Deadzone) { h = 0f; v = 0f; }
            else
            {
                float rescaled = Mathf.Clamp01((rawMag - Deadzone) / (1f - Deadzone) / rawMag);
                h = raw.x * rescaled;
                v = raw.y * rescaled;
            }
        }

        // CAMERA-RELATIVE MOVEMENT: stick is read in camera space (up = away
        // from camera), body turns to match - keeps the directional walk/run
        // clips lined up with the action on screen.
        float cameraYaw = CameraForwardYaw();
        if (cameraYaw != NoCameraYaw)
        {
            // Flatten camera forward/right, combine with stick, normalize,
            // SmoothDampAngle the body yaw onto the target, move along it.
            Vector2 input = new Vector2(h, v);
            float inputMag = Mathf.Clamp01(input.magnitude);

            if (input.sqrMagnitude > 0.0001f)
            {
                Quaternion camRot = Quaternion.Euler(0f, cameraYaw, 0f);
                Vector3 camFwd = camRot * Vector3.forward;   // horizontal, Y already stripped
                Vector3 camRight = camRot * Vector3.right;   // by CameraForwardYaw()

                Vector3 moveDir = camFwd * input.y + camRight * input.x;
                moveDir = moveDir.normalized; // stable direction even at partial deflection

                // Face where we're going (yaw = Atan2(x, z)): diagonals settle
                // on 45 deg steps, exactly what the diagonal clips assume.
                float currentYaw = rb.rotation.eulerAngles.y;
                float targetYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                float smoothYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, rotationSmoothTime);
                rb.MoveRotation(Quaternion.Euler(0f, smoothYaw, 0f));

                // WALK/RUN off the stick position: partial deflect walks (slower,
                // walk clips), full push to the edge runs (runSpeed, run clips).
                rb.MovePosition(rb.position + moveDir * SpeedForInput(inputMag) * Time.fixedDeltaTime);
            }
            else
            {
                yawVelocity = 0f; // released the stick: damping starts from rest next push
            }

            UpdateLocomotionAnimator(h, v, inputMag);
            UpdateFootstepAudio(h, v, inputMag);
        }
        else
        {
            // NO camera found (rare - camera tagged scene without a "MainCamera"):
            // fall back to the original tank controls so movement never breaks.
            float turnDelta = h * 120f * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turnDelta, 0f));
            Vector3 move = transform.forward * v * speed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + move);
            UpdateLocomotionAnimator(0f, v, Mathf.Clamp01(Mathf.Abs(v)));
            UpdateFootstepAudio(0f, v, Mathf.Clamp01(Mathf.Abs(v)));
        }
    }

    // Hashes of the animator parameters (avoids per-frame string lookups).
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    /// <summary>Stick magnitude -> speed: ramps 0..walkSpeed (WALK) up to
    /// runThreshold, then walkSpeed..runSpeed (RUN); IsRunning flips at the
    /// same threshold.</summary>
    private float SpeedForInput(float mag)
    {
        if (mag <= 0f) return 0f;
        if (mag <= runThreshold) return speed * (mag / runThreshold);
        float t = (mag - runThreshold) / Mathf.Max(0.0001f, 1f - runThreshold);
        return Mathf.Lerp(speed, runSpeed, t);
    }

    // Yaw sentinel meaning "no MainCamera in this scene yet" (0-359 is always
    // a real yaw, so we need an out-of-range marker for the fallback branch).
    private const float NoCameraYaw = 9999f;

    private static float CameraForwardYaw()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return NoCameraYaw;
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) f = Vector3.forward; // camera looking dead-vertical: treat as +Z
            return Quaternion.LookRotation(f.normalized).eulerAngles.y;
        }
        catch (Exception)
        {
            return NoCameraYaw;
        }
    }

    /// <summary>Drives IsWalking/IsRunning bools and MoveX/MoveY (-1..1) for
    /// the directional blend trees. Names must match PlayerAnimatorBuilder
    /// exactly; failure is cosmetic only, movement is never affected.</summary>
    private void UpdateLocomotionAnimator(float h, float v, float inputMag = -1f)
    {
        try
        {
            if (animator == null)
            {
                ConfigurePlayableAnimator(); // covers animators appearing later (model swap/prefab reassign)
                if (animator == null) return;
            }
            Vector2 input = new Vector2(h, v);
            float mag = inputMag >= 0f ? inputMag : Mathf.Clamp01(input.magnitude);
            bool moving = input.sqrMagnitude > 0.0025f;
            animator.SetBool(IsWalkingHash, moving);
            // Same runThreshold as SpeedForInput: cross the stick threshold
            // and the controller blends from the Walk blend tree to the Run
            // blend tree (each has its own 4-direction direction set).
            animator.SetBool(IsRunningHash, mag >= runThreshold);
            animator.SetFloat(MoveXHash, Mathf.Clamp(h, -1f, 1f));
            animator.SetFloat(MoveYHash, Mathf.Clamp(v, -1f, 1f));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Locomotion animator update skipped: " + e.Message);
            animator = null;
        }
    }

    /// <summary>
    /// Caches the playable animator and aligns its update mode with physics.
    /// </summary>
    private void ConfigurePlayableAnimator()
    {
        FindAndCacheAnimator();
        if (animator == null) return;
        try
        {
            // Animate Physics samples inside the physics step so mesh,
            // collider and camera share one clock (kills the micro-shimmer
            // that RB interpolation otherwise exposes).
            if (animator.updateMode != AnimatorUpdateMode.AnimatePhysics)
                animator.updateMode = AnimatorUpdateMode.AnimatePhysics;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Could not set Animator update mode: " + e.Message);
        }
    }

    // FOOTSTEP AUDIO: two looping child AudioSources (walk + run), exactly one
    // audible, chosen by the same runThreshold as the animator; pitch (= speed)
    // is live-tunable. Wrapped in try/catch: audio can never break movement.

    private void SetupFootstepAudio()
    {
        try
        {
            walkSource = CreateFootstepSource("Footsteps (Walk)", walkSound, walkSoundSpeed);
            runSource = CreateFootstepSource("Footsteps (Run)", runSound, runSoundSpeed);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Footstep audio setup skipped: " + e.Message);
            walkSource = runSource = null;
        }
    }

    private AudioSource CreateFootstepSource(string sourceName, AudioClip clip, float speed)
    {
        var child = new GameObject(sourceName);
        child.transform.SetParent(transform, false); // dies with the player
        var src = child.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;         // repeats for as long as you keep moving
        src.playOnAwake = false;
        src.volume = 0f;         // faded in by UpdateFootstepAudio - no pop on start
        src.pitch = Mathf.Clamp(speed, 0.1f, 3f);
        src.spatialBlend = 0f;   // 2D: the camera follows the player, so
                                 // there is never any meaningful distance
        return src;
    }

    private void UpdateFootstepAudio(float h, float v, float inputMag)
    {
        try
        {
            // Identical movement/running decision as UpdateLocomotionAnimator
            // (same deadzone-cleaned input, same thresholds), so the audio
            // can never disagree with the playing clip.
            Vector2 input = new Vector2(h, v);
            float mag = inputMag >= 0f ? inputMag : Mathf.Clamp01(input.magnitude);
            bool moving = input.sqrMagnitude > 0.0025f;
            bool running = mag >= runThreshold;

            // Volume moves in fixed-step increments: footstepFadeTime is the
            // full fade duration from silence to full (or back).
            float fadeStep = Time.fixedDeltaTime / Mathf.Max(0.01f, footstepFadeTime);

            if (walkSource != null)
            {
                walkSource.pitch = Mathf.Clamp(walkSoundSpeed, 0.1f, 3f); // live retune
                float target = (moving && !running) ? walkSoundVolume : 0f;
                walkSource.volume = Mathf.MoveTowards(walkSource.volume, target, fadeStep);
                if (walkSource.clip != null && walkSource.volume > 0f && !walkSource.isPlaying)
                    walkSource.Play();
                else if (walkSource.volume <= 0f && walkSource.isPlaying)
                    walkSource.Stop();
            }

            if (runSource != null)
            {
                runSource.pitch = Mathf.Clamp(runSoundSpeed, 0.1f, 3f);
                float target = (moving && running) ? runSoundVolume : 0f;
                runSource.volume = Mathf.MoveTowards(runSource.volume, target, fadeStep);
                if (runSource.clip != null && runSource.volume > 0f && !runSource.isPlaying)
                    runSource.Play();
                else if (runSource.volume <= 0f && runSource.isPlaying)
                    runSource.Stop();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Footstep audio update skipped: " + e.Message);
            walkSource = runSource = null;
        }
    }

    /// <summary>Guaranteed silence for both loops; hooked from OnDisable so
    /// footsteps never keep playing over the encounter battle animations.</summary>
    private void StopFootsteps()
    {
        try
        {
            if (walkSource != null) { walkSource.Stop(); walkSource.volume = 0f; }
            if (runSource != null) { runSource.Stop(); runSource.volume = 0f; }
        }
        catch (Exception) { /* audio can never break movement */ }
    }

    /// <summary>Zero-friction material on the player's colliders: a capsule
    /// rim scraping the ground creates stick-slip jitter every physics step.
    /// Player only - the world keeps its own physics settings.</summary>
    private void MakeCollidersFrictionless()
    {
        try
        {
            var pm = new PhysicMaterial("PlayerMovementF0")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
                col.material = pm;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerMovement] Frictionless collider setup skipped: " + e.Message);
        }
    }

    /// Finds the Animator that actually has a controller (root first, then
    /// children) - a bare wrapper Animator silently swallows SetTrigger/SetBool.
    private void FindAndCacheAnimator()
    {
        // FBX imports ship an idle-only child Animator that wins the "first
        // with a controller" race; prefer one that HAS the encounter states.
        foreach (Animator a in GetComponentsInChildren<Animator>(true))
        {
            if (a == null || a.runtimeAnimatorController == null) continue;
            if (a.HasState(0, Animator.StringToHash("Attack")) ||
                a.HasState(0, Animator.StringToHash("TakeDamage")))
            {
                animator = a;
                return;
            }
        }

        animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null) return;

        animator = null;
        foreach (Animator a in GetComponentsInChildren<Animator>(true))
        {
            if (a != null && a.runtimeAnimatorController != null) { animator = a; break; }
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>Reads the hardest-pushed left stick across ALL gamepads (covers
    /// OnScreenStick's virtual pad; avoids Gamepad.current null/steal traps).
    /// Try/catch: worst case is no joystick input, never the frozen player.</summary>
    private static Vector2 ReadGamepadLeftStick()
    {
        try
        {
            Vector2 best = Vector2.zero;
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                Vector2 s = pads[i].leftStick.ReadValue();
                if (s.sqrMagnitude > best.sqrMagnitude)
                    best = s;
            }
            return best;
        }
        catch (Exception e)
        {
            if (!loggedGamepadWarning)
            {
                loggedGamepadWarning = true;
                Debug.LogWarning("[PlayerMovement] Gamepad/joystick read unavailable (" +
                                 e.Message + "). Falling back to keyboard / VirtualJoystick.");
            }
            return Vector2.zero;
        }
    }
#endif

    /// <summary>Restores the spawn staged by SaveLoadManager / SceneTransition:
    /// RespawnPoint (Vector3, zero = nothing staged) and RespawnYRotation
    /// (float?, null = nothing staged). Try/catch: worst case spawns at default.</summary>
    private void TryApplyTransitionRestore()
    {
        try
        {
            // Zero sentinel = nothing staged; clearing after use stops a stale
            // value teleporting the player on a later New Game.
            if (SceneTransition.RespawnPoint != Vector3.zero)
            {
                Vector3 savedPos = SceneTransition.RespawnPoint;
                transform.position = savedPos;
                rb.position = savedPos;
                SceneTransition.RespawnPoint = Vector3.zero;
                yawVelocity = 0f;
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
