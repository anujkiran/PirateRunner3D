using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Attach to: the TrackManagerScipt
// Function: position tiles to form the track
// For simplicity, the player will be at x=0, y=0 upon starts and each tile is 2 meter's long along z axis

public class TrackManager : MonoBehaviour
{
    // the list that saves all the tile prefabs for generating endless path
    public List<GameObject> TilePrefabs;

    // how many tiles you want to have that lays in front of the player
    public int tileCountInFront = 5;
    // how many tiles you want to have that lays in front of the player
    public int tileCountBehind = 2;

    // saves the player object
    public GameObject player;

    // the list that saves all a pool of tiles to reuse.
    [SerializeField] private List<GameObject> TilePool = new List<GameObject>();

    // the list that saves all the existing tiles.
    [SerializeField] private List<GameObject> Tiles = new List<GameObject>();

    // saves the total number of tiles generated
    [SerializeField] private int numTiles = 0;
    // saves the tile has the player on it
    [SerializeField] private int curTileWithPlayer;

    // saves the tile length on z direction
    // for easier coding, it is important to make all the parent/manager objects with scale (1,1,1) so that the child obj's scale does not get modified
    float tileLenZ;
    float nextTilePosZ;
    
    // Track the Z position of the oldest tile (for cleanup)
    float oldestTileEndZ;

    // ---- Tile spawn announcement ----
    [System.Serializable]
    public struct TileInfo
    {
        public GameObject tileRoot;  // the tile GameObject
        public Transform ground;     // tileRoot/Ground
        public float zStart;         // world z start of this tile
        public float zEnd;           // world z end of this tile
        public float xHalf;          // half width in world units
        public float y;              // ground Y (world)
    }

    public event System.Action<TileInfo> TileSpawned;
    // ---------------------------------

    void Start()
    {
        // init all your variables
        if (TilePrefabs.Count == 0)
            Debug.LogError("No Tile Prefabs assigned to the Track Manger");
        if (player == null)
            Debug.LogError("Assign player obj to the TackManager obj");

        // Use Ground localScale.z as your logical tile length (matches your existing setup)
        tileLenZ = TilePrefabs[0].transform.Find("Ground").localScale.z;

        // Apply horizon color to existing tiles
        Invoke(nameof(ApplyHorizonColorToAllExistingTiles), 0.1f);
        LayTilesAtBeginning();
        curTileWithPlayer = PlayerOnTileIndex();
        
        // Initialize oldest tile end Z position
        if (Tiles.Count > 0)
        {
            oldestTileEndZ = Tiles[0].transform.position.z + tileLenZ / 2f;
        }
    }
    
    void Update()
    {
        if (player == null) return;
        
        float playerZ = player.transform.position.z;
        
        // Check if we need to add more tiles in front
        // Add a new tile when the player gets within tileCountInFront tiles of the last tile
        float lastTileEndZ = nextTilePosZ; // nextTilePosZ is where the NEXT tile would be placed
        float distanceToLastTile = lastTileEndZ - playerZ;
        
        // If player is getting close to the end of generated tiles, add more
        if (distanceToLastTile < tileCountInFront * tileLenZ)
        {
            AddATile();
        }
        
        // Check if we need to remove old tiles behind the player
        // Remove tiles that are more than tileCountBehind tiles behind the player
        if (Tiles.Count > 0)
        {
            GameObject oldestTile = Tiles[0];
            float oldestTileZ = oldestTile.transform.position.z;
            float distanceBehind = playerZ - (oldestTileZ + tileLenZ / 2f);
            
            // If the oldest tile is too far behind, remove it
            if (distanceBehind > tileCountBehind * tileLenZ)
            {
                DeleteEnd();
                
                // Update oldest tile tracking
                if (Tiles.Count > 0)
                {
                    oldestTileEndZ = Tiles[0].transform.position.z + tileLenZ / 2f;
                }
            }
        }
        
        // Update current tile with player
        curTileWithPlayer = PlayerOnTileIndex();
    }

    void GenerateTilePool() { }

    // position tileCountBehind+tileCountInFront number of tiles to form the initial path
    void LayTilesAtBeginning()
    {
        int playerTileLocationIndex = PlayerOnTileIndex();

        nextTilePosZ = -tileLenZ / 2 - (tileCountBehind - 1) * tileLenZ; // location of the farthest tile behind the player

        // Lay tiles around the player object
        for (int tileCn = 0; tileCn < tileCountBehind + tileCountInFront; tileCn++)
        {
            AddATile(); // will raise TileSpawned for each initial tile too
        }
    }

    // Find out which tile the player is on
    // Each tile is 2m long in Z. The 1st tile will be at z=0-2,  2nd tile be at z=2-4...
    int PlayerOnTileIndex()
    {
        int index = 0;
        float playerZpos = player.transform.position.z;
        index = Mathf.FloorToInt(playerZpos / tileLenZ);
        return index;
    }

    // delete a tile at the end of the list
    void DeleteEnd()
    {
        GameObject TileToRemove = Tiles[0];
        Tiles.RemoveAt(0);
        GameObject.Destroy(TileToRemove);
    }

    void AddATile()
    {
        // get a random tile prefab index
        int randomeTileIndexToInit = Random.Range(0, TilePrefabs.Count);
        // create a new tile by making a copy of the saved prefab object
        GameObject newTile = Instantiate(TilePrefabs[randomeTileIndexToInit]);
        // position the tile
        newTile.transform.position = new Vector3(0f, 0f, nextTilePosZ);
        // make the tile object the child object of the track manager
        newTile.transform.parent = this.transform;
        numTiles++;

        newTile.transform.name = numTiles.ToString() + newTile.transform.name;
        // add this tile to the list of tiles
        Tiles.Add(newTile);

        // Attach runtime material applier to ensure it matches door
        if (newTile.GetComponent<TileMatApplier>() == null)
            newTile.gameObject.AddComponent<TileMatApplier>();

        // Build a nicer tiling material from pirate textures
        Texture2D tex = null;
        Material sourceMat = null;
#if UNITY_EDITOR
        tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hand Painted Seamless Wood Texture/Textures/Wood3.tga");
        // Try to derive the exact material/texture from the door, if present
        var door = GameObject.Find("GreenDoor");
        if (door != null)
        {
            var doorRenderer = door.GetComponent<MeshRenderer>();
            if (doorRenderer != null)
            {
                sourceMat = doorRenderer.sharedMaterial;
            }
        }
#endif

        // Function to apply texture/color to track (Ground)
        System.Action<Transform> applyToTrack = (tf) =>
        {
            if (tf == null) return;
            var rend = tf.GetComponent<Renderer>();
            if (rend == null) return;

            Material mat;
            if (rend.sharedMaterial != null)
                mat = new Material(rend.sharedMaterial);
            else
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            if (tex != null)
            {
                if (sourceMat != null && sourceMat.mainTexture != null)
                {
                    mat.mainTexture = sourceMat.mainTexture;
                    mat.SetTexture("_BaseMap", sourceMat.mainTexture);
                }
                else
                {
                    mat.mainTexture = tex;
                    mat.SetTexture("_BaseMap", tex);
                }
                mat.mainTextureScale = new Vector2(6f, 6f);
                mat.SetTextureScale("_BaseMap", new Vector2(6f, 6f));
                mat.color = Color.white; // light tint to brighten
            }
            else
            {
                // Use original light blue color for track
                mat.color = new Color(0.75f, 0.85f, 0.95f, 1f);
            }

            rend.material = mat;
        };

        // Function to apply original color to track edges (Left/Right)
        System.Action<Transform> applyToEdges = (tf) =>
        {
            if (tf == null) return;
            var rend = tf.GetComponent<Renderer>();
            if (rend == null) return;

            Material mat;
            if (rend.sharedMaterial != null)
                mat = new Material(rend.sharedMaterial);
            else
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            // Apply original color to track edges
            mat.color = new Color(0.75f, 0.85f, 0.95f, 1f);
            rend.material = mat;
        };

        // Apply colors: track and edges stay original
        Transform groundTransform = newTile.transform.Find("Ground");
        applyToTrack(groundTransform);           // Track gets original color
        applyToEdges(newTile.transform.Find("Left"));   // Track edges get original color
        applyToEdges(newTile.transform.Find("Right"));  // Track edges get original color

        // ---- announce tile spawn with bounds ----
        TileSpawned?.Invoke(BuildTileInfo(newTile, groundTransform));

        nextTilePosZ += tileLenZ;
    }

    // Centralized TileInfo computation (uses Renderer bounds if available)
    TileInfo BuildTileInfo(GameObject tile, Transform groundTransform)
    {
        Bounds gBounds;
        var groundRenderer = groundTransform ? groundTransform.GetComponent<Renderer>() : null;

        if (groundRenderer != null)
            gBounds = groundRenderer.bounds;
        else
            gBounds = new Bounds(groundTransform.position, groundTransform.localScale);

        // Prefer renderer bounds for width; z uses logical tileLenZ to match your placement
        float xHalf = gBounds.extents.x;

        // Ground Y: bottom of the ground renderer (or its position if no renderer)
        float groundY = (groundRenderer != null)
            ? groundRenderer.bounds.max.y
            : groundTransform.position.y;

        float centerZ = tile.transform.position.z;
        float zStart = centerZ - (tileLenZ * 0.5f);
        float zEnd = centerZ + (tileLenZ * 0.5f);

        return new TileInfo
        {
            tileRoot = tile,
            ground = groundTransform,
            zStart = zStart,
            zEnd = zEnd,
            xHalf = xHalf,
            y = groundY
        };
    }

    // Public: ask TrackManager to re-announce current tiles (for late subscribers)
    public void AnnounceExistingTiles()
    {
        for (int i = 0; i < Tiles.Count; i++)
        {
            var t = Tiles[i];
            if (t == null) continue;
            var ground = t.transform.Find("Ground");
            if (ground != null)
                TileSpawned?.Invoke(BuildTileInfo(t, ground));
        }
    }

    public void TileAdjustment(GameObject ExitTile)
    {
        int curTile = PlayerOnTileIndex();
        AddATile();   // will raise TileSpawned for the new tile
        DeleteEnd();
    }

    public void ApplyHorizonColorToAllExistingTiles()
    {
        // Apply colors to all existing tiles
        foreach (GameObject tile in Tiles)
        {
            if (tile != null)
            {
                // Apply original color to track (Ground)
                Transform groundTransform = tile.transform.Find("Ground");
                if (groundTransform != null)
                {
                    Renderer rend = groundTransform.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Material mat = new Material(rend.material);
                        mat.color = new Color(0.75f, 0.85f, 0.95f, 1f); // Original track color
                        rend.material = mat;
                        Debug.Log($"Applied original color to track: {tile.name}");
                    }
                }

                // Apply original color to track edges (Left/Right)
                Transform leftTransform = tile.transform.Find("Left");
                if (leftTransform != null)
                {
                    Renderer rend = leftTransform.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Material mat = new Material(rend.material);
                        mat.color = new Color(0.75f, 0.85f, 0.95f, 1f); // Original edge color
                        rend.material = mat;
                        Debug.Log($"Applied original color to left edge: {tile.name}");
                    }
                }

                Transform rightTransform = tile.transform.Find("Right");
                if (rightTransform != null)
                {
                    Renderer rend = rightTransform.GetComponent<Renderer>();
                    if (rend != null)
                    {
                        Material mat = new Material(rend.material);
                        mat.color = new Color(0.75f, 0.85f, 0.95f, 1f); // Original edge color
                        rend.material = mat;
                        Debug.Log($"Applied original color to right edge: {tile.name}");
                    }
                }
            }
        }
    }
}