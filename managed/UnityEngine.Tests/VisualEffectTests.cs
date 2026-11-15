using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The Visual Effect module without the graph package: compiled graphs handed to a
    /// resource by hand, as the package's compiler would, and effects of the assets made of them.</summary>
    public class VisualEffectTests
    {
        public class Link : ScriptableObject
        {
            public Link other;
            public int number;
        }

        sealed class Capture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            public readonly List<string> Errors = new List<string>();
            public Capture() { Debug.unityLogger.logHandler = this; }
            public void Dispose() { Debug.unityLogger.logHandler = m_Previous; }
            public void LogException(Exception exception, Object context) { }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) Errors.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
            }
        }

        static VFXExpressionDesc Value(VFXValueType type) =>
            new VFXExpressionDesc { op = VFXExpressionOperation.Value, data = new[] { -1, -1, -1, (int)type } };

        static VFXExpressionDesc Expression(VFXExpressionOperation op, params int[] data) => new VFXExpressionDesc { op = op, data = data };

        static VFXExposedMapping Exposed(string name, int expression, VFXSpace space = VFXSpace.None) =>
            new VFXExposedMapping { mapping = new VFXMapping(name, expression), space = space };

        static VFXExpressionValueContainerDesc Constant<T>(int expression, T value) =>
            new VFXExpressionValueContainerDesc<T> { expressionIndex = (uint)expression, value = value };

        static VFXEditorSystemDesc Described(string name, VFXSystemType type, uint layer) =>
            new VFXEditorSystemDesc { name = name, type = type, layer = layer, tasks = Array.Empty<VFXEditorTaskDesc>() };

        /// <summary>An asset whose compiled graph is what a resource took from the sheet.</summary>
        static VisualEffectAsset Asset(VFXExpressionSheet sheet, VFXCPUBufferDesc[] cpuBuffers = null)
        {
            var resource = new VisualEffectResource();
            resource.SetRuntimeData(sheet, new[] { Described("Spawn", VFXSystemType.Spawner, uint.MaxValue), Described("Particles", VFXSystemType.Particle, 0) },
                new[] { new VFXEventDesc { name = "OnPlay" } }, null, cpuBuffers, null, null, ShadowCastingMode.Off, MotionVectorGenerationMode.Camera,
                VFXInstancingDisabledReason.None, 7);
            var data = resource.m_Data;
            data.Seal();
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            return asset;
        }

        /// <summary>Float "Rate" 4, int "Count" -7, 2D texture "Image", 3D texture "Volume",
        /// curve "Curve", world position "Where".</summary>
        static VisualEffectAsset First() => Asset(new VFXExpressionSheet
        {
            expressions = new[]
            {
                Value(VFXValueType.Float), Value(VFXValueType.Int32), Value(VFXValueType.Texture2D), Value(VFXValueType.Texture3D),
                Value(VFXValueType.Curve), Value(VFXValueType.Float3),
            },
            values = new[] { Constant(0, 4f), Constant(1, -7), Constant(4, AnimationCurve.Linear(0f, 0f, 1f, 2f)), Constant(5, new Vector3(1f, 2f, 3f)) },
            exposed = new[] { Exposed("Count", 1), Exposed("Curve", 4), Exposed("Image", 2), Exposed("Rate", 0), Exposed("Volume", 3), Exposed("Where", 5, VFXSpace.World) },
        });

        /// <summary>Float "Rate" 6 and "Count" as a float 1.5: one name with the first asset's
        /// type, one with another.</summary>
        static VisualEffectAsset Second() => Asset(new VFXExpressionSheet
        {
            expressions = new[] { Value(VFXValueType.Float), Value(VFXValueType.Float) },
            values = new[] { Constant(0, 6f), Constant(1, 1.5f) },
            exposed = new[] { Exposed("Count", 1), Exposed("Rate", 0) },
        });

        static VisualEffect Effect(VisualEffectAsset asset)
        {
            var effect = new GameObject("effect").AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            return effect;
        }

        [Fact]
        public void OperationTablesAnswerTypesParentsSizesAndDimensions()
        {
            Assert.Equal(VFXValueType.Float3, VFXExpressionHelper.GetTypeOfOperation(VFXExpressionOperation.Combine3f, 0, 0, 0, 0));
            Assert.Equal(VFXValueType.Matrix4x4, VFXExpressionHelper.GetTypeOfOperation(VFXExpressionOperation.Add, 0, 0, 0, (int)VFXValueType.Matrix4x4));
            Assert.Equal(VFXValueType.None, VFXExpressionHelper.GetTypeOfOperation((VFXExpressionOperation)155, 0, 0, 0, 1));
            Assert.Equal(4, VFXExpressionHelper.ParentCount(VFXExpressionOperation.RowToMatrix));
            Assert.Equal(-1, VFXExpressionHelper.ParentCount((VFXExpressionOperation)(-1)));
            Assert.Equal(16, VFXExpressionHelper.GetSizeOfType(VFXValueType.Matrix4x4));
            Assert.Equal(4, VFXExpressionHelper.GetSizeOfType(VFXValueType.Buffer));
            Assert.Equal(TextureDimension.CubeArray, VFXExpressionHelper.GetTextureDimension(VFXValueType.TextureCubeArray));
            Assert.Equal(TextureDimension.Unknown, VFXExpressionHelper.GetTextureDimension(VFXValueType.CameraBuffer));
            using var capture = new Capture();
            Assert.Equal(-1, VFXExpressionHelper.GetSizeOfType(VFXValueType.Spline));
            Assert.Equal(-1, VFXExpressionHelper.GetSizeOfType((VFXValueType)21));
            Assert.Equal(new[] { "Invalid value type: 17", "Invalid value type: 21" }, capture.Errors);
        }

        [Fact]
        public void RuntimeDataRefusesDecreasingLayersAndInvalidExpressions()
        {
            var resource = new VisualEffectResource();
            var sheet = new VFXExpressionSheet { expressions = new[] { Value(VFXValueType.Float) } };
            var systems = new[] { Described("A", VFXSystemType.Particle, 2), Described("Spawn", VFXSystemType.Spawner, uint.MaxValue), Described("B", VFXSystemType.Particle, 1) };
            var error = Assert.Throws<ArgumentException>(() => resource.SetRuntimeData(sheet, systems, null, null, null, null, null, ShadowCastingMode.Off,
                MotionVectorGenerationMode.Camera, VFXInstancingDisabledReason.None));
            Assert.StartsWith("System in wrong order: B", error.Message);
            Assert.Empty(resource.m_Data.Systems);

            using var capture = new Capture();
            void Refused(params VFXExpressionDesc[] expressions)
            {
                var invalid = Assert.Throws<ArgumentException>(() => resource.SetRuntimeData(new VFXExpressionSheet { expressions = expressions }, null, null, null, null,
                    null, null, ShadowCastingMode.Off, MotionVectorGenerationMode.Camera, VFXInstancingDisabledReason.None));
                Assert.StartsWith("Invalid expression sheet", invalid.Message);
                Assert.Empty(resource.m_Data.Expressions);
            }
            Refused(Expression((VFXExpressionOperation)155, -1, -1, -1, -1));
            Refused(Expression(VFXExpressionOperation.Add, 0, 5, -1, (int)VFXValueType.Float));
            Refused(Value(VFXValueType.None));
            // The component taken must lie inside the parent's size, at most four components.
            Refused(Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 3, 3));
            Refused(Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 4, 5));
            Refused(Value(VFXValueType.Float), Value(VFXValueType.Float), Expression(VFXExpressionOperation.SampleCurve, 0, 1, -1, -1));
            Refused(Value(VFXValueType.Curve), Value(VFXValueType.Float), Expression(VFXExpressionOperation.SampleGradient, 0, 1, -1, -1));
            Assert.Equal(new[]
            {
                "Invalid VFX expression opcode: 155", "Invalid VFX expression parent index: 5 for expression 39",
                "Invalid VFX expression return type: 0 for expression 1", "Invalid VFX expression extract operands", "Invalid VFX expression extract operands",
                "Invalid VFX parent for sample Curve : 1", "Invalid VFX parent for sample Gradient : 14",
            }, capture.Errors);

            // Valid: a component inside its parent, a curve sampled, parents among the per-event expressions.
            resource.SetRuntimeData(new VFXExpressionSheet
            {
                expressions = new[] { Value(VFXValueType.Float3), Expression(VFXExpressionOperation.ExtractComponent, 0, -1, 2, 3), Value(VFXValueType.Curve),
                    Expression(VFXExpressionOperation.SampleCurve, 2, 4, -1, -1) },
                expressionsPerSpawnEventAttribute = new[] { Value(VFXValueType.Float) },
            }, null, null, null, null, null, null, ShadowCastingMode.Off, MotionVectorGenerationMode.Camera, VFXInstancingDisabledReason.None);
            Assert.Equal(4, resource.m_Data.Expressions.Length);
        }

        [Fact]
        public void RuntimeDataDropsEventsNamedTwiceAndKeepsTheRest()
        {
            var resource = new VisualEffectResource();
            var events = new[] { new VFXEventDesc { name = "Fire" }, new VFXEventDesc { name = "Cease" }, new VFXEventDesc { name = "Fire" } };
            var error = Assert.Throws<ArgumentException>(() => resource.SetRuntimeData(new VFXExpressionSheet { expressions = new[] { Value(VFXValueType.Float) } },
                new[] { Described("Particles", VFXSystemType.Particle, 0) }, events, null, null, null, null, ShadowCastingMode.Off, MotionVectorGenerationMode.Camera,
                VFXInstancingDisabledReason.None));
            Assert.StartsWith("Event named 'Fire' is referenced twice", error.Message);
            Assert.Empty(resource.m_Data.Events);
            Assert.Single(resource.m_Data.Systems);
            Assert.Single(resource.m_Data.Expressions);
        }

        [Fact]
        public void AssetListsItsPropertiesEventsAndSystems()
        {
            var asset = First();
            var exposed = new List<VFXExposedProperty>();
            asset.GetExposedProperties(exposed);
            Assert.Equal(new[] { "Count", "Curve", "Image", "Rate", "Volume", "Where" }, exposed.ConvertAll(p => p.name));
            Assert.Equal(new[] { typeof(int), typeof(AnimationCurve), typeof(Texture), typeof(float), typeof(Texture), typeof(Vector3) }, exposed.ConvertAll(p => p.type));
            Assert.Equal(TextureDimension.Tex3D, asset.GetTextureDimension("Volume"));
            Assert.Equal(TextureDimension.Unknown, asset.GetTextureDimension("Rate"));
            Assert.Equal(TextureDimension.Unknown, asset.GetTextureDimension("Nope"));
            Assert.Equal(VFXSpace.World, asset.GetExposedSpace("Where"));
            Assert.Equal(VFXSpace.None, asset.GetExposedSpace("Nope"));
            var names = new List<string>();
            asset.GetEvents(names);
            Assert.Equal(new[] { "OnPlay" }, names);
            asset.GetParticleSystemNames(names);
            Assert.Equal(new[] { "Particles" }, names);
            asset.GetSpawnSystemNames(names);
            Assert.Equal(new[] { "Spawn" }, names);
            Assert.True(asset.HasSystem(Shader.PropertyToID("Spawn")));
            Assert.False(asset.HasSystem(Shader.PropertyToID("Nope")));
        }

        [Fact]
        public void PropertiesAnswerByNameAndTypeAndComplainOtherwise()
        {
            var effect = Effect(First());
            try
            {
                Assert.True(effect.HasFloat("Rate"));
                Assert.False(effect.HasInt("Rate"));
                Assert.True(effect.HasTexture("Image") && effect.HasTexture("Volume"));
                Assert.False(effect.HasFloat("Nope"));
                Assert.Equal(4f, effect.GetFloat("Rate"));
                Assert.Equal(-7, effect.GetInt("Count"));
                Assert.Equal(new Vector3(1f, 2f, 3f), effect.GetVector3("Where"));
                Assert.Equal(2, effect.GetAnimationCurve("Curve").length);
                using var capture = new Capture();
                Assert.Equal(0f, effect.GetFloat("Count"));
                Assert.Equal(0, effect.GetInt("Nope"));
                effect.SetFloat("Count", 3f);
                effect.SetInt("Nope", 3);
                Assert.Equal(0, effect.GetAnimationCurve("Nope").length);
                Assert.Equal(new[]
                {
                    "Value type for 'Count' is incorrect", "Value of name 'Nope' was not found", "Value type for 'Count' is incorrect",
                    "Value of name 'Nope' was not found", "Value of name 'Nope' was not found",
                }, capture.Errors);
                Assert.Equal(-7, effect.GetInt("Count"));

                effect.SetFloat("Rate", 9f);
                effect.SetInt("Count", 41);
                Assert.Equal(9f, effect.GetFloat("Rate"));
                effect.ResetOverride("Rate");
                effect.ResetOverride("Nope");
                Assert.Equal(4f, effect.GetFloat("Rate"));
                Assert.Equal(41, effect.GetInt("Count"));
                // A curve set is a copy, and so is one read.
                var curve = AnimationCurve.Constant(0f, 1f, 5f);
                effect.SetAnimationCurve("Curve", curve);
                curve.AddKey(2f, 2f);
                effect.GetAnimationCurve("Curve").AddKey(3f, 3f);
                Assert.Equal(2, effect.GetAnimationCurve("Curve").length);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void TextureOfAnotherDimensionIsRefused()
        {
            var effect = Effect(First());
            var flat = new Texture2D(2, 2);
            var volume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false);
            try
            {
                using var capture = new Capture();
                effect.SetTexture("Image", flat);
                effect.SetTexture("Volume", flat);
                effect.SetTexture("Image", volume);
                Assert.Same(flat, effect.GetTexture("Image"));
                Assert.Null(effect.GetTexture("Volume"));
                Assert.Equal(new[]
                {
                    "Error assigning 2D texture to 3D texture property 'Volume': Dimensions must match",
                    "Error assigning 3D texture to 2D texture property 'Image': Dimensions must match",
                }, capture.Errors);
                // No texture is always accepted.
                effect.SetTexture("Image", null);
                Assert.Null(effect.GetTexture("Image"));
                Assert.Equal(2, capture.Errors.Count);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(flat);
                Object.DestroyImmediate(volume);
            }
        }

        [Fact]
        public void AnAssetChangeKeepsCurrentAndSavedOverridesByNameAndType()
        {
            var first = First();
            var second = Second();
            var effect = Effect(first);
            GameObject clone = null;
            try
            {
                effect.SetFloat("Rate", 50f);
                effect.SetInt("Count", 41);
                effect.visualEffectAsset = second;
                // The same name and type keeps its override; another type has the asset's value.
                Assert.Equal(50f, effect.GetFloat("Rate"));
                Assert.Equal(1.5f, effect.GetFloat("Count"));
                Assert.False(effect.HasInt("Count"));
                effect.SetFloat("Rate", 60f);
                effect.visualEffectAsset = first;
                Assert.Equal(60f, effect.GetFloat("Rate"));
                // The int override only lived in the first sheet: the change away dropped it.
                Assert.Equal(-7, effect.GetInt("Count"));

                // A clone serializes the effect: its overrides are saved from then on.
                effect.SetInt("Count", 41);
                clone = Object.Instantiate(effect.gameObject);
                Assert.Equal(41, clone.GetComponent<VisualEffect>().GetInt("Count"));
                Assert.Equal(60f, clone.GetComponent<VisualEffect>().GetFloat("Rate"));
                effect.visualEffectAsset = second;
                effect.visualEffectAsset = first;
                Assert.Equal(41, effect.GetInt("Count"));
                // Changes after the save are current ones: they win at the next change...
                effect.SetFloat("Rate", 70f);
                effect.visualEffectAsset = first;
                Assert.Equal(70f, effect.GetFloat("Rate"));
                // ...but are not saved: with no asset in between, the saved value is back.
                effect.visualEffectAsset = null;
                Assert.False(effect.HasFloat("Rate"));
                effect.visualEffectAsset = first;
                Assert.Equal(60f, effect.GetFloat("Rate"));
                // A reset override is saved as overridden still: it returns the same way.
                effect.ResetOverride("Count");
                Assert.Equal(-7, effect.GetInt("Count"));
                effect.visualEffectAsset = first;
                Assert.Equal(-7, effect.GetInt("Count"));
                effect.visualEffectAsset = null;
                effect.visualEffectAsset = first;
                Assert.Equal(41, effect.GetInt("Count"));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        [Fact]
        public void SerializedSheetAppliesEntriesOfMatchingNameAndType()
        {
            var first = First();
            var effect = new GameObject("serialized").AddComponent<VisualEffect>();
            try
            {
                effect.visualEffectAsset = first;
                ObjectGraphBuilder.ReadComponent(effect, TestYaml.DocumentBody(@"
VisualEffect:
  m_Enabled: 1
  m_InitialEventName: Custom
  m_InitialEventNameOverriden: 1
  m_StartSeed: 77
  m_ResetSeedOnPlay: 0
  m_PropertySheet:
    m_Float:
      m_Array:
      - m_Value: 12
        m_Name: Rate
        m_Overridden: 1
      - m_Value: 3
        m_Name: Count
        m_Overridden: 1
      - m_Value: 8
        m_Name: Gone
        m_Overridden: 1
    m_Int:
      m_Array:
      - m_Value: 99
        m_Name: Count
        m_Overridden: 0
    m_Vector3f:
      m_Array:
      - m_Value: {x: 7, y: 8, z: 9}
        m_Name: Where
        m_Overridden: 1
"), null);
                // The reader leaves the asset the document names (none here); the sheet applies to ours.
                effect.visualEffectAsset = first;
                Assert.Equal(12f, effect.GetFloat("Rate"));
                Assert.Equal(-7, effect.GetInt("Count"));
                Assert.Equal(new Vector3(7f, 8f, 9f), effect.GetVector3("Where"));
                Assert.Equal("Custom", effect.initialEventName);
                Assert.Equal(77u, effect.startSeed);
                Assert.False(effect.resetSeedOnPlay);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void InitialEventComesFromTheEffectThenTheAssetThenPlay()
        {
            var asset = First();
            asset.m_Settings.InitialEventName = "Begin";
            var effect = new GameObject("initial").AddComponent<VisualEffect>();
            try
            {
                Assert.Equal("OnPlay", effect.initialEventName);
                Assert.NotNull(effect.GetComponent<VFXRenderer>());
                effect.visualEffectAsset = asset;
                Assert.Equal("Begin", effect.initialEventName);
                effect.initialEventName = "";
                Assert.Equal("", effect.initialEventName);
                effect.initialEventID = Shader.PropertyToID("Other");
                Assert.Equal("Other", effect.initialEventName);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void EventAttributesFollowTheAssetsLayout()
        {
            var data = new VFXCPUBufferData();
            data.PushFloat(1f);
            data.PushFloat(0.25f);
            data.PushFloat(0.5f);
            data.PushFloat(0.75f);
            var layout = new[]
            {
                new VFXLayoutElementDesc { name = "spawnCount", type = VFXValueType.Float, offset = new VFXLayoutOffset { structure = 4, element = 0 } },
                new VFXLayoutElementDesc { name = "color", type = VFXValueType.Float3, offset = new VFXLayoutOffset { structure = 4, element = 1 } },
            };
            var asset = Asset(new VFXExpressionSheet { expressions = new[] { Value(VFXValueType.Float) } },
                new[] { new VFXCPUBufferDesc { layout = layout, capacity = 1, stride = 4, initialData = data } });
            var effect = Effect(asset);
            try
            {
                var bare = new GameObject("bare").AddComponent<VisualEffect>();
                Assert.Null(bare.CreateVFXEventAttribute());
                Object.DestroyImmediate(bare.gameObject);
                var attribute = effect.CreateVFXEventAttribute();
                Assert.True(attribute.HasFloat("spawnCount"));
                Assert.False(attribute.HasInt("spawnCount"));
                Assert.True(attribute.HasVector3("color"));
                Assert.False(attribute.HasFloat("position"));
                Assert.Equal(1f, attribute.GetFloat("spawnCount"));
                Assert.Equal(new Vector3(0.25f, 0.5f, 0.75f), attribute.GetVector3("color"));
                attribute.SetInt("spawnCount", 3);
                attribute.SetFloat("position", 3f);
                Assert.Equal(1f, attribute.GetFloat("spawnCount"));
                Assert.Equal(0, attribute.GetInt("spawnCount"));
                attribute.SetFloat("spawnCount", 7.5f);
                var copy = new VFXEventAttribute(attribute);
                attribute.SetFloat("spawnCount", 2f);
                Assert.Equal(7.5f, copy.GetFloat("spawnCount"));
                var other = effect.CreateVFXEventAttribute();
                other.CopyValuesFrom(copy);
                Assert.Equal(7.5f, other.GetFloat("spawnCount"));
                Assert.Throws<ArgumentNullException>(() => new VFXEventAttribute(null));
                Assert.Throws<ArgumentNullException>(() => other.CopyValuesFrom(null));
                attribute.Dispose();
                Assert.Throws<NullReferenceException>(() => attribute.GetFloat("spawnCount"));
                Assert.Throws<NullReferenceException>(() => attribute.HasFloat("spawnCount"));
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        static VFXLayoutElementDesc Attribute(string name, VFXValueType type, uint element, uint stride) =>
            new VFXLayoutElementDesc { name = name, type = type, offset = new VFXLayoutOffset { structure = stride, element = element } };

        static VisualEffectAsset WithAttributes(uint stride, params VFXLayoutElementDesc[] layout)
        {
            var data = new VFXCPUBufferData();
            for (uint i = 0; i < stride; i++) data.PushFloat(0f);
            return Asset(new VFXExpressionSheet { expressions = new[] { Value(VFXValueType.Float) } },
                new[] { new VFXCPUBufferDesc { layout = layout, capacity = 1, stride = stride, initialData = data } });
        }

        [Fact]
        public void EventAttributesCopyByNameBetweenLayouts()
        {
            var first = Effect(WithAttributes(9, Attribute("spawnCount", VFXValueType.Float, 0, 9), Attribute("lifetime", VFXValueType.Float, 1, 9),
                Attribute("velocity", VFXValueType.Float3, 2, 9), Attribute("color", VFXValueType.Float3, 5, 9), Attribute("alive", VFXValueType.Boolean, 8, 9)));
            var second = Effect(WithAttributes(6, Attribute("spawnCount", VFXValueType.Float, 0, 6), Attribute("color", VFXValueType.Float3, 1, 6),
                Attribute("size", VFXValueType.Float, 4, 6), Attribute("lifetime", VFXValueType.Float, 5, 6)));
            try
            {
                var a = first.CreateVFXEventAttribute();
                a.SetFloat("spawnCount", 5f);
                a.SetFloat("lifetime", 9f);
                a.SetVector3("velocity", new Vector3(1f, 2f, 3f));
                a.SetVector3("color", new Vector3(0.125f, 0.25f, 0.375f));
                a.SetBool("alive", true);
                var b = second.CreateVFXEventAttribute();
                b.SetFloat("size", 4f);
                b.SetVector3("color", new Vector3(0.875f, 0.75f, 0.625f));
                b.CopyValuesFrom(a);
                // Shared names take the other set's values wherever they lie; its own keep theirs.
                Assert.Equal(5f, b.GetFloat("spawnCount"));
                Assert.Equal(9f, b.GetFloat("lifetime"));
                Assert.Equal(new Vector3(0.125f, 0.25f, 0.375f), b.GetVector3("color"));
                Assert.Equal(4f, b.GetFloat("size"));
                Assert.False(b.HasVector3("velocity"));
                var back = first.CreateVFXEventAttribute();
                back.SetVector3("velocity", new Vector3(7f, 8f, 9f));
                back.SetBool("alive", true);
                b.SetFloat("lifetime", 2f);
                back.CopyValuesFrom(b);
                Assert.Equal(2f, back.GetFloat("lifetime"));
                Assert.Equal(new Vector3(7f, 8f, 9f), back.GetVector3("velocity"));
                Assert.True(back.GetBool("alive"));
                Assert.Equal(new Vector3(0.125f, 0.25f, 0.375f), back.GetVector3("color"));
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Fact]
        public void AnAttributeCopyThatDoesNotFitStopsAndSaysSo()
        {
            // "x" is a vector in the first graph and a float at the end of the second.
            var first = Effect(WithAttributes(5, Attribute("spawnCount", VFXValueType.Float, 0, 5), Attribute("x", VFXValueType.Float3, 1, 5),
                Attribute("y", VFXValueType.Float, 4, 5)));
            var second = Effect(WithAttributes(3, Attribute("spawnCount", VFXValueType.Float, 0, 3), Attribute("y", VFXValueType.Float, 1, 3),
                Attribute("x", VFXValueType.Float, 2, 3)));
            try
            {
                var a = first.CreateVFXEventAttribute();
                a.SetFloat("spawnCount", 5f);
                a.SetVector3("x", new Vector3(1f, 2f, 3f));
                a.SetFloat("y", 7f);
                var b = second.CreateVFXEventAttribute();
                b.SetFloat("x", 9f);
                using var capture = new Capture();
                b.CopyValuesFrom(a);
                // What came before the value that does not fit is copied; that value is not.
                Assert.Equal(5f, b.GetFloat("spawnCount"));
                Assert.Equal(7f, b.GetFloat("y"));
                Assert.Equal(9f, b.GetFloat("x"));
                Assert.Contains("'x' is a Float3 in the other set and does not fit this one", Assert.Single(capture.Errors));
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Fact]
        public void TheDoctorListsEffectsWithAnAssetAsNotDrawn()
        {
            const string scene = "--- !u!2083052967 &11\nVisualEffect:\n  m_Enabled: 1\n  m_Asset: {fileID: 8926484042661614526, guid: 0123456789abcdef0123456789abcdef, type: 3}\n"
                + "--- !u!2083052967 &12\nVisualEffect:\n  m_Enabled: 1\n  m_Asset: {fileID: 0}\n";
            var found = new ClassCoverage.Found
            {
                ClassId = 2083052967, TypeName = "VisualEffect", Keys = new List<string> { "m_Enabled", "m_Asset" }, Referenced = new List<string>(),
                Documents = new Dictionary<string, string> { ["Assets/Scene.unity"] = scene },
            };
            var entry = Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes);
            Assert.Equal(ClassReading.Unsupported.ToString(), entry.Reading);
            Assert.Equal("Assets/Scene.unity fileID 11: " + VisualEffect.NotDrawn, entry.Detail);
            // Without an asset there is nothing to draw: the class is simply read.
            found.Documents["Assets/Scene.unity"] = "--- !u!2083052967 &12\nVisualEffect:\n  m_Enabled: 1\n  m_Asset: {fileID: 0}\n";
            entry = Assert.Single(ClassCoverage.Report("6000.0.71f1", new[] { found }).Classes);
            Assert.Equal(ClassReading.Read.ToString(), entry.Reading);
        }

        [Fact]
        public void EditorTuplesCompareByTheirItems()
        {
            var a = new UnityEditor.Tuple<Type, bool>(typeof(int), true);
            var b = UnityEditor.Tuple.Create(typeof(int), true);
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, new UnityEditor.Tuple<Type, bool>(typeof(int), false));
            Assert.Equal("(1, two, 3)", UnityEditor.Tuple.Create(1, "two", 3).ToString());
            var map = new Dictionary<UnityEditor.Tuple<int, string>, int> { [UnityEditor.Tuple.Create(1, "a")] = 5 };
            Assert.Equal(5, map[UnityEditor.Tuple.Create(1, "a")]);
            Assert.True(((IComparable)UnityEditor.Tuple.Create(1, 2)).CompareTo(UnityEditor.Tuple.Create(1, 3)) < 0);
            Assert.Equal(1, ((IComparable)UnityEditor.Tuple.Create(1)).CompareTo(null));
            Assert.Throws<ArgumentException>(() => new UnityEditor.Tuple<int, int, int, int, int, int, int, int>(1, 2, 3, 4, 5, 6, 7, 8));
            var eight = new UnityEditor.Tuple<int, int, int, int, int, int, int, UnityEditor.Tuple<int>>(1, 2, 3, 4, 5, 6, 7, UnityEditor.Tuple.Create(8));
            Assert.Equal("(1, 2, 3, 4, 5, 6, 7, 8)", eight.ToString());
        }

        [Fact]
        public void EditorTupleHashesUseBalancedGroupsAndTheLastEightItems()
        {
            Assert.Equal(1152, UnityEditor.Tuple.Create(1, 2, 3).GetHashCode());
            Assert.Equal(1252, UnityEditor.Tuple.Create(1, 2, 3, 4).GetHashCode());
            var tail = UnityEditor.Tuple.Create(8, 9, 10, 11, 12, 13, 14, 15);
            var a = new UnityEditor.Tuple<int, int, int, int, int, int, int, UnityEditor.Tuple<int, int, int, int, int, int, int, UnityEditor.Tuple<int>>>(1, 2, 3, 4, 5, 6, 7, tail);
            var b = new UnityEditor.Tuple<int, int, int, int, int, int, int, UnityEditor.Tuple<int, int, int, int, int, int, int, UnityEditor.Tuple<int>>>(-1, -2, -3, -4, -5, -6, -7, tail);
            Assert.NotEqual(a, b);
            Assert.Equal(tail.GetHashCode(), a.GetHashCode());
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.Equal(15, ((UnityEditor.ITuple)a).Length);
            Assert.Equal(15, ((UnityEditor.ITuple)a)[14]);
            Assert.Throws<IndexOutOfRangeException>(() => ((UnityEditor.ITuple)a)[-1]);
            Assert.Throws<IndexOutOfRangeException>(() => ((UnityEditor.ITuple)a)[15]);
        }

        [Fact]
        public void MemorySerializerRestoresInPlaceAndCopiesWithReferencesFollowing()
        {
            var first = ScriptableObject.CreateInstance<Link>();
            var second = ScriptableObject.CreateInstance<Link>();
            var outside = ScriptableObject.CreateInstance<Link>();
            try
            {
                first.other = second;
                first.number = 1;
                second.other = outside;
                second.number = 2;
                var snapshot = VFXMemorySerializer.StoreObjectsToByteArray(new ScriptableObject[] { first, second });
                first.number = 10;
                first.other = null;
                var restored = VFXMemorySerializer.ExtractObjects(snapshot, false);
                Assert.Same(first, restored[0]);
                Assert.Equal(1, first.number);
                Assert.Same(second, first.other);

                var copies = VFXMemorySerializer.DuplicateObjects(new ScriptableObject[] { first, second });
                var firstCopy = (Link)copies[0];
                var secondCopy = (Link)copies[1];
                Assert.NotSame(first, firstCopy);
                Assert.Equal(1, firstCopy.number);
                // A reference inside the set follows the copies; one outside stays.
                Assert.Same(secondCopy, firstCopy.other);
                Assert.Same(outside, secondCopy.other);
                foreach (var copy in copies) Object.DestroyImmediate(copy);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(outside);
            }
        }
    }
}
