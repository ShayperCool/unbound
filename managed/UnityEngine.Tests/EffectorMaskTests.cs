using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An effector's collider mask against the colliders' layer overrides (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/cookingforcesvideo effector.*): a mask that leaves a
    /// body's layer out keeps the effector off it even when the body's collider includes the
    /// effector's layer; a body whose layer the mask has is pushed (control).
    /// </summary>
    public class EffectorMaskTests : IDisposable
    {
        const float Dt = 0.02f;
        const int EffectorLayer = 8, LayerA = 9, LayerB = 10;
        readonly List<GameObject> m_Made = new List<GameObject>();

        public EffectorMaskTests()
        {
            Physics2D.autoSyncTransforms = true;
            Physics2D.gravity = new Vector2(0f, -9.81f);
            for (int i = 0; i < 32; i++) Physics2D.SetLayerCollisionMask(i, -1);
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            Physics2D.Simulate(Dt);
        }

        GameObject Make(string name, Vector2 position, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            m_Made.Add(go);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            return go;
        }

        Rigidbody2D Body(string name, Vector2 position, int layer)
        {
            var go = Make(name, position, layer);
            go.AddComponent<CircleCollider2D>().radius = 0.25f;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            return body;
        }

        [Fact]
        public void AMaskLeavingALayerOutWinsOverTheBodysIncludeLayers()
        {
            var at = new Vector2(0f, 3000f);
            var area = Make("Area", at, EffectorLayer);
            var box = area.AddComponent<BoxCollider2D>();
            box.size = new Vector2(4f, 4f);
            box.isTrigger = true;
            box.usedByEffector = true;
            var effector = area.AddComponent<AreaEffector2D>();
            effector.forceAngle = 0f;
            effector.forceMagnitude = 10f;
            effector.useColliderMask = true;
            effector.colliderMask = 1 << LayerA;
            var a = Body("A", at + new Vector2(-0.5f, -1f), LayerA);
            var b = Body("B", at + new Vector2(0.5f, 1f), LayerB);
            b.GetComponent<Collider2D>().includeLayers = 1 << EffectorLayer;
            // The same for a body whose Rigidbody2D includes the effector's layer (the rule's
            // other include layers).
            var c = Body("C", at + new Vector2(0.5f, -1f), LayerB);
            c.includeLayers = 1 << EffectorLayer;
            for (int i = 0; i < 10; i++) Physics2D.Simulate(Dt);
            Assert.True(a.linearVelocity.x > 0.1f);
            Assert.Equal(0f, b.linearVelocity.x);
            Assert.Equal(0f, c.linearVelocity.x);
        }

        const string ForceLayers = "Collider2D.forceSendLayers / forceReceiveLayers";

        static int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == ForceLayers).Value;

        /// <summary>Force layers that leave layers out are kept but not applied to contacts: setting
        /// or reading them is reported; Everything (the default) is not.</summary>
        [Fact]
        public void ForceLayersLeavingLayersOutAreReported()
        {
            var collider = Make("Forces", new Vector2(0f, 3100f), LayerA).AddComponent<CircleCollider2D>();
            int before = Hits();
            collider.forceReceiveLayers = -1;
            collider.forceSendLayers = -1;
            Assert.Equal(before, Hits());
            collider.forceReceiveLayers = ~(1 << EffectorLayer);
            Assert.Equal(before + 1, Hits());
            Assert.Equal(~(1 << EffectorLayer), collider.forceReceiveLayers.value);

            var read = Make("Read", new Vector2(0f, 3200f), LayerA).AddComponent<CircleCollider2D>();
            Unbound.Runtime.ObjectGraphBuilder.ReadComponent(read, TestYaml.DocumentBody(
                "CircleCollider2D:\n  m_ForceSendLayers:\n    serializedVersion: 2\n    m_Bits: 4294967295\n  m_ForceReceiveLayers:\n    serializedVersion: 2\n    m_Bits: 4294967295\n"), new Unbound.Runtime.LoadedFile());
            Assert.Equal(before + 1, Hits());
            Unbound.Runtime.ObjectGraphBuilder.ReadComponent(read, TestYaml.DocumentBody(
                "CircleCollider2D:\n  m_ForceSendLayers:\n    serializedVersion: 2\n    m_Bits: 4294966271\n"), new Unbound.Runtime.LoadedFile());
            Assert.Equal(before + 2, Hits());
            Assert.Equal(~(1 << LayerB), read.forceSendLayers.value);
        }
    }
}
