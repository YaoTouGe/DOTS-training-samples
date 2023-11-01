using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class AntAuthoring : MonoBehaviour
{
    public float facingAngle;
    public float speed;
    public float brightness;
    public bool holdingResource;

    public void Awake()
    {
        // Position will be set in spawn system later
        // position = pos;
        facingAngle = Random.value * Mathf.PI * 2f;
        speed = 0f;
        holdingResource = false;
        brightness = Random.Range(.75f, 1.25f);
    }

    public class Baker: Baker<AntAuthoring>
    {
        public override void Bake(AntAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<AntInfo>(entity, new AntInfo()
            {
                facingAngle = authoring.facingAngle,
                speed = authoring.speed,
                brightness = authoring.brightness,
                holdingResource = authoring.holdingResource
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
