using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class RotateCubeAuthoring : MonoBehaviour
{
    public float rotateSpeed = 100f;

    public enum CubeType
    {
        Red,
        Blue,
        Green
    }

    public CubeType type = CubeType.Red;
    
    public class Baker: Baker<RotateCubeAuthoring>
    {
        public override void Bake(RotateCubeAuthoring authoring)
        {
            Entity e = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(e, new RotateCube() { rotateSpeed = authoring.rotateSpeed});
            switch (authoring.type)
            {
                case CubeType.Red:
                    AddComponent(e, new CubeTypeRed());
                    break;
                case CubeType.Blue:
                    AddComponent(e, new CubeTypeBlue());
                    break;
                case CubeType.Green:
                    AddComponent(e, new CubeTypeGreen());
                    break;
            }
        }
    }
}

public struct RotateCube : IComponentData
{
    public float rotateSpeed;
    
}

public struct CubeTypeRed : IComponentData
{
}

public struct CubeTypeGreen : IComponentData
{
}

public struct CubeTypeBlue : IComponentData
{
}
