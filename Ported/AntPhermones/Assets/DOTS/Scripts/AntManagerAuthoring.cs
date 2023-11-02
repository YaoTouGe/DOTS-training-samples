using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;

public class AntManagerAuthoring : MonoBehaviour
{
    public Material basePheromoneMaterial;
    public Renderer pheromoneRenderer;
    /*public Material antMaterial;
    public Material obstacleMaterial;
    public Material resourceMaterial;
    public Material colonyMaterial;
    public Mesh antMesh;
    public Mesh colonyMesh;
    public Mesh resourceMesh;
    */
    public GameObject obstaclePrefab;
    public GameObject antPrefab;
    public GameObject colonyPrefab;
    public GameObject resourcePrefab;
    
    public Color searchColor;
    public Color carryColor;
    public int antCount;
    public int mapSize = 128;
    public int bucketResolution;
    public Vector3 antSize;
    public float antSpeed;
    [Range(0f,1f)]
    public float antAccel;
    public float trailAddSpeed;
    [Range(0f,1f)]
    public float trailDecay;
    public float randomSteering;
    public float pheromoneSteerStrength;
    public float wallSteerStrength;
    public float goalSteerStrength;
    public float outwardStrength;
    public float inwardStrength;
    public int rotationResolution = 360;
    public int obstacleRingCount;
    [Range(0f,1f)]
    public float obstaclesPerRing;
    public float obstacleRadius;

    Texture2D pheromoneTexture;
    Material myPheromoneMaterial;

    Color[] pheromones;
    Ant[] ants;
    Matrix4x4[][] matrices;
    Vector4[][] antColors;
    MaterialPropertyBlock[] matProps;
    Obstacle[] obstacles;
    Matrix4x4[][] obstacleMatrices;
    Obstacle[,][] obstacleBuckets;

    Matrix4x4 resourceMatrix;
    Matrix4x4 colonyMatrix;

    const int instancesPerBatch = 1023;

    Matrix4x4[] rotationMatrixLookup;
    Obstacle[] emptyBucket = new Obstacle[0];

    public class Baker : Baker<AntManagerAuthoring>
    {
	    public override void Bake(AntManagerAuthoring antManager)
	    {
		    var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new AntManagerConfig()
		    {
                obstacleRingCount = antManager.obstacleRingCount,
                obstaclesPerRing = antManager.obstaclesPerRing,
                obstacleRadius = antManager.obstacleRadius,
                mapSize = antManager.mapSize,
                antCount = antManager.antCount,
                instancesPerBatch = instancesPerBatch,
                bucketResolution = antManager.bucketResolution,
                antSpeed = antManager.antSpeed,
                randomSteering = antManager.randomSteering,
                inwardStrength = antManager.inwardStrength,
                pheromoneSteerStrength = antManager.pheromoneSteerStrength,
                wallSteerStrength = antManager.wallSteerStrength,
                outwardStrength = antManager.outwardStrength,
                goalSteerStrength = antManager.goalSteerStrength,
                antAccel = antManager.antAccel,
                trailAddSpeed = antManager.trailAddSpeed,
                trailDecay = antManager.trailDecay,
                obstaclePrefab = GetEntity(antManager.obstaclePrefab, TransformUsageFlags.Dynamic),
                antPrefab = GetEntity(antManager.antPrefab, TransformUsageFlags.Dynamic),
                colonyPrefab = GetEntity(antManager.colonyPrefab, TransformUsageFlags.Dynamic),
                resourcePrefab = GetEntity(antManager.resourcePrefab, TransformUsageFlags.Dynamic),
            });
        }
    }
}

public struct CellRange
{
    public CellRange(int start, int length)
    {
        this.start = start;
        this.length = length;
    }
	public int start;
	public int length;
}

public struct ObstacleInfo
{
	public Vector2 position;
	public float radius;
}

[BurstCompile]
public struct AntManagerConfig: IComponentData
{
	public int obstacleRingCount;
    public int antCount;
	public float obstaclesPerRing;
	public float obstacleRadius;
	public Entity obstaclePrefab;
    public Entity antPrefab;
    public Entity colonyPrefab;
    public Entity resourcePrefab;

    public int mapSize;
    public float antSpeed;
    public float randomSteering;
    public float pheromoneSteerStrength;
    public float wallSteerStrength;
    public float goalSteerStrength;
    public float outwardStrength;
    public float inwardStrength;
    public float antAccel;
    public float trailAddSpeed;
    public float trailDecay;
    public Vector3 resourcePosition;
    public Vector3 colonyPosition;
    public static NativeArray<Matrix4x4> obstacleMatrices;
	
	public int instancesPerBatch;
	public int bucketResolution;
	public static NativeArray<CellRange> obstacleBuckets;
	public static NativeArray<ObstacleInfo> obstacles;

    public static NativeArray<Vector4> pheromones;
}