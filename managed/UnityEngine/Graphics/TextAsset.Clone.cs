using Unbound.Runtime;

namespace UnityEngine
{
    public partial class TextAsset
    {
        // Read a lazy imported asset now: the clone owns a snapshot, not the backing file or
        // the original's pinned array and safety handle.
        internal Object CloneObject() => new TextAsset((System.ReadOnlySpan<byte>)m_Bytes);
    }
}
