using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

partial struct AntMoveSystem : ISystem
{
    void OnUpdate(ref SystemState state)
    {

    }

    void UpdateAnts(ref SystemState state)
    {
        foreach(var (antInfo, transform) in SystemAPI.Query<RefRW<AntInfo>, RefRW<LocalTransform>>())
        {
            ref AntInfo ant = ref antInfo.ValueRW;
            float targetSpeed = AntManagerConfig.antSpeed;
            ref LocalTransform antTransform = ref transform.ValueRW;
            ant.facingAngle += UnityEngine.Random.Range(-AntManagerConfig.randomSteering, AntManagerConfig.randomSteering);

            float pheroSteering = PheromoneSteering(ref ant, ref antTransform, 3f);
            int wallSteering = WallSteering(ref ant, ref antTransform, 1.5f);
            ant.facingAngle += pheroSteering * AntManagerConfig.pheromoneSteerStrength;
            ant.facingAngle += wallSteering * AntManagerConfig.wallSteerStrength;

            targetSpeed *= 1f - (Mathf.Abs(pheroSteering) + Mathf.Abs(wallSteering)) / 3f;

            ant.speed += (targetSpeed - ant.speed) * AntManagerConfig.antAccel;

            
            float3 targetPos;
            /* TODO: ant colors
            int index1 = i / AntManagerConfig.instancesPerBatch;
            int index2 = i % AntManagerConfig.instancesPerBatch;
            if (ant.holdingResource == false)
            {
                targetPos = AntManagerConfig.resourcePosition;

                antColors[index1][index2] += ((Vector4)searchColor * ant.brightness - antColors[index1][index2]) * .05f;
            }
            else
            {
                targetPos = AntManagerConfig.colonyPosition;
                antColors[index1][index2] += ((Vector4)carryColor * ant.brightness - antColors[index1][index2]) * .05f;
            }*/

            if (ant.holdingResource == false)
            {
                targetPos = AntManagerConfig.resourcePosition;
            }
            else
            {
                targetPos = AntManagerConfig.colonyPosition;
            }
            if (Linecast(transform.ValueRO.Position, targetPos) == false)
            {
                Color color = Color.green;
                float targetAngle = Mathf.Atan2(targetPos.y - transform.ValueRO.Position.y, targetPos.x - transform.ValueRO.Position.x);
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
                    if (Mathf.Abs(targetAngle - ant.facingAngle) < Mathf.PI * .5f)
                        ant.facingAngle += (targetAngle - ant.facingAngle) * AntManagerConfig.goalSteerStrength;
                }

                //Debug.DrawLine(ant.position/mapSize,targetPos/mapSize,color);
            }
            if ( math.lengthsq((transform.ValueRO.Position - targetPos)) < 4f * 4f)
            {
                ant.holdingResource = !ant.holdingResource;
                ant.facingAngle += Mathf.PI;
            }

            float vx = Mathf.Cos(ant.facingAngle) * ant.speed;
            float vy = Mathf.Sin(ant.facingAngle) * ant.speed;
            float ovx = vx;
            float ovy = vy;

            if (transform.ValueRO.Position.x + vx < 0f || transform.ValueRO.Position.x + vx > AntManagerConfig.mapSize)
            {
                vx = -vx;
            }
            else
            {
                transform.ValueRW.Position.x += vx;
            }
            if (transform.ValueRO.Position.y + vy < 0f || transform.ValueRO.Position.y + vy > AntManagerConfig.mapSize)
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
                if (sqrDist < AntManagerConfig.obstacleRadius * AntManagerConfig.obstacleRadius)
                {
                    dist = Mathf.Sqrt(sqrDist);
                    dx /= dist;
                    dy /= dist;
                    antTransform.Position.x = obstacle.position.x + dx * AntManagerConfig.obstacleRadius;
                    antTransform.Position.y = obstacle.position.y + dy * AntManagerConfig.obstacleRadius;

                    vx -= dx * (dx * vx + dy * vy) * 1.5f;
                    vy -= dy * (dx * vx + dy * vy) * 1.5f;
                }
            }

            float inwardOrOutward = -AntManagerConfig.outwardStrength;
            float pushRadius = AntManagerConfig.mapSize * .4f;
            if (ant.holdingResource)
            {
                inwardOrOutward = AntManagerConfig.inwardStrength;
                pushRadius = AntManagerConfig.mapSize;
            }
            dx = AntManagerConfig.colonyPosition.x - antTransform.Position.x;
            dy = AntManagerConfig.colonyPosition.y - antTransform.Position.y;
            dist = Mathf.Sqrt(dx * dx + dy * dy);
            inwardOrOutward *= 1f - Mathf.Clamp01(dist / pushRadius);
            vx += dx / dist * inwardOrOutward;
            vy += dy / dist * inwardOrOutward;

            if (ovx != vx || ovy != vy)
            {
                ant.facingAngle = Mathf.Atan2(vy, vx);
            }

            //if (ant.holdingResource == false) {
            //float excitement = 1f-Mathf.Clamp01((targetPos - ant.position).magnitude / (mapSize * 1.2f));
            float excitement = .3f;
            if (ant.holdingResource)
            {
                excitement = 1f;
            }
            excitement *= ant.speed / AntManagerConfig.antSpeed;
            DropPheromones(antTransform.Position, excitement);
            //}

            /*Matrix4x4 matrix = GetRotationMatrix(ant.facingAngle);
            matrix.m03 = ant.position.x / mapSize;
            matrix.m13 = ant.position.y / mapSize;
            matrices[i / instancesPerBatch][i % instancesPerBatch] = matrix;*/
        }

        for (int x = 0; x < AntManagerConfig.mapSize; x++)
        {
            for (int y = 0; y < AntManagerConfig.mapSize; y++)
            {
                int index = PheromoneIndex(x, y);
                Vector4 oldValue = AntManagerConfig.pheromones[index];
                oldValue.x *= AntManagerConfig.trailDecay;
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
        return x + y * AntManagerConfig.mapSize;
    }

    void DropPheromones(float3 position, float strength)
    {
        int x = Mathf.FloorToInt(position.x);
        int y = Mathf.FloorToInt(position.y);
        if (x < 0 || y < 0 || x >= AntManagerConfig.mapSize || y >= AntManagerConfig.mapSize)
        {
            return;
        }

        int index = PheromoneIndex(x, y);
        var oldVal = AntManagerConfig.pheromones[index];
        oldVal.x += (AntManagerConfig.trailAddSpeed * strength * Time.fixedDeltaTime) * (1f - oldVal.x);
        
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
            float angle = ant.facingAngle + i * Mathf.PI * .25f;
            float testX = transform.Position.x + Mathf.Cos(angle) * distance;
            float testY = transform.Position.y + Mathf.Sin(angle) * distance;

            if (testX < 0 || testY < 0 || testX >= AntManagerConfig.mapSize || testY >= AntManagerConfig.mapSize)
            {

            }
            else
            {
                int index = PheromoneIndex((int)testX, (int)testY);
                float value = AntManagerConfig.pheromones[index].x;
                output += value * i;
            }
        }
        return Mathf.Sign(output);
    }

    int WallSteering(ref AntInfo ant, ref LocalTransform transform, float distance)
    {
        int output = 0;

        for (int i = -1; i <= 1; i += 2)
        {
            float angle = ant.facingAngle + i * Mathf.PI * .25f;
            float testX = transform.Position.x + Mathf.Cos(angle) * distance;
            float testY = transform.Position.y + Mathf.Sin(angle) * distance;

            if (testX < 0 || testY < 0 || testX >= AntManagerConfig.mapSize || testY >= AntManagerConfig.mapSize)
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
        float dist = Mathf.Sqrt(dx * dx + dy * dy);

        int stepCount = Mathf.CeilToInt(dist * .5f);
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
        int x = (int)(posX / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution);
        int y = (int)(posY / AntManagerConfig.mapSize * AntManagerConfig.bucketResolution);
        if (x < 0 || y < 0 || x >= AntManagerConfig.bucketResolution || y >= AntManagerConfig.bucketResolution)
        {
            return new CellRange(0, 0);
        }
        else
        {
            return AntManagerConfig.obstacleBuckets[x*AntManagerConfig.bucketResolution + y];
        }
    }
}
