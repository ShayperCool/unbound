using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>When a controller read from its file can be written whole from its objects once
    /// objects are added to it (tests/projects/scenesave controller-additions), and when that
    /// would lose something of the file and it is refused.</summary>
    public sealed class ControllerAdoptionTests : System.IDisposable
    {
        const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
        const string Controller = "--- !u!91 &9100000\nAnimatorController:\n  m_Name: Probe\n";
        const string State = "--- !u!1102 &1102000\nAnimatorState:\n  m_Name: Idle\n";
        readonly AnimatorController m_Controller = new AnimatorController();
        readonly AnimatorState m_State = new AnimatorState();

        public void Dispose()
        {
            Object.DestroyImmediate(m_Controller);
            Object.DestroyImmediate(m_State);
        }

        Dictionary<long, Object> Both() => new Dictionary<long, Object> { [9100000] = m_Controller, [1102000] = m_State };

        [Fact]
        public void AFileReadWholeWithKnownReferencesIsWritten()
        {
            Assert.Null(AssetDatabase.AdoptionRefusal(Header + Controller + State, Both(), Header + Controller + State));
            var withClip = Header + Controller + State + "  m_Motion: {fileID: 7400000, guid: 0000000000000000000000000000ab01, type: 2}\n";
            Assert.Null(AssetDatabase.AdoptionRefusal(withClip, Both(), withClip));
        }

        [Fact]
        public void AnythingTheWholeWriteWouldLoseIsRefused()
        {
            // A document the load did not make an object of (a skipped one, say).
            Assert.NotNull(AssetDatabase.AdoptionRefusal(Header + Controller + State, new Dictionary<long, Object> { [9100000] = m_Controller }, null));
            // An object that is gone.
            Assert.NotNull(AssetDatabase.AdoptionRefusal(Header + Controller + State, new Dictionary<long, Object> { [9100000] = m_Controller, [1102000] = null }, null));
            // A reference written back as none (to an asset or object Unbound cannot find).
            var missing = Header + Controller + State + "  m_Motion: {fileID: 7400000, guid: 0123456789abcdef0123456789abcdef, type: 2}\n";
            var cleared = Header + Controller + State + "  m_Motion: {fileID: 0}\n";
            Assert.NotNull(AssetDatabase.AdoptionRefusal(missing, Both(), cleared));
            var dangling = Header + Controller + State + "  m_Transitions:\n  - {fileID: 4242}\n";
            Assert.NotNull(AssetDatabase.AdoptionRefusal(dangling, Both(), Header + Controller + State + "  m_Transitions:\n  - {fileID: 0}\n"));
            // Binary serialization.
            Assert.NotNull(AssetDatabase.AdoptionRefusal("binary", Both(), null));
        }
    }
}
