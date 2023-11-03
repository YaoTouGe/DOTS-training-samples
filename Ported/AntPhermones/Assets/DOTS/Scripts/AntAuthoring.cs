using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Rendering;
using Unity.Mathematics;

public class AntAuthoring : MonoBehaviour
{
    public class Baker: Baker<AntAuthoring>
    {
        public override void Bake(AntAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<AntInfo>(entity, new AntInfo()
            {
                facingAngle = UnityEngine.Random.value * Mathf.PI * 2f,
                speed = 0f,
                holdingResource = false,
                brightness = UnityEngine.Random.Range(.75f, 1.25f)
        });
            AddComponent<AntColor>(entity, new AntColor()
            {
                Value = default
            });
        }
    }
}

public struct AntInfo : IComponentData
{
    public float facingAngle;
    public float speed;
    public float brightness;
    public bool holdingResource;
}

[MaterialProperty("_BaseColor")]
public struct AntColor : IComponentData
{
    public float4 Value;
}