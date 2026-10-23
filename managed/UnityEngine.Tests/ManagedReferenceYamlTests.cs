using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>[SerializeReference] fields read from both of Unity's reference table layouts.</summary>
    public class ManagedReferenceYamlTests
    {
        static SerializedSample Read(string yaml)
        {
            var sample = ScriptableObject.CreateInstance<SerializedSample>();
            new YamlReader(null).ReadScriptObject(sample, TestYaml.DocumentBody(yaml));
            return sample;
        }

        [Fact]
        public void Version2TablesListReferencesByRid()
        {
            var sample = Read(@"
MonoBehaviour:
  shape:
    rid: 7
  references:
    version: 2
    RefIds:
    - rid: 7
      type: {class: CircleData, ns: UnityEngine.Tests, asm: UnityEngine.Tests}
      data:
        radius: 1.5
");
            var circle = Assert.IsType<CircleData>(sample.shape);
            Assert.Equal(1.5f, circle.radius);
        }

        [Fact]
        public void Version1TablesKeyReferencesByEightHexDigitIds()
        {
            var sample = Read(@"
MonoBehaviour:
  shape:
    id: 12
  references:
    version: 1
    0000000C:
      type: {class: CircleData, ns: UnityEngine.Tests, asm: UnityEngine.Tests}
      data:
        radius: 2.5
");
            var circle = Assert.IsType<CircleData>(sample.shape);
            Assert.Equal(2.5f, circle.radius);
        }
    }
}

namespace UnityEngine.Tests
{
    [System.Serializable]
    public struct CallbackStruct : ISerializationCallbackReceiver
    {
        public string name;
        public int length;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() => length = name?.Length ?? 0;
    }

    public class CallbackHolder : ScriptableObject
    {
        public CallbackStruct value;
    }

    public class StructCallbackTests
    {
        [Fact]
        public void StructCallbacksKeepWhatTheySet()
        {
            var holder = ScriptableObject.CreateInstance<CallbackHolder>();
            new Unbound.Runtime.YamlReader(null).ReadScriptObject(holder, TestYaml.DocumentBody(@"
MonoBehaviour:
  value:
    name: hello
"));
            Assert.Equal("hello", holder.value.name);
            Assert.Equal(5, holder.value.length);
        }
    }
}
