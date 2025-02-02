using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using System;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

[BurstCompile]
[UpdateAfter(typeof(SpawnSystem))]
partial struct AntMoveSystem : ISystem
{

    AntManagerConfig config;
    DynamicBuffer<ObstacleData> obstacles;
    DynamicBuffer<ObstacleBucket> obstacleBuckets;
    DynamicBuffer<PheromoneData> pheromones;
    NativeArray<Unity.Mathematics.Random> randGenerators;

    void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AntManagerConfig>();
        randGenerators = CollectionHelper.CreateNativeArray<Unity.Mathematics.Random>(Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount; ++i)
        {
            randGenerators[i] = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, int.MaxValue));
        }
    }

    [BurstCompile]
    void OnUpdate(ref SystemState state)
    {
        UpdateAnts(ref state);
    }

    void OnDestroy(ref SystemState state)
    {
        CollectionHelper.DisposeNativeArray(randGenerators, Allocator.Persistent);
    }

    void UpdateAnts(ref SystemState state)
    {
        config = SystemAPI.GetSingleton<AntManagerConfig>();
        obstacles = SystemAPI.GetSingletonBuffer<ObstacleData>();
        obstacleBuckets = SystemAPI.GetSingletonBuffer<ObstacleBucket>();
        pheromones = SystemAPI.GetSingletonBuffer<PheromoneData>();

        MoveJob move = new MoveJob()
        {
            config = SystemAPI.GetSingleton<AntManagerConfig>(),
            obstacles = SystemAPI.GetSingletonBuffer<ObstacleData>(),
            obstacleBuckets = SystemAPI.GetSingletonBuffer<ObstacleBucket>(),
            pheromones = SystemAPI.GetSingletonBuffer<PheromoneData>(),
            deltaTime = Time.fixedDeltaTime,
            randomGenerators = this.randGenerators
        };
        state.Dependency = move.ScheduleParallel(state.Dependency);

        PheromonDecay decayJob = new PheromonDecay
        {
            trailDecay = config.trailDecay,
            pheromones = move.pheromones
        };
        state.Dependency = decayJob.Schedule(config.mapSize * config.mapSize, 64, state.Dependency);
        state.Dependency.Complete();
    }

    [BurstCompile]
    public partial struct PheromonDecay: IJobParallelFor
    {
        public float trailDecay;
        [NativeDisableContainerSafetyRestriction]
        public DynamicBuffer<PheromoneData> pheromones;
        public void Execute(int index)
        {
            Vector4 oldValue = pheromones[index].value;
            oldValue.x *= trailDecay;
            pheromones[index] = new PheromoneData() { value = oldValue };
        }
    }

    [BurstCompile]
    public partial struct MoveJob : IJobEntity
    {
        public float deltaTime;
        public AntManagerConfig config;
        [ReadOnly]
        public DynamicBuffer<ObstacleData> obstacles;
        [ReadOnly]
        public DynamicBuffer<ObstacleBucket> obstacleBuckets;
        [NativeDisableContainerSafetyRestriction]
        public DynamicBuffer<PheromoneData> pheromones;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<Unity.Mathematics.Random> randomGenerators;
        [Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndex] int threadId;
        public void Execute(ref AntInfo ant, ref LocalTransform antTransform, ref AntColor antColor)
        {
            var rand = randomGenerators[threadId];
            float targetSpeed = config.antSpeed;
            ant.facingAngle += rand.NextFloat(-config.randomSteering, config.randomSteering);

            float pheroSteering = PheromoneSteering(ref ant, ref antTransform, 3f);
            int wallSteering = WallSteering(ref ant, ref antTransform, 1.5f);
            ant.facingAngle += pheroSteering * config.pheromoneSteerStrength;
            ant.facingAngle += wallSteering * config.wallSteerStrength;

            targetSpeed *= 1f - (math.abs(pheroSteering) + math.abs(wallSteering)) / 3f;

            ant.speed += (targetSpeed - ant.speed) * config.antAccel;


            float3 targetPos;
            if (ant.holdingResource == false)
            {
                targetPos = config.resourcePosition;

                antColor.Value += (config.searchColor * ant.brightness - antColor.Value) * .05f;
            }
            else
            {
                targetPos = config.colonyPosition;
                antColor.Value += (config.carryColor * ant.brightness - antColor.Value) * .05f;
            }

            if (Linecast(antTransform.Position, targetPos) == false)
            {
                Color color = Color.green;
                float targetAngle = math.atan2(targetPos.y - antTransform.Position.y, targetPos.x - antTransform.Position.x);
                if (targetAngle - ant.facingAngle > Mathf.PI)
                {
                    ant.facingAngle += Mathf.PI * 2f;
                    color = Color.red;
                }
                else if (targetAngle - ant.facingAngle < -Mathf.PI)
                {
                    ant.facingAngle -= Mathf.PI * 2f;
                    color = Color.red;
                }
                else
                {
                    if (math.abs(targetAngle - ant.facingAngle) < Mathf.PI * .5f)
                        ant.facingAngle += (targetAngle - ant.facingAngle) * config.goalSteerStrength;
                }

                //Debug.DrawLine(ant.position/mapSize,targetPos/mapSize,color);
            }
            if (math.lengthsq((antTransform.Position - targetPos)) < 4f * 4f)
            {
                ant.holdingResource = !ant.holdingResource;
                ant.facingAngle += Mathf.PI;
            }

            float vx = math.cos(ant.facingAngle) * ant.speed;
            float vy = math.sin(ant.facingAngle) * ant.speed;
            float ovx = vx;
            float ovy = vy;

            if (antTransform.Position.x + vx < 0f || antTransform.Position.x + vx > config.mapSize)
            {
                vx = -vx;
            }
            else
            {
                antTransform.Position.x += vx;
            }
            if (antTransform.Position.y + vy < 0f || antTransform.Position.y + vy > config.mapSize)
            {
                vy = -vy;
            }
            else
            {
                antTransform.Position.y += vy;
            }

            float dx, dy, dist;

            CellRange nearbyObstacles = GetObstacleBucket(antTransform.Position);
            for (int j = 0; j < nearbyObstacles.length; j++)
            {
                ObstacleInfo obstacle = obstacles[j + nearbyObstacles.start].obstacleInfo;
                dx = antTransform.Position.x - obstacle.position.x;
                dy = antTransform.Position.y - obstacle.position.y;
                float sqrDist = dx * dx + dy * dy;
                if (sqrDist < config.obstacleRadius * config.obstacleRadius)
                {
                    dist = math.sqrt(sqrDist);
                    dx /= dist;
                    dy /= dist;
                    antTransform.Position.x = obstacle.position.x + dx * config.obstacleRadius;
                    antTransform.Position.y = obstacle.position.y + dy * config.obstacleRadius;

                    vx -= dx * (dx * vx + dy * vy) * 1.5f;
                    vy -= dy * (dx * vx + dy * vy) * 1.5f;
                }
            }

            float inwardOrOutward = -config.outwardStrength;
            float pushRadius = config.mapSize * .4f;
            if (ant.holdingResource)
            {
                inwardOrOutward = config.inwardStrength;
                pushRadius = config.mapSize;
            }
            dx = config.colonyPosition.x - antTransform.Position.x;
            dy = config.colonyPosition.y - antTransform.Position.y;
            dist = math.sqrt(dx * dx + dy * dy);
            inwardOrOutward *= 1f - math.clamp(dist / pushRadius, 0, 1);
            vx += dx / dist * inwardOrOutward;
            vy += dy / dist * inwardOrOutward;

            if (ovx != vx || ovy != vy)
            {
                ant.facingAngle = math.atan2(vy, vx);
            }

            // Drop pheromones
            
            float excitement = .3f;
            if (ant.holdingResource)
            {
                excitement = 1f;
            }
            excitement *= ant.speed / config.antSpeed;
            DropPheromones(antTransform.Position, excitement);

            /*Matrix4x4 matrix = GetRotationMatrix(ant.facingAngle);
            matrix.m03 = ant.position.x / mapSize;
            matrix.m13 = ant.position.y / mapSize;
            matrices[i / instancesPerBatch][i % instancesPerBatch] = matrix;*/
        }

        int PheromoneIndex(int x, int y)
        {
            return x + y * config.mapSize;
        }

        unsafe void DropPheromones(float3 position, float strength)
        {
            int x = (int)math.floor(position.x);
            int y = (int)math.floor(position.y);
            if (x < 0 || y < 0 || x >= config.mapSize || y >= config.mapSize)
            {
                return;
            }

            int index = PheromoneIndex(x, y);
            
            // CAS to write to pheromones parallelly
            float4* ptr = (float4*)pheromones.GetUnsafePtr();
            float newCurrentVal = ptr[index].x;
            while (true)
            {
                float newVal = newCurrentVal;
                newVal += (config.trailAddSpeed * strength * deltaTime) * (1f - newVal);

                if (newVal > 1f)
                {
                    newVal = 1f;
                }
                var currentValue = newCurrentVal;
                newCurrentVal = Interlocked.CompareExchange(ref ptr[index].x, newVal, currentValue);

                if(currentValue.Equals(newCurrentVal))
                    break;
            }
        }

        float PheromoneSteering(ref AntInfo ant, ref LocalTransform transform, float distance)
        {
            float output = 0;

            for (int i = -1; i <= 1; i += 2)
            {
                float angle = ant.facingAngle + i * math.PI * .25f;
                float testX = transform.Position.x + math.cos(angle) * distance;
                float testY = transform.Position.y + math.sin(angle) * distance;

                if (testX < 0 || testY < 0 || testX >= config.mapSize || testY >= config.mapSize)
                {

                }
                else
                {
                    int index = PheromoneIndex((int)testX, (int)testY);
                    float value = pheromones[index].value.x;
                    output += value * i;
                }
            }
            return math.sign(output);
        }

        int WallSteering(ref AntInfo ant, ref LocalTransform transform, float distance)
        {
            int output = 0;

            for (int i = -1; i <= 1; i += 2)
            {
                float angle = ant.facingAngle + i * math.PI * .25f;
                float testX = transform.Position.x + math.cos(angle) * distance;
                float testY = transform.Position.y + math.sin(angle) * distance;

                if (testX < 0 || testY < 0 || testX >= config.mapSize || testY >= config.mapSize)
                {

                }
                else
                {
                    int value = GetObstacleBucket(testX, testY).length;
                    if (value > 0)
                    {
                        output -= i;
                    }
                }
            }
            return output;
        }

        bool Linecast(Vector3 point1, Vector3 point2)
        {
            float dx = point2.x - point1.x;
            float dy = point2.y - point1.y;
            float dist = math.sqrt(dx * dx + dy * dy);

            int stepCount = (int)math.ceil(dist * .5f);
            for (int i = 0; i < stepCount; i++)
            {
                float t = (float)i / stepCount;
                if (GetObstacleBucket(point1.x + dx * t, point1.y + dy * t).length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        CellRange GetObstacleBucket(float3 pos)
        {
            return GetObstacleBucket(pos.x, pos.y);
        }
        CellRange GetObstacleBucket(float posX, float posY)
        {
            int x = (int)(posX / config.mapSize * config.bucketResolution);
            int y = (int)(posY / config.mapSize * config.bucketResolution);
            if (x < 0 || y < 0 || x >= config.bucketResolution || y >= config.bucketResolution)
            {
                return new CellRange(0, 0);
            }
            else
            {
                return obstacleBuckets[x * config.bucketResolution + y].range;
            }
        }
    }
}


