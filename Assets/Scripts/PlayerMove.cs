using System.Collections;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    public Rigidbody rb;
    public float forwardForce = 2000f;      // legacy, not used for speed now
    public float sidewaysForce = 500f;

    [Header("Jump")]
    public float jumpForce = 6.5f;
    public float groundedCheckExtra = 0.05f;

    [Header("Slide")]
    public float slideDuration = 0.6f;
    public float slideSpeedMultiplier = 1.15f;   // no longer used for crazy boosts
    public float slideColliderScale = 0.5f;

    [Header("Animator State Names (fallback if tags not set)")]
    public string RunningState = "Running";
    public string JumpState = "Jump";
    public string SlideState = "Slide";
    public string WinState = "Win";

    [Header("Speed Debuff")]
    [SerializeField] private float speedMultiplier = 1f;   // 1 = normal
    private Coroutine debuffCo;

    [Header("Movement Smoothing")]
    [SerializeField] private float forwardAccel = 12f;     // how fast z reaches target
    [SerializeField] private float lateralSpeed = 6f;      // target x m/s when holding A/D
    [SerializeField] private float lateralAccel = 30f;     // how fast x reaches target
    [SerializeField] private float lateralDamp = 22f;      // how fast x returns to 0 with no input

    [Header("Jump Assist")]
    [SerializeField] private LayerMask groundMask = ~0;        // set to "Ground" layer(s)
    [SerializeField] private float groundProbePadding = 0.05f; // extra reach below capsule

    [Header("Speed Progression")]
    [SerializeField] private bool enableSpeedRamp = true;
    [Tooltip("Approximate % growth per second (no longer used in logic, kept so Inspector doesn't break.")]
    [SerializeField] private float speedRampRate = 0.1f;   // unused now
    [SerializeField] private float maxSpeedMultiplier = 3f; // unused now

    // Direct forward speed in m/s
    [SerializeField] private float startForwardSpeed = 4f;   // starting forward speed (m/s)
    [SerializeField] private float maxForwardSpeed = 20f;    // hard cap speed
    private float currentForwardSpeed;

    //how long we've been running
    private float runTime = 0f;

    private int jumpCount = 0;
    private int maxJumps = 2;
    private bool wasGrounded = false;

    private bool gameWon = false;
    private bool wonPlayed = false;
    private bool isSliding = false;
    private float slideEndTime = 0f;

    private Animator animator;
    private CapsuleCollider capsule;

    private float capOrigHeight;
    private Vector3 capOrigCenter;

    void Awake()
    {
        animator = GetComponent<Animator>();
        capsule = GetComponent<CapsuleCollider>();

        if (capsule)
        {
            capOrigHeight = capsule.height;
            capOrigCenter = capsule.center;
        }

        transform.position = Vector3.zero;

        if (rb) rb.linearVelocity = Vector3.zero;

        if (animator && animator.runtimeAnimatorController)
            animator.SetBool("isMoving", false);

        // start at base forward speed
        currentForwardSpeed = startForwardSpeed;
        runTime = 0f;
    }

    void Update()
    {
        if (gameWon)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.forceRenderingOff = false;
                smr.enabled = true;
                smr.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f));
            }
            return;
        }
        if (!enabled) return;

        // ---------- SPEED PROGRESSION (RUNNING ONLY) ----------
        // Simple, brutally obvious ramp: speed = start + (t * rampPerSecond)
        if (enableSpeedRamp)
        {
            // How many m/s we add PER SECOND
            const float rampPerSecond = 0.1f;   // tweak this value if needed

            runTime += Time.deltaTime;
            float rawSpeed = startForwardSpeed + rampPerSecond * runTime;
            currentForwardSpeed = Mathf.Clamp(rawSpeed, startForwardSpeed, maxForwardSpeed);
        }
        else
        {
            currentForwardSpeed = startForwardSpeed;
        }

        // ---------- MOVEMENT INPUT ----------
        Vector3 v = rb.linearVelocity;

        // Running & sliding use the SAME forward speed.
        float forwardSpeed = currentForwardSpeed * speedMultiplier;

        // DIRECTLY set forward speed; no smoothing that can hide the ramp
        v.z = forwardSpeed;

        // Lateral control with A/D
        float h = 0f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;

        float targetX = h * lateralSpeed * speedMultiplier;
        if (Mathf.Abs(h) > 0.01f)
            v.x = Mathf.MoveTowards(v.x, targetX, lateralAccel * Time.deltaTime);
        else
            v.x = Mathf.MoveTowards(v.x, 0f, lateralDamp * Time.deltaTime);

        // ---------- GROUND & JUMP ----------
        bool grounded = IsGroundedRobust();

        if (grounded && !wasGrounded)
        {
            // just landed
            jumpCount = 0;
        }
        wasGrounded = grounded;

        if (!isSliding && Input.GetKeyDown(KeyCode.Space) && jumpCount < maxJumps)
        {
            if (grounded || (jumpCount < maxJumps && !grounded))
            {
                v.y = 0f;
                rb.linearVelocity = v;
                rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);

                jumpCount++;

                if (animator)
                {
                    animator.ResetTrigger("jumpTrigger");
                    animator.SetTrigger("jumpTrigger");
                }
            }
        }

        // ---------- SLIDE (S) ----------
        if (!isSliding && grounded && Input.GetKeyDown(KeyCode.S))
            StartSlide(ref v);

        if (isSliding)
        {
            if (Time.time >= slideEndTime || Input.GetKeyUp(KeyCode.S))
                EndSlide();
        }

        // Apply velocity & clamp sideways only (keep forward ramp intact)
        rb.linearVelocity = v;

        float maxSideSpeed = lateralSpeed * speedMultiplier * 1.2f; // small safety margin
        rb.linearVelocity = new Vector3(
            Mathf.Clamp(rb.linearVelocity.x, -maxSideSpeed, maxSideSpeed),
            rb.linearVelocity.y,
            rb.linearVelocity.z // let Z grow with currentForwardSpeed
        );

        // ---------- ANIMATOR DRIVE ----------
        if (animator && animator.runtimeAnimatorController)
        {
            var s = animator.GetCurrentAnimatorStateInfo(0);
            bool inJump = s.IsTag(JumpState) || s.IsName(JumpState);
            bool inSlide = s.IsTag(SlideState) || s.IsName(SlideState);
            bool inWin = s.IsTag(WinState) || s.IsName(WinState);

            if (!inJump && !inSlide && !inWin)
            {
                animator.SetBool("isMoving", true);
            }

            if (!animator.IsInTransition(0))
            {
                if ((s.IsTag(JumpState) || s.IsName(JumpState)) && s.normalizedTime >= 0.98f && grounded)
                    ResumeRunningHard();

                if ((s.IsTag(SlideState) || s.IsName(SlideState)) && s.normalizedTime >= 0.98f && !isSliding)
                    ResumeRunningHard();
            }
        }
    }

    private void ResumeRunningHard()
    {
        if (!animator) return;
        animator.SetBool("isMoving", true);

        int runHashFull = Animator.StringToHash($"Base Layer.{RunningState}");
        if (animator.HasState(0, runHashFull))
            animator.CrossFadeInFixedTime(runHashFull, 0.05f, 0, 0f);
        else
            animator.CrossFadeInFixedTime(RunningState, 0.05f, 0, 0f);
    }

    void SetCapsuleHeightKeepingBottom(float newHeight)
    {
        float origBottom = capOrigCenter.y - capOrigHeight * 0.5f;
        capsule.height = newHeight;
        float newCenterY = origBottom + newHeight * 0.5f;
        capsule.center = new Vector3(capOrigCenter.x, newCenterY, capOrigCenter.z);
    }

    private void StartSlide(ref Vector3 currentVelocity)
    {
        isSliding = true;
        slideEndTime = Time.time + slideDuration;

        if (capsule)
        {
            float newH = capOrigHeight * slideColliderScale;
            SetCapsuleHeightKeepingBottom(newH);
        }

        if (animator)
        {
            animator.ResetTrigger("slideTrigger");
            animator.SetTrigger("slideTrigger");
        }

        // Do NOT change currentVelocity.z here.
        // Slide keeps the same speed as running.
    }

    public void ApplySpeedDebuff(float multiplier, float duration)
    {
        multiplier = Mathf.Clamp01(multiplier);
        if (debuffCo != null) StopCoroutine(debuffCo);
        debuffCo = StartCoroutine(DebuffRoutine(multiplier, duration));
    }

    private IEnumerator DebuffRoutine(float m, float dur)
    {
        speedMultiplier = m;
        yield return new WaitForSeconds(dur);
        speedMultiplier = 1f;
        debuffCo = null;
    }

    private void EndSlide()
    {
        if (!isSliding) return;
        isSliding = false;

        if (capsule)
            SetCapsuleHeightKeepingBottom(capOrigHeight);

        if (animator) animator.SetBool("isMoving", rb.linearVelocity.z > 0.1f);
    }

    private bool IsGroundedRobust()
    {
        if (!capsule)
        {
            return Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                0.3f,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }

        float radius = Mathf.Max(0.01f, capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) * 0.95f);
        Vector3 center = capsule.bounds.center;
        float bottomY = center.y - capsule.bounds.extents.y + radius + 0.01f;
        Vector3 sphereOrigin = new Vector3(center.x, bottomY, center.z);

        float castDist = groundedCheckExtra + groundProbePadding;
        return Physics.SphereCast(
            sphereOrigin,
            radius,
            Vector3.down,
            out _,
            castDist,
            groundMask,
            QueryTriggerInteraction.Ignore
        );
    }

    public void OnGameWon()
    {
        if (wonPlayed)
        {
            Debug.Log("[Win] Ignored duplicate call");
            return;
        }
        wonPlayed = true;
        gameWon = true;

        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
            smr.forceRenderingOff = false;
            smr.enabled = true;
            smr.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f));
        }

        if (animator)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.speed = 1f;

            animator.SetBool("isMoving", false);
            animator.ResetTrigger("winTrigger");
            animator.SetTrigger("winTrigger");

            for (int i = 1; i < animator.layerCount; i++)
                animator.SetLayerWeight(i, 0f);
        }

        gameObject.SetActive(true);
        StartCoroutine(ForceVisibleForFrames(10));
        StartCoroutine(WinDiagnostics());

        Debug.Log("Player won! Win animation triggered.");
    }

    private IEnumerator WinDiagnostics()
    {
        float end = Time.time + 3f;
        while (Time.time < end)
        {
            var pos = transform.position;
            bool active = gameObject.activeSelf && gameObject.activeInHierarchy;
            int smrCount = 0, smrEnabled = 0;
            foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smrCount++;
                if (s.enabled) smrEnabled++;
            }
            Debug.Log($"[WinDiag] t={(end - Time.time):0.00}s pos={pos} active={active} smrEnabled={smrEnabled}/{smrCount}");
            yield return new WaitForSeconds(0.25f);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag("Obstacle"))
        {
            //cut speed to 40% for 1 second
            ApplySpeedDebuff(0.4f, 1.0f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle"))
        {
            // obstacles use triggers
            ApplySpeedDebuff(0.4f, 1.0f);
        }
    }

    private IEnumerator ForceVisibleForFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.forceRenderingOff = false;
                smr.enabled = true;
            }
            yield return null;
        }
    }
}