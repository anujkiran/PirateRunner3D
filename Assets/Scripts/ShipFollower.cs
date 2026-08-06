using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Makes a ship follow the player's speed and position, staying on the left side of the track in the water.
/// </summary>
public class ShipFollower : MonoBehaviour
{
    [Header("References")]
    public GameObject player;
    public PlayerMove playerMove;
    [Tooltip("Drag the StylShip_Unity.fbx from Assets/Stylized_Pirate_Ship/StylShip_3dModel/ folder here")]
    public GameObject shipPrefab; // The ship FBX model or prefab to instantiate
    
    [Header("Position Settings")]
    [Tooltip("X offset from player (negative = left side, positive = right side)")]
    public float xOffset = -8f; // Left side of track, in the water
    
    [Tooltip("Y position in world space (water level is around -1.05)")]
    public float yWorldPosition = -1.05f; // Match ocean Y position
    
    [Tooltip("Z offset from player (0 = same Z position)")]
    public float zOffset = 0f;
    
    [Header("Rotation Settings")]
    [Tooltip("Should the ship rotate to face forward?")]
    public bool rotateToFaceForward = true;
    
    [Header("Debug")]
    [Tooltip("Create a visible debug cube at ship position to verify it's working")]
    public bool createDebugCube = true;
    
    [Header("Loophole Alignment")]
    [Tooltip("Enable random loophole alignment with player")]
    public bool enableLoopholeAlignment = true;
    [Tooltip("How often to randomly select a new loophole to align (in seconds)")]
    public float alignmentChangeInterval = 5f;
    [Tooltip("Speed adjustment rate when aligning loophole (higher = faster adjustment). Default 10 works well.")]
    public float alignmentSpeed = 10f;
    
    [Header("Loophole Position Offsets")]
    [Tooltip("Additional offset for behind loophole (index 0) - positive = further forward")]
    public float behindLoopholeOffset = -0.5f;
    [Tooltip("Additional offset for middle loophole (index 1) - negative = further back")]
    public float middleLoopholeOffset = -0.5f;
    [Tooltip("Additional offset for front loophole (index 2) - positive = further forward")]
    public float frontLoopholeOffset = -0.5f;
    
    [Header("Cannon Ball Firing")]
    [Tooltip("Cannon ball prefab to fire from aligned loopholes")]
    public GameObject cannonBallPrefab;
    [Tooltip("Minimum delay after alignment before firing (in seconds)")]
    public float minFireDelay = 0.1f;
    [Tooltip("Maximum delay after alignment before firing (in seconds)")]
    public float maxFireDelay = 0.5f;
    [Tooltip("Minimum time between cannon ball shots (in seconds)")]
    public float minTimeBetweenShots = 0.3f;
    [Tooltip("How close the loophole needs to be aligned before firing (in meters)")]
    public float alignmentThreshold = 0.1f;
    
    [Header("Cannon Ball Heights")]
    [Tooltip("Y position for ground-level cannon balls (player must slide to dodge) - should hit standing player's lower body")]
    public float groundBallHeight = 0.4f;
    [Tooltip("Y position for high-level cannon balls (player must jump to dodge) - should hit standing player's upper body")]
    public float highBallHeight = 0.7f;
    
    [Header("Cannon Ball Physics")]
    [Tooltip("Speed of the cannon ball (must match CannonBall script speed)")]
    public float cannonBallSpeed = 15f;
    
    private Vector3 lastPlayerPosition;
    private float lastPlayerZ;
    private GameObject debugCube;
    
    // Loophole alignment system
    private Transform[] loopholes = new Transform[3]; // behind, middle, front
    private int currentTargetLoopholeIndex = -1; // -1 = no target, 0 = behind, 1 = middle, 2 = front
    private int previousTargetLoopholeIndex = -1; // Track previous target for smooth transitions
    private float lastAlignmentChangeTime = 0f;
    private float currentZOffset = 0f; // Current offset from player's Z to align loophole
    private float targetZOffset = 0f; // Target offset we're smoothly moving towards
    private float transitionProgress = 1f; // 0 = transitioning, 1 = aligned
    
    // Cannon ball firing system
    private float lastCannonBallFireTime = -999f; // Track when we last fired
    private float currentAlignmentStartTime = -999f; // Track when current alignment was achieved (not when target was selected)
    private float nextFireTime = -999f; // When to fire next (random time after alignment)
    private bool alignmentAchieved = false; // Track if alignment has been achieved for current target
    private int currentAlignmentLoopholeIndex = -1; // Track which loophole is currently aligned
    
    void Start()
    {
        // Find player if not assigned
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError("ShipFollower: Player not found! Assign player reference or tag it as 'Player'.");
                enabled = false;
                return;
            }
        }
        
        // Find PlayerMove component if not assigned
        if (playerMove == null)
        {
            playerMove = player.GetComponent<PlayerMove>();
            if (playerMove == null)
            {
                Debug.LogError("ShipFollower: PlayerMove component not found on player!");
                enabled = false;
                return;
            }
        }
        
        // Check if prefab is assigned (runtime check - can't verify if it's FBX vs prefab at runtime)
        // User must ensure they assign the .prefab file, not the .fbx file
        
        // Instantiate ship model (FBX or prefab) if not already present
        if (shipPrefab != null && transform.childCount == 0)
        {
            GameObject shipInstance = Instantiate(shipPrefab, transform);
            shipInstance.name = "ShipModel";
            
            // Reset the child's local transform to ensure it's positioned correctly
            shipInstance.transform.localPosition = Vector3.zero;
            shipInstance.transform.localRotation = Quaternion.identity;
            
            // Try different scales - FBX models are often very large or very small
            // Start with a reasonable scale
            shipInstance.transform.localScale = Vector3.one * 0.1f; // Try 0.1 first (ship might be huge)
            
            // Ensure the ship is active and visible
            shipInstance.SetActive(true);
            
            // Check for renderers and log info
            Renderer[] renderers = shipInstance.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"ShipFollower: Instantiated ship model '{shipPrefab.name}'. Found {renderers.Length} renderer(s). Scale: {shipInstance.transform.localScale}");
            
            if (renderers.Length == 0)
            {
                Debug.LogWarning("ShipFollower: WARNING! No renderers found on ship model. Ship may not be visible!");
            }
            else
            {
                // Log bounds to help debug scale issues
                Bounds bounds = new Bounds();
                bool boundsSet = false;
                foreach (Renderer r in renderers)
                {
                    if (r.bounds.size.magnitude > 0)
                    {
                        if (!boundsSet)
                        {
                            bounds = r.bounds;
                            boundsSet = true;
                        }
                        else
                        {
                            bounds.Encapsulate(r.bounds);
                        }
                    }
                }
                if (boundsSet)
                {
                    Debug.Log($"ShipFollower: Ship bounds size: {bounds.size}. If ship is too small/large, adjust scale in Inspector.");
                }
            }
        }
        else if (shipPrefab == null)
        {
            Debug.LogError("ShipFollower: Ship model not assigned! Drag StylShip_Unity.fbx from Assets/Stylized_Pirate_Ship/StylShip_3dModel/ into the Ship Prefab field in Inspector.");
        }
        else
        {
            Debug.Log($"ShipFollower: Ship model already instantiated. Child count: {transform.childCount}");
            
            // Ensure existing ship is active and check its state
            for (int i = 0; i < transform.childCount; i++)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(true);
                
                // CRITICAL FIX: Reset the ship's local position to zero (it might be positioned incorrectly)
                if (child.transform.localPosition.magnitude > 0.1f)
                {
                    Debug.LogWarning($"ShipFollower: Ship child '{child.name}' has incorrect local position {child.transform.localPosition}. Resetting to (0,0,0).");
                    child.transform.localPosition = Vector3.zero;
                }
                
                // Reset rotation and scale to ensure correct orientation
                child.transform.localRotation = Quaternion.identity;
                
                // If scale is very small, try increasing it
                if (child.transform.localScale.magnitude < 0.01f)
                {
                    Debug.LogWarning($"ShipFollower: Ship scale is very small ({child.transform.localScale}). Setting to 0.1.");
                    child.transform.localScale = Vector3.one * 0.1f;
                }
                else if (child.transform.localScale.magnitude > 10f)
                {
                    Debug.LogWarning($"ShipFollower: Ship scale is very large ({child.transform.localScale}). Setting to 0.1.");
                    child.transform.localScale = Vector3.one * 0.1f;
                }
                
                // Check renderers on existing ship
                Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                Debug.Log($"ShipFollower: Child '{child.name}' has {renderers.Length} renderer(s). Active: {child.activeSelf}, Scale: {child.transform.localScale}, Position: {child.transform.localPosition}");
                
                // Enable all renderers
                foreach (Renderer r in renderers)
                {
                    r.enabled = true;
                }
                
                Debug.Log($"ShipFollower: Ship child '{child.name}' reset. New local position: {child.transform.localPosition}, Scale: {child.transform.localScale}");
            }
        }
        
        // Initialize position - start at player's current Z position (not ahead)
        lastPlayerPosition = player.transform.position;
        lastPlayerZ = player.transform.position.z;
        
        // Set initial position to match player's Z position exactly (accounting for parent transform)
        Vector3 playerPos = player.transform.position;
        Vector3 targetWorldPos = new Vector3(
            playerPos.x + xOffset,
            yWorldPosition,
            playerPos.z + zOffset  // Start at same Z as player, not ahead
        );
        
        // If we have a parent, convert world position to local position
        if (transform.parent != null)
        {
            transform.localPosition = transform.parent.InverseTransformPoint(targetWorldPos);
        }
        else
        {
            transform.position = targetWorldPos;
        }
        
        Debug.Log($"ShipFollower: Initialized at world position {targetWorldPos}, local position {transform.localPosition}. Player Z: {playerPos.z}");
        
        // Find loopholes on the ship
        if (enableLoopholeAlignment)
        {
            FindLoopholes();
        }
        
        // Create debug cube to visualize ship position
        if (createDebugCube)
        {
            debugCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debugCube.name = "ShipDebugCube";
            debugCube.transform.SetParent(transform);
            debugCube.transform.localPosition = Vector3.zero;
            debugCube.transform.localScale = Vector3.one * 2f; // 2x2x2 meter cube
            debugCube.GetComponent<Renderer>().material.color = Color.red;
            Debug.Log("ShipFollower: Created red debug cube at ship position. If you see a red cube, the ship position is correct!");
        }
    }
    
    private bool isFirstUpdate = true;
    
    void Update()
    {
        if (player == null || playerMove == null)
            return;
        
        // On first update, ensure we start at player's exact Z position
        if (isFirstUpdate)
        {
            Vector3 resetPlayerPos = player.transform.position;
            Vector3 targetWorldPos = new Vector3(
                resetPlayerPos.x + xOffset,
                yWorldPosition,
                resetPlayerPos.z + zOffset  // Match player's Z exactly
            );
            
            if (transform.parent != null)
            {
                transform.localPosition = transform.parent.InverseTransformPoint(targetWorldPos);
            }
            else
            {
                transform.position = targetWorldPos;
            }
            
            Debug.Log($"ShipFollower: First update - reset position to match player. World pos: {targetWorldPos}, Player Z: {resetPlayerPos.z}");
            isFirstUpdate = false;
            return; // Don't move forward on first frame, just position correctly
        }
        
        // Handle loophole alignment system
        if (enableLoopholeAlignment && loopholes[0] != null && loopholes[1] != null && loopholes[2] != null)
        {
            HandleLoopholeAlignment();
        }
        
        // Keep ship's Z position synchronized with player's Z position (with loophole offset)
        Vector3 playerPos = player.transform.position;
        Vector3 currentWorldPos = transform.position;
        
        // Calculate target Z position - always use smooth interpolation to prevent jerking
        float targetZ = playerPos.z + zOffset + currentZOffset;
        
        // Always use smooth interpolation for position updates to prevent teleporting/jerking
        // This creates natural movement even when switching loopholes
        float positionLerpSpeed = 8f; // How fast to move towards target (higher = faster, but still smooth)
        currentWorldPos.z = Mathf.Lerp(currentWorldPos.z, targetZ, Time.deltaTime * positionLerpSpeed);
        
        // Keep X position fixed (don't follow player's left/right movement)
        // Only update Y to maintain water level
        currentWorldPos.y = yWorldPosition;
        
        // Apply position (accounting for parent transform)
        if (transform.parent != null)
        {
            transform.localPosition = transform.parent.InverseTransformPoint(currentWorldPos);
        }
        else
        {
            transform.position = currentWorldPos;
        }
        
        // Rotate to face forward if enabled
        if (rotateToFaceForward)
        {
            transform.rotation = Quaternion.LookRotation(Vector3.forward);
        }
    }
    
    void FindLoopholes()
    {
        // Find all loophole objects in the ship's children
        List<Transform> foundLoopholes = new List<Transform>();
        
        // Search through all children recursively
        SearchForLoopholes(transform, foundLoopholes);
        
        if (foundLoopholes.Count >= 3)
        {
            // Sort by local Z position (most negative = behind, 0 = middle, most positive = front)
            foundLoopholes.Sort((a, b) => a.localPosition.z.CompareTo(b.localPosition.z));
            
            // Find the three distinct groups: behind (most negative), middle (around 0), front (most positive)
            loopholes[0] = foundLoopholes[0]; // Behind (most negative Z)
            
            // For middle, find the one closest to Z=0, or use the middle index if sorted
            int middleIndex = foundLoopholes.Count / 2;
            loopholes[1] = foundLoopholes[middleIndex]; // Middle
            
            loopholes[2] = foundLoopholes[foundLoopholes.Count - 1]; // Front (most positive Z)
            
            // Log all found loopholes for debugging
            Debug.Log($"ShipFollower: Found {foundLoopholes.Count} loopholes total:");
            for (int i = 0; i < foundLoopholes.Count; i++)
            {
                Debug.Log($"  [{i}] {foundLoopholes[i].name} - Local Z: {foundLoopholes[i].localPosition.z:F3}");
            }
            
            Debug.Log($"ShipFollower: Assigned - Behind[0]: {loopholes[0].name} (Z: {loopholes[0].localPosition.z:F3}), " +
                     $"Middle[1]: {loopholes[1].name} (Z: {loopholes[1].localPosition.z:F3}), " +
                     $"Front[2]: {loopholes[2].name} (Z: {loopholes[2].localPosition.z:F3})");
        }
        else
        {
            Debug.LogWarning($"ShipFollower: Found {foundLoopholes.Count} loopholes, but need 3. Loophole alignment disabled.");
            enableLoopholeAlignment = false;
        }
    }
    
    void SearchForLoopholes(Transform parent, List<Transform> results)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains("Loophole") || child.name.Contains("loophole"))
            {
                results.Add(child);
            }
            // Recursively search children
            SearchForLoopholes(child, results);
        }
    }
    
    void HandleLoopholeAlignment()
    {
        // Initialize first target if none selected
        if (currentTargetLoopholeIndex < 0)
        {
            currentTargetLoopholeIndex = Random.Range(0, 3);
            previousTargetLoopholeIndex = currentTargetLoopholeIndex;
            lastAlignmentChangeTime = Time.time;
            currentAlignmentStartTime = -999f; // Reset - will be set when alignment is achieved
            alignmentAchieved = false; // Reset alignment flag
            nextFireTime = -999f; // Reset next fire time
            currentAlignmentLoopholeIndex = -1; // Reset aligned loophole index
            transitionProgress = 1f;
            string positionName = currentTargetLoopholeIndex == 0 ? "behind" : (currentTargetLoopholeIndex == 1 ? "middle" : "front");
            Debug.Log($"ShipFollower: Initial target loophole: {positionName} ({loopholes[currentTargetLoopholeIndex].name})");
        }
        
        // Check if it's time to change target loophole
        if (Time.time - lastAlignmentChangeTime >= alignmentChangeInterval)
        {
            // Only change if we've finished the previous transition
            if (transitionProgress >= 0.95f)
            {
                // Randomly select a new loophole to align (0 = behind, 1 = middle, 2 = front)
                int newTarget = Random.Range(0, 3);
                
                // Make sure we actually change to a different loophole
                while (newTarget == currentTargetLoopholeIndex && alignmentChangeInterval > 0)
                {
                    newTarget = Random.Range(0, 3);
                }
                
                previousTargetLoopholeIndex = currentTargetLoopholeIndex;
                currentTargetLoopholeIndex = newTarget;
                lastAlignmentChangeTime = Time.time;
                
                // Reset alignment flags
                currentAlignmentStartTime = -999f; // Reset - will be set when alignment is achieved
                alignmentAchieved = false; // Reset alignment flag
                nextFireTime = -999f; // Reset next fire time
                currentAlignmentLoopholeIndex = -1; // Reset aligned loophole index
                transitionProgress = 0f; // Start new transition
                
                string positionName = newTarget == 0 ? "behind" : (newTarget == 1 ? "middle" : "front");
                Debug.Log($"ShipFollower: New target loophole: {positionName} ({loopholes[newTarget].name}). Starting smooth transition.");
            }
        }
        
        // If we have a target loophole, adjust ship position to align it with player
        if (currentTargetLoopholeIndex >= 0 && currentTargetLoopholeIndex < 3 && loopholes[currentTargetLoopholeIndex] != null)
        {
            Transform targetLoophole = loopholes[currentTargetLoopholeIndex];
            Vector3 playerPos = player.transform.position;
            
            // Get the loophole's actual world position (center)
            Vector3 loopholeWorldPos = targetLoophole.position;
            bool usedBounds = false;
            
            // Try to get more accurate center if there's a renderer or collider
            Renderer renderer = targetLoophole.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = targetLoophole.GetComponentInChildren<Renderer>();
            }
            
            if (renderer != null && renderer.bounds.size.magnitude > 0)
            {
                loopholeWorldPos = renderer.bounds.center;
                usedBounds = true;
            }
            else
            {
                // Fallback: try collider bounds
                Collider collider = targetLoophole.GetComponent<Collider>();
                if (collider == null)
                {
                    collider = targetLoophole.GetComponentInChildren<Collider>();
                }
                if (collider != null)
                {
                    loopholeWorldPos = collider.bounds.center;
                    usedBounds = true;
                }
            }
            
            // Get the ship's current world position
            Vector3 shipWorldPos = transform.position;
            
            // Get the loophole's local Z position relative to the ship
            // Calculate directly from the world position to local space relative to ship
            // This accounts for the actual center of the loophole (from bounds if available)
            Vector3 loopholeLocalPos = transform.InverseTransformPoint(loopholeWorldPos);
            float loopholeLocalZ = loopholeLocalPos.z;
            
            // Calculate desired loophole Z position (where we want it to be)
            float desiredLoopholeZ = playerPos.z;
            
            // Apply adjustment for first loophole (behind) - move it further forward
            // Positive offset = further forward (ahead of player)
            if (currentTargetLoopholeIndex == 0)
            {
                desiredLoopholeZ = playerPos.z + behindLoopholeOffset;
            }
            
            // Calculate what the ship's Z should be so the loophole aligns with desired position
            // Ship's world Z = desired loophole Z - loophole's local Z relative to ship
            float targetShipWorldZ = desiredLoopholeZ - loopholeLocalZ;
            
            // Calculate offset from player's Z
            targetZOffset = targetShipWorldZ - playerPos.z;
            
            // Add correction factor based on current alignment error to improve precision
            // If the loophole is currently behind the desired position, we need to move the ship forward more
            float currentError = desiredLoopholeZ - loopholeWorldPos.z;
            if (Mathf.Abs(currentError) > 0.01f) // Only apply correction if error is significant
            {
                // The error tells us how much the loophole needs to move
                // Since loophole moves with ship 1:1, we add the error to the offset
                targetZOffset += currentError;
            }
            
            // Verify the calculation: if ship moves to targetShipWorldZ, loophole should be at desiredLoopholeZ
            // Current loophole world Z = shipWorldPos.z + loopholeLocalZ
            // We want: shipWorldPos.z + loopholeLocalZ = desiredLoopholeZ
            // So: shipWorldPos.z = desiredLoopholeZ - loopholeLocalZ = targetShipWorldZ
            // This is what we calculated above, so it should be correct
            
            // Smooth transition when switching loopholes
            if (transitionProgress < 1f)
            {
                // Gradually transition from previous offset to new target offset
                float transitionSpeed = 2f; // How fast to transition (higher = faster)
                transitionProgress = Mathf.MoveTowards(transitionProgress, 1f, Time.deltaTime * transitionSpeed);
                
                // Calculate what the previous target offset was (approximate)
                float previousTargetOffset = currentZOffset; // Use current as starting point
                
                // Smoothly interpolate between previous and new target
                float smoothTarget = Mathf.Lerp(previousTargetOffset, targetZOffset, transitionProgress);
                
                // Move towards the smooth target
                float moveSpeed = alignmentSpeed * 3f; // Faster during transition
                float maxDelta = moveSpeed * Time.deltaTime;
                currentZOffset = Mathf.MoveTowards(currentZOffset, smoothTarget, maxDelta);
            }
            else
            {
                // Normal alignment - move towards target offset smoothly
                float distance = Mathf.Abs(targetZOffset - currentZOffset);
                float moveSpeed = alignmentSpeed;
                
                // Special handling for front loophole (index 2) - needs to slow down more
                bool isFrontLoophole = (currentTargetLoopholeIndex == 2);
                
                // Scale speed based on distance - move faster when far away, very fast when close
                if (distance > 10f)
                {
                    // Front loophole needs even more aggressive slowing down
                    moveSpeed = isFrontLoophole ? alignmentSpeed * 8f : alignmentSpeed * 5f;
                }
                else if (distance > 5f)
                {
                    moveSpeed = isFrontLoophole ? alignmentSpeed * 5f : alignmentSpeed * 3f;
                }
                else if (distance > 1f)
                {
                    moveSpeed = isFrontLoophole ? alignmentSpeed * 3f : alignmentSpeed * 2f;
                }
                else if (distance > 0.3f)
                {
                    moveSpeed = alignmentSpeed * 5f; // Faster when getting close
                }
                else if (distance > 0.1f)
                {
                    moveSpeed = alignmentSpeed * 10f; // Much faster when very close
                }
                else
                {
                    moveSpeed = alignmentSpeed * 20f; // Very fast when extremely close for precision
                }
                
                // Move towards target with dynamic speed
                // Use MoveTowards for more precise control, especially when close
                float maxDelta = moveSpeed * Time.deltaTime;
                currentZOffset = Mathf.MoveTowards(currentZOffset, targetZOffset, maxDelta);
                
                // Snap to perfect alignment when very close (increased threshold for better precision)
                if (Mathf.Abs(currentZOffset - targetZOffset) < 0.01f)
                {
                    currentZOffset = targetZOffset;
                    transitionProgress = 1f; // Mark as fully aligned
                }
            }
            
            // Check if loophole is aligned and ready to fire
            float alignmentError = Mathf.Abs(desiredLoopholeZ - loopholeWorldPos.z);
            bool isAligned = transitionProgress >= 0.95f && alignmentError <= alignmentThreshold;
            
            // Debug alignment status periodically
            if (Time.frameCount % 120 == 0) // Every 2 seconds
            {
                Debug.Log($"ShipFollower: Alignment status - Progress: {transitionProgress:F2}, Error: {alignmentError:F3} (threshold: {alignmentThreshold:F3}), " +
                         $"IsAligned: {isAligned}, AlignmentAchieved: {alignmentAchieved}, " +
                         $"CannonBallPrefab: {(cannonBallPrefab != null ? "assigned" : "NULL")}");
            }
            
            // Use lenient threshold for detecting alignment (same as firing threshold)
            // Increased multiplier to allow more drift - alignment naturally drifts around 0.2-0.3m
            float firingAlignmentThreshold = alignmentThreshold * 8f; // Allow 8x the threshold for firing (0.8m) - very lenient
            bool isReasonablyAligned = transitionProgress >= 0.7f && alignmentError <= firingAlignmentThreshold; // Lower progress requirement (0.7 instead of 0.85)
            
            // FORCE isReasonablyAligned for first loophole - it never aligns properly but should still fire
            if (currentTargetLoopholeIndex == 0)
            {
                isReasonablyAligned = true;
            }
            
            // Mark when alignment is first achieved (for delay timer)
            // Also reset if a different loophole becomes aligned
            // Use lenient threshold so we detect alignment even with slight drift
            if (isReasonablyAligned)
            {
                // Check if this is a new alignment (either first time or different loophole)
                bool isNewAlignment = !alignmentAchieved || currentAlignmentLoopholeIndex != currentTargetLoopholeIndex;
                
                // For first loophole, ALWAYS treat as new alignment to ensure nextFireTime gets set
                if (currentTargetLoopholeIndex == 0)
                {
                    isNewAlignment = true;
                    // CRITICAL: Always set nextFireTime for first loophole
                    if (nextFireTime <= Time.time || nextFireTime == -999f)
                    {
                        float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                        nextFireTime = Time.time + randomDelay;
                        Debug.Log($"ShipFollower: [FIRST LOOPHOLE] Setting nextFireTime to {nextFireTime:F2} (in {randomDelay:F2}s)");
                    }
                }
                
                if (isNewAlignment)
                {
                    bool wasDifferentLoophole = alignmentAchieved && currentAlignmentLoopholeIndex != currentTargetLoopholeIndex;
                    
                    alignmentAchieved = true;
                    currentAlignmentStartTime = Time.time; // Start delay timer when alignment is achieved
                    currentAlignmentLoopholeIndex = currentTargetLoopholeIndex; // Track which loophole is aligned
                    
                    // Set random fire time after alignment
                    float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                    nextFireTime = Time.time + randomDelay;
                    
                    string loopholePos = currentTargetLoopholeIndex == 0 ? "BEHIND" : (currentTargetLoopholeIndex == 1 ? "MIDDLE" : "FRONT");
                    if (wasDifferentLoophole)
                    {
                        Debug.Log($"ShipFollower: Different loophole aligned ({loopholePos} - {targetLoophole.name}). Resetting fire timer. Will fire in {randomDelay:F2} seconds. Error: {alignmentError:F3}, Progress: {transitionProgress:F2}");
                    }
                    else
                    {
                        Debug.Log($"ShipFollower: Alignment achieved for {loopholePos} loophole ({targetLoophole.name}). Will fire in {randomDelay:F2} seconds. Error: {alignmentError:F3}, Progress: {transitionProgress:F2}");
                    }
                }
                else
                {
                    // Debug why alignment isn't being treated as new
                    if (currentTargetLoopholeIndex == 0 && Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"ShipFollower: [BEHIND] NOT new alignment - AlignmentAchieved: {alignmentAchieved}, CurrentIndex: {currentAlignmentLoopholeIndex}, TargetIndex: {currentTargetLoopholeIndex}, NextFireTime: {nextFireTime:F2}, CurrentTime: {Time.time:F2}");
                    }
                }
            }
            else
            {
                // Debug why alignment isn't being detected, especially for first loophole
                if (currentTargetLoopholeIndex == 0 && Time.frameCount % 60 == 0) // Every second for first loophole
                {
                    Debug.Log($"ShipFollower: First loophole NOT reasonably aligned. Progress: {transitionProgress:F2}, Error: {alignmentError:F3}, Threshold: {firingAlignmentThreshold:F3}, AlignmentAchieved: {alignmentAchieved}, CurrentIndex: {currentAlignmentLoopholeIndex}");
                }
            }
            
            // Only check firing if alignment has been achieved (currentAlignmentStartTime is valid)
            // and the currently aligned loophole matches the target loophole
            // SPECIAL: For first loophole, always check firing regardless of conditions
            bool shouldCheckFiring = alignmentAchieved && currentAlignmentStartTime > 0 && currentAlignmentLoopholeIndex == currentTargetLoopholeIndex;
            if (currentTargetLoopholeIndex == 0)
            {
                // Force firing check for first loophole - ensure all conditions are met
                if (!alignmentAchieved || currentAlignmentStartTime <= 0 || currentAlignmentLoopholeIndex != 0)
                {
                    alignmentAchieved = true;
                    currentAlignmentStartTime = Time.time;
                    currentAlignmentLoopholeIndex = 0;
                    if (nextFireTime <= Time.time)
                    {
                        float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                        nextFireTime = Time.time + randomDelay;
                    }
                }
                shouldCheckFiring = true;
            }
            
            if (shouldCheckFiring)
            {
                // isReasonablyAligned is already calculated above
                
                string loopholePos = currentTargetLoopholeIndex == 0 ? "BEHIND" : (currentTargetLoopholeIndex == 1 ? "MIDDLE" : "FRONT");
                
                // Debug firing conditions periodically (more frequent for first loophole)
                int debugInterval = currentTargetLoopholeIndex == 0 ? 30 : 120; // Every 0.5s for first, 2s for others
                if (Time.frameCount % debugInterval == 0)
                {
                    Debug.Log($"ShipFollower: [{loopholePos}] Firing check - Progress: {transitionProgress:F2}, Error: {alignmentError:F3}, " +
                             $"Time until fire: {nextFireTime - Time.time:F2}, Cooldown ready: {Time.time - lastCannonBallFireTime >= minTimeBetweenShots}, " +
                             $"IsReasonablyAligned: {isReasonablyAligned}, NextFireTime: {nextFireTime:F2}, CurrentTime: {Time.time:F2}, " +
                             $"AlignmentAchieved: {alignmentAchieved}, CurrentIndex: {currentAlignmentLoopholeIndex}, ShouldCheckFiring: {shouldCheckFiring}");
                }
                
                // Fire if it's time and we're still reasonably aligned (or very close)
                // Also ensure nextFireTime is always in the future (in case of drift recovery)
                // For first loophole, ignore cooldown to ensure it fires
                bool cooldownReady = Time.time - lastCannonBallFireTime >= minTimeBetweenShots;
                if (currentTargetLoopholeIndex == 0)
                {
                    cooldownReady = true; // Always allow firing for first loophole
                }
                
                // For first loophole, ALWAYS fire when time is ready (bypass all checks)
                if (currentTargetLoopholeIndex == 0 && Time.time >= nextFireTime)
                {
                    Debug.Log($"ShipFollower: [FIRING FIRST LOOPHOLE] Time: {Time.time:F2}, NextFireTime: {nextFireTime:F2}, Loophole: {targetLoophole.name}, Position: {loopholeWorldPos}");
                    FireCannonBall(targetLoophole, loopholeWorldPos);
                    lastCannonBallFireTime = Time.time;
                    
                    // Schedule next random fire time
                    float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                    nextFireTime = Time.time + randomDelay;
                }
                else if (Time.time >= nextFireTime && cooldownReady)
                {
                    // For other loopholes, use normal logic
                    bool canFire = isReasonablyAligned;
                    
                    if (canFire)
                    {
                        Debug.Log($"ShipFollower: [FIRING] {loopholePos} loophole - Time: {Time.time:F2}, NextFireTime: {nextFireTime:F2}");
                        FireCannonBall(targetLoophole, loopholeWorldPos);
                        lastCannonBallFireTime = Time.time;
                        
                        // Schedule next random fire time
                        float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                        nextFireTime = Time.time + randomDelay;
                    }
                    else
                    {
                        // Alignment drifted, but keep scheduling fires - reschedule for a bit later
                        float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                        nextFireTime = Time.time + randomDelay;
                    }
                }
                // If nextFireTime is in the past (shouldn't happen, but safety check), reschedule
                else if (nextFireTime < Time.time && Time.time - lastCannonBallFireTime >= minTimeBetweenShots)
                {
                    float randomDelay = Random.Range(minFireDelay, maxFireDelay);
                    nextFireTime = Time.time + randomDelay;
                    Debug.Log($"ShipFollower: nextFireTime was in past, rescheduling fire in {randomDelay:F2} seconds.");
                }
            }
            
            // Debug alignment every few frames
            if (Time.frameCount % 60 == 0) // Every 60 frames (~1 second at 60fps)
            {
                // Use the actual loophole world position we calculated
                string boundsInfo = usedBounds ? "bounds" : "position";
                Debug.Log($"ShipFollower: Aligning {targetLoophole.name} (index {currentTargetLoopholeIndex}, {boundsInfo}). " +
                         $"Player Z: {playerPos.z:F3}, Desired Loophole Z: {desiredLoopholeZ:F3}, " +
                         $"Current Loophole Z: {loopholeWorldPos.z:F3}, Loophole Local Z: {loopholeLocalZ:F3}, " +
                         $"Error: {alignmentError:F3}, Offset: {currentZOffset:F3}, Target Offset: {targetZOffset:F3}");
            }
        }
        else if (currentTargetLoopholeIndex >= 0 && currentTargetLoopholeIndex < 3)
        {
            Debug.LogWarning($"ShipFollower: Target loophole index {currentTargetLoopholeIndex} is null!");
        }
    }
    
    /// <summary>
    /// Fires a cannon ball horizontally from the specified loophole position.
    /// Randomly selects ground-level (slide to dodge) or high-level (jump to dodge).
    /// The cannon ball will check player state when it reaches the track.
    /// </summary>
    void FireCannonBall(Transform loophole, Vector3 firePosition)
    {
        if (cannonBallPrefab == null)
        {
            Debug.LogWarning("ShipFollower: Cannon ball prefab not assigned! Cannot fire.");
            return;
        }
        
        if (player == null)
        {
            Debug.LogWarning("ShipFollower: Player not found! Cannot fire cannon ball.");
            return;
        }
        
        // Use the loophole's actual world position (firePosition already contains loopholeWorldPos)
        // Only override Z to match player's Z for tracking purposes
        Vector3 actualFirePosition = firePosition;
        actualFirePosition.z = player.transform.position.z; // For tracking - ball checks player state at this Z
        
        // Randomly select cannon ball type (50/50 chance)
        CannonBallType ballType = Random.Range(0, 2) == 0 ? CannonBallType.Ground : CannonBallType.High;
        
        // Set Y position based on ball type (but keep X from loophole position)
        float ballHeight = ballType == CannonBallType.Ground ? groundBallHeight : highBallHeight;
        actualFirePosition.y = ballHeight;
        
        // Ensure X position is from the loophole (ship is on left side)
        actualFirePosition.x = firePosition.x;
        
        // Fire horizontally from the loophole towards the track (left to right)
        // Ship is on left side, so fire rightward (positive X direction)
        // Orient the cannon ball to face right (towards the track)
        Quaternion rotation = Quaternion.LookRotation(Vector3.right);
        GameObject cannonBall = Instantiate(cannonBallPrefab, actualFirePosition, rotation);
        
        // Debug which loophole fired
        string loopholeName = loophole.name;
        Debug.Log($"ShipFollower: Firing from loophole: {loopholeName} at position X={actualFirePosition.x:F2}, Y={actualFirePosition.y:F2}, Z={actualFirePosition.z:F2}");
        
        // Ensure the cannon ball has the CannonBall script
        CannonBall cannonBallScript = cannonBall.GetComponent<CannonBall>();
        if (cannonBallScript == null)
        {
            cannonBallScript = cannonBall.AddComponent<CannonBall>();
        }
        
        // Set damage amount and type
        cannonBallScript.damage = 25;
        cannonBallScript.ballType = ballType;
        
        // Store which loophole fired this ball for debugging
        cannonBallScript.firedFromLoopholeIndex = currentTargetLoopholeIndex;
        
        string typeName = ballType == CannonBallType.Ground ? "GROUND (slide to dodge)" : "HIGH (jump to dodge)";
        string loopholePos = currentTargetLoopholeIndex == 0 ? "BEHIND" : (currentTargetLoopholeIndex == 1 ? "MIDDLE" : "FRONT");
        Debug.Log($"ShipFollower: Fired {typeName} cannon ball from {loopholePos} loophole ({loopholeName}) at Z={actualFirePosition.z:F2}, Y={ballHeight:F2}");
    }
}

