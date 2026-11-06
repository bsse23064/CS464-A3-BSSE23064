using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float speed = 5f;

    [Header("Jump")]
    [SerializeField] float jumpHeight = 1.8f;
    [SerializeField] float fallMultiplier = 2.2f;
    [SerializeField] float coyoteTime = 0.1f;       // still allowed to jump this long after leaving the ground
    [SerializeField] float jumpBuffer = 0.12f;      // a jump pressed this early still counts on landing

    float speedMultiplier = 1f;
    Coroutine boost;

    Rigidbody rb;
    Transform cam;
    Vector3 move;
    bool jumpQueued;
    float lastGroundedTime = -1f, lastPressedTime = -1f, lastJumpTime = -1f;
    float noControlUntil;                           // steering is off until this time (knockback)

    // Moving platforms: remember what we stand on so we can ride it
    Transform groundTf, carriedTf;
    Vector3 carriedLastPos;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;                   // a capsule would tip over
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.sleepThreshold = 0f;                     // a sleeping body stops reporting ground contacts

        // Zero friction so the capsule doesn't stick to walls while airborne
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.material = new PhysicsMaterial
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };

        if (Camera.main != null) cam = Camera.main.transform;
    }

    void Update()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // Move relative to the camera (W = away from the camera), or the world if there's no camera
        Vector3 input = new Vector3(h, 0f, v);
        if (cam != null)
        {
            Vector3 forward = cam.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = cam.right;     right.y = 0f;   right.Normalize();
            input = right * h + forward * v;
        }
        move = Vector3.ClampMagnitude(input, 1f);

        if (Input.GetKeyDown(KeyCode.Space))
            lastPressedTime = Time.time;

        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        bool wantJump = Time.time - lastPressedTime <= jumpBuffer;
        if (canJump && wantJump)
        {
            jumpQueued = true;
            lastJumpTime = Time.time;
            lastGroundedTime = lastPressedTime = -1f;   // used up
        }
    }

    void FixedUpdate()
    {
        // Ride a moving platform: copy how far it moved since the last step
        Vector3 carry = Vector3.zero;
        if (groundTf != null && Time.time - lastGroundedTime < 0.1f)
        {
            if (groundTf == carriedTf)
            {
                Vector3 delta = groundTf.position - carriedLastPos;
                carry = new Vector3(delta.x, 0f, delta.z) / Time.fixedDeltaTime;
            }
            carriedTf = groundTf;
            carriedLastPos = groundTf.position;
        }
        else carriedTf = null;

        // Steering is off briefly after a hit, so the knockback isn't overwritten
        if (Time.time >= noControlUntil)
        {
            Vector3 flat = move * speed * speedMultiplier + carry;
            rb.linearVelocity = new Vector3(flat.x, rb.linearVelocity.y, flat.z);
        }

        if (jumpQueued)
        {
            jumpQueued = false;
            Vector3 vel = rb.linearVelocity;
            rb.linearVelocity = new Vector3(vel.x, 0f, vel.z);
            float g = -Physics.gravity.y;
            float v0 = Mathf.Sqrt(2f * g * jumpHeight);
            rb.AddForce(Vector3.up * rb.mass * v0, ForceMode.Impulse);
        }

        if (rb.linearVelocity.y < 0f)
            rb.AddForce(Physics.gravity * (fallMultiplier - 1f), ForceMode.Acceleration);
    }

    // Grounded = touching something whose surface faces mostly upward
    void OnCollisionStay(Collision collision)
    {
        if (Time.time - lastJumpTime < 0.15f) return;   // don't re-ground on the jump frame

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                lastGroundedTime = Time.time;
                groundTf = collision.transform;
                return;
            }
        }
    }

    // A push that steering can't wipe: called by Hazard when a hit lands
    public void Knockback(Vector3 push, float seconds)
    {
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(0f, v.y, 0f);
        rb.AddForce(push, ForceMode.Impulse);
        noControlUntil = Time.time + seconds;
    }

    // Called by PowerUp. A second pickup restarts the timer (the "refresh" rule).
    public void BoostSpeed(float multiplier, float seconds)
    {
        if (boost != null) StopCoroutine(boost);
        boost = StartCoroutine(Boost(multiplier, seconds));
    }

    IEnumerator Boost(float multiplier, float seconds)
    {
        speedMultiplier = multiplier;
        yield return new WaitForSeconds(seconds);
        speedMultiplier = 1f;
        boost = null;
    }
}