using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A built-in Material (`unity_builtin_extra`, `unity default resources`) is made with the
    /// render queue override its installed object stores, `m_CustomRenderQueue`: -1 is none (the
    /// material follows its shader's queue), another value is its override (Material.rawRenderQueue
    /// and Material.renderQueue, 6000.0). A stand-in host answers the `builtin-material-queue`
    /// call. Where a row names an installed object, its value is the byte the 6000.0.71f1 install
    /// stores (a Native asset-state observation of the resource file, not a measured API readout);
    /// the host's own reading of the installed files is tested on the Rust side. Rows without an
    /// installed object, the malformed answers and the shader declarations are mechanism
    /// stand-ins (unit extrapolations), not claims about Unity.
    /// </summary>
    public unsafe class BuiltinMaterialQueueTests : IDisposable
    {
        const string Extra = BuiltinShaders.ExtraGuid;
        const string Default = BuiltinShaders.DefaultGuid;
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        static readonly FieldInfo Api = typeof(Native).GetField("s_Api", PrivateStatic);
        static readonly FieldInfo Ready = typeof(Native).GetField("s_Ready", PrivateStatic);
        static readonly IDictionary Objects = (IDictionary)typeof(BuiltinObjects).GetField("s_Objects", PrivateStatic).GetValue(null);
        static readonly IDictionary ShadersByName = (IDictionary)typeof(Shader).GetField("s_ByName", PrivateStatic).GetValue(null);
        static readonly IDictionary BuiltinShaderCache = (IDictionary)typeof(BuiltinShaders).GetField("s_Shaders", PrivateStatic).GetValue(null);
        static readonly IDictionary Replacements = (IDictionary)typeof(FrameBuilder).GetField("s_ReplacementMaterials", PrivateStatic).GetValue(null);
        static readonly MethodInfo ReplacementMaterial = typeof(FrameBuilder).GetMethod("ReplacementMaterial", PrivateStatic);

        /// <summary>The stand-in host's answers by (guid, fileId): status 0 with JSON, else an error.</summary>
        static readonly Dictionary<(string, long), (int Status, string Answer)> s_Answers = new Dictionary<(string, long), (int, string)>();
        /// <summary>Every native call in order: "method guid fileId" for queue calls, else the method.</summary>
        static readonly List<string> s_Calls = new List<string>();

        readonly object m_PreviousApi = Api.GetValue(null), m_PreviousReady = Ready.GetValue(null);
        readonly List<(Dictionary<long, string> Names, Dictionary<long, int> Classes, long Id, string Name, int Class, bool Had)> m_Registered =
            new List<(Dictionary<long, string>, Dictionary<long, int>, long, string, int, bool)>();
        readonly HashSet<object> m_Objects, m_ShadersByName, m_BuiltinShaders, m_Replacements;
        readonly List<string> m_Declared = new List<string>();
        readonly List<Object> m_Owned = new List<Object>();

        const string NotFound = "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";
        /// <summary>Two SubShaders without a Queue tag, as the committed 6000.0.71f1
        /// shaderkeywords rows record Particles/Standard Unlit's (stand-in for the host's answer).</summary>
        const string TwoWithoutQueue = "{\"found\":true,\"builtin\":true,\"subshaders_known\":true,\"keywords_known\":true,\"transient\":false,\"reason\":\"\",\"subshaders\":["
            + "{\"tags\":[],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":0,\"restricted\":false},"
            + "{\"tags\":[],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":0,\"restricted\":false}],\"keywords\":[]}";

        public BuiltinMaterialQueueTests()
        {
            m_Objects = Keys(Objects);
            m_ShadersByName = Keys(ShadersByName);
            m_BuiltinShaders = Keys(BuiltinShaderCache);
            m_Replacements = Keys(Replacements);
            s_Answers.Clear();
            s_Calls.Clear();
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Declarations;
            var api = new NativeApi
            {
                Version = NativeApi.CurrentVersion, Call = &Call, FreeBuffer = &Free, SubmitCommands = &Submit,
                ReleaseMaterial = &Release, ReleaseTexture = &Release, ReleaseMesh = &Release,
            };
            Native.Initialize(&api);
        }

        public void Dispose()
        {
            foreach (var key in Keys(Replacements).Where(k => !m_Replacements.Contains(k)).ToList())
            {
                var copy = (Material)((ITuple)Replacements[key])[0];
                Replacements.Remove(key);
                if (copy) Object.DestroyImmediate(copy, true);
            }
            foreach (var key in Keys(Objects).Where(k => !m_Objects.Contains(k)).ToList())
            {
                var obj = (Object)Objects[key];
                Objects.Remove(key);
                if (obj) Object.DestroyImmediate(obj, true);
            }
            foreach (var key in Keys(ShadersByName).Where(k => !m_ShadersByName.Contains(k)).ToList()) ShadersByName.Remove(key);
            foreach (var key in Keys(BuiltinShaderCache).Where(k => !m_BuiltinShaders.Contains(k)).ToList()) BuiltinShaderCache.Remove(key);
            for (int i = m_Owned.Count - 1; i >= 0; i--)
                if (m_Owned[i]) Object.DestroyImmediate(m_Owned[i], true);
            for (int i = m_Registered.Count - 1; i >= 0; i--)
            {
                var r = m_Registered[i];
                if (r.Had)
                {
                    r.Names[r.Id] = r.Name;
                    r.Classes[r.Id] = r.Class;
                }
                else
                {
                    r.Names.Remove(r.Id);
                    r.Classes.Remove(r.Id);
                }
            }
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
            Api.SetValue(null, m_PreviousApi);
            Ready.SetValue(null, m_PreviousReady);
            s_Answers.Clear();
            s_Calls.Clear();
        }

        static HashSet<object> Keys(IDictionary dictionary) => new HashSet<object>(dictionary.Keys.Cast<object>());

        string Declarations(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            var name = document.RootElement.GetProperty("name").GetString();
            m_Declared.Add(name);
            return method == "shader-declarations" && (name == "Particles/Standard Unlit" || name == "Standard") ? TwoWithoutQueue : NotFound;
        }

        [UnmanagedCallersOnly]
        static int Call(byte* method, int methodLength, byte* request, int requestLength, NativeBuffer* response)
        {
            int status = 0;
            string result;
            var name = Encoding.UTF8.GetString(method, methodLength);
            try
            {
                if (name == "builtin-material-queue")
                {
                    using var input = JsonDocument.Parse(Encoding.UTF8.GetString(request, requestLength));
                    var guid = input.RootElement.GetProperty("guid").GetString();
                    long fileId = input.RootElement.GetProperty("fileId").GetInt64();
                    s_Calls.Add(name + " " + guid + " " + fileId);
                    (status, result) = s_Answers.TryGetValue((guid, fileId), out var answer) ? answer : (1, "no object " + fileId);
                }
                else
                {
                    s_Calls.Add(name);
                    // Built-in shaders have no source here; nothing else is answered.
                    (status, result) = name == "builtin-shader-source" ? (0, "null") : (1, "not answered by this stand-in host");
                }
            }
            catch (Exception e)
            {
                (status, result) = (1, e.Message);
            }
            var bytes = Encoding.UTF8.GetBytes(result);
            response->Data = (byte*)NativeMemory.Alloc((nuint)Math.Max(1, bytes.Length));
            response->Length = bytes.Length;
            bytes.CopyTo(new Span<byte>(response->Data, bytes.Length));
            return status;
        }

        [UnmanagedCallersOnly]
        static void Free(NativeBuffer buffer) => NativeMemory.Free(buffer.Data);

        [UnmanagedCallersOnly]
        static void Submit(byte* commands, long length) { }

        [UnmanagedCallersOnly]
        static void Release(ulong handle) { }

        void Register(string guid, long fileId, string name, int classId)
        {
            bool extra = guid == Extra;
            var names = extra ? BuiltinShaders.ExtraNames : BuiltinShaders.DefaultNames;
            var classes = extra ? BuiltinObjects.ExtraClasses : BuiltinObjects.DefaultClasses;
            bool had = names.TryGetValue(fileId, out var oldName) & classes.TryGetValue(fileId, out var oldClass);
            m_Registered.Add((names, classes, fileId, oldName, oldClass, had));
            names[fileId] = name;
            classes[fileId] = classId;
        }

        /// <summary>A built-in Material in the profile's table whose installed object the host
        /// answers with <paramref name="answer"/> (status 0) or an error, and the built-in shader
        /// its name is made with.</summary>
        void Builtin(string guid, long fileId, string name, string shader, long shaderFileId, int status, string answer)
        {
            Register(guid, fileId, name, 21);
            Register(Extra, shaderFileId, shader, 48);
            s_Answers[(guid, fileId)] = (status, answer);
        }

        void Builtin(string guid, long fileId, string name, string shader, long shaderFileId, int stored) =>
            Builtin(guid, fileId, name, shader, shaderFileId, 0,
                "{\"classId\":21,\"name\":\"" + name + "\",\"customRenderQueue\":" + stored + "}");

        static int Asked(string guid, long fileId) => s_Calls.Count(c => c == "builtin-material-queue " + guid + " " + fileId);

        static List<string> Errors(Action action)
        {
            var errors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error) errors.Add(message); };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return errors;
        }

        // ------------------------------------------------------------------ installed objects

        /// <summary>Red on 1f41393e (assertion): no `builtin-material-queue` call is made (Asked 0)
        /// and the override rows come out -1 instead of the stored 3000 / 2000. Every row is the
        /// 6000.0.71f1 install's class-21 object (Native asset-state observation:
        /// builtin-material-state.json), with the shader its name is made with (the stored shader
        /// is not read here; SpatialMapping and FrameDebugger names are made with Standard).</summary>
        [Theory]
        [InlineData(Extra, 10301L, "Default-Particle", "Legacy Shaders/Particles/Alpha Blended Premultiply", 207L, -1)]
        [InlineData(Extra, 10302L, "Default-Diffuse", "Legacy Shaders/Diffuse", 7L, -1)]
        [InlineData(Extra, 10303L, "Default-Material", "Standard", 46L, -1)]
        [InlineData(Extra, 10304L, "Default-Skybox", "Skybox/Procedural", 106L, -1)]
        [InlineData(Extra, 10306L, "Default-Line", "Legacy Shaders/Particles/Alpha Blended Premultiply", 207L, -1)]
        [InlineData(Extra, 10308L, "Default-ParticleSystem", "Particles/Standard Unlit", 211L, 3000)]
        [InlineData(Extra, 10650L, "Default-Terrain-Diffuse", "Nature/Terrain/Diffuse", 10505L, -1)]
        [InlineData(Extra, 10651L, "Default-Terrain-Specular", "Nature/Terrain/Specular", 10620L, -1)]
        [InlineData(Extra, 10652L, "Default-Terrain-Standard", "Nature/Terrain/Standard", 10623L, -1)]
        [InlineData(Extra, 10754L, "Sprites-Default", "Sprites/Default", 10753L, -1)]
        [InlineData(Extra, 10758L, "Sprites-Mask", "Sprites/Mask", 10757L, -1)]
        [InlineData(Extra, 15302L, "SpatialMappingOcclusion", "Standard", 46L, 2000)]
        [InlineData(Extra, 15303L, "SpatialMappingWireframe", "Standard", 46L, 2000)]
        [InlineData(Default, 10756L, "FrameDebuggerRenderTargetDisplay", "Standard", 46L, -1)]
        public void InstalledBuiltinMaterialsAreMadeWithTheirStoredRawQueue(string guid, long fileId, string name, string shader, long shaderFileId, int stored)
        {
            Builtin(guid, fileId, name, shader, shaderFileId, stored);
            var material = BuiltinShaders.Resolve(guid, fileId) as Material;
            Assert.NotNull(material);
            Assert.Equal(1, Asked(guid, fileId));
            Assert.Equal(stored, material.rawRenderQueue);
            Assert.Equal(name, material.name);
            Assert.Equal(shader, material.shader.name);
        }

        /// <summary>Red on 1f41393e (assertion): Default-ParticleSystem's renderQueue is its
        /// shader's 2000, not its stored override 3000. -1 follows the shader (documented); the
        /// override does not, whatever the shader's queue. Both values are the install's (Native
        /// asset-state observation); the shader declarations are stand-ins.</summary>
        [Fact]
        public void NoStoredOverrideFollowsTheShaderAndAStoredOverrideDoesNot()
        {
            Builtin(Extra, 10303, "Default-Material", "Standard", 46, -1);
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            var plain = (Material)BuiltinShaders.Resolve(Extra, 10303);
            var particles = (Material)BuiltinShaders.Resolve(Extra, 10308);
            Assert.Equal(-1, plain.rawRenderQueue);
            Assert.Equal(plain.shader.renderQueue, plain.renderQueue);
            Assert.Equal(2000, particles.shader.renderQueue);
            Assert.Equal(3000, particles.rawRenderQueue);
            Assert.Equal(3000, particles.renderQueue);
        }

        /// <summary>Red on 1f41393e (assertion): the host is never asked (Asked 0) and the raw
        /// queue is -1. During the load only the stored queue is read: no shader declarations or
        /// passes are asked for (no first use of Shader.renderQueue), and the queue call comes
        /// before any shader call; the value is in place when the material is handed out.</summary>
        [Fact]
        public void TheStoredQueueIsReadBeforeTheMaterialIsHandedOutWithoutAShaderQueueQuery()
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            var material = (Material)BuiltinObjects.Get(Extra, 10308, "Default-ParticleSystem");
            Assert.Equal(1, Asked(Extra, 10308));
            Assert.Equal("builtin-material-queue " + Extra + " 10308", s_Calls[0]);
            Assert.DoesNotContain("shader-passes", s_Calls);
            Assert.Empty(m_Declared);
            Assert.Equal(3000, material.rawRenderQueue);
        }

        /// <summary>Red on 1f41393e (assertion: Asked 0). The material is made once; later
        /// references get the same object without asking again.</summary>
        [Fact]
        public void AMaterialMadeOnceIsHandedOutAgainWithoutAskingAgain()
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            var first = BuiltinShaders.Resolve(Extra, 10308);
            var second = BuiltinShaders.Resolve(Extra.ToUpperInvariant(), 10308);
            Assert.Same(first, second);
            Assert.Equal(1, Asked(Extra, 10308));
        }

        /// <summary>Red on 1f41393e (assertion): the shader setter's documented reset needs the
        /// stored override first (raw is -1 before and after there). Preserved: a shader change
        /// resets the built-in's override to the new shader's queue.</summary>
        [Fact]
        public void AShaderChangeStillResetsTheStoredOverride()
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            Builtin(Extra, 10303, "Default-Material", "Standard", 46, -1);
            var particles = (Material)BuiltinShaders.Resolve(Extra, 10308);
            Assert.Equal(3000, particles.rawRenderQueue);
            particles.shader = Shader.Find("Standard");
            Assert.Equal(-1, particles.rawRenderQueue);
        }

        // ------------------------------------------------------------------ failures

        /// <summary>Red on 1f41393e (assertion): a material is made with -1 although nothing was
        /// read. A record the host cannot read, of another class or with no integer queue makes no
        /// material: an error names the object, nothing is cached and the next reference asks
        /// again. Stand-in answers (mechanism, not Unity data).</summary>
        [Theory]
        [InlineData(1, "built-in Material 10308 of unity_builtin_extra: no object 10308")]
        [InlineData(1, "built-in Material 10308: object 10308 is class 48, not a Material (21)")]
        [InlineData(1, "unknown native method `builtin-material-queue`")]
        [InlineData(0, "{\"classId\":48,\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":-1}")]
        [InlineData(0, "{\"classId\":21,\"name\":\"Default-ParticleSystem\"}")]
        [InlineData(0, "{\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":3000}")]
        [InlineData(0, "{\"classId\":21,\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":\"3000\"}")]
        [InlineData(0, "{\"classId\":21,\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":3000.5}")]
        [InlineData(0, "{\"classId\":21,\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":4294967295}")]
        [InlineData(0, "{\"classId\":21,\"name\":\"Default-ParticleSystem\",\"customRenderQueue\":null}")]
        [InlineData(0, "null")]
        [InlineData(0, "[]")]
        [InlineData(0, "not json")]
        public void AnUnreadableStoredQueueMakesNoMaterial(int status, string answer)
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, status, answer);
            Object material = null;
            var errors = Errors(() => material = BuiltinShaders.Resolve(Extra, 10308));
            Assert.Null(material);
            var error = Assert.Single(errors);
            Assert.Contains("built-in Material 'Default-ParticleSystem' ({fileID: 10308, guid: " + Extra + "})", error);
            Assert.Contains("stored render queue", error);
            Assert.DoesNotContain(Keys(Objects).Cast<ValueTuple<string, long>>(), k => k == (Extra, 10308L));
            Errors(() => BuiltinShaders.Resolve(Extra, 10308));
            Assert.Equal(2, Asked(Extra, 10308));
        }

        /// <summary>Red on 1f41393e (assertion): the material is made with -1. Without a host the
        /// installed object cannot be read: no material, an error naming it.</summary>
        [Fact]
        public void WithoutAHostNoBuiltinMaterialIsMade()
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            Ready.SetValue(null, false);
            Object material = null;
            var errors = Errors(() => material = BuiltinShaders.Resolve(Extra, 10308));
            Assert.Null(material);
            var error = Assert.Single(errors);
            Assert.Contains("built-in Material 'Default-ParticleSystem' ({fileID: 10308, guid: " + Extra + "})", error);
            Assert.Empty(s_Calls);
        }

        // ------------------------------------------------------------------ preserved paths

        /// <summary>Red on 1f41393e (assertion): the copy's raw queue is -1 and it sorts at the
        /// replacement's 2001. The replacement copy (D1) keeps the built-in's stored override
        /// across the replacement shader exactly as it keeps any material's; ordering of custom
        /// queues under replacement stays unmeasured. A no-override built-in still follows the
        /// replacement (guard: passes on 1f41393e too).</summary>
        [Fact]
        public void TheReplacementCopyKeepsAStoredOverrideAndFollowsTheReplacementWithoutOne()
        {
            Builtin(Extra, 10308, "Default-ParticleSystem", "Particles/Standard Unlit", 211, 3000);
            Builtin(Extra, 10303, "Default-Material", "Standard", 46, -1);
            var replacement = Shader.Create("Tests/BuiltinMaterialQueue/Replacement", 2001);
            m_Owned.Add(replacement);
            var particles = (Material)BuiltinShaders.Resolve(Extra, 10308);
            var plain = (Material)BuiltinShaders.Resolve(Extra, 10303);
            var plainCopy = (Material)ReplacementMaterial.Invoke(null, new object[] { plain, replacement });
            Assert.Same(replacement, plainCopy.shader);
            Assert.Equal(-1, plainCopy.rawRenderQueue);
            Assert.Equal(2001, plainCopy.renderQueue);
            var copy = (Material)ReplacementMaterial.Invoke(null, new object[] { particles, replacement });
            Assert.Same(replacement, copy.shader);
            Assert.Equal(3000, copy.rawRenderQueue);
            Assert.Equal(3000, copy.renderQueue);
            Assert.Equal(3000, particles.rawRenderQueue);
        }

        /// <summary>Guard (passes on 1f41393e too): a saved Material is read by its own reader
        /// with its own stored queue; the built-in queue call is not made for it, even when its
        /// shader is a built-in one.</summary>
        [Fact]
        public void ASavedMaterialKeepsItsOwnReader()
        {
            Register(Extra, 211, "Particles/Standard Unlit", 48);
            var material = new Material();
            m_Owned.Add(material);
            ((ISerializedAsset)material).ReadSerialized(TestYaml.DocumentBody(@"Material:
  m_Name: Saved
  m_Shader: {fileID: 211, guid: 0000000000000000f000000000000000, type: 0}
  m_CustomRenderQueue: 2450
"), new BuiltinReferences());
            Assert.Equal("Particles/Standard Unlit", material.shader.name);
            Assert.Equal(2450, material.rawRenderQueue);
            Assert.DoesNotContain(s_Calls, c => c.StartsWith("builtin-material-queue", StringComparison.Ordinal));
        }

        sealed class BuiltinReferences : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.Guid != null ? BuiltinShaders.Resolve(reference.Guid, reference.FileId) : null;
        }
    }
}
