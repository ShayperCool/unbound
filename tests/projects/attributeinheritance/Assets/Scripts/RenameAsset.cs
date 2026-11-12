using UnityEngine;
using UnityEngine.Serialization;
namespace AttributeInheritanceControls
{
    public class RenameAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("oldValue")] public int value;
        [System.NonSerialized] public int callbacks;
        public void OnBeforeSerialize() {}
        public void OnAfterDeserialize() { callbacks++; }
    }
}
