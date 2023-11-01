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
    void OnUpdate(ref SystemState state)
    {
	    var ecb = new EntityCommandBuffer(Allocator.Temp);
	    foreach (var (antmgrConfigEntity, entity) in SystemAPI.Query<RefRO<AntManagerConfig>>().WithEntityAccess())
	    {
		    var antmgrConfig = antmgrConfigEntity.ValueRO;

			// Colony
			Entity colony = state.EntityManager.Instantiate(antmgrConfig.colonyPrefab);
			AntManagerConfig.colonyPosition = Vector3.one * AntManagerConfig.mapSize * .5f;

			SystemAPI.GetComponentRW<LocalTransform>(colony).ValueRW = new LocalTransform
			{
				Rotation = quaternion.identity,
				Position = AntManagerConfig.colonyPosition,
				Scale = 4f
			};

			// Resource position
			float resourceAngle = Random.value * 2f * Mathf.PI;
			AntManagerConfig.resourcePosition = Vector2.one * AntManagerConfig.mapSize * .5f + new Vector2(Mathf.Cos(resourceAngle) * AntManagerConfig.mapSize * .475f, Mathf.Sin(resourceAngle) * AntManagerConfig.mapSize * .475f);
			var resource = state.EntityManager.Instantiate(antmgrConfig.resourcePrefab);
			SystemAPI.GetComponentRW<LocalTransform>(resource).ValueRW = new LocalTransform
			{
				Rotation = quaternion.identity,
				Position = AntManagerConfig.resourcePosition,
				Scale = 4f
			};

			GenerateObstacles(ref state, ref antmgrConfig);

			// Spawning ants
			GenerateAnts(ref state, ref antmgrConfig);

			// only create the first time, so we destroy it after spawning
			ecb.DestroyEntity(entity);
		}
	    ecb.Playback(state.EntityManager);
    }

	void GenerateAnts(ref SystemState state, ref AntManagerConfig antmgrConfig)
	{
		var ants = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(AntManagerConfig.antCount, ref state.World.Unmanaged.UpdateAllocator, NativeArrayOptions.UninitializedMemory);
		state.EntityManager.Instantiate(antmgrConfig.antPrefab, ants);
		foreach(var (antInfo, transform) in SystemAPI.Query<RefRO<AntInfo>, RefRW<LocalTransform>>())
		{
			transform.ValueRW.Position = new Vector3(Random.Range(-5f, 5f) + AntManagerConfig.mapSize * .5f, Random.Range(-5f, 5f) + AntManagerConfig.mapSize * .5f, 0);
		}
	}

	void GenerateObstacles(ref SystemState state, ref AntManagerConfig antmgrConfig)
	{
		var localToWorldLookup = SystemAPI.GetComponentLookup<LocalTransform>();
		NativeList<ObstacleInfo> output = new NativeList<ObstacleInfo>(Allocator.Temp);
		for (int i = 1; i <= AntManagerConfig.obstacleRingCount; i++)
		{
			float ringRadius = (i / (AntManagerConfig.obstacleRingCount + 1f)) * (AntManagerConfig.mapSize * .5f);
			float circumference = ringRadius * 2f * Mathf.PI;
			int maxCount = Mathf.CeilToInt(circumference / (2f * AntManagerConfig.obstacleRadius) * 2f);
			int offset = Random.Range(0, maxCount);
			int holeCount = Random.Range(1, 3);
			for (int j = 0; j < maxCount; j++)
			{
				float t = (float)j / maxCount;
				if ((t * holeCount) % 1f < AntManagerConfig.obstaclesPerRing)
				{
					float angle = (j + offset) / (float)maxCount * (2f * Mathf.PI);
					ObstacleInfo obstacle = new ObstacleInfo();
					obstacle.position = new Vector2(AntManagerConfig.mapSize * .5f + Mathf.Cos(angle) * ringRadius, AntManagerConfig.mapSize * .5f + Mathf.Sin(angle) * ringRadius);
					obstacle.radius = AntManagerConfig.obstacleRadius;
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

		/*AntManagerConfig.obstacleMatrices = new NativeArray<Matrix4x4>(Mathf.CeilToInt((float)output.Length / AntManagerConfig.instancesPerBatch) * AntManagerConfig.instancesPerBatch, Allocator.Persistent);
		    for (int i=0;i<AntManagerConfig.obstacleMatrices.Length;i++) {
			    AntManagerConfig.obstacleMatrices[i] = Matrix4x4.TRS(output[i].position / AntManagerConfig.mapSize,Quaternion.identity,new Vector3(AntManagerConfig.obstacleRadius*2f,AntManagerConfig.obstacleRadius*2f,1f)/AntManagerConfig.mapSize);
		    }*/

		List<ObstacleInfo>[,] tempObstacleBuckets = new List<ObstacleInfo>[AntManagerConfig.bucketResolution, AntManagerConfig.bucketResolution];

		for (int x = 0; x < AntManagerConfig.bucketResolution; x++)
		{
			for (int y = 0; y < AntManagerConfig.bucketResolution; y++)
			{
				tempObstacleBuckets[x, y] = new List<ObstacleInfo>();
			}
		}


		for (int i = 0; i < output.Length; i++)
		{
			Vector2 pos = output[i].position;
			float radius = output[i].radius;
			for (int x = Mathf.FloorToInt((pos.x - radius) / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution); x <= Mathf.FloorToInt((pos.x + radius) / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution); x++)
			{
				if (x < 0 || x >= AntManagerConfig.bucketResolution)
				{
					continue;
				}
				for (int y = Mathf.FloorToInt((pos.y - radius) / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution); y <= Mathf.FloorToInt((pos.y + radius) / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution); y++)
				{
					if (y < 0 || y >= AntManagerConfig.bucketResolution)
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
		AntManagerConfig.obstacles = new NativeArray<ObstacleInfo>(totalSize, Allocator.Persistent);
		AntManagerConfig.obstacleBuckets = new NativeArray<CellRange>(AntManagerConfig.bucketResolution * AntManagerConfig.bucketResolution, Allocator.Persistent);

		int accIdx = 0;
		for (int x = 0; x < AntManagerConfig.bucketResolution; x++)
		{
			for (int y = 0; y < AntManagerConfig.bucketResolution; y++)
			{
				var bucketObstacles = tempObstacleBuckets[x, y];
				CellRange range = new CellRange()
				{
					start = accIdx,
					length = bucketObstacles.Count
				};

				for (int obstacleIdx = 0; obstacleIdx < bucketObstacles.Count; obstacleIdx++)
				{
					AntManagerConfig.obstacles[accIdx + obstacleIdx] = bucketObstacles[obstacleIdx];
				}

				AntManagerConfig.obstacleBuckets[x * AntManagerConfig.bucketResolution + y] = range;
				accIdx += bucketObstacles.Count;
			}
		}

		AntManagerConfig.pheromones = new NativeArray<Vector4>(AntManagerConfig.mapSize * AntManagerConfig.mapSize, Allocator.Persistent);
	}

    void OnDestroy()
    {
	    AntManagerConfig.obstacleMatrices.Dispose();
	    AntManagerConfig.obstacleBuckets.Dispose();
	    AntManagerConfig.obstacles.Dispose();
		AntManagerConfig.pheromones.Dispose();
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