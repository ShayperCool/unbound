using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AssetDatabase.SaveAssets' OnWillSaveAssets step as Unity's published C# runs it
    /// (AssetModificationProcessorInternal.OnWillSaveAssets): each processor in turn gets the paths
    /// still to save, a non-null result replaces them, blank paths are dropped with a warning and a
    /// callback of another shape is skipped with one; and a graph file's type names kept as the
    /// file holds them where they name the same type (VisualEffectWrites, tests/projects/vfxwrites).
    /// </summary>
    public sealed class OnWillSaveAssetsTests : IDisposable
    {
        static readonly List<string> s_Calls = new List<string>();
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;

        public OnWillSaveAssetsTests()
        {
            s_Calls.Clear();
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
        }

        public void Dispose()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            m_Logger.logHandler = m_PreviousHandler;
            s_Calls.Clear();
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;
            internal CaptureHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                    m_Logs.Add($"{type}: {(args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args))}");
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        /// <summary>Adds a path to what it is given.</summary>
        public sealed class Adding : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_Calls.Add("adding[" + string.Join(",", paths) + "]");
                var result = new List<string>(paths) { "Assets/Added.asset" };
                return result.ToArray();
            }
        }

        /// <summary>Returns null: the paths stay as they were.</summary>
        public sealed class Keeping : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_Calls.Add("keeping[" + string.Join(",", paths) + "]");
                return null;
            }
        }

        /// <summary>Keeps one path only.</summary>
        public sealed class Filtering : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                s_Calls.Add("filtering[" + string.Join(",", paths) + "]");
                return Array.FindAll(paths, p => p.EndsWith(".vfx", StringComparison.Ordinal));
            }
        }

        /// <summary>A callback of another shape.</summary>
        public sealed class WrongShape : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(List<string> paths)
            {
                s_Calls.Add("wrong");
                return null;
            }
        }

        public sealed class Silent : AssetModificationProcessor { }

        [Fact]
        public void EachProcessorGetsThePathsTheOneBeforeReturned()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(Silent), typeof(Adding), typeof(Keeping), typeof(Filtering) };
            var saved = AssetModificationProcessorInternal.OnWillSaveAssets(new[] { "Assets/A.vfx", "Assets/B.mat" });
            Assert.Equal(new[] { "adding[Assets/A.vfx,Assets/B.mat]", "keeping[Assets/A.vfx,Assets/B.mat,Assets/Added.asset]", "filtering[Assets/A.vfx,Assets/B.mat,Assets/Added.asset]" }, s_Calls);
            Assert.Equal(new[] { "Assets/A.vfx" }, saved);
            Assert.Empty(m_Logs);
        }

        /// <summary>The engine's own processors (the terrain editor's, which saves every path it is
        /// given, as Unity's C# does) keep the paths: a save with no project processor saves them all.</summary>
        [Fact]
        public void TheEnginesOwnProcessorsKeepEveryPath()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = null;
            var saved = AssetModificationProcessorInternal.OnWillSaveAssets(new[] { "Assets/A.asset", "Assets/B.mat" });
            Assert.Equal(new[] { "Assets/A.asset", "Assets/B.mat" }, saved);
            Assert.Empty(m_Logs);
        }

        [Fact]
        public void NoneDirtyStillCallsWithAnEmptyList()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(Keeping) };
            var saved = AssetModificationProcessorInternal.OnWillSaveAssets(Array.Empty<string>());
            Assert.Equal(new[] { "keeping[]" }, s_Calls);
            Assert.Empty(saved);
        }

        [Fact]
        public void BlankPathsAreDroppedWithAWarning()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(Keeping) };
            var saved = AssetModificationProcessorInternal.OnWillSaveAssets(new[] { "Assets/A.vfx", " ", null });
            Assert.Equal(new[] { "keeping[Assets/A.vfx]" }, s_Calls);
            Assert.Equal(new[] { "Assets/A.vfx" }, saved);
            Assert.Equal(new[] { "Warning: OnWillSaveAssets: Skipped 2 null or empty path(s)." }, m_Logs);
        }

        [Fact]
        public void ACallbackOfAnotherShapeIsSkippedWithAWarning()
        {
            AssetModificationProcessorInternal.ProcessorsForTests = new[] { typeof(WrongShape), typeof(Keeping) };
            var saved = AssetModificationProcessorInternal.OnWillSaveAssets(new[] { "Assets/A.vfx" });
            Assert.Equal(new[] { "keeping[Assets/A.vfx]" }, s_Calls);
            Assert.Equal(new[] { "Assets/A.vfx" }, saved);
            Assert.Single(m_Logs);
            Assert.StartsWith("Warning: Parameter type mismatch at parameter 0. Expected: System.String[] Got: System.Collections.Generic.List`1[System.String]", m_Logs[0]);
        }

        static YText TypeMap(string raw)
        {
            var map = YText.Map();
            map.Set("m_SerializableType", YText.Scalar(raw));
            return map;
        }

        const string MonoSingle = "System.Single, mscorlib, Version=4.0.0.0, Culture=neutral,\n    PublicKeyToken=b77a5c561934e089";

        [Fact]
        public void AGraphTypeNameOfTheSameTypeKeepsTheFilesText()
        {
            var written = TypeMap(typeof(float).AssemblyQualifiedName);
            VisualEffectWrites.KeepTypeNames(TypeMap(MonoSingle), written, "Assets/A.vfx");
            Assert.Equal(MonoSingle, written.Get("m_SerializableType").Raw);
        }

        [Fact]
        public void AChangedTypeNameIsWrittenAsMonosName()
        {
            var written = TypeMap(typeof(int).AssemblyQualifiedName);
            VisualEffectWrites.KeepTypeNames(TypeMap(MonoSingle), written, "Assets/A.vfx");
            Assert.Equal("System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                written.Get("m_SerializableType").Raw);
        }

        [Fact]
        public void AChangedOtherClassLibraryTypeNameIsRefused()
        {
            var written = TypeMap(typeof(decimal).AssemblyQualifiedName);
            var e = Assert.Throws<NotSupportedException>(() => VisualEffectWrites.KeepTypeNames(TypeMap(MonoSingle), written, "Assets/A.vfx"));
            Assert.Contains("System.Decimal, System.Private.CoreLib", e.Message);
        }

        /// <summary>A sequence's item maps sit one level below their key (an operator's m_Type and
        /// m_Operands: the package's Editor-written OrientFaceCamera.vfx and 05_Head_Trail.vfx).</summary>
        [Fact]
        public void TypeNamesInSequencesAreLaidOutAtTheirItemsLevel()
        {
            var body = YText.Map();
            var types = YText.Seq();
            types.Items.Add(TypeMap(typeof(float).AssemblyQualifiedName));
            body.Set("m_Type", types);
            var operands = YText.Seq();
            var operand = YText.Map();
            operand.Set("name", YText.Scalar("a"));
            operand.Set("type", TypeMap(typeof(float).AssemblyQualifiedName));
            operands.Items.Add(operand);
            body.Set("m_Operands", operands);
            VisualEffectWrites.MonoTypeNames(body, 1, "Assets/A.vfx");
            Assert.Equal("System.Single, mscorlib, Version=4.0.0.0, Culture=neutral,\n      PublicKeyToken=b77a5c561934e089",
                body.Get("m_Type").Items[0].Get("m_SerializableType").Raw);
            Assert.Equal("System.Single, mscorlib, Version=4.0.0.0, Culture=neutral,\n        PublicKeyToken=b77a5c561934e089",
                body.Get("m_Operands").Items[0].Get("type").Get("m_SerializableType").Raw);
        }

        /// <summary>A part an existing document gains (an operand added) is written as a new
        /// object's: its .NET names made Mono's.</summary>
        [Fact]
        public void APartAnExistingDocumentGainsGetsMonosNames()
        {
            var old = YText.Map();
            var oldOperands = YText.Seq();
            var a = YText.Map();
            a.Set("type", TypeMap(MonoSingle));
            oldOperands.Items.Add(a);
            old.Set("m_Operands", oldOperands);
            var written = YText.Map();
            var operands = YText.Seq();
            var a2 = YText.Map();
            a2.Set("type", TypeMap(typeof(float).AssemblyQualifiedName));
            operands.Items.Add(a2);
            var b = YText.Map();
            b.Set("type", TypeMap(typeof(float).AssemblyQualifiedName));
            operands.Items.Add(b);
            written.Set("m_Operands", operands);
            VisualEffectWrites.KeepTypeNames(old, written, "Assets/A.vfx");
            Assert.Equal(MonoSingle, written.Get("m_Operands").Items[0].Get("type").Get("m_SerializableType").Raw);
            // At the item's type's level (3): continued at 8 spaces, as 05_Head_Trail.vfx has it.
            Assert.Equal("System.Single, mscorlib, Version=4.0.0.0, Culture=neutral,\n        PublicKeyToken=b77a5c561934e089",
                written.Get("m_Operands").Items[1].Get("type").Get("m_SerializableType").Raw);
        }

        [Fact]
        public void ANewTypeNameOutsideTheClassLibraryIsWritten()
        {
            var written = TypeMap("UnityEngine.Vector3, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
            VisualEffectWrites.KeepTypeNames(TypeMap(MonoSingle), written, "Assets/A.vfx");
            Assert.StartsWith("UnityEngine.Vector3", written.Get("m_SerializableType").Raw);
        }

        /// <summary>A new object's .NET class library name is written as the Editor's Mono names it,
        /// laid out at its level as the writer lays out a string.</summary>
        [Fact]
        public void ANewObjectsClassLibraryTypeNameIsWrittenAsMonosName()
        {
            var body = YText.Map();
            var property = YText.Map();
            var type = YText.Map();
            type.Set("m_SerializableType", YText.Scalar(typeof(float).AssemblyQualifiedName));
            property.Set("m_serializedType", type);
            body.Set("m_Property", property);
            VisualEffectWrites.MonoTypeNames(body, 1, "Assets/A.vfx");
            // The vfxwrites fixture's text for a float slot at this depth (BoundsAuto.vfx).
            Assert.Equal("System.Single, mscorlib, Version=4.0.0.0, Culture=neutral,\n        PublicKeyToken=b77a5c561934e089",
                body.Get("m_Property").Get("m_serializedType").Get("m_SerializableType").Raw);
        }

        [Fact]
        public void ANewObjectsOtherClassLibraryTypeNameIsRefused()
        {
            var body = TypeMap(typeof(decimal).AssemblyQualifiedName);
            var e = Assert.Throws<NotSupportedException>(() => VisualEffectWrites.MonoTypeNames(body, 1, "Assets/A.vfx"));
            Assert.Contains("System.Decimal", e.Message);
        }

        [Fact]
        public void ANewObjectsEngineTypeNameIsLeftAsWritten()
        {
            const string vector = "UnityEngine.Vector3, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
            var body = TypeMap(vector);
            VisualEffectWrites.MonoTypeNames(body, 1, "Assets/A.vfx");
            Assert.Equal(vector, body.Get("m_SerializableType").Raw);
        }

        [Fact]
        public void NestedTypeNamesAreKeptToo()
        {
            var old = YText.Map();
            old.Set("m_Property", TypeMap(MonoSingle));
            var written = YText.Map();
            written.Set("m_Property", TypeMap(typeof(float).AssemblyQualifiedName));
            VisualEffectWrites.KeepTypeNames(old, written, "Assets/A.vfx");
            Assert.Equal(MonoSingle, written.Get("m_Property").Get("m_SerializableType").Raw);
        }
    }
}
