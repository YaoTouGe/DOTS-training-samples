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

public partial struct ObstacleAuthoringSystem : ISystem
{
    void OnUpdate(ref SystemState state)
    {
	    Debug.Log("executed");
	    // only create the first time
	    foreach (var (antmgrConfigEntity, entity) in SystemAPI.Query<RefRO<AntManagerConfig>>().WithEntityAccess())
	    {
		    Debug.Log("xxxx");
		    var localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>();
		    var antmgrConfig = antmgrConfigEntity.ValueRO; 
		    NativeList<ObstacleInfo> output = new NativeList<ObstacleInfo>();
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
		    antmgrConfig.obstacles = output.AsArray();

		    var setPosition = new SetLocalToWorld()
		    {
			    LocalToWorldFromEntity = localToWorldLookup,
			    Entities = obstacleEntities,
			    ObstacleInfos = output
		    };
		    var handle = setPosition.Schedule(output.Length, 64);
		    state.Dependency = handle; 
		
		    state.EntityManager.DestroyEntity(entity);
		    
		    /*
		    antmgrConfig.obstacleMatrices = new NativeArray<Matrix4x4>(Mathf.CeilToInt((float)output.Length / antmgrConfig.instancesPerBatch) * antmgrConfig.instancesPerBatch, Allocator.Persistent);
		    for (int i=0;i<antmgrConfig.obstacleMatrices.Length;i++) {
			    antmgrConfig.obstacleMatrices[i] = Matrix4x4.TRS(output[i].position / antmgrConfig.mapSize,Quaternion.identity,new Vector3(antmgrConfig.obstacleRadius*2f,antmgrConfig.obstacleRadius*2f,1f)/antmgrConfig.mapSize);
		    }

		    List<Obstacle>[,] tempObstacleBuckets = new List<Obstacle>[antmgrConfig.bucketResolution,antmgrConfig.bucketResolution];

		    for (int x = 0; x < antmgrConfig.bucketResolution; x++) {
			    for (int y = 0; y < antmgrConfig.bucketResolution; y++) {
				    tempObstacleBuckets[x,y] = new List<Obstacle>();
			    }
		    }

		    for (int i = 0; i < antmgrConfig.obstacles.Length; i++) {
			    Vector2 pos = antmgrConfig.obstacles[i].position;
			    float radius = antmgrConfig.obstacles[i].radius;
			    for (int x = Mathf.FloorToInt((pos.x - radius)/antmgrConfig.mapSize*antmgrConfig.bucketResolution); x <= Mathf.FloorToInt((pos.x + radius)/antmgrConfig.mapSize*antmgrConfig.bucketResolution); x++) {
				    if (x < 0 || x >= antmgrConfig.bucketResolution) {
					    continue;
				    }
				    for (int y = Mathf.FloorToInt((pos.y - radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y <= Mathf.FloorToInt((pos.y + radius) / antmgrConfig.mapSize * antmgrConfig.bucketResolution); y++) {
					    if (y<0 || y>=antmgrConfig.bucketResolution) {
						    continue;
					    }
					    tempObstacleBuckets[x,y].Add(antmgrConfig.obstacles[i]);
				    }
			    }
		    }

		    antmgrConfig.obstacleBuckets = new Obstacle[antmgrConfig.bucketResolution,antmgrConfig.bucketResolution][];
		    for (int x = 0; x < antmgrConfig.bucketResolution; x++) {
			    for (int y = 0; y < antmgrConfig.bucketResolution; y++) {
				    antmgrConfig.obstacleBuckets[x,y] = tempObstacleBuckets[x,y].ToArray();
			    }
		    }*/
	    }
    }

    [BurstCompile]
    struct SetLocalToWorld : IJobParallelFor
    {
	    //[NativeDisableContainerSafetyRestriction] [NativeDisableParallelForRestriction]
	    public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;
	    public NativeArray<Entity> Entities;
	    public NativeList<ObstacleInfo> ObstacleInfos;
	    
	    public void Execute(int index)
	    {
		    var localToWorld = new LocalToWorld()
		    {
			    Value = float4x4.TRS(new float3(ObstacleInfos[index].position.x, ObstacleInfos[index].position.y, 0f), quaternion.identity, new Vector3(ObstacleInfos[index].radius,ObstacleInfos[index].radius,ObstacleInfos[index].radius)),
		    };
		    var entity = Entities[index];
		    LocalToWorldFromEntity[entity] = localToWorld;
	    }
    }
}