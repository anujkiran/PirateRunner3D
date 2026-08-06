using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages infinite ocean by spawning ocean prefabs in a grid pattern around the player.
/// </summary>
public class WaterManager : MonoBehaviour
{
    [Header("References")]
    public GameObject player;
    public GameObject oceanPrefab; // The ocean prefab to instantiate
    public Transform oceanParent; // Parent object to spawn oceans under (e.g., Environment)
    
    [Header("Ocean Spawning Settings")]
    [Tooltip("Size of each ocean tile in meters (X and Z dimensions). Smaller = closer together")]
    public float oceanTileSize = 10f;
    
    [Tooltip("Scale of ocean prefab (X and Y scale - Z stays at 1)")]
    public Vector2 oceanScale = new Vector2(12f, 12f);
    
    [Tooltip("Y position offset for ocean (adjust if ocean appears floating). Original ocean is at world Y ~ -1.05")]
    public float oceanYOffset = -1.05f;
    
    [Tooltip("Overlap between ocean tiles to ensure no gaps (0 = no overlap, 0.1 = 10% overlap)")]
    [Range(0f, 0.5f)]
    public float tileOverlap = 0.1f;
    
    [Tooltip("How many ocean tiles to spawn ahead of player")]
    public int tilesAhead = 20;
    
    [Tooltip("How many ocean tiles to spawn behind player")]
    public int tilesBehind = 20;
    
    [Tooltip("How many ocean tiles to spawn on each side of player")]
    public int tilesPerSide = 15;
    
    [Tooltip("How often to check and update ocean tiles (in seconds)")]
    public float updateInterval = 0.5f;
    
    [Tooltip("Distance player must move before updating ocean tiles (meters)")]
    public float updateDistanceThreshold = 10f;
    
    // Track spawned ocean tiles
    private Dictionary<Vector2Int, GameObject> spawnedOceans = new Dictionary<Vector2Int, GameObject>();
    
    // Current grid position of player (in tile coordinates)
    private Vector2Int currentPlayerGridPos;
    private Vector2Int lastUpdateGridPos;
    
    void Start()
    {
        if (player == null)
        {
            Debug.LogError("WaterManager: Player reference is missing!");
            return;
        }
        
        if (oceanPrefab == null)
        {
            Debug.LogError("WaterManager: Ocean prefab reference is missing!");
            return;
        }
        
        // Set parent to Environment if not set
        if (oceanParent == null)
        {
            GameObject env = GameObject.Find("Environment");
            if (env != null)
            {
                oceanParent = env.transform;
            }
            else
            {
                oceanParent = transform; // Fallback to WaterManager's transform
            }
        }
        
        // Initialize grid position
        UpdatePlayerGridPosition();
        lastUpdateGridPos = currentPlayerGridPos;
        
        // Spawn initial ocean tiles around player
        SpawnInitialOceans();
        
        // Start periodic update coroutine
        StartCoroutine(UpdateOceansPeriodically());
    }
    
    void UpdatePlayerGridPosition()
    {
        if (player == null) return;
        
        Vector3 playerPos = player.transform.position;
        // Use spacing with overlap for grid calculation to match world positions
        float spacing = oceanTileSize * (1f - tileOverlap);
        int gridX = Mathf.FloorToInt(playerPos.x / spacing);
        int gridZ = Mathf.FloorToInt(playerPos.z / spacing);
        currentPlayerGridPos = new Vector2Int(gridX, gridZ);
    }
    
    void SpawnInitialOceans()
    {
        // Spawn a large grid of ocean tiles around the player
        // This ensures ocean is visible in all directions
        for (int x = -tilesPerSide; x <= tilesPerSide; x++)
        {
            for (int z = -tilesBehind; z <= tilesAhead; z++)
            {
                Vector2Int gridPos = new Vector2Int(
                    currentPlayerGridPos.x + x,
                    currentPlayerGridPos.y + z
                );
                SpawnOceanTile(gridPos);
            }
        }
    }
    
    void SpawnOceanTile(Vector2Int gridPos)
    {
        // Skip if already spawned
        if (spawnedOceans.ContainsKey(gridPos))
            return;
        
        // Calculate world position for this grid cell
        // Apply overlap to ensure tiles connect seamlessly
        float spacing = oceanTileSize * (1f - tileOverlap);
        float worldX = gridPos.x * spacing;
        float worldZ = gridPos.y * spacing;
        Vector3 worldPos = new Vector3(worldX, oceanYOffset, worldZ);
        
        // Instantiate ocean prefab with proper rotation (270 degrees on X axis to lay flat)
        // The original ocean in the scene uses rotation x: 270.019775, y: 0, z: 0
        Quaternion oceanRotation = Quaternion.Euler(270f, 0f, 0f);
        GameObject ocean = Instantiate(oceanPrefab, worldPos, oceanRotation, oceanParent);
        ocean.name = $"Ocean_{gridPos.x}_{gridPos.y}";
        
        // Set scale to match sample scene (X and Y scale, Z stays at 1)
        ocean.transform.localScale = new Vector3(oceanScale.x, oceanScale.y, 1f);
        
        // Ensure the prefab is properly instantiated (not showing as prefab icon)
        // This should already be handled by Instantiate, but ensure it's active
        ocean.SetActive(true);
        
        // Store reference
        spawnedOceans[gridPos] = ocean;
    }
    
    void RemoveOceanTile(Vector2Int gridPos)
    {
        if (spawnedOceans.TryGetValue(gridPos, out GameObject ocean))
        {
            if (ocean != null)
            {
                Destroy(ocean);
            }
            spawnedOceans.Remove(gridPos);
        }
    }
    
    void UpdateOceans()
    {
        UpdatePlayerGridPosition();
        
        // Check if player has moved enough to trigger update
        Vector2Int gridDelta = currentPlayerGridPos - lastUpdateGridPos;
        if (Mathf.Abs(gridDelta.x) < 1 && Mathf.Abs(gridDelta.y) < 1)
            return; // Player hasn't moved to a new grid cell
        
        // Spawn new ocean tiles ahead of player
        for (int x = -tilesPerSide; x <= tilesPerSide; x++)
        {
            for (int z = currentPlayerGridPos.y; z <= currentPlayerGridPos.y + tilesAhead; z++)
            {
                Vector2Int gridPos = new Vector2Int(currentPlayerGridPos.x + x, z);
                SpawnOceanTile(gridPos);
            }
        }
        
        // Remove ocean tiles that are too far behind
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kvp in spawnedOceans)
        {
            Vector2Int gridPos = kvp.Key;
            int zDistance = gridPos.y - currentPlayerGridPos.y;
            
            // Remove if too far behind or too far to the side
            if (zDistance < -tilesBehind || 
                Mathf.Abs(gridPos.x - currentPlayerGridPos.x) > tilesPerSide)
            {
                toRemove.Add(gridPos);
            }
        }
        
        foreach (var gridPos in toRemove)
        {
            RemoveOceanTile(gridPos);
        }
        
        lastUpdateGridPos = currentPlayerGridPos;
    }
    
    IEnumerator UpdateOceansPeriodically()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);
            
            if (player == null) continue;
            
            UpdateOceans();
        }
    }
    
    void OnDestroy()
    {
        // Clean up all spawned oceans
        foreach (var ocean in spawnedOceans.Values)
        {
            if (ocean != null)
            {
                Destroy(ocean);
            }
        }
        spawnedOceans.Clear();
    }
}
