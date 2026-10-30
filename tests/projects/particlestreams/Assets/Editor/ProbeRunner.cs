using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/particlestreams -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_STREAMS_OUT</c> set: a new empty scene in Play mode, where
/// <see cref="ParticleStreamsProbe"/> records the simulations and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var prefab=new GameObject("Streams");var ps=prefab.AddComponent<ParticleSystem>();var main=ps.main;main.playOnAwake=false;ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer=prefab.GetComponent<ParticleSystemRenderer>();
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Custom1XYZW,ParticleSystemVertexStream.Velocity,ParticleSystemVertexStream.Custom2XY});
        renderer.SetActiveTrailVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.NextTrailCenter,ParticleSystemVertexStream.PreviousTrailCenter,ParticleSystemVertexStream.PercentageAlongTrail,ParticleSystemVertexStream.TrailWidth});
        PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Resources/Streams.prefab");Object.DestroyImmediate(prefab);
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
