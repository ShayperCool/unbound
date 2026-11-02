using System;
using UnityEngine;

/// <summary>Scene data: LazyLoadReference fields of several kinds and a serialized Pose.</summary>
public class LazyHolder : MonoBehaviour
{
    public LazyLoadReference<Material> mat;
    public LazyLoadReference<LazyData> data;
    public LazyLoadReference<GameObject> prefab;
    public LazyLoadReference<Material> none;
    public LazyLoadReference<Material> broken;
    public LazyLoadReference<Texture2D> wrongType;
    public LazyLoadReference<Material>[] mats;
    public LazyLoadReference<GameObject> sceneRef;
    public Pose pose;
    public Pose[] poses;
}

[Serializable]
public class LazyJson
{
    public int x = 3;
    public LazyLoadReference<Material> mat;
}
