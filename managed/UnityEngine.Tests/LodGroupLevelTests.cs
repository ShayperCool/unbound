using System;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>Which level of a LODGroup a camera draws (Unity 6000.0.71f1, tests/projects/lod):
    /// the probe's group, levels at heights 0.6, 0.3 and 0.1 of a group of size 1.</summary>
    public class LodGroupLevelTests : IDisposable
    {
        readonly GameObject m_Root = new GameObject("LodGroupLevelTests");
        readonly Camera m_Camera = new GameObject("LodGroupLevelTests Camera").AddComponent<Camera>();
        readonly LODGroup m_Group;
        readonly float m_Bias = QualitySettings.lodBias;
        readonly int m_Maximum = QualitySettings.maximumLODLevel;

        public LodGroupLevelTests()
        {
            m_Group = m_Root.AddComponent<LODGroup>();
            m_Group.SetLODs(new[] { new LOD(0.6f, new Renderer[0]), new LOD(0.3f, new Renderer[0]), new LOD(0.1f, new Renderer[0]) });
            m_Group.size = 1f;
            m_Group.localReferencePoint = Vector3.zero;
            QualitySettings.lodBias = 1f;
            QualitySettings.maximumLODLevel = 0;
            m_Camera.fieldOfView = 60f;
        }

        public void Dispose()
        {
            QualitySettings.lodBias = m_Bias;
            QualitySettings.maximumLODLevel = m_Maximum;
            Object.DestroyImmediate(m_Root);
            Object.DestroyImmediate(m_Camera.gameObject);
        }

        int Level(float distance) => m_Group.LevelFor(m_Camera, new Vector3(0f, 0f, -distance));

        /// <summary>The probe's base sweep: levels end at 1.4434, 2.8868 and 8.6603 (the size over
        /// the height, over 2 tan 30 degrees).</summary>
        [Fact]
        public void LevelsEndWhereTheGroupIsAsHighAsTheirTransitionHeight()
        {
            Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, -1 }, new[] { Level(0.5f), Level(1.44f), Level(1.46f), Level(2.88f), Level(2.9f), Level(8.66f), Level(8.68f) });
            // From aside the distance counts, not the depth.
            Assert.Equal(2, m_Group.LevelFor(m_Camera, new Vector3(3f, 0f, -8f)));
            Assert.Equal(-1, m_Group.LevelFor(m_Camera, new Vector3(3f, 0f, -8.2f)));
        }

        /// <summary>At equality the level has ended: with a field of view of 90 degrees the last
        /// level reaches to 5 exactly, and the camera at 5 draws nothing; an orthographic camera
        /// of size 5 neither, at any distance.</summary>
        [Fact]
        public void ALevelEndsAtEquality()
        {
            m_Camera.fieldOfView = 90f;
            Assert.Equal(2, Level(4.98f));
            Assert.Equal(-1, Level(5f));
            Assert.Equal(1, Level(5f / 3f - 0.01f));
            m_Camera.orthographic = true;
            foreach (float distance in new[] { 1f, 50f })
            {
                m_Camera.orthographicSize = 5f;
                Assert.Equal(-1, Level(distance));
                m_Camera.orthographicSize = 4.9999995f;
                Assert.Equal(2, Level(distance));
                m_Camera.orthographicSize = 0.83f;
                Assert.Equal(0, Level(distance));
                m_Camera.orthographicSize = 0.84f;
                Assert.Equal(1, Level(distance));
            }
        }

        /// <summary>The bias, the group's scale and size stretch the reaches; the maximum level
        /// skips the levels before it and a forced level is drawn anywhere.</summary>
        [Fact]
        public void BiasScaleSizeMaximumAndForcedLevels()
        {
            QualitySettings.lodBias = 2f;
            Assert.Equal(new[] { 0, 1 }, new[] { Level(2.88f), Level(2.9f) });
            QualitySettings.lodBias = 0.5f;
            Assert.Equal(new[] { 1, 2, -1 }, new[] { Level(1.44f), Level(1.46f), Level(4.34f) });
            QualitySettings.lodBias = 1f;
            m_Root.transform.localScale = new Vector3(1f, 2f, 0.5f);
            Assert.Equal(new[] { 0, 1 }, new[] { Level(2.88f), Level(2.9f) });
            m_Root.transform.localScale = Vector3.one;
            m_Group.size = 3f;
            Assert.Equal(new[] { 0, 1 }, new[] { Level(4.32f), Level(4.34f) });
            m_Group.size = 1f;
            QualitySettings.maximumLODLevel = 1;
            Assert.Equal(new[] { 1, 1, 2, -1 }, new[] { Level(0.5f), Level(2.88f), Level(2.9f), Level(8.68f) });
            QualitySettings.maximumLODLevel = 3;
            Assert.Equal(-1, Level(0.5f));
            QualitySettings.maximumLODLevel = 0;
            m_Group.ForceLOD(2);
            Assert.Equal(new[] { 2, 2 }, new[] { Level(0.5f), Level(100f) });
            m_Group.ForceLOD(-1);
            Assert.Equal(0, Level(0.5f));
        }

        /// <summary>A level of height 0 reaches any distance; a group without size is not drawn
        /// (Unbound's choice, not measured: its reach is 0 / 0).</summary>
        [Fact]
        public void AZeroHeightReachesEverywhereAndNoSizeNowhere()
        {
            m_Group.SetLODs(new[] { new LOD(0.5f, new Renderer[0]), new LOD(0f, new Renderer[0]) });
            Assert.Equal(new[] { 0, 1, 1 }, new[] { Level(1f), Level(100f), Level(1e6f) });
            m_Group.size = 0f;
            Assert.Equal(-1, Level(1f));
        }
    }
}
