using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial class Object
    {
        // A managed wrapper without an engine object has no identity, unlike an object
        // that was created and later destroyed (defaultstate, 6000.0.71f1).
        internal void MarkUncreated()
        {
            if (m_InstanceID != 0) ObjectRegistry.Unregister(this);
            m_InstanceID = 0;
            m_Destroyed = true;
        }

        internal void ThrowIfUncreated()
        {
            if (m_InstanceID == 0 && m_Destroyed && Compat.UncreatedObjectWrappersMeasured)
                throw new NullReferenceException();
        }

        internal void PreserveCreationIdentity()
        {
            if (m_InstanceID == 0) m_InstanceID = ObjectRegistry.Register(this);
            m_Destroyed = false;
            if (m_Name == null) m_Name = "";
        }
    }
}
