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

[BurstCompile]
[UpdateAfter(typeof(SpawnSystem))]
partial struct AntMoveSystem : ISystem
{

    AntManagerConfig config;
    void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AntManagerConfig>();
    }

    [BurstCompile]
    void OnUpdate(ref SystemState state)
    {
        UpdateAnts(ref state);
    }

    void UpdateAnts(ref SystemState state)
    {
        config = SystemAPI.GetSingleton<AntManagerConfig>();
        foreach (var (antInfo, transform) in SystemAPI.Query<RefRW<AntInfo>, RefRW<LocalTransform>>())
        {
            ref AntInfo ant = ref antInfo.ValueRW;
            float targetSpeed = config.antSpeed;
            ref LocalTransform antTransform = ref transform.ValueRW;
            ant.facingAngle += UnityEngine.Random.Range(-config.randomSteering, config.randomSteering);

            float pheroSteering = PheromoneSteering(ref ant, ref antTransform, 3f);
            int wallSteering = WallSteering(ref ant, ref antTransform, 1.5f);
            ant.facingAngle += pheroSteering * config.pheromoneSteerStrength;
            ant.facingAngle += wallSteering * config.wallSteerStrength;

            targetSpeed *= 1f - (math.abs(pheroSteering) + math.abs(wallSteering)) / 3f;

            ant.speed += (targetSpeed - ant.speed) * config.antAccel;

            
            float3 targetPos;
            /* TODO: ant colors
            int index1 = i / config.instancesPerBatch;
            int index2 = i % config.instancesPerBatch;
            if (ant.holdingResource == false)
            {
                targetPos = config.resourcePosition;

                antColors[index1][index2] += ((Vector4)searchColor * ant.brightness - antColors[index1][index2]) * .05f;
            }
            else
            {
                targetPos = config.colonyPosition;
                antColors[index1][index2] += ((Vector4)carryColor * ant.brightness - antColors[index1][index2]) * .05f;
            }*/

            if (ant.holdingResource == false)
            {
                targetPos = config.resourcePosition;
            }
            else
            {
                targetPos = config.colonyPosition;
            }
            if (Linecast(transform.ValueRO.Position, targetPos) == false)
            {
                Color color = Color.green;
                float targetAngle = math.atan2(targetPos.y - transform.ValueRO.Position.y, targetPos.x - transform.ValueRO.Position.x);
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
            if ( math.lengthsq((transform.ValueRO.Position - targetPos)) < 4f * 4f)
            {
                ant.holdingResource = !ant.holdingResource;
                ant.facingAngle += Mathf.PI;
            }

            float vx = math.cos(ant.facingAngle) * ant.speed;
            float vy = math.sin(ant.facingAngle) * ant.speed;
            float ovx = vx;
            float ovy = vy;

            if (transform.ValueRO.Position.x + vx < 0f || transform.ValueRO.Position.x + vx > config.mapSize)
            {
                vx = -vx;
            }
            else
            {
                transform.ValueRW.Position.x += vx;
            }
            if (transform.ValueRO.Position.y + vy < 0f || transform.ValueRO.Position.y + vy > config.mapSize)
            {
                vy = -vy;
            }
            else
            {
                transform.ValueRW.Position.y += vy;
            }

            float dx, dy, dist;

            CellRange nearbyObstacles = GetObstacleBucket(antTransform.Position);
            for (int j = 0; j < nearbyObstacles.length; j++)
            {
                ObstacleInfo obstacle = AntManagerConfig.obstacles[j+nearbyObstacles.start];
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

            //if (ant.holdingResource == false) {
            //float excitement = 1f-Mathf.Clamp01((targetPos - ant.position).magnitude / (mapSize * 1.2f));
            float excitement = .3f;
            if (ant.holdingResource)
            {
                excitement = 1f;
            }
            excitement *= ant.speed / config.antSpeed;
            DropPheromones(antTransform.Position, excitement);
            //}

            /*Matrix4x4 matrix = GetRotationMatrix(ant.facingAngle);
            matrix.m03 = ant.position.x / mapSize;
            matrix.m13 = ant.position.y / mapSize;
            matrices[i / instancesPerBatch][i % instancesPerBatch] = matrix;*/
        }

        for (int x = 0; x < config.mapSize; x++)
        {
            for (int y = 0; y < config.mapSize; y++)
            {
                int index = PheromoneIndex(x, y);
                Vector4 oldValue = AntManagerConfig.pheromones[index];
                oldValue.x *= config.trailDecay;
                AntManagerConfig.pheromones[index] =  oldValue;
            }
        }

        /* TODO: Display pheromoneTexture
        pheromoneTexture.SetPixels(pheromones);
        pheromoneTexture.Apply();

        for (int i = 0; i < matProps.Length; i++)
        {
            matProps[i].SetVectorArray("_Color", antColors[i]);
        }*/
    }

    int PheromoneIndex(int x, int y)
    {
        return x + y * config.mapSize;
    }

    void DropPheromones(float3 position, float strength)
    {
        int x = (int)math.floor(position.x);
        int y = (int)math.floor(position.y);
        if (x < 0 || y < 0 || x >= config.mapSize || y >= config.mapSize)
        {
            return;
        }

        int index = PheromoneIndex(x, y);
        var oldVal = AntManagerConfig.pheromones[index];
        oldVal.x += (config.trailAddSpeed * strength * Time.fixedDeltaTime) * (1f - oldVal.x);
        
        if (oldVal.x > 1f)
        {
            oldVal.x = 1f;
        }
        AntManagerConfig.pheromones[index] = oldVal;
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
                float value = AntManagerConfig.pheromones[index].x;
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
            return AntManagerConfig.obstacleBuckets[x*config.bucketResolution + y];
        }
    }
}

[BurstCompile]
public partial struct MoveJob: IJobEntity
{
    public void Execute()
    {

    }
}