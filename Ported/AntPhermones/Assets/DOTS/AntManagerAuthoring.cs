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
    public Material antMaterial;
    public Material obstacleMaterial;
    public Material resourceMaterial;
    public Material colonyMaterial;
    public Mesh antMesh;
    public GameObject obstaclePrefab;
    public Mesh colonyMesh;
    public Mesh resourceMesh;
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

    Vector2 resourcePosition;
    Vector2 colonyPosition;

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
			    obstacleRingCount =  antManager.obstacleRingCount,
			    obstaclesPerRing = antManager.obstaclesPerRing,
			    obstacleRadius = antManager.obstacleRadius,
			    obstaclePrefab = GetEntity(antManager.obstaclePrefab, TransformUsageFlags.Dynamic),
			    mapSize =  antManager.mapSize,
			    
			    instancesPerBatch = instancesPerBatch,
			    bucketResolution = antManager.bucketResolution
		    });
	    }
    }
}

public struct CellRange
{
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
	public float obstaclesPerRing;
	public float obstacleRadius;
	public Entity obstaclePrefab;
	public int mapSize;
	public static NativeArray<Matrix4x4> obstacleMatrices;
	
	public int instancesPerBatch;
	public int bucketResolution;
	public static NativeArray<CellRange> obstacleBuckets;
	public static  NativeArray<ObstacleInfo> obstacles;
}