using Unbound.Runtime;

namespace UnityEngine
{
    public partial class TextAsset : Instantiation.ICloneable
    {
        // Read a lazy imported asset now: the clone owns a snapshot, not the backing file or
        // the original's pinned array and safety handle.
        Object Instantiation.ICloneable.CloneObject() => new TextAsset((System.ReadOnlySpan<byte>)m_Bytes);
    }
}
