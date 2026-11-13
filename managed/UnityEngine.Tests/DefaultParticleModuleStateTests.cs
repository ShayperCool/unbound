using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultParticleModuleStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultParticleModuleStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void MissingOwnersReturnZeroValuesIncludingCurvesAndGradients()
        {
            var main = default(ParticleSystem.MainModule);
            Assert.Equal(0f, main.duration);
            Assert.False(main.loop);
            Assert.Equal(default(ParticleSystem.MinMaxCurve), main.startLifetime);
            Assert.Equal(default(ParticleSystem.MinMaxGradient), main.startColor);
            Assert.Equal(default(Vector3), default(ParticleSystem.ShapeModule).scale);
            Assert.Throws<NullReferenceException>(() => default(ParticleSystem.CollisionModule).maxPlaneCount);
            Assert.Equal(ParticleSystemCullingMode.PauseAndCatchup, main.cullingMode);
            Assert.Equal(ParticleSystemShapeTextureChannel.Alpha, default(ParticleSystem.ShapeModule).textureClipChannel);
            Assert.True(default(ParticleSystem.TextureSheetAnimationModule).useRandomRow);
            Assert.Equal(0, default(ParticleSystem.TextureSheetAnimationModule).spriteCount);
            Assert.Equal(default(ParticleSystem.MinMaxCurve), default(ParticleSystem.NoiseModule).strength);
            Assert.Equal(default(ParticleSystem.MinMaxGradient), default(ParticleSystem.TrailModule).colorOverTrail);
            Assert.Equal(Vector2.one, default(ParticleSystem.TrailModule).textureScale);
        }

        [Fact]
        public void CopiedHandlesWriteThroughAndDestroyedOwnersStopExposingState()
        {
            var owner = new GameObject("particle handle"); owner.SetActive(false);
            var system = owner.AddComponent<ParticleSystem>();
            var main = system.main; main.startSpeed = 13f;
            var copy = main; copy.startSpeedMultiplier = 17f;
            var shape = system.shape; shape.radius = 2.75f;
            var collisions = system.collision;
            Assert.Equal(17f, main.startSpeed.constant);
            Assert.Equal(17f, system.main.startSpeedMultiplier);
            Assert.Equal(2.75f, shape.radius);
            Assert.Equal(0f, default(ParticleSystem.MainModule).startSpeedMultiplier);
            Object.DestroyImmediate(owner);
            Assert.Equal(default(ParticleSystem.MinMaxCurve), main.startSpeed);
            Assert.Equal(0f, shape.radius);
            Assert.Throws<NullReferenceException>(() => collisions.planeCount);
        }

        [Fact]
        public void RemovedSubEmitterPropertiesThrowForAttachedAndEmptyHandles()
        {
            var owner = new GameObject("removed sub-emitter"); owner.SetActive(false);
            try
            {
                var system = owner.AddComponent<ParticleSystem>();
                foreach (var module in new[] { default(ParticleSystem.SubEmittersModule), system.subEmitters })
                    foreach (var property in typeof(ParticleSystem.SubEmittersModule).GetProperties())
                    {
                        if (property.PropertyType != typeof(ParticleSystem)) continue;
                        Assert.IsType<NotImplementedException>(Assert.Throws<System.Reflection.TargetInvocationException>(() => property.GetValue(module)).InnerException);
                        Assert.IsType<NotImplementedException>(Assert.Throws<System.Reflection.TargetInvocationException>(() => property.SetValue(module, system)).InnerException);
                    }
                Assert.Equal(0, system.subEmitters.subEmittersCount);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void DefaultModuleKeysKeepUnweightedHandleMetadataAndExplicitValuesRemainWritable()
        {
            var owner = new GameObject("particle defaults"); owner.SetActive(false);
            try
            {
                var system = owner.AddComponent<ParticleSystem>();
                foreach (var curve in new[] { system.sizeOverLifetime.x, system.sizeBySpeed.x,
                    system.lifetimeByEmitterSpeed.curve, system.noise.remap, system.textureSheetAnimation.frameOverTime })
                    foreach (var key in curve.curve.keys)
                    {
                        Assert.Equal(1f / 3f, key.inWeight); Assert.Equal(1f / 3f, key.outWeight);
                        Assert.Equal(WeightedMode.None, key.weightedMode);
                    }
                Assert.Equal(ParticleSystemEmitterVelocityMode.Rigidbody, system.main.emitterVelocityMode);
                Assert.False(system.collision.enableInteriorCollisions);
                var noise = system.noise; Assert.Equal(ParticleSystemNoiseQuality.Medium, noise.quality);
                noise.quality = ParticleSystemNoiseQuality.High; Assert.Equal(ParticleSystemNoiseQuality.High, noise.quality);
                var field = owner.AddComponent<ParticleSystemForceField>();
                Assert.Equal(1f, field.length); field.length = 3.5f; Assert.Equal(3.5f, field.length);
                var curveModule = system.sizeOverLifetime;
                var custom = new AnimationCurve(new Keyframe(0f, 2f), new Keyframe(1f, 3f));
                curveModule.x = new ParticleSystem.MinMaxCurve(1f, custom);
                Assert.Equal(0f, curveModule.x.curve.keys[0].inWeight);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Fact]
        public void InitialSpriteSlotIsReplacedAndExplicitNullSlotsRemainWritable()
        {
            var owner = new GameObject("particle sprites"); owner.SetActive(false);
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            try
            {
                var sheet = owner.AddComponent<ParticleSystem>().textureSheetAnimation;
                Assert.Equal(1, sheet.spriteCount); Assert.Null(sheet.GetSprite(0));
                Assert.Equal("sprite is null", Assert.Throws<NullReferenceException>(() => sheet.AddSprite(null)).Message);
                Assert.Equal(1, sheet.spriteCount);
                sheet.AddSprite(sprite); Assert.Equal(1, sheet.spriteCount); Assert.Same(sprite, sheet.GetSprite(0));
                sheet.AddSprite(sprite); Assert.Equal(2, sheet.spriteCount); sheet.RemoveSprite(1);
                sheet.SetSprite(0, null); Assert.Null(sheet.GetSprite(0));
                sheet.RemoveSprite(0); Assert.Equal(0, sheet.spriteCount);
                sheet.AddSprite(sprite); Assert.Equal(1, sheet.spriteCount);
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void RemovedCollisionAccessorRejectsEmptyAndPopulatedPayloads()
        {
            var property = typeof(ParticleCollisionEvent).GetProperty("collider");
            foreach (var payload in new[] { new ParticleCollisionEvent(), new ParticleCollisionEvent { m_ColliderInstanceID = 1234 } })
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => property.GetValue(payload));
                Assert.IsType<InvalidOperationException>(error.InnerException);
                Assert.Equal("collider property is deprecated. Use colliderComponent instead, which supports Collider and Collider2D components", error.InnerException.Message);
            }
        }

        [Fact]
        public void UnmeasuredReleasesKeepTheirExistingMissingOwnerBehavior()
        {
            ProjectInfo.UnityVersion = "6000.0.70f1";
            Assert.Throws<NullReferenceException>(() => default(ParticleSystem.MainModule).duration);
        }
    }
}
