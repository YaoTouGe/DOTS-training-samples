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
public partial struct ObstacleAuthoringSystem : ISystem
{
    void OnUpdate(ref SystemState state)
    {
	    var ecb = new EntityCommandBuffer(Allocator.Temp);
	    foreach (var (antmgrConfigEntity, entity) in SystemAPI.Query<RefRO<AntManagerConfig>>().WithEntityAccess())
	    {
		    Debug.Log("xxxx");
		    var localToWorldLookup = SystemAPI.GetComponentLookup<LocalTransform>();
		    var antmgrConfig = antmgrConfigEntity.ValueRO; 
		    NativeList<ObstacleInfo> output = new NativeList<ObstacleInfo>(Allocator.Persistent);
		    for (int i=1;i<=antmgrConfig.obstacleRingCount;i++) {
			    float ringRadius = (i / (antmgrConfig.obstacleRingCount+1f)) * (antmgrConfig.mapSize * .5f);
			    float circumference = ringRadius * 2f * Mathf.PI;
			    int maxCount = Mathf.CeilToInt(circumference / (2f * antmgrConfig.obstacleRadius) * 2f);
			    int offset = Random.Range(0,maxCount);
			    int holeCount = Random.Range(1,3);
			    for (int j=0;j<maxCount;j++) {
				    float t = (float)j / maxCount;
				    if ((t * holeCount)%1f < antmgrConfig.obstaclesPerRing) {
					    float angle = (j + offset) / (float)maxCount * (2f * Mathf.PI);
					    ObstacleInfo obstacle = new ObstacleInfo();
					    obstacle.position = new Vector2(antmgrConfig.mapSize * .5f + Mathf.Cos(angle) * ringRadius,antmgrConfig.mapSize * .5f + Mathf.Sin(angle) * ringRadius);
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
		    AntManagerConfig.obstacles = output.AsArray();

		    var setPosition = new SetLocalToWorld()
		    {
			    LocalToWorldFromEntity = localToWorldLookup,
			    Entities = obstacleEntities,
			    ObstacleInfos = output
		    };
		    setPosition.Schedule(output.Length, 64).Complete();
		
		    // only create the first time, so we destroy it after spawning
		    ecb.DestroyEntity(entity);
		    

		    /*AntManagerConfig.obstacleMatrices = new NativeArray<Matrix4x4>(Mathf.CeilToInt((float)output.Length / antmgrConfig.instancesPerBatch) * antmgrConfig.instancesPerBatch, Allocator.Persistent);
		    for (int i=0;i<AntManagerConfig.obstacleMatrices.Length;i++) {
			    AntManagerConfig.obstacleMatrices[i] = Matrix4x4.TRS(output[i].position / antmgrConfig.mapSize,Quaternion.identity,new Vector3(antmgrConfig.obstacleRadius*2f,antmgrConfig.obstacleRadius*2f,1f)/antmgrConfig.mapSize);
		    }*/

		    List<ObstacleInfo>[,] tempObstacleBuckets = new List<ObstacleInfo>[antmgrConfig.bucketResolution,antmgrConfig.bucketResolution];

		    for (int x = 0; x < antmgrConfig.bucketResolution; x++) {
			    for (int y = 0; y < antmgrConfig.bucketResolution; y++) {
				    tempObstacleBuckets[x,y] = new List<ObstacleInfo>();
			    }
		    }

			
		    for (int i = 0; i < AntManagerConfig.obstacles.Length; i++) {
			    Vector2 pos = AntManagerConfig.obstacles[i].position;
			    float radius = AntManagerConfig.obstacles[i].radius;
			    for (int x = Mathf.FloorToInt((pos.x - radius)/antmgrConfig.mapSize*antmgrConfig.bucketResolution); x <= Mathf.FloorToInt((pos.x + radius)/antmgrConfig.mapSize*antmgrConfig.bucketResolution); x++) {
				    if (x < 0 || x >= antmgrConfig.bucketResolution) {
					    continue;
				    }
				    for (int y = Mathf.FloorToInt((pos.y - radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y <= Mathf.FloorToInt((pos.y + radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y++) {
					    if (y<0 || y>=antmgrConfig.bucketResolution) {
						    continue;
					    }
					    tempObstacleBuckets[x,y].Add(AntManagerConfig.obstacles[i]);
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
		    AntManagerConfig.obstacleBuckets = new NativeArray<CellRange>(antmgrConfig.bucketResolution * antmgrConfig.bucketResolution, Allocator.Persistent);

		    int accIdx = 0;
		    for (int x = 0; x < antmgrConfig.bucketResolution; x++) {
			    for (int y = 0; y < antmgrConfig.bucketResolution; y++) {
				    var obstacles = tempObstacleBuckets[x,y];
				    CellRange range = new CellRange()
				    {
					    start = accIdx,
					    length = obstacles.Count
				    };
				    
				    for (int obstacleIdx = 0; obstacleIdx < obstacles.Count; obstacleIdx++)
				    {
					    obstacles[accIdx + obstacleIdx] = obstacles[obstacleIdx];
				    }

				    AntManagerConfig.obstacleBuckets[x * antmgrConfig.bucketResolution + y] = range;
				    accIdx += obstacles.Count;
			    }
		    }
	    }
	    ecb.Playback(state.EntityManager);
    }

    void OnDestroy()
    {
	    AntManagerConfig.obstacleMatrices.Dispose();
	    AntManagerConfig.obstacleBuckets.Dispose();
	    AntManagerConfig.obstacles.Dispose();
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