using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[UpdateAfter(typeof(AntMoveSystem))]
public partial class UpdatePheromoneImageSystem : SystemBase
{
    bool textureCreated;
    Texture2D pheromoneTexture;
    protected override void OnCreate()
    {
        RequireForUpdate<AntManagerConfig>();
        textureCreated = false;
    }

    protected override void OnUpdate()
    {
        if (!textureCreated)
        {
            var antMgrCfg = SystemAPI.GetSingleton<AntManagerConfig>();
            pheromoneTexture = new Texture2D(antMgrCfg.mapSize, antMgrCfg.mapSize, TextureFormat.RGBAFloat, false);
            PheromoneRawImage.Image.texture = pheromoneTexture;
            textureCreated = true;
        }
        

        var pheromoneData = SystemAPI.GetSingletonBuffer<PheromoneData>();
        pheromoneTexture.SetPixelData(pheromoneData.AsNativeArray(), 0);
        pheromoneTexture.Apply();
    }

    protected override void OnDestroy()
    {
        if (textureCreated)
            Object.Destroy(pheromoneTexture);
    }
}
