using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Constraints loaded from a file whose documents put them before the Transform.</summary>
    [Collection("Animation")]
    public class ConstraintLoadingTests : System.IDisposable
    {
        readonly System.Collections.Generic.List<string> m_Logs = new System.Collections.Generic.List<string>();

        public ConstraintLoadingTests() => Application.logMessageReceived += OnLog;

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Log) m_Logs.Add($"{type}: {message}");
        }

        public void Dispose() => Application.logMessageReceived -= OnLog;

        // Documents are in file id order: the constraint (fileID 100) comes before the Transform (200).
        const string Yaml = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1
GameObject:
  m_Name: Follower
  m_Component:
  - component: {fileID: 200}
  - component: {fileID: 100}
  m_IsActive: 1
--- !u!1818360608 &100
PositionConstraint:
  m_GameObject: {fileID: 1}
  m_Enabled: 1
  m_Weight: 0.5
  m_TranslationAtRest: {x: 1, y: 2, z: 3}
  m_TranslationOffset: {x: 0, y: 0, z: 0}
  m_AffectTranslationX: 1
  m_AffectTranslationY: 1
  m_AffectTranslationZ: 1
  m_Active: 1
  m_IsLocked: 1
  m_Sources: []
--- !u!4 &200
Transform:
  m_GameObject: {fileID: 1}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 7, y: 8, z: 9}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
";

        [Fact]
        public void AConstraintBeforeItsTransformLoadsWithItsSerializedRest()
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(Yaml), "constraint.prefab", null, ObjectGraphBuilder.Kind.Asset, null);
            Assert.Empty(m_Logs);
            var constraint = Assert.IsType<PositionConstraint>(file.Objects[100]);
            Assert.Equal(new Vector3(1, 2, 3), constraint.translationAtRest);
            Assert.Equal(0.5f, constraint.weight);
            Assert.Same(file.Objects[200], constraint.transform);
        }

        [Fact]
        public void AnAddedConstraintTakesItsTransformAsTheRest()
        {
            // The control outside a file: AddComponent captures the Transform's values.
            var go = new GameObject("Added");
            go.transform.localPosition = new Vector3(4, 5, 6);
            var constraint = go.AddComponent<PositionConstraint>();
            Assert.Equal(new Vector3(4, 5, 6), constraint.translationAtRest);
            Object.DestroyImmediate(go);
        }
    }
}
