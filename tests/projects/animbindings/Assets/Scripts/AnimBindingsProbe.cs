using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, the bindings AnimationUtility.GetAnimatableBindings offers for each engine
/// component type: on a fresh GameObject with the component added, the float, discrete and object
/// reference bindings of that type, in the Editor's order; UNADDABLE when AddComponent gives none,
/// NONE when it offers no binding of the type, missing when the type is not loaded.
/// </summary>
static class AnimBindingsProbe
{
    static readonly string[] s_Types =
    {
            "UnityEngine.AI.NavMeshAgent",
            "UnityEngine.AI.NavMeshObstacle",
            "UnityEngine.AI.OffMeshLink",
            "UnityEngine.AnchoredJoint2D",
            "UnityEngine.Animation",
            "UnityEngine.Animations.AimConstraint",
            "UnityEngine.Animations.LookAtConstraint",
            "UnityEngine.Animations.ParentConstraint",
            "UnityEngine.Animations.PositionConstraint",
            "UnityEngine.Animations.RotationConstraint",
            "UnityEngine.Animations.ScaleConstraint",
            "UnityEngine.Animator",
            "UnityEngine.AreaEffector2D",
            "UnityEngine.ArticulationBody",
            "UnityEngine.AudioBehaviour",
            "UnityEngine.AudioChorusFilter",
            "UnityEngine.AudioDistortionFilter",
            "UnityEngine.AudioEchoFilter",
            "UnityEngine.AudioHighPassFilter",
            "UnityEngine.AudioListener",
            "UnityEngine.AudioLowPassFilter",
            "UnityEngine.AudioReverbFilter",
            "UnityEngine.AudioReverbZone",
            "UnityEngine.AudioSource",
            "UnityEngine.Behaviour",
            "UnityEngine.BillboardRenderer",
            "UnityEngine.BoxCollider",
            "UnityEngine.BoxCollider2D",
            "UnityEngine.BuoyancyEffector2D",
            "UnityEngine.Camera",
            "UnityEngine.Canvas",
            "UnityEngine.CanvasGroup",
            "UnityEngine.CanvasRenderer",
            "UnityEngine.CapsuleCollider",
            "UnityEngine.CapsuleCollider2D",
            "UnityEngine.CharacterController",
            "UnityEngine.CharacterJoint",
            "UnityEngine.CircleCollider2D",
            "UnityEngine.Cloth",
            "UnityEngine.Collider",
            "UnityEngine.Collider2D",
            "UnityEngine.Component",
            "UnityEngine.CompositeCollider2D",
            "UnityEngine.ConfigurableJoint",
            "UnityEngine.ConstantForce",
            "UnityEngine.ConstantForce2D",
            "UnityEngine.CustomCollider2D",
            "UnityEngine.DistanceJoint2D",
            "UnityEngine.EdgeCollider2D",
            "UnityEngine.Effector2D",
            "UnityEngine.FixedJoint",
            "UnityEngine.FixedJoint2D",
            "UnityEngine.FlareLayer",
            "UnityEngine.FrictionJoint2D",
            "UnityEngine.Grid",
            "UnityEngine.GridLayout",
            "UnityEngine.HingeJoint",
            "UnityEngine.HingeJoint2D",
            "UnityEngine.Joint",
            "UnityEngine.Joint2D",
            "UnityEngine.LODGroup",
            "UnityEngine.LensFlare",
            "UnityEngine.Light",
            "UnityEngine.LightProbeGroup",
            "UnityEngine.LightProbeProxyVolume",
            "UnityEngine.LineRenderer",
            "UnityEngine.MeshCollider",
            "UnityEngine.MeshFilter",
            "UnityEngine.MeshRenderer",
            "UnityEngine.OcclusionArea",
            "UnityEngine.OcclusionPortal",
            "UnityEngine.ParticleSystem",
            "UnityEngine.ParticleSystemForceField",
            "UnityEngine.ParticleSystemRenderer",
            "UnityEngine.PhysicsUpdateBehaviour2D",
            "UnityEngine.PlatformEffector2D",
            "UnityEngine.Playables.PlayableDirector",
            "UnityEngine.PointEffector2D",
            "UnityEngine.PolygonCollider2D",
            "UnityEngine.Projector",
            "UnityEngine.RectTransform",
            "UnityEngine.ReflectionProbe",
            "UnityEngine.RelativeJoint2D",
            "UnityEngine.Renderer",
            "UnityEngine.Rendering.SortingGroup",
            "UnityEngine.Rigidbody",
            "UnityEngine.Rigidbody2D",
            "UnityEngine.SkinnedMeshRenderer",
            "UnityEngine.Skybox",
            "UnityEngine.SliderJoint2D",
            "UnityEngine.SphereCollider",
            "UnityEngine.SpringJoint",
            "UnityEngine.SpringJoint2D",
            "UnityEngine.SpriteMask",
            "UnityEngine.SpriteRenderer",
            "UnityEngine.StreamingController",
            "UnityEngine.SurfaceEffector2D",
            "UnityEngine.TargetJoint2D",
            "UnityEngine.Terrain",
            "UnityEngine.TerrainCollider",
            "UnityEngine.TextMesh",
            "UnityEngine.Tilemaps.Tilemap",
            "UnityEngine.Tilemaps.TilemapCollider2D",
            "UnityEngine.Tilemaps.TilemapRenderer",
            "UnityEngine.TrailRenderer",
            "UnityEngine.Transform",
            "UnityEngine.Tree",
            "UnityEngine.U2D.SpriteShapeRenderer",
            "UnityEngine.UIElements.UIRenderer",
            "UnityEngine.VFX.VFXRenderer",
            "UnityEngine.VFX.VisualEffect",
            "UnityEngine.Video.VideoPlayer",
            "UnityEngine.WheelCollider",
            "UnityEngine.WheelJoint2D",
            "UnityEngine.WindZone"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("ANIM_BINDINGS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var name in s_Types)
        {
            var type = assemblies.Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            if (type == null) { Put(name, "missing"); continue; }
            var go = new GameObject("probe");
            try
            {
                Component component = null;
                try { component = go.AddComponent(type); } catch (Exception) { }
                if (component == null) { Put(name, "UNADDABLE"); continue; }
                var bindings = AnimationUtility.GetAnimatableBindings(go, go).Where(b => b.type == type).ToList();
                if (bindings.Count == 0) { Put(name, "NONE"); continue; }
                string List(IEnumerable<EditorCurveBinding> list) => string.Join(",", list.Select(b => b.propertyName));
                var floats = bindings.Where(b => !b.isDiscreteCurve && !b.isPPtrCurve).ToList();
                var discrete = bindings.Where(b => b.isDiscreteCurve && !b.isPPtrCurve).ToList();
                var pptr = bindings.Where(b => b.isPPtrCurve).ToList();
                Put(name, $"float({floats.Count})={List(floats)}|discrete({discrete.Count})={List(discrete)}|pptr({pptr.Count})={List(pptr)}");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
