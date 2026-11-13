using System;
using System.Buffers;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Gizmos
    {
        internal static bool GameViewEnabled;
        static bool s_Drawing;
        static readonly List<MonoBehaviour> s_Scripts = new List<MonoBehaviour>();
        static readonly HashSet<MonoBehaviour> s_Tracked = new HashSet<MonoBehaviour>(ReferenceEqualityComparer.Instance);
        static Material s_Lit, s_Wire;
        static Mesh s_Cube, s_Sphere, s_Edges, s_Line;

        internal static void Refresh()
        {
            s_Scripts.Clear(); s_Tracked.Clear();
            foreach (var go in SceneRegistry.AllGameObjects(includeInactive: true))
                foreach (var component in go.m_Components)
                    if (component is MonoBehaviour script) Track(script);
        }

        internal static void Reset()
        {
            s_Scripts.Clear(); s_Tracked.Clear(); s_Drawing = false;
            Object.DestroyImmediate(s_Cube); Object.DestroyImmediate(s_Sphere); Object.DestroyImmediate(s_Edges); Object.DestroyImmediate(s_Line);
            Object.DestroyImmediate(s_Lit); Object.DestroyImmediate(s_Wire);
            s_Cube = s_Sphere = s_Edges = s_Line = null; s_Lit = s_Wire = null;
        }

        internal static void Track(MonoBehaviour script)
        {
            var messages = ScriptMessages.For(script.GetType());
            if ((messages.OnDrawGizmos != null || messages.OnDrawGizmosSelected != null) && s_Tracked.Add(script))
                s_Scripts.Add(script);
        }

        internal static void Untrack(Object obj)
        {
            if (obj is MonoBehaviour script && s_Tracked.Remove(script)) IdentityLists.Remove(s_Scripts, script);
        }

        internal static void Render(Camera camera)
        {
            if (!GameViewEnabled) return;
            if (!Compat.RenderingMeasured) throw new NotSupportedException("Unbound: Game-view gizmos have only been measured for Unity 6000.0.71f1.");
            var scripts = ArrayPool<MonoBehaviour>.Shared.Rent(s_Scripts.Count);
            int count = s_Scripts.Count;
            s_Scripts.CopyTo(scripts);
            s_Drawing = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var script = scripts[i];
                    if (script == null || script.m_GameObject == null || script.m_GameObject.m_IsPrefabAsset || !script.m_GameObject.ActiveInHierarchyNoThrow) continue;
                    var messages = ScriptMessages.For(script.GetType());
                    Invoke(script, messages.OnDrawGizmos);
                    if (UnityEditor.Selection.Contains(script.gameObject)) Invoke(script, messages.OnDrawGizmosSelected);
                }
            }
            finally
            {
                s_Drawing = false;
                Array.Clear(scripts, 0, count);
                ArrayPool<MonoBehaviour>.Shared.Return(scripts);
            }
        }

        static void Invoke(MonoBehaviour script, Action<MonoBehaviour> callback)
        {
            if (callback == null) return;
            s_Matrix = Matrix4x4.identity; s_Color = new Color32(255,255,255,255);
            try { callback(script); }
            catch (Exception e) { Debug.LogException(e, script); }
        }

        static void Draw(Mesh mesh, Matrix4x4 model, bool wire = false, int submesh = -1)
        {
            if (!s_Drawing) OutsideCallback();
            if (mesh == null) return;
            var material = wire
                ? s_Wire ??= new Material(Shader.Find("Hidden/Editor Gizmo")) { hideFlags = HideFlags.HideAndDontSave }
                : s_Lit ??= new Material(Shader.Find("Hidden/Editor Gizmo Lit")) { hideFlags = HideFlags.HideAndDontSave };
            ulong geometry = mesh.Sync(), shader = material.Sync();
            var previous = GfxStream.Current;
            GfxStream.Camera(previous.View, previous.Projection, previous.Flags | GfxStream.CameraGizmoDraw);
            try
            {
                int end = submesh < 0 ? mesh.subMeshCount : submesh + 1;
                for (int sub = Math.Max(0, submesh); sub < end; sub++)
                    GfxStream.Draw(geometry, sub, shader, GfxStream.AllPasses, s_Matrix * model, (Color)s_Color);
            }
            finally { GfxStream.Camera(previous.View, previous.Projection, previous.Flags); }
        }

        static Mesh Cube => s_Cube ??= BuiltinMeshes.Create("Cube");
        static Mesh Sphere => s_Sphere ??= BuiltinMeshes.Icosphere();
        static Mesh EdgeCube
        {
            get
            {
                if (s_Edges != null) return s_Edges;
                var points = new Vector3[8];
                for (int i = 0; i < 8; i++) points[i] = new Vector3((i & 1) - .5f, ((i >> 1) & 1) - .5f, ((i >> 2) & 1) - .5f);
                var indices = new List<int>();
                for (int i = 0; i < 8; i++)
                    for (int bit = 1; bit <= 4; bit <<= 1)
                        if ((i & bit) == 0) { indices.Add(i); indices.Add(i | bit); }
                s_Edges = new Mesh { vertices = points, hideFlags = HideFlags.HideAndDontSave };
                s_Edges.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
                return s_Edges;
            }
        }

        static void Line(Vector3 from, Vector3 to)
        {
            if (!s_Drawing) OutsideCallback();
            if (s_Line == null)
            {
                s_Line = new Mesh { vertices = new[] { Vector3.zero, Vector3.right }, hideFlags = HideFlags.HideAndDontSave };
                s_Line.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
            }
            var model = Matrix4x4.identity;
            model.SetColumn(0, new Vector4(to.x - from.x, to.y - from.y, to.z - from.z, 0));
            model.SetColumn(3, new Vector4(from.x, from.y, from.z, 1));
            Draw(s_Line, model, true);
        }
    }
}
