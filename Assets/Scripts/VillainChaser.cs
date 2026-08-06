using System.Collections;
using UnityEngine;

/// <summary>
/// Subway Surfers-style villain chaser:
/// - Normal: Villain stays far behind (out of camera frame)
/// - First hit: Villain rushes up into frame (visible, close, scary!) but doesn't catch
/// - Player recovers: Villain gradually falls back out of frame
/// - Second hit while debuffed: Villain catches up and kills player
/// </summary>
public class VillainChaser : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public PlayerMove playerMove;
    public PlayerHealth playerHealth;
    
    [Header("Distance Settings")]
    [Tooltip("Normal distance behind player (out of camera frame)")]
    public float safeDistance = 12f;
    [Tooltip("Close distance when player is hit (in frame, visible) - must be less than camera offset!")]
    public float dangerDistance = 1.5f;
    [Tooltip("Distance at which villain catches player")]
    public float catchDistance = 0.3f;
    
    [Header("Speed Settings")]
    [Tooltip("How fast villain closes in when player gets hit")]
    public float rushSpeed = 15f;
    [Tooltip("How fast villain falls back after player recovers")]
    public float retreatSpeed = 4f;
    [Tooltip("Speed when catching player (second hit)")]
    public float killSpeed = 25f;
    
    [Header("Danger Window")]
    [Tooltip("How long villain stays in frame after first hit (danger window). Second hit during this = death!")]
    public float dangerWindowDuration = 3.5f;
    
    [Header("Animation")]
    public Animator animator;
    public string runningParameter = "isMoving";
    public string victoryTrigger = "winTrigger";
    
    [Header("Audio (Optional)")]
    public AudioSource audioSource;
    public AudioClip catchSound;
    public AudioClip rushSound; // Play when villain rushes in
    
    // State
    private enum VillainState { Safe, Rushing, InFrame, Retreating, Catching, Caught, RushingToBody, CelebratingAtBody }
    private VillainState currentState = VillainState.Safe;
    
    private float currentDistance; // Current distance behind player
    private float targetDistance;  // Where we want to be
    private bool hasCaughtPlayer = false;
    private bool isActive = false;
    
    // Danger window tracking (villain stays in frame during this time)
    private bool inDangerWindow = false;
    private float dangerWindowEndTime = 0f;
    
    // Speed tracking (to match player's base speed)
    private float baseSpeed;
    private float runTime = 0f;
    private const float RAMP_PER_SECOND = 0.1f;
    private float startForwardSpeed = 4f;
    private float maxForwardSpeed = 20f;
    
    void Awake()
    {
        if (!player)
            player = GameObject.FindWithTag("Player")?.transform;
        
        if (player)
        {
            if (!playerMove)
                playerMove = player.GetComponent<PlayerMove>();
            if (!playerHealth)
                playerHealth = player.GetComponent<PlayerHealth>();
        }
        
        if (!animator)
            animator = GetComponent<Animator>();
        if (!animator)
            animator = GetComponentInChildren<Animator>();
    }
    
    void Start()
    {
        if (!player || !playerMove)
        {
            Debug.LogError("VillainChaser: Missing player references!");
            enabled = false;
            return;
        }
        
        // Get settings from player
        startForwardSpeed = GetPlayerStartSpeed();
        maxForwardSpeed = GetPlayerMaxSpeed();
        baseSpeed = startForwardSpeed;
        
        if (playerHealth)
        {
            playerHealth.HealthChanged += OnPlayerHealthChanged;
            playerHealth.OnPlayerDied += OnPlayerDiedFromOtherCause;
        }
        
        // Start at safe distance (out of frame)
        currentDistance = safeDistance;
        targetDistance = safeDistance;
        UpdatePosition();
        
        if (animator)
            animator.SetBool(runningParameter, true);
        
        isActive = true;
        currentState = VillainState.Safe;
        
        Debug.Log($"VillainChaser: Starting at safe distance ({safeDistance}m behind player)");
    }
    
    void OnDestroy()
    {
        if (playerHealth)
        {
            playerHealth.HealthChanged -= OnPlayerHealthChanged;
            playerHealth.OnPlayerDied -= OnPlayerDiedFromOtherCause;
        }
    }
    
    void Update()
    {
        if (!player) return;
        
        // Allow RushingToBody state even after player is caught/dead
        if (!isActive && currentState != VillainState.RushingToBody && currentState != VillainState.CelebratingAtBody)
            return;
        if (hasCaughtPlayer && currentState != VillainState.RushingToBody && currentState != VillainState.CelebratingAtBody)
            return;
        
        // Update base speed (same ramp as player) - only when actively chasing
        if (isActive)
        {
            runTime += Time.deltaTime;
            baseSpeed = Mathf.Clamp(startForwardSpeed + RAMP_PER_SECOND * runTime, startForwardSpeed, maxForwardSpeed);
        }
        
        // Check if danger window ended
        if (inDangerWindow && Time.time > dangerWindowEndTime)
        {
            OnDangerWindowEnded();
        }
        
        // Handle state-based movement
        switch (currentState)
        {
            case VillainState.Safe:
                // Stay at safe distance, match player speed
                targetDistance = safeDistance;
                MoveTowardsTarget(retreatSpeed);
                break;
                
            case VillainState.Rushing:
                // Rush towards danger distance (into frame)
                targetDistance = dangerDistance;
                MoveTowardsTarget(rushSpeed);
                
                // Check if we reached danger distance
                if (currentDistance <= dangerDistance + 0.5f)
                {
                    currentState = VillainState.InFrame;
                    Debug.Log("VillainChaser: Now IN FRAME! Player better not get hit again...");
                }
                break;
                
            case VillainState.InFrame:
                // Stay at danger distance (visible, threatening)
                targetDistance = dangerDistance;
                MoveTowardsTarget(rushSpeed * 0.5f); // Slower adjustment to stay in frame
                break;
                
            case VillainState.Retreating:
                // Fall back to safe distance
                targetDistance = safeDistance;
                MoveTowardsTarget(retreatSpeed);
                
                // Check if we're back at safe distance
                if (currentDistance >= safeDistance - 1f)
                {
                    currentState = VillainState.Safe;
                    Debug.Log("VillainChaser: Back to safe distance (out of frame)");
                }
                break;
                
            case VillainState.Catching:
                // Rush to catch player!
                targetDistance = 0f;
                MoveTowardsTarget(killSpeed);
                
                // Check if caught
                if (currentDistance <= catchDistance)
                {
                    CatchPlayer();
                }
                break;
                
            case VillainState.RushingToBody:
                // Rush to the player's dead body
                RushToDeadBody();
                break;
                
            case VillainState.CelebratingAtBody:
                // Already at body, just stay there (animation playing)
                break;
        }
        
        UpdatePosition();
    }
    
    void MoveTowardsTarget(float speed)
    {
        // Get player's current speed
        float playerSpeed = GetPlayerCurrentSpeed();
        
        // Calculate how fast we need to move to change the gap
        float gapChangeRate;
        
        if (currentDistance > targetDistance)
        {
            // We need to close the gap (move faster than player)
            gapChangeRate = speed;
        }
        else if (currentDistance < targetDistance)
        {
            // We need to increase the gap (move slower than player, or player moves faster)
            gapChangeRate = -retreatSpeed;
        }
        else
        {
            gapChangeRate = 0f;
        }
        
        // Apply gap change
        currentDistance -= gapChangeRate * Time.deltaTime;
        currentDistance = Mathf.Max(currentDistance, 0f);
    }
    
    void UpdatePosition()
    {
        if (!player) return;
        
        Vector3 pos = transform.position;
        pos.z = player.position.z - currentDistance;
        pos.x = player.position.x; // Follow player's lane
        pos.y = 0;
        transform.position = pos;
        
        // Always face forward
        transform.rotation = Quaternion.LookRotation(Vector3.forward);
    }
    
    void OnPlayerHealthChanged(int current, int max)
    {
        if (hasCaughtPlayer) return;
        
        // Player took damage!
        if (inDangerWindow)
        {
            // SECOND HIT while in danger window - CATCH THEM!
            currentState = VillainState.Catching;
            Debug.Log("VillainChaser: SECOND HIT during danger window! Going for the KILL!");
            
            // Play rush sound if available
            if (audioSource && rushSound)
                audioSource.PlayOneShot(rushSound);
        }
        else
        {
            // FIRST HIT - rush into frame, start danger window
            inDangerWindow = true;
            dangerWindowEndTime = Time.time + dangerWindowDuration;
            currentState = VillainState.Rushing;
            
            Debug.Log($"VillainChaser: Player hit! Rushing into frame. Danger window: {dangerWindowDuration}s");
            
            // Play rush sound
            if (audioSource && rushSound)
                audioSource.PlayOneShot(rushSound);
        }
    }
    
    void OnDangerWindowEnded()
    {
        if (currentState == VillainState.Catching || hasCaughtPlayer)
            return; // Don't retreat if we're catching or already caught
        
        inDangerWindow = false;
        currentState = VillainState.Retreating;
        
        Debug.Log("VillainChaser: Danger window ended! Retreating out of frame...");
    }
    
    /// <summary>
    /// Called when player dies from something OTHER than the villain (cannonball, obstacle, etc.)
    /// </summary>
    void OnPlayerDiedFromOtherCause()
    {
        // Don't do anything if villain already caught the player
        if (hasCaughtPlayer || currentState == VillainState.Caught)
            return;
        
        // Don't do anything if already rushing to body
        if (currentState == VillainState.RushingToBody || currentState == VillainState.CelebratingAtBody)
            return;
        
        Debug.Log("VillainChaser: Player died from other cause! Rushing to body to celebrate...");
        
        // Stop normal chase behavior
        isActive = false;
        inDangerWindow = false;
        
        // Start rushing to the body
        currentState = VillainState.RushingToBody;
        bodyPosition = player.position; // Remember where the body is
    }
    
    private Vector3 bodyPosition;
    
    void RushToDeadBody()
    {
        if (!player) return;
        
        // Move towards the body position
        Vector3 targetPos = bodyPosition;
        Vector3 currentPos = transform.position;
        
        // Calculate direction to body
        float distanceToBody = targetPos.z - currentPos.z;
        
        if (distanceToBody > 0.5f)
        {
            // Still need to reach the body - rush forward
            currentPos.z += killSpeed * Time.unscaledDeltaTime; // Use unscaled since game might be slowed
            currentPos.x = Mathf.Lerp(currentPos.x, targetPos.x, 5f * Time.unscaledDeltaTime);
            currentPos.y = 0;
            transform.position = currentPos;
        }
        else
        {
            // Reached the body - celebrate!
            CelebrateAtBody();
        }
    }
    
    void CelebrateAtBody()
    {
        currentState = VillainState.CelebratingAtBody;
        
        Debug.Log("VillainChaser: Reached the body! Celebrating victory!");
        
        // Play victory animation
        if (animator)
        {
            animator.SetBool(runningParameter, false);
            animator.ResetTrigger(victoryTrigger);
            animator.SetTrigger(victoryTrigger);
        }
        
        // Play sound if available
        if (audioSource && catchSound)
            audioSource.PlayOneShot(catchSound);
    }
    
    void CatchPlayer()
    {
        if (hasCaughtPlayer) return;
        hasCaughtPlayer = true;
        isActive = false;
        currentState = VillainState.Caught;
        
        Debug.Log("VillainChaser: CAUGHT THE PLAYER! Victory!");
        
        if (audioSource && catchSound)
            audioSource.PlayOneShot(catchSound);
        
        // Play victory animation
        if (animator)
        {
            animator.SetBool(runningParameter, false);
            animator.ResetTrigger(victoryTrigger);
            animator.SetTrigger(victoryTrigger);
        }
        
        // Kill the player - use coroutine to bypass minHitInterval
        StartCoroutine(KillPlayerAfterDelay());
    }
    
    IEnumerator KillPlayerAfterDelay()
    {
        // Small delay so villain victory animation starts first
        yield return new WaitForSeconds(0.15f);
        
        if (playerHealth)
        {
            // Use ForceKill to bypass all damage interval checks
            playerHealth.ForceKill();
        }
    }
    
    float GetPlayerStartSpeed()
    {
        if (playerMove != null)
        {
            var field = typeof(PlayerMove).GetField("startForwardSpeed",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (field != null)
                return (float)field.GetValue(playerMove);
        }
        return 4f;
    }
    
    float GetPlayerMaxSpeed()
    {
        if (playerMove != null)
        {
            var field = typeof(PlayerMove).GetField("maxForwardSpeed",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (field != null)
                return (float)field.GetValue(playerMove);
        }
        return 20f;
    }
    
    float GetPlayerCurrentSpeed()
    {
        if (!player) return baseSpeed;
        
        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb)
            return Mathf.Max(rb.linearVelocity.z, 0.1f);
        
        return baseSpeed;
    }
    
    public void ResetVillain()
    {
        hasCaughtPlayer = false;
        isActive = true;
        inDangerWindow = false;
        currentState = VillainState.Safe;
        currentDistance = safeDistance;
        targetDistance = safeDistance;
        runTime = 0f;
        baseSpeed = startForwardSpeed;
        
        UpdatePosition();
        
        if (animator)
            animator.SetBool(runningParameter, true);
    }
    
    #if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!player) return;
        
        Vector3 playerPos = player.position;
        
        // Draw safe zone
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerPos + Vector3.back * safeDistance, 0.5f);
        UnityEditor.Handles.Label(playerPos + Vector3.back * safeDistance + Vector3.up, "SAFE");
        
        // Draw danger zone
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(playerPos + Vector3.back * dangerDistance, 0.5f);
        UnityEditor.Handles.Label(playerPos + Vector3.back * dangerDistance + Vector3.up, "DANGER");
        
        // Draw catch zone
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(playerPos + Vector3.back * catchDistance, 0.3f);
        
        // Draw villain position
        if (Application.isPlaying)
        {
            Gizmos.color = currentState == VillainState.Catching ? Color.red : Color.cyan;
            Gizmos.DrawLine(transform.position, playerPos);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, 
                $"State: {currentState}\nDist: {currentDistance:F1}m");
        }
    }
    #endif
}
