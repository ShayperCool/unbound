using UnityEngine.Audio;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Instantiated mixer groups and snapshots (Unity 6000.0.71f1, tests/projects/mixerclone).</summary>
    public sealed class AudioMixerCloneTests
    {
        [Fact]
        public void AnInstantiatedGroupKeepsItsMixerButIsNoneOfItsGroups()
        {
            var mixer = new AudioMixer { name = "Mixer" };
            var master = new AudioMixerGroup { name = "Master", m_Mixer = mixer, m_GroupId = "g", m_VolumeGuid = "v" };
            var music = new AudioMixerGroup { name = "Music", m_Mixer = mixer, m_Parent = master };
            master.m_Children.Add(music);
            mixer.m_Groups.Add(master);
            mixer.m_Groups.Add(music);
            var clone = Object.Instantiate(master);
            Assert.NotSame(master, clone);
            Assert.Equal("Master(Clone)", clone.name);
            Assert.Same(mixer, clone.audioMixer);
            Assert.DoesNotContain(clone, mixer.m_Groups);
            Assert.Equal(new[] { music }, clone.m_Children);
            Assert.Equal("v", clone.m_VolumeGuid);
            Assert.True(clone is UnityEngine.Internal.ISubAssetNotDuplicatable);
        }

        [Fact]
        public void AnInstantiatedSnapshotCopiesItsValues()
        {
            var mixer = new AudioMixer { name = "Mixer" };
            var snapshot = new AudioMixerSnapshot { name = "Snapshot", m_Mixer = mixer, m_SnapshotId = "s" };
            snapshot.m_Values["p"] = 2f;
            mixer.m_Snapshots.Add(snapshot);
            var clone = Object.Instantiate(snapshot);
            Assert.Equal("Snapshot(Clone)", clone.name);
            Assert.Same(mixer, clone.audioMixer);
            Assert.DoesNotContain(clone, mixer.m_Snapshots);
            Assert.Equal(2f, clone.m_Values["p"]);
            snapshot.m_Values["p"] = 3f;
            Assert.Equal(2f, clone.m_Values["p"]);
        }
    }
}
