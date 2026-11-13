using Unbound.Runtime;

namespace UnityEngine.Networking
{
    public partial class UnityWebRequestAsyncOperation
    {
        // The request association is installed by SendWebRequest before publishing its operation.
        internal override bool HasOperation => m_WebRequest != null || base.HasOperation;
        internal override OperationCapabilities Capabilities => Compat.UncreatedObjectWrappersMeasured
            ? OperationCapabilities.None : base.Capabilities;
    }
}
