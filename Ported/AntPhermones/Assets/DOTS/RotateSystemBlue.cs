using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public partial struct RotateCubeSystemBlue : ISystem
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
        foreach (var (transform, speed, type) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotateCube>, RefRO<CubeTypeBlue>>())
        {
            transform.ValueRW = transform.ValueRO.RotateX(speed.ValueRO.rotateSpeed * time);
        }
    }
}
