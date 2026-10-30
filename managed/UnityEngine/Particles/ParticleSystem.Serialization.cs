using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    // Reading the serialized ParticleSystem (class 198) and ParticleSystemRenderer (class 199), and
    // copying them for Instantiate. Field names are those of Unity's YAML; older layouts are
    // accepted where they differ (bursts with min/max counts, MinMaxCurves without minScalar,
    // `randomRow`, `useRigidbodyForVelocity`, numbered collision planes and trigger shapes).
    public sealed partial class ParticleSystem
    {
        static T Resolve<T>(IReferenceResolver resolver, YNode node) where T : Object
        {
            if (node == null || resolver == null) return null;
            var r = node.AsRef();
            return r.IsNull ? null : resolver.Resolve(r, typeof(T)) as T;
        }

        static MinMaxCurve Curve(YNode module, string key, float fallback) => MinMaxCurve.Read(module?.Get(key), fallback);

        static MinMaxCurve Curve(YNode module, string key, MinMaxCurve fallback)
        {
            var node = module?.Get(key);
            return node != null && node.Kind == YKind.Map ? MinMaxCurve.Read(node, fallback.constant) : fallback;
        }

        static int Mask(YNode node, int fallback) =>
            node == null ? fallback : node.Kind == YKind.Map ? unchecked((int)node.Long("m_Bits", fallback)) : node.AsInt(fallback);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var M = m_Main;
            M.duration = Mathf.Max(0.05f, body.Float("lengthInSec", 5f));
            M.simulationSpeed = body.Float("simulationSpeed", 1f);
            M.stopAction = (ParticleSystemStopAction)body.Int("stopAction");
            M.cullingMode = (ParticleSystemCullingMode)body.Int("cullingMode");
            M.ringBufferMode = (ParticleSystemRingBufferMode)body.Int("ringBufferMode");
            if (body.Get("ringBufferLoopRange") != null) M.ringBufferLoopRange = body.Vector2("ringBufferLoopRange");
            M.emitterVelocityMode = body.Get("emitterVelocityMode") != null
                ? (ParticleSystemEmitterVelocityMode)body.Int("emitterVelocityMode")
                : body.Bool("useRigidbodyForVelocity", true) ? ParticleSystemEmitterVelocityMode.Rigidbody : ParticleSystemEmitterVelocityMode.Transform;
            M.loop = body.Bool("looping", true);
            M.prewarm = body.Bool("prewarm");
            M.playOnAwake = body.Bool("playOnAwake", true);
            M.useUnscaledTime = body.Bool("useUnscaledTime");
            M.autoRandomSeed = body.Bool("autoRandomSeed", true);
            M.randomSeed = unchecked((uint)body.Long("randomSeed"));
            M.startDelay = Curve(body, "startDelay", 0f);
            M.simulationSpace = (ParticleSystemSimulationSpace)body.Int("moveWithTransform");
            M.customSimulationSpace = Resolve<Transform>(resolver, body.Get("moveWithCustomTransform"));
            M.scalingMode = (ParticleSystemScalingMode)body.Int("scalingMode", 1);

            if (body.Get("InitialModule") is YNode I)
            {
                M.startLifetime = Curve(I, "startLifetime", 5f);
                M.startSpeed = Curve(I, "startSpeed", 5f);
                M.startColor = MinMaxGradient.Read(I.Get("startColor"));
                M.startSizeX = Curve(I, "startSize", 1f);
                M.startSizeY = Curve(I, "startSizeY", 1f);
                M.startSizeZ = Curve(I, "startSizeZ", 1f);
                M.startRotationX = Curve(I, "startRotationX", 0f);
                M.startRotationY = Curve(I, "startRotationY", 0f);
                M.startRotationZ = Curve(I, "startRotation", 0f);
                M.flipRotation = I.Float("randomizeRotationDirection");
                M.maxParticles = Math.Max(0, I.Int("maxNumParticles", 1000));
                M.customEmitterVelocity = I.Vector3("customEmitterVelocity");
                M.startSize3D = I.Bool("size3D");
                M.startRotation3D = I.Bool("rotation3D");
                M.gravityModifier = Curve(I, "gravityModifier", 0f);
                M.gravitySource = (ParticleSystemGravitySource)I.Int("gravitySource");
            }

            if (body.Get("ShapeModule") is YNode S) ReadShape(S, resolver);

            if (body.Get("EmissionModule") is YNode E)
            {
                var D = m_Emission;
                D.enabled = E.Bool("enabled", true);
                D.rateOverTime = E.Get("rateOverTime") != null ? Curve(E, "rateOverTime", 10f) : Curve(E, "rate", 10f);
                D.rateOverDistance = Curve(E, "rateOverDistance", 0f);
                D.bursts.Clear();
                foreach (var b in E.Get("m_Bursts")?.Seq() ?? Array.Empty<YNode>())
                {
                    var burst = new Burst { m_Time = b.Float("time") };
                    burst.m_Count = b.Get("countCurve") is YNode count && count.Kind == YKind.Map
                        ? MinMaxCurve.Read(count, 30f)
                        : b.Get("minCount") != null && b.Int("minCount") != b.Int("maxCount")
                            ? new MinMaxCurve(b.Int("minCount"), b.Int("maxCount"))
                            : new MinMaxCurve(b.Float("maxCount", b.Float("minCount", 30f)));
                    burst.m_RepeatCount = b.Int("cycleCount", 1) - 1;
                    burst.m_RepeatInterval = b.Float("repeatInterval", 0.01f);
                    burst.m_InvProbability = 1f - Mathf.Clamp01(b.Float("probability", 1f));
                    D.bursts.Add(burst);
                }
                // Data from before m_Bursts was a list: `m_BurstCount` with `time0..3`, `cnt0..3`.
                if (D.bursts.Count == 0 && E.Get("cnt0") != null)
                {
                    int n = Mathf.Clamp(E.Int("m_BurstCount"), 0, 4);
                    for (int i = 0; i < n; i++)
                        D.bursts.Add(new Burst(E.Float($"time{i}"), (short)E.Int($"cnt{i}")));
                }
                m_BurstCycles = new int[D.bursts.Count];
            }

            if (body.Get("SizeModule") is YNode Z)
            {
                m_Size.enabled = Z.Bool("enabled");
                m_Size.x = Curve(Z, "curve", 1f);
                m_Size.y = Curve(Z, "y", 1f);
                m_Size.z = Curve(Z, "z", 1f);
                m_Size.separateAxes = Z.Bool("separateAxes");
            }

            if (body.Get("RotationModule") is YNode RO)
            {
                m_Rotation.enabled = RO.Bool("enabled");
                m_Rotation.x = Curve(RO, "x", 0f);
                m_Rotation.y = Curve(RO, "y", 0f);
                m_Rotation.z = Curve(RO, "curve", 0f);
                m_Rotation.separateAxes = RO.Bool("separateAxes");
            }

            if (body.Get("ColorModule") is YNode C)
            {
                m_Color.enabled = C.Bool("enabled");
                m_Color.color = MinMaxGradient.Read(C.Get("gradient"));
            }

            if (body.Get("UVModule") is YNode U) ReadTextureSheet(U, resolver);

            if (body.Get("VelocityModule") is YNode V)
            {
                var D = m_Velocity;
                D.enabled = V.Bool("enabled");
                D.x = Curve(V, "x", 0f);
                D.y = Curve(V, "y", 0f);
                D.z = Curve(V, "z", 0f);
                D.orbitalX = Curve(V, "orbitalX", 0f);
                D.orbitalY = Curve(V, "orbitalY", 0f);
                D.orbitalZ = Curve(V, "orbitalZ", 0f);
                D.orbitalOffsetX = Curve(V, "orbitalOffsetX", 0f);
                D.orbitalOffsetY = Curve(V, "orbitalOffsetY", 0f);
                D.orbitalOffsetZ = Curve(V, "orbitalOffsetZ", 0f);
                D.radial = Curve(V, "radial", 0f);
                D.speedModifier = Curve(V, "speedModifier", 1f);
                D.space = V.Bool("inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            }

            if (body.Get("InheritVelocityModule") is YNode IV)
            {
                m_InheritVelocity.enabled = IV.Bool("enabled");
                m_InheritVelocity.mode = (ParticleSystemInheritVelocityMode)IV.Int("m_Mode");
                m_InheritVelocity.curve = Curve(IV, "m_Curve", 0f);
            }

            if (body.Get("LifetimeByEmitterSpeedModule") is YNode LE)
            {
                m_LifetimeByEmitterSpeed.enabled = LE.Bool("enabled");
                m_LifetimeByEmitterSpeed.curve = Curve(LE, "m_Curve", 1f);
                if (LE.Get("m_Range") != null) m_LifetimeByEmitterSpeed.range = LE.Vector2("m_Range");
            }

            if (body.Get("ForceModule") is YNode F)
            {
                m_Force.enabled = F.Bool("enabled");
                m_Force.x = Curve(F, "x", 0f);
                m_Force.y = Curve(F, "y", 0f);
                m_Force.z = Curve(F, "z", 0f);
                m_Force.space = F.Bool("inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                m_Force.randomized = F.Bool("randomizePerFrame");
            }

            if (body.Get("ExternalForcesModule") is YNode X)
            {
                var D = m_ExternalForces;
                D.enabled = X.Bool("enabled");
                D.multiplier = X.Float("multiplier", 1f);
                D.multiplierCurve = X.Get("multiplierCurve") != null ? Curve(X, "multiplierCurve", 1f) : new MinMaxCurve(D.multiplier);
                D.influenceFilter = (ParticleSystemGameObjectFilter)X.Int("influenceFilter");
                D.influenceMask = Mask(X.Get("influenceMask"), -1);
                D.influences.Clear();
                foreach (var item in X.Get("influenceList")?.Seq() ?? Array.Empty<YNode>())
                {
                    var field = Resolve<ParticleSystemForceField>(resolver, item);
                    if (field != null) D.influences.Add(field);
                }
            }

            if (body.Get("ClampVelocityModule") is YNode L)
            {
                var D = m_LimitVelocity;
                D.enabled = L.Bool("enabled");
                D.limitX = Curve(L, "x", 1f);
                D.limitY = Curve(L, "y", 1f);
                D.limitZ = Curve(L, "z", 1f);
                D.limit = Curve(L, "magnitude", 1f);
                D.separateAxes = L.Bool("separateAxis");
                D.space = L.Bool("inWorldSpace") ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                D.multiplyDragByParticleSize = L.Bool("multiplyDragByParticleSize", true);
                D.multiplyDragByParticleVelocity = L.Bool("multiplyDragByParticleVelocity", true);
                D.dampen = L.Float("dampen", 1f);
                D.drag = Curve(L, "drag", 0f);
            }

            if (body.Get("NoiseModule") is YNode N)
            {
                var D = m_Noise;
                D.enabled = N.Bool("enabled");
                D.strengthX = Curve(N, "strength", 1f);
                D.strengthY = Curve(N, "strengthY", 1f);
                D.strengthZ = Curve(N, "strengthZ", 1f);
                D.separateAxes = N.Bool("separateAxes");
                D.frequency = N.Float("frequency", 0.5f);
                D.damping = N.Bool("damping", true);
                D.octaveCount = Mathf.Clamp(N.Int("octaves", 1), 1, 4);
                D.octaveMultiplier = N.Float("octaveMultiplier", 0.5f);
                D.octaveScale = N.Float("octaveScale", 2f);
                D.quality = (ParticleSystemNoiseQuality)N.Int("quality", 2);
                D.scrollSpeed = Curve(N, "scrollSpeed", 0f);
                D.remapX = Curve(N, "remap", D.remapX);
                D.remapY = Curve(N, "remapY", D.remapY);
                D.remapZ = Curve(N, "remapZ", D.remapZ);
                D.remapEnabled = N.Bool("remapEnabled");
                D.positionAmount = Curve(N, "positionAmount", 1f);
                D.rotationAmount = Curve(N, "rotationAmount", 0f);
                D.sizeAmount = Curve(N, "sizeAmount", 0f);
            }

            if (body.Get("SizeBySpeedModule") is YNode SS)
            {
                m_SizeBySpeed.enabled = SS.Bool("enabled");
                m_SizeBySpeed.x = Curve(SS, "curve", 1f);
                m_SizeBySpeed.y = Curve(SS, "y", 1f);
                m_SizeBySpeed.z = Curve(SS, "z", 1f);
                if (SS.Get("range") != null) m_SizeBySpeed.range = SS.Vector2("range");
                m_SizeBySpeed.separateAxes = SS.Bool("separateAxes");
            }

            if (body.Get("RotationBySpeedModule") is YNode RS)
            {
                m_RotationBySpeed.enabled = RS.Bool("enabled");
                m_RotationBySpeed.x = Curve(RS, "x", 0f);
                m_RotationBySpeed.y = Curve(RS, "y", 0f);
                m_RotationBySpeed.z = Curve(RS, "curve", 0f);
                m_RotationBySpeed.separateAxes = RS.Bool("separateAxes");
                if (RS.Get("range") != null) m_RotationBySpeed.range = RS.Vector2("range");
            }

            if (body.Get("ColorBySpeedModule") is YNode CS)
            {
                m_ColorBySpeed.enabled = CS.Bool("enabled");
                m_ColorBySpeed.color = MinMaxGradient.Read(CS.Get("gradient"));
                if (CS.Get("range") != null) m_ColorBySpeed.range = CS.Vector2("range");
            }

            if (body.Get("CollisionModule") is YNode CO) ReadCollision(CO, resolver);

            if (body.Get("TriggerModule") is YNode T)
            {
                var D = m_Trigger;
                D.enabled = T.Bool("enabled");
                D.inside = (ParticleSystemOverlapAction)T.Int("inside", 1);
                D.outside = (ParticleSystemOverlapAction)T.Int("outside");
                D.enter = (ParticleSystemOverlapAction)T.Int("enter");
                D.exit = (ParticleSystemOverlapAction)T.Int("exit");
                D.colliderQueryMode = (ParticleSystemColliderQueryMode)T.Int("colliderQueryMode");
                D.radiusScale = T.Float("radiusScale", 1f);
                D.colliders.Clear();
                if (T.Get("primitives") is YNode primitives)
                {
                    foreach (var item in primitives.Seq())
                        D.colliders.Add(Resolve<Component>(resolver, item));
                }
                else
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var c = Resolve<Component>(resolver, T.Get($"collisionShape{i}"));
                        if (c != null) D.colliders.Add(c);
                    }
                }
            }

            if (body.Get("SubModule") is YNode SM)
            {
                var D = m_SubEmitters;
                D.enabled = SM.Bool("enabled");
                D.emitters.Clear();
                if (SM.Get("subEmitters") is YNode list && list.Kind == YKind.Seq)
                {
                    foreach (var item in list.Seq())
                    {
                        var system = Resolve<ParticleSystem>(resolver, item.Get("emitter"));
                        if (system == null) continue;
                        D.emitters.Add(new SubEmitter
                        {
                            system = system,
                            type = (ParticleSystemSubEmitterType)item.Int("type"),
                            properties = (ParticleSystemSubEmitterProperties)item.Int("properties"),
                            emitProbability = item.Float("emitProbability", 1f),
                        });
                    }
                }
                else
                {
                    // Older data: fixed slots.
                    void Slot(string key, ParticleSystemSubEmitterType type)
                    {
                        var system = Resolve<ParticleSystem>(resolver, SM.Get(key));
                        if (system != null) D.emitters.Add(new SubEmitter { system = system, type = type, emitProbability = 1f });
                    }
                    Slot("subEmitterBirth", ParticleSystemSubEmitterType.Birth);
                    Slot("subEmitterBirth1", ParticleSystemSubEmitterType.Birth);
                    Slot("subEmitterCollision", ParticleSystemSubEmitterType.Collision);
                    Slot("subEmitterCollision1", ParticleSystemSubEmitterType.Collision);
                    Slot("subEmitterDeath", ParticleSystemSubEmitterType.Death);
                    Slot("subEmitterDeath1", ParticleSystemSubEmitterType.Death);
                }
            }

            if (body.Get("LightsModule") is YNode LI)
            {
                var D = m_Lights;
                D.enabled = LI.Bool("enabled");
                D.ratio = LI.Float("ratio");
                D.light = Resolve<Object>(resolver, LI.Get("light"));
                D.useRandomDistribution = LI.Bool("randomDistribution", true);
                D.useParticleColor = LI.Bool("color", true);
                D.sizeAffectsRange = LI.Bool("range", true);
                D.alphaAffectsIntensity = LI.Bool("intensity", true);
                D.range = Curve(LI, "rangeCurve", 1f);
                D.intensity = Curve(LI, "intensityCurve", 1f);
                D.maxLights = LI.Int("maxLights", 20);
            }

            if (body.Get("TrailModule") is YNode TR)
            {
                var D = m_Trails;
                D.enabled = TR.Bool("enabled");
                D.mode = (ParticleSystemTrailMode)TR.Int("mode");
                D.ratio = TR.Float("ratio", 1f);
                D.lifetime = Curve(TR, "lifetime", 1f);
                D.minVertexDistance = TR.Float("minVertexDistance", 0.2f);
                D.textureMode = (ParticleSystemTrailTextureMode)TR.Int("textureMode");
                if (TR.Get("textureScale") != null) D.textureScale = TR.Vector2("textureScale");
                D.ribbonCount = TR.Int("ribbonCount", 1);
                D.shadowBias = TR.Float("shadowBias", 0.5f);
                D.worldSpace = TR.Bool("worldSpace");
                D.dieWithParticles = TR.Bool("dieWithParticles", true);
                D.sizeAffectsWidth = TR.Bool("sizeAffectsWidth", true);
                D.sizeAffectsLifetime = TR.Bool("sizeAffectsLifetime");
                D.inheritParticleColor = TR.Bool("inheritParticleColor", true);
                D.generateLightingData = TR.Bool("generateLightingData");
                D.splitSubEmitterRibbons = TR.Bool("splitSubEmitterRibbons");
                D.attachRibbonsToTransform = TR.Bool("attachRibbonsToTransform");
                D.colorOverLifetime = MinMaxGradient.Read(TR.Get("colorOverLifetime"));
                D.widthOverTrail = Curve(TR, "widthOverTrail", 1f);
                D.colorOverTrail = MinMaxGradient.Read(TR.Get("colorOverTrail"));
            }

            if (body.Get("CustomDataModule") is YNode CD)
            {
                var D = m_CustomData;
                D.enabled = CD.Bool("enabled");
                for (int s = 0; s < 2; s++)
                {
                    D.modes[s] = (ParticleSystemCustomDataMode)CD.Int($"mode{s}");
                    D.componentCounts[s] = CD.Int($"vectorComponentCount{s}", 4);
                    D.colors[s] = MinMaxGradient.Read(CD.Get($"color{s}"));
                    for (int c = 0; c < 4; c++) D.vectors[s, c] = Curve(CD, $"vector{s}_{c}", 0f);
                }
            }
        }

        void ReadShape(YNode S, IReferenceResolver resolver)
        {
            var D = m_Shape;
            D.enabled = S.Bool("enabled", true);
            D.shapeType = (ParticleSystemShapeType)S.Int("type", (int)ParticleSystemShapeType.Cone);
            D.angle = S.Float("angle", 25f);
            D.length = S.Float("length", 5f);
            D.boxThickness = S.Vector3("boxThickness");
            D.radiusThickness = S.Float("radiusThickness", 1f);
            D.donutRadius = S.Float("donutRadius", 0.2f);
            D.position = S.Vector3("m_Position");
            D.rotation = S.Vector3("m_Rotation");
            if (S.Get("m_Scale") != null) D.scale = S.Vector3("m_Scale", Vector3.one);
            else if (S.Get("boxX") != null) D.scale = new Vector3(S.Float("boxX", 1f), S.Float("boxY", 1f), S.Float("boxZ", 1f));
            D.meshShapeType = (ParticleSystemMeshShapeType)S.Int("placementMode");
            D.meshMaterialIndex = S.Int("m_MeshMaterialIndex");
            D.normalOffset = S.Float("m_MeshNormalOffset");
            if (S.Get("m_MeshSpawn") is YNode spawn)
            {
                D.meshSpawnMode = (ParticleSystemShapeMultiModeValue)spawn.Int("mode");
                D.meshSpawnSpread = spawn.Float("spread");
                D.meshSpawnSpeed = Curve(spawn, "speed", 1f);
            }
            D.mesh = Resolve<Mesh>(resolver, S.Get("m_Mesh"));
            D.meshRenderer = Resolve<MeshRenderer>(resolver, S.Get("m_MeshRenderer"));
            D.skinnedMeshRenderer = Resolve<Renderer>(resolver, S.Get("m_SkinnedMeshRenderer"));
            D.sprite = Resolve<Sprite>(resolver, S.Get("m_Sprite"));
            D.spriteRenderer = Resolve<SpriteRenderer>(resolver, S.Get("m_SpriteRenderer"));
            D.useMeshMaterialIndex = S.Bool("m_UseMeshMaterialIndex");
            D.useMeshColors = S.Bool("m_UseMeshColors", true);
            D.alignToDirection = S.Bool("alignToDirection");
            D.texture = Resolve<Texture2D>(resolver, S.Get("m_Texture"));
            D.textureClipChannel = (ParticleSystemShapeTextureChannel)S.Int("m_TextureClipChannel", 3);
            D.textureClipThreshold = S.Float("m_TextureClipThreshold");
            D.textureUVChannel = S.Int("m_TextureUVChannel");
            D.textureColorAffectsParticles = S.Bool("m_TextureColorAffectsParticles", true);
            D.textureAlphaAffectsParticles = S.Bool("m_TextureAlphaAffectsParticles", true);
            D.textureBilinearFiltering = S.Bool("m_TextureBilinearFiltering");
            D.randomDirectionAmount = S.Get("randomDirectionAmount") != null ? S.Float("randomDirectionAmount") : S.Bool("randomDirection") ? 1f : 0f;
            D.sphericalDirectionAmount = S.Float("sphericalDirectionAmount");
            D.randomPositionAmount = S.Float("randomPositionAmount");
            if (S.Get("radius") is YNode radius)
            {
                if (radius.Kind == YKind.Map)
                {
                    D.radius = radius.Float("value", 1f);
                    D.radiusMode = (ParticleSystemShapeMultiModeValue)radius.Int("mode");
                    D.radiusSpread = radius.Float("spread");
                    D.radiusSpeed = Curve(radius, "speed", 1f);
                }
                else D.radius = radius.AsFloat(1f);
            }
            if (S.Get("arc") is YNode arc)
            {
                if (arc.Kind == YKind.Map)
                {
                    D.arc = arc.Float("value", 360f);
                    D.arcMode = (ParticleSystemShapeMultiModeValue)arc.Int("mode");
                    D.arcSpread = arc.Float("spread");
                    D.arcSpeed = Curve(arc, "speed", 1f);
                }
                else D.arc = arc.AsFloat(360f);
            }
        }

        void ReadTextureSheet(YNode U, IReferenceResolver resolver)
        {
            var D = m_TextureSheet;
            D.enabled = U.Bool("enabled");
            D.mode = (ParticleSystemAnimationMode)U.Int("mode");
            D.timeMode = (ParticleSystemAnimationTimeMode)U.Int("timeMode");
            D.fps = U.Float("fps", 30f);
            D.frameOverTime = Curve(U, "frameOverTime", D.frameOverTime);
            D.startFrame = Curve(U, "startFrame", 0f);
            if (U.Get("speedRange") != null) D.speedRange = U.Vector2("speedRange");
            D.numTilesX = Math.Max(1, U.Int("tilesX", 1));
            D.numTilesY = Math.Max(1, U.Int("tilesY", 1));
            D.animation = (ParticleSystemAnimationType)U.Int("animationType");
            D.rowIndex = U.Int("rowIndex");
            D.cycleCount = Math.Max(1, U.Int("cycles", 1));
            D.uvChannelMask = (Rendering.UVChannelFlags)U.Int("uvChannelMask", -1);
            D.rowMode = U.Get("rowMode") != null
                ? (ParticleSystemAnimationRowMode)U.Int("rowMode")
                : U.Bool("randomRow", true) ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom;
            D.flipU = U.Float("flipU");
            D.flipV = U.Float("flipV");
            D.sprites.Clear();
            foreach (var item in U.Get("sprites")?.Seq() ?? Array.Empty<YNode>())
            {
                var sprite = Resolve<Sprite>(resolver, item.Get("sprite"));
                if (sprite != null) D.sprites.Add(sprite);
            }
        }

        void ReadCollision(YNode C, IReferenceResolver resolver)
        {
            var D = m_Collision;
            D.enabled = C.Bool("enabled");
            D.type = (ParticleSystemCollisionType)C.Int("type");
            D.mode = (ParticleSystemCollisionMode)C.Int("collisionMode");
            D.colliderForce = C.Float("colliderForce");
            D.multiplyColliderForceByParticleSize = C.Bool("multiplyColliderForceByParticleSize");
            D.multiplyColliderForceByParticleSpeed = C.Bool("multiplyColliderForceByParticleSpeed");
            D.multiplyColliderForceByCollisionAngle = C.Bool("multiplyColliderForceByCollisionAngle", true);
            D.planes.Clear();
            if (C.Get("m_Planes") is YNode planes)
            {
                foreach (var item in planes.Seq()) D.planes.Add(Resolve<Transform>(resolver, item));
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    var plane = Resolve<Transform>(resolver, C.Get($"plane{i}"));
                    if (plane != null) D.planes.Add(plane);
                }
            }
            D.dampen = Curve(C, "m_Dampen", 0f);
            D.bounce = Curve(C, "m_Bounce", 1f);
            D.lifetimeLoss = Curve(C, "m_EnergyLossOnCollision", 0f);
            D.minKillSpeed = C.Float("minKillSpeed");
            D.maxKillSpeed = C.Float("maxKillSpeed", 10000f);
            D.radiusScale = C.Float("radiusScale", 1f);
            D.collidesWith = Mask(C.Get("collidesWith"), -1);
            D.maxCollisionShapes = C.Int("maxCollisionShapes", 256);
            D.quality = (ParticleSystemCollisionQuality)C.Int("quality");
            D.voxelSize = C.Float("voxelSize", 0.5f);
            D.sendCollisionMessages = C.Bool("collisionMessages");
            D.enableDynamicColliders = C.Bool("collidesWithDynamic", true);
            D.enableInteriorCollisions = C.Bool("interiorCollisions", true);
        }

        // ------------------------------------------------------------------ cloning

        /// <summary>Instantiate: the settings are copied (references remapped into the clone);
        /// the clone starts with no particles and stopped.</summary>
        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (ParticleSystem)source;
            T Map<T>(T o) where T : Object => o == null ? null : (remap?.Invoke(o) as T) ?? o;

            m_Main = (MainData)s.m_Main.MemberwiseCopy();
            m_Main.customSimulationSpace = Map(s.m_Main.customSimulationSpace);
            m_Emission = (EmissionData)s.m_Emission.MemberwiseCopy();
            m_Emission.bursts = new List<Burst>(s.m_Emission.bursts);
            m_BurstCycles = new int[m_Emission.bursts.Count];
            m_Shape = (ShapeData)s.m_Shape.MemberwiseCopy();
            m_Shape.meshRenderer = Map(s.m_Shape.meshRenderer);
            m_Shape.skinnedMeshRenderer = Map(s.m_Shape.skinnedMeshRenderer);
            m_Shape.spriteRenderer = Map(s.m_Shape.spriteRenderer);
            m_Velocity = (VelocityData)s.m_Velocity.MemberwiseCopy();
            m_LimitVelocity = (LimitVelocityData)s.m_LimitVelocity.MemberwiseCopy();
            m_InheritVelocity = (InheritVelocityData)s.m_InheritVelocity.MemberwiseCopy();
            m_LifetimeByEmitterSpeed = (LifetimeByEmitterSpeedData)s.m_LifetimeByEmitterSpeed.MemberwiseCopy();
            m_Force = (ForceData)s.m_Force.MemberwiseCopy();
            m_Color = (ColorData)s.m_Color.MemberwiseCopy();
            m_ColorBySpeed = (ColorBySpeedData)s.m_ColorBySpeed.MemberwiseCopy();
            m_Size = (SizeData)s.m_Size.MemberwiseCopy();
            m_SizeBySpeed = (SizeBySpeedData)s.m_SizeBySpeed.MemberwiseCopy();
            m_Rotation = (RotationData)s.m_Rotation.MemberwiseCopy();
            m_RotationBySpeed = (RotationBySpeedData)s.m_RotationBySpeed.MemberwiseCopy();
            m_ExternalForces = (ExternalForcesData)s.m_ExternalForces.MemberwiseCopy();
            m_ExternalForces.influences = s.m_ExternalForces.influences.ConvertAll(Map);
            m_Noise = (NoiseData)s.m_Noise.MemberwiseCopy();
            m_Collision = (CollisionData)s.m_Collision.MemberwiseCopy();
            m_Collision.planes = s.m_Collision.planes.ConvertAll(Map);
            m_Trigger = (TriggerData)s.m_Trigger.MemberwiseCopy();
            m_Trigger.colliders = s.m_Trigger.colliders.ConvertAll(Map);
            m_SubEmitters = new SubEmittersData { enabled = s.m_SubEmitters.enabled };
            foreach (var e in s.m_SubEmitters.emitters)
            {
                var copy = e;
                copy.system = Map(e.system);
                m_SubEmitters.emitters.Add(copy);
            }
            m_TextureSheet = (TextureSheetData)s.m_TextureSheet.MemberwiseCopy();
            m_TextureSheet.sprites = new List<Sprite>(s.m_TextureSheet.sprites);
            m_Lights = (LightsData)s.m_Lights.MemberwiseCopy();
            m_Lights.light = Map(s.m_Lights.light);
            m_Trails = (TrailData)s.m_Trails.MemberwiseCopy();
            m_CustomData = new CustomDataData { enabled = s.m_CustomData.enabled };
            Array.Copy(s.m_CustomData.modes, m_CustomData.modes, 2);
            Array.Copy(s.m_CustomData.componentCounts, m_CustomData.componentCounts, 2);
            Array.Copy(s.m_CustomData.vectors, m_CustomData.vectors, 8);
            Array.Copy(s.m_CustomData.colors, m_CustomData.colors, 2);
        }
    }

    public sealed partial class ParticleSystemRenderer
    {
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_RenderMode = (ParticleSystemRenderMode)body.Int("m_RenderMode");
            m_SortMode = (ParticleSystemSortMode)body.Int("m_SortMode");
            m_MinParticleSize = body.Float("m_MinParticleSize");
            m_MaxParticleSize = body.Float("m_MaxParticleSize", 0.5f);
            m_CameraVelocityScale = body.Float("m_CameraVelocityScale");
            m_VelocityScale = body.Float("m_VelocityScale");
            m_LengthScale = body.Float("m_LengthScale", 2f);
            m_SortingFudge = body.Float("m_SortingFudge");
            m_NormalDirection = body.Float("m_NormalDirection", 1f);
            m_ShadowBias = body.Float("m_ShadowBias");
            m_RenderAlignment = (ParticleSystemRenderSpace)body.Int("m_RenderAlignment");
            m_Pivot = body.Vector3("m_Pivot");
            m_Flip = body.Vector3("m_Flip");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            m_EnableGPUInstancing = body.Bool("m_EnableGPUInstancing", true);
            m_AllowRoll = body.Bool("m_AllowRoll", true);
            m_FreeformStretching = body.Bool("m_FreeformStretching");
            m_RotateWithStretchDirection = body.Bool("m_RotateWithStretchDirection", true);
            m_MeshDistribution = (ParticleSystemMeshDistribution)body.Int("m_MeshDistribution");
            m_Meshes.Clear();
            m_MeshWeightings.Clear();
            for (int i = 0; i < 4; i++)
            {
                string suffix = i == 0 ? "" : i.ToString();
                var mesh = body.Get("m_Mesh" + suffix);
                if (mesh == null) continue;
                var m = mesh.Kind == YKind.Map && !mesh.AsRef().IsNull ? resolver.Resolve(mesh.AsRef(), typeof(Mesh)) as Mesh : null;
                if (m == null && i > 0) continue;
                m_Meshes.Add(m);
                m_MeshWeightings.Add(body.Float("m_MeshWeighting" + suffix, 1f));
            }
            if (m_Meshes.Count == 1 && m_Meshes[0] == null)
            {
                m_Meshes.Clear();
                m_MeshWeightings.Clear();
            }
            if (body.Bool("m_UseCustomVertexStreams") && body.Get("m_VertexStreams") is YNode streams && streams.Kind == YKind.Scalar)
            {
                m_VertexStreams.Clear();
                var hex = streams.Scalar;
                for (int i = 0; i + 1 < hex.Length; i += 2)
                    m_VertexStreams.Add((ParticleSystemVertexStream)Convert.ToInt32(hex.Substring(i, 2), 16));
            }
            if (body.Bool("m_UseCustomTrailVertexStreams") && body.Get("m_TrailVertexStreams") is YNode trailStreams && trailStreams.Kind == YKind.Scalar)
            {
                m_TrailVertexStreams.Clear();
                var hex = trailStreams.Scalar;
                for (int i = 0; i + 1 < hex.Length; i += 2)
                    m_TrailVertexStreams.Add((ParticleSystemVertexStream)Convert.ToInt32(hex.Substring(i, 2), 16));
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (ParticleSystemRenderer)source;
            CopyRendererData(s);
            m_RenderMode = s.m_RenderMode;
            m_SortMode = s.m_SortMode;
            m_MinParticleSize = s.m_MinParticleSize;
            m_MaxParticleSize = s.m_MaxParticleSize;
            m_CameraVelocityScale = s.m_CameraVelocityScale;
            m_VelocityScale = s.m_VelocityScale;
            m_LengthScale = s.m_LengthScale;
            m_SortingFudge = s.m_SortingFudge;
            m_NormalDirection = s.m_NormalDirection;
            m_ShadowBias = s.m_ShadowBias;
            m_RenderAlignment = s.m_RenderAlignment;
            m_Pivot = s.m_Pivot;
            m_Flip = s.m_Flip;
            m_MaskInteraction = s.m_MaskInteraction;
            m_EnableGPUInstancing = s.m_EnableGPUInstancing;
            m_AllowRoll = s.m_AllowRoll;
            m_FreeformStretching = s.m_FreeformStretching;
            m_RotateWithStretchDirection = s.m_RotateWithStretchDirection;
            m_MeshDistribution = s.m_MeshDistribution;
            m_Meshes.Clear();
            m_Meshes.AddRange(s.m_Meshes);
            m_MeshWeightings.Clear();
            m_MeshWeightings.AddRange(s.m_MeshWeightings);
            m_VertexStreams.Clear();
            m_VertexStreams.AddRange(s.m_VertexStreams);
            m_TrailVertexStreams.Clear();
            m_TrailVertexStreams.AddRange(s.m_TrailVertexStreams);
        }
    }
}
