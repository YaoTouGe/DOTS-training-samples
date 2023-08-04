using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public partial struct RotateCubeSystemGreen : ISystem
{
    void OnCreate(ref SystemState state)
    {
        
    }

    void OnDestroy(ref SystemState state)
    {
        
    }

    void OnUpdate(ref SystemState state)
    {
        float time = SystemAPI.Time.DeltaTime;
        foreach (var (transform, speed, type) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotateCube>, RefRO<CubeTypeGreen>>())
        {
            transform.ValueRW = transform.ValueRO.RotateY(speed.ValueRO.rotateSpeed * time);
        }
    }
}
