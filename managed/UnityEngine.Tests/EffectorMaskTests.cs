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

        /// <summary>A circle of layer <paramref name="layer"/>, radius 0.5.</summary>
        Rigidbody2D Circle(string name, Vector2 at, int layer, float gravity)
        {
            var go = Make(name, at, layer);
            go.AddComponent<CircleCollider2D>().radius = 0.5f;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = gravity;
            return body;
        }

        /// <summary>Force layers, as the Unity 6000.0.71f1 Editor applies them
        /// (tests/projects/forcelayers2d): a circle that receives no force from the ground's layer
        /// falls through it, touching it as it goes; one the ground sends no force to likewise.</summary>
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        public void ABodyThatTakesNoForceFromTheGroundFallsThroughIt(bool circleReceivesNot, bool groundSendsNot, bool fallsThrough)
        {
            var at = new Vector2(0f, 3300f + (circleReceivesNot ? 20f : 0f) + (groundSendsNot ? 40f : 0f));
            var ground = Make("Ground", at, EffectorLayer).AddComponent<BoxCollider2D>();
            ground.size = new Vector2(4f, 1f);
            var circle = Circle("Circle", at + new Vector2(0f, 1.2f), LayerA, 1f);
            if (circleReceivesNot) circle.GetComponent<Collider2D>().forceReceiveLayers = ~(1 << EffectorLayer);
            if (groundSendsNot) ground.forceSendLayers = ~(1 << LayerA);
            for (int i = 0; i < 30; i++) Physics2D.Simulate(Dt);
            // The Editor at step 30: resting at 1.0 above the ground's center, or fallen to -0.62.
            Assert.Equal(fallsThrough ? -0.62f : 1f, circle.position.y - at.y, 2);
            Assert.True(circle.GetComponent<Collider2D>().IsTouching(ground));
        }

        /// <summary>One circle at 3 m/s hits another at rest: one that takes no force from the
        /// other is immovable for it, so the other stops against it (B takes none) or carries it
        /// along at its own speed (A takes none); taking none either way, they pass through.</summary>
        [Theory]
        [InlineData("all", 1.5f, 1.5f)]
        [InlineData("aSendsNotB", 0f, 0f)]
        [InlineData("bSendsNotA", 3f, 3f)]
        public void ABodyThatTakesNoForceFromAnotherIsImmovableForIt(string variant, float vA, float vB)
        {
            var at = new Vector2(0f, 3500f + variant.Length * 10f);
            var a = Circle("A", at, LayerA, 0f);
            var b = Circle("B", at + new Vector2(1.5f, 0f), LayerB, 0f);
            a.linearVelocity = new Vector2(3f, 0f);
            if (variant == "aSendsNotB") a.GetComponent<Collider2D>().forceSendLayers = ~(1 << LayerB);
            if (variant == "bSendsNotA") b.GetComponent<Collider2D>().forceSendLayers = ~(1 << LayerA);
            for (int i = 0; i < 50; i++) Physics2D.Simulate(Dt);
            Assert.Equal(vA, a.linearVelocity.x, 1);
            Assert.Equal(vB, b.linearVelocity.x, 1);
        }
    }
}
