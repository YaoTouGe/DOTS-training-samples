using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Random = UnityEngine.Random;

[BurstCompile]
public partial struct SpawnSystem : ISystem
{
	bool hasSpawned;

	void OnCreate(ref SystemState state)
	{
		hasSpawned = false;
	}

    void OnUpdate(ref SystemState state)
    {
		if (hasSpawned)
			return;

	    foreach (var (antmgrConfigEntity, entity) in SystemAPI.Query<RefRW<AntManagerConfig>>().WithEntityAccess())
	    {
		    ref var antmgrConfig = ref antmgrConfigEntity.ValueRW;

			// Colony
			Entity colony = state.EntityManager.Instantiate(antmgrConfig.colonyPrefab);
			antmgrConfig.colonyPosition = Vector3.one * antmgrConfig.mapSize * .5f;

			SystemAPI.GetComponentRW<LocalTransform>(colony).ValueRW = new LocalTransform
			{
				Rotation = quaternion.identity,
				Position = antmgrConfig.colonyPosition,
				Scale = 4f
			};

			// Resource position
			float resourceAngle = Random.value * 2f * Mathf.PI;
			antmgrConfig.resourcePosition = Vector2.one * antmgrConfig.mapSize * .5f + new Vector2(Mathf.Cos(resourceAngle) * antmgrConfig.mapSize * .475f, Mathf.Sin(resourceAngle) * antmgrConfig.mapSize * .475f);
			var resource = state.EntityManager.Instantiate(antmgrConfig.resourcePrefab);
			SystemAPI.GetComponentRW<LocalTransform>(resource).ValueRW = new LocalTransform
			{
				Rotation = quaternion.identity,
				Position = antmgrConfig.resourcePosition,
				Scale = 4f
			};

			GenerateObstacles(ref state, ref antmgrConfig, entity);

			// Spawning ants
			GenerateAnts(ref state, ref antmgrConfig);

			hasSpawned = true;
		}
    }

	void GenerateAnts(ref SystemState state, ref AntManagerConfig antmgrConfig)
	{
		var ants = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(antmgrConfig.antCount, ref state.World.Unmanaged.UpdateAllocator, NativeArrayOptions.UninitializedMemory);
		state.EntityManager.Instantiate(antmgrConfig.antPrefab, ants);
		foreach(var (antInfo, transform) in SystemAPI.Query<RefRO<AntInfo>, RefRW<LocalTransform>>())
		{
			transform.ValueRW.Position = new Vector3(Random.Range(-5f, 5f) + antmgrConfig.mapSize * .5f, Random.Range(-5f, 5f) + antmgrConfig.mapSize * .5f, 0);
		}
	}

	void GenerateObstacles(ref SystemState state, ref AntManagerConfig antmgrConfig, Entity configEntity)
	{
		var localToWorldLookup = SystemAPI.GetComponentLookup<LocalTransform>();
		NativeList<ObstacleInfo> output = new NativeList<ObstacleInfo>(Allocator.TempJob);
		for (int i = 1; i <= antmgrConfig.obstacleRingCount; i++)
		{
			float ringRadius = (i / (antmgrConfig.obstacleRingCount + 1f)) * (antmgrConfig.mapSize * .5f);
			float circumference = ringRadius * 2f * Mathf.PI;
			int maxCount = Mathf.CeilToInt(circumference / (2f * antmgrConfig.obstacleRadius) * 2f);
			int offset = Random.Range(0, maxCount);
			int holeCount = Random.Range(1, 3);
			for (int j = 0; j < maxCount; j++)
			{
				float t = (float)j / maxCount;
				if ((t * holeCount) % 1f < antmgrConfig.obstaclesPerRing)
				{
					float angle = (j + offset) / (float)maxCount * (2f * Mathf.PI);
					ObstacleInfo obstacle = new ObstacleInfo();
					obstacle.position = new Vector2(antmgrConfig.mapSize * .5f + Mathf.Cos(angle) * ringRadius, antmgrConfig.mapSize * .5f + Mathf.Sin(angle) * ringRadius);
					obstacle.radius = antmgrConfig.obstacleRadius;
					output.Add(obstacle);
					//Debug.DrawRay(obstacle.position / mapSize,-Vector3.forward * .05f,Color.green,10000f);
				}
			}
		}

		var obstacleEntities =
			CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(output.Length,
				ref state.World.Unmanaged.UpdateAllocator);
		state.EntityManager.Instantiate(antmgrConfig.obstaclePrefab, obstacleEntities);

		var setPosition = new SetLocalToWorld()
		{
			LocalToWorldFromEntity = localToWorldLookup,
			Entities = obstacleEntities,
			ObstacleInfos = output
		};
		setPosition.Schedule(output.Length, 64).Complete();

		/*antmgrConfig.obstacleMatrices = new NativeArray<Matrix4x4>(Mathf.CeilToInt((float)output.Length / antmgrConfig.instancesPerBatch) * antmgrConfig.instancesPerBatch, Allocator.Persistent);
		    for (int i=0;i<antmgrConfig.obstacleMatrices.Length;i++) {
			    antmgrConfig.obstacleMatrices[i] = Matrix4x4.TRS(output[i].position / antmgrConfig.mapSize,Quaternion.identity,new Vector3(antmgrConfig.obstacleRadius*2f,antmgrConfig.obstacleRadius*2f,1f)/antmgrConfig.mapSize);
		    }*/

		List<ObstacleInfo>[,] tempObstacleBuckets = new List<ObstacleInfo>[antmgrConfig.bucketResolution, antmgrConfig.bucketResolution];

		for (int x = 0; x < antmgrConfig.bucketResolution; x++)
		{
			for (int y = 0; y < antmgrConfig.bucketResolution; y++)
			{
				tempObstacleBuckets[x, y] = new List<ObstacleInfo>();
			}
		}


		for (int i = 0; i < output.Length; i++)
		{
			Vector2 pos = output[i].position;
			float radius = output[i].radius;
			for (int x = Mathf.FloorToInt((pos.x - radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); x <= Mathf.FloorToInt((pos.x + radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); x++)
			{
				if (x < 0 || x >= antmgrConfig.bucketResolution)
				{
					continue;
				}
				for (int y = Mathf.FloorToInt((pos.y - radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y <= Mathf.FloorToInt((pos.y + radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y++)
				{
					if (y < 0 || y >= antmgrConfig.bucketResolution)
					{
						continue;
					}
					tempObstacleBuckets[x, y].Add(output[i]);
				}
			}
		}

		int totalSize = 0;
		foreach (var bucket in tempObstacleBuckets)
		{
			totalSize += bucket.Count;
		}

		// A flattern grid of cell, each grid contains a range of obstacle infos
		DynamicBuffer<ObstacleData> obstacles = SystemAPI.GetBuffer<ObstacleData>(configEntity);
		obstacles.ResizeUninitialized(totalSize);
		DynamicBuffer<ObstacleBucket> obstacleBuckets = SystemAPI.GetBuffer<ObstacleBucket>(configEntity);
		obstacleBuckets.ResizeUninitialized(antmgrConfig.bucketResolution * antmgrConfig.bucketResolution);

		int accIdx = 0;
		for (int x = 0; x < antmgrConfig.bucketResolution; x++)
		{
			for (int y = 0; y < antmgrConfig.bucketResolution; y++)
			{
				var bucketObstacles = tempObstacleBuckets[x, y];
				CellRange curRange = new CellRange()
				{
					start = accIdx,
					length = bucketObstacles.Count
				};

				for (int obstacleIdx = 0; obstacleIdx < bucketObstacles.Count; obstacleIdx++)
				{
					obstacles[accIdx + obstacleIdx] = new ObstacleData() { obstacleInfo = bucketObstacles[obstacleIdx] };
				}

				obstacleBuckets[x * antmgrConfig.bucketResolution + y] = new ObstacleBucket(){range = curRange};
				accIdx += bucketObstacles.Count;
			}
		}

		DynamicBuffer<PheromoneData> pheromones = SystemAPI.GetBuffer<PheromoneData>(configEntity);
		pheromones.Resize(antmgrConfig.mapSize * antmgrConfig.mapSize, NativeArrayOptions.ClearMemory);
	}

    void OnDestroy()
    {
	}

    [BurstCompile]
    struct SetLocalToWorld : IJobParallelFor
    {
	    [NativeDisableParallelForRestriction]
	    public ComponentLookup<LocalTransform> LocalToWorldFromEntity;
	    public NativeArray<Entity> Entities;
	    [ReadOnly]
	    public NativeList<ObstacleInfo> ObstacleInfos;
	    
	    public void Execute(int index)
	    {
		    var localtransform = new LocalTransform()
		    {
				Position = new float3(ObstacleInfos[index].position.x, ObstacleInfos[index].position.y, 0f),
				Scale =  ObstacleInfos[index].radius,
				Rotation = quaternion.identity
		    };
		    var entity = Entities[index];
		    LocalToWorldFromEntity[entity] = localtransform;
	    }
    }
}