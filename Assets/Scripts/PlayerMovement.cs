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
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.freezeRotation = true;

        if (SceneTransition.RespawnPoint != Vector3.zero)
        {
            transform.position = SceneTransition.RespawnPoint;
            SceneTransition.RespawnPoint = Vector3.zero;
        }
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
}