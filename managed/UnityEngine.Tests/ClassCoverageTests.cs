using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;
using Xunit;

namespace Unbound.Tests
{
    public class ClassCoverageTests
    {
        // A class id no release has, so no other test has warned about it.
        const int Unread = 2147400001;

        static string Document(long fileId) =>
            $"--- !u!{Unread} &{fileId}\nUnreadThing:\n  m_Name: thing\n  m_Payload: 3\n";

        /// <summary>Unbound's diagnostics ("Unbound: ...") bypass Application's log callbacks:
        /// observed at the logger's handler, every call forwarded to the original one.</summary>
        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly int m_ClassId;
            internal readonly List<string> Warnings = new List<string>();

            internal Capture(ILogHandler inner, int classId = Unread) { m_Inner = inner; m_ClassId = classId; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0 ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Warning && message.Contains($"class {m_ClassId}")) lock (Warnings) Warnings.Add(message);
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        [Fact]
        public void AnUnreadClassWarnsOnceNamingTheClassAndFile()
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture(previous);
            logger.logHandler = capture;
            try
            {
                ObjectGraphBuilder.Build(UnityYaml.Parse(Document(1) + Document(2)), "Assets/first.asset", null, ObjectGraphBuilder.Kind.Asset, null);
                ObjectGraphBuilder.Build(UnityYaml.Parse(Document(3)), "Assets/second.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            }
            finally
            {
                logger.logHandler = previous;
            }
            var warning = Assert.Single(capture.Warnings);
            Assert.Contains("UnreadThing", warning);
            Assert.Contains("Assets/first.asset", warning);
            Assert.Contains("unbound doctor", warning);
        }

        [Fact]
        public void BundleSceneDiagnosticsAlwaysPointToDoctor()
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture(previous, Unread + 1);
            logger.logHandler = capture;
            try
            {
                ClassCoverage.Note(new YDocument { ClassId = Unread + 1, TypeName = "BundleThing", Body = YNode.MakeMap() }, "bundle:7/BuildPlayer-Scene");
            }
            finally { logger.logHandler = previous; }
            Assert.Contains("unbound doctor --all --json", Assert.Single(capture.Warnings));
        }

        static ClassCoverage.Entry Listed(int classId, string name, string[] keys, string[] referenced = null) =>
            Assert.Single(ClassCoverage.Report("6000.0.71f1", new[]
            {
                new ClassCoverage.Found { ClassId = classId, TypeName = name, Keys = keys.ToList(), Referenced = referenced?.ToList() },
            }).Classes);

        [Fact]
        public void TheDoctorListingClassifiesEachClass()
        {
            var unknown = Listed(Unread, "UnreadThing", new[] { "m_Name", "m_Payload" });
            Assert.Equal("Unknown", unknown.Reading);
            Assert.Null(unknown.UnityName);
            // ClassId213 now reads its serialized render data through ISerializedAsset.
            Assert.Equal("Read", Listed(213, "Sprite", new[] { "m_Name", "m_Rect" }).Reading);
            Assert.Equal("Read", Listed(213, "Sprite", new[] { "m_ObjectHideFlags", "m_Name" }).Reading);
            Assert.Equal("Read", Listed(23, "MeshRenderer", new[] { "m_Materials" }).Reading);
            Assert.Equal("Read", Listed(84, "RenderTexture", new[] { "m_Width" }).Reading);
            Assert.Equal("Loader", Listed(104, "RenderSettings", new[] { "m_Fog" }).Reading);
            // Scene settings: ignored with only bake inputs, unread with baked data.
            Assert.Equal("Ignored", Listed(29, "OcclusionCullingSettings", new[] { "m_SceneGUID", "m_OcclusionCullingData" }).Reading);
            var baked = Listed(29, "OcclusionCullingSettings", new[] { "m_OcclusionCullingData" }, new[] { "m_OcclusionCullingData" });
            Assert.Equal("Unread", baked.Reading);
            Assert.Equal("OcclusionCullingSettings", baked.UnityName);
            // A scene's baked NavMesh is loaded with it.
            var navMesh = Listed(196, "NavMeshSettings", new[] { "m_NavMeshData" }, new[] { "m_NavMeshData" });
            Assert.Equal("Loader", navMesh.Reading);
            Assert.Equal("NavMeshSettings", navMesh.UnityName);
            Assert.Equal("Ignored", Listed(1002, "EditorExtensionImpl", new[] { "m_Object" }).Reading);
            // Editor scripts can apply a preset: it stays loud.
            Assert.Equal("Unsupported", Listed(181963792, "Preset", new[] { "m_Properties" }).Reading);
        }

        [Fact]
        public void EnabledOnlyComponentsAreReadWithoutAProjectDocument()
        {
            var unread = ClassCoverage.Report("6000.0.71f1", new ClassCoverage.Found[0]).UnreadInRelease;
            Assert.DoesNotContain(unread, e => e.ClassId == 81 || e.ClassId == 124);
            foreach (int id in new[] { 81, 124 })
                Assert.Equal(ClassReading.Read, ClassCoverage.Classify(id, null).Reading);
        }

        [Fact]
        public void LegacyGuiLayerIsRemovedOnlyInTheMeasuredRelease()
        {
            Assert.Equal(ClassReading.Ignored, ClassCoverage.Classify(92, _ => true, "6000.0.71f1").Reading);
            Assert.Equal(ClassReading.Unknown, ClassCoverage.Classify(92, _ => true, "2022.3.1f1").Reading);
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse("--- !u!1 &1\nGameObject:\n  m_Name: legacy\n--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 1}\n--- !u!92 &92\nGUILayer:\n  m_GameObject: {fileID: 1}\n  m_Enabled: 1\n"),
                "Assets/legacy.unity", null, ObjectGraphBuilder.Kind.Asset, null);
            Assert.False(file.Objects.ContainsKey(92));
        }

        [Fact]
        public void EveryRegisteredClassIsOneOfTheReleasesClasses()
        {
            var table = ClassCoverage.UnityTable("6000.0.71f1");
            Assert.NotNull(table);
            Assert.Equal("RenderTexture", table[84].Name);
            Assert.Empty(EngineTypes.RegisteredIds.Where(id => !table.ContainsKey(id)));
            // The release's concrete classes without a reader are listed, settings managers are not.
            var unread = ClassCoverage.Report("6000.0.71f1", new ClassCoverage.Found[0]).UnreadInRelease;
            Assert.Contains(unread, e => e.ClassId == 2083052967);
            // ShaderVariantCollection and LightingSettings read their data now.
            Assert.DoesNotContain(unread, e => e.ClassId == 200 || e.ClassId == 850595691);
            Assert.DoesNotContain(unread, e => e.ClassId == 5 || e.ClassId == 100000);
        }
    }
}
