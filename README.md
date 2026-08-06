# Pirate Runner 3D

> **A High-Performance 3D Endless Runner Built in Unity & C#**  
> *Showcasing custom physics, state-driven enemy chasers, procedural track generation, object pooling, and dynamic spatial ocean systems.*

<div align="center">

![Engine](https://img.shields.io/badge/Engine-Unity_3D-0078D4?style=for-the-badge&logo=unity&logoColor=white)
![Language](https://img.shields.io/badge/Language-C%23-239120?style=for-the-badge&logo=csharp&logoColor=white)
![Architecture](https://img.shields.io/badge/Architecture-Object_Pooling_%26_FSM-orange?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-PC-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)

</div>

---

## Gameplay Demonstration

<div align="center">
  <video src="https://github.com/user-attachments/assets/7a91a4aa-b39d-4cb5-b02d-012592cd57ca](https://github.com/user-attachments/assets/1520d678-4260-4adf-a99c-d2c22d4a9913" controls width="100%"></video>
</div>

---

## Executive Summary

**Pirate Runner 3D** is an action-packed, pirate-themed 3D endless runner developed in Unity. The game features fast-paced obstacle avoidance, cannonball artillery strikes from an enemy pirate ship sailing alongside the track, an aggressive enemy chaser that rushes in when errors are made, and infinite procedural environment generation.

> **Note on Repository Scope:**  
> To keep this portfolio light, fast-loading, and proprietary, raw Unity binary asset caches (`Library/`, `Temp/`, high-poly FBX mesh bundles) have been excluded. This repository directly exposes the core **C# architecture and gameplay logic systems** engineered for the project.

---

## Key Features & Gameplay Mechanics

- **Smooth 3D Player Controller (`PlayerMove.cs`)**: Dynamic forward velocity ramping, lateral smoothing with acceleration/dampening vectors, dual-state jumping via ground probing, and dynamic capsule collider resizing for sliding under obstacles.
- **Procedural Track Generator & Tile Pool (`TrackManager.cs`)**: Memory-optimized endless track spawner utilizing an object pool to recycle terrain tiles ahead of and behind the player dynamically.
- **Dynamic Enemy Pirate Ship Artillery (`ShipFollower.cs`)**: Side-sailing pirate ship featuring loophole alignment mechanics, target calculation, and dynamic cannonball trajectory firing.
- **State-Driven Enemy Chaser (`VillainChaser.cs`)**: *Subway Surfers*-style villain chaser featuring finite state machine transitions (`Safe`, `Rushing`, `InFrame`, `Retreating`, `Catching`), danger windows, and auditory threat cues.
- **Infinite Ocean Spatial Grid (`WaterManager.cs`)**: Infinite sea rendering utilizing a 2D spatial hash grid (`Dictionary<Vector2Int, GameObject>`) to dynamically instantiate, position, and recycle ocean tiles around player coordinates.

---

## System Architecture & Design Patterns

The core codebase is designed around clean object-oriented principles, modularity, and runtime memory efficiency.

| Component / Script | Primary Responsibility | Architectural Pattern |
| :--- | :--- | :--- |
| [`PlayerMove.cs`](./Assets/Scripts/PlayerMove.cs) | Player physics, ground probing, jump assist, slide collider resizing | **Kinematic Physics & State Driven** |
| [`TrackManager.cs`](./Assets/Scripts/TrackManager.cs) | Endless track generation, tile spawning, memory recycling | **Object Pooling & Event Observer** |
| [`VillainChaser.cs`](./Assets/Scripts/VillainChaser.cs) | Enemy pursuit mechanics, danger windows, speed catch-up logic | **Finite State Machine (FSM)** |
| [`ShipFollower.cs`](./Assets/Scripts/ShipFollower.cs) | Side-sailing ship movement, loophole target alignment, cannon firing | **Procedural Target Tracking** |
| [`WaterManager.cs`](./Assets/Scripts/WaterManager.cs) | Infinite surrounding ocean tiles generation & cleanup | **Spatial Grid Hashing** |

---

## Code Showcase & Systems Deep Dive

### 1. Finite State Machine Enemy Chaser (`VillainChaser.cs`)
The chaser stays out of frame during clean runs. When the player strikes an obstacle, the chaser triggers a `Rushing` state into the camera frame. A timed **Danger Window** is activated—if the player hits another obstacle while debuffed, the villain catches and defeats the player.

```csharp
private enum VillainState { 
    Safe, 
    Rushing, 
    InFrame, 
    Retreating, 
    Catching, 
    Caught 
}

void Update()
{
    if (!isActive || hasCaughtPlayer) return;

    switch (currentState)
    {
        case VillainState.Safe:
            targetDistance = safeDistance;
            break;

        case VillainState.Rushing:
            targetDistance = dangerDistance;
            currentDistance = Mathf.MoveTowards(currentDistance, targetDistance, rushSpeed * Time.deltaTime);
            if (Mathf.Abs(currentDistance - targetDistance) < 0.1f)
            {
                currentState = VillainState.InFrame;
                dangerWindowEndTime = Time.time + dangerWindowDuration;
            }
            break;

        case VillainState.Retreating:
            targetDistance = safeDistance;
            currentDistance = Mathf.MoveTowards(currentDistance, targetDistance, retreatSpeed * Time.deltaTime);
            if (Mathf.Abs(currentDistance - targetDistance) < 0.1f)
                currentState = VillainState.Safe;
            break;
    }
    
    // Smoothly position villain behind player on Z-axis
    Vector3 targetPos = player.position - Vector3.forward * currentDistance;
    transform.position = new Vector3(player.position.x, targetPos.y, targetPos.z);
}
```

---

### 2. Procedural Track Object Pooling (`TrackManager.cs`)
Rather than instantiating and destroying tiles continuously (causing garbage collection spikes), the `TrackManager` reuses inactive tile prefabs via a `TilePool` array and triggers C# event delegates when new tiles enter world space.

```csharp
public event System.Action<TileInfo> TileSpawned;

private void AddATile()
{
    GameObject tile;
    if (TilePool.Count > 0)
    {
        tile = TilePool[0];
        TilePool.RemoveAt(0);
        tile.SetActive(true);
    }
    else
    {
        tile = Instantiate(TilePrefabs[Random.Range(0, TilePrefabs.Count)]);
    }

    tile.transform.position = new Vector3(0, 0, nextTilePosZ);
    nextTilePosZ += tileLenZ;
    Tiles.Add(tile);

    // Notify listeners (e.g. ObstacleSpawner, ItemCollector)
    TileSpawned?.Invoke(new TileInfo { 
        tileRoot = tile, 
        zStart = nextTilePosZ - tileLenZ, 
        zEnd = nextTilePosZ 
    });
}
```

---

### 3. Spatial Hash Ocean Grid (`WaterManager.cs`)
Renders an infinite ocean by computing player tile grid coordinates `(gridX, gridZ)` and maintaining a dictionary of surrounding ocean tiles.

```csharp
private Vector2Int GetGridPos(Vector3 worldPos)
{
    int x = Mathf.FloorToInt((worldPos.x + oceanTileSize * 0.5f) / oceanTileSize);
    int z = Mathf.FloorToInt((worldPos.z + oceanTileSize * 0.5f) / oceanTileSize);
    return new Vector2Int(x, z);
}

private void UpdateOceanGrid()
{
    Vector2Int playerGrid = GetGridPos(player.transform.position);
    HashSet<Vector2Int> requiredTiles = new HashSet<Vector2Int>();

    for (int x = -tilesPerSide; x <= tilesPerSide; x++)
    {
        for (int z = -tilesBehind; z <= tilesAhead; z++)
        {
            requiredTiles.Add(new Vector2Int(playerGrid.x + x, playerGrid.y + z));
        }
    }

    // Recycle tiles outside threshold & spawn missing grid cells
    DespawnDistantTiles(requiredTiles);
    SpawnMissingTiles(requiredTiles);
}
```
