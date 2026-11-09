using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    // Internal per-element properties: values stored on an element by key (controls keep the
    // element they bind, the column a cell shows, ...). Few per element, so a small list.
    public partial class VisualElement
    {
        List<KeyValuePair<PropertyName, object>> m_UaProperties;

        internal object GetProperty(PropertyName key)
        {
            if (m_UaProperties == null) return null;
            for (int i = 0; i < m_UaProperties.Count; i++)
                if (m_UaProperties[i].Key == key) return m_UaProperties[i].Value;
            return null;
        }

        internal void SetProperty(PropertyName key, object value)
        {
            m_UaProperties ??= new List<KeyValuePair<PropertyName, object>>(2);
            for (int i = 0; i < m_UaProperties.Count; i++)
            {
                if (m_UaProperties[i].Key != key) continue;
                m_UaProperties[i] = new KeyValuePair<PropertyName, object>(key, value);
                return;
            }
            m_UaProperties.Add(new KeyValuePair<PropertyName, object>(key, value));
        }

        internal bool HasProperty(PropertyName key)
        {
            if (m_UaProperties == null) return false;
            for (int i = 0; i < m_UaProperties.Count; i++)
                if (m_UaProperties[i].Key == key) return true;
            return false;
        }

        internal bool ClearProperty(PropertyName key)
        {
            if (m_UaProperties == null) return false;
            for (int i = 0; i < m_UaProperties.Count; i++)
            {
                if (m_UaProperties[i].Key != key) continue;
                m_UaProperties.RemoveAt(i);
                return true;
            }
            return false;
        }
    }
}
