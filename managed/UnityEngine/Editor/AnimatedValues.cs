using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEditor.AnimatedValues
{
    /// <summary>
    /// A value that eases from where it is to a new target, advanced by the Editor's update loop:
    /// the eased position is <c>1 - (1 - t)^4</c> of linear progress <c>t</c>, which grows by
    /// <see cref="speed"/> per second of <see cref="EditorApplication.timeSinceStartup"/>.
    /// <see cref="valueChanged"/> is invoked on each update while it moves.
    /// </summary>
    public abstract partial class BaseAnimValue<T> : ISerializationCallbackReceiver
    {
        T m_Start;
        [SerializeField] T m_Target;
        double m_LastTime;
        double m_LerpPosition = 1.0;
        float m_Speed;

        public float speed = 2f;

        [NonSerialized] public UnityEvent valueChanged;

        [NonSerialized] bool m_Animating;

        protected BaseAnimValue(T value)
        {
            m_Start = value;
            m_Target = value;
            valueChanged = new UnityEvent();
        }

        protected BaseAnimValue(T value, UnityAction callback) : this(value)
        {
            valueChanged.AddListener(callback);
        }

        public bool isAnimating => m_Animating;

        protected float lerpPosition
        {
            get
            {
                double remaining = 1.0 - m_LerpPosition;
                return (float)(1.0 - remaining * remaining * remaining * remaining);
            }
        }

        protected T start => m_Start;

        public T target
        {
            get => m_Target;
            set
            {
                if (!AreEqual(m_Target, value)) BeginAnimating(value, this.value);
            }
        }

        public T value
        {
            get => lerpPosition >= 1f ? target : GetValue();
            set => StopAnim(value);
        }

        protected virtual bool AreEqual(T a, T b) => a.Equals(b);

        protected abstract T GetValue();

        protected void BeginAnimating(T newTarget, T newStart) => BeginAnimating(newTarget, newStart, speed);

        void BeginAnimating(T newTarget, T newStart, float animationSpeed)
        {
            m_Speed = animationSpeed;
            m_Start = newStart;
            m_Target = newTarget;
            if (!m_Animating) Subscribe();
            m_Animating = true;
            m_LastTime = EditorApplication.timeSinceStartup;
            m_LerpPosition = 0.0;
        }

        void Subscribe()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        internal void Update()
        {
            if (!m_Animating)
            {
                EditorApplication.update -= Update;
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            m_LerpPosition = Math.Clamp(m_LerpPosition + (now - m_LastTime) * m_Speed, 0.0, 1.0);
            m_LastTime = now;
            valueChanged?.Invoke();
            if (lerpPosition >= 1f)
            {
                m_Animating = false;
                EditorApplication.update -= Update;
            }
        }

        protected void StopAnim(T newValue)
        {
            bool notify = (!AreEqual(newValue, GetValue()) || m_LerpPosition < 1.0) && valueChanged != null;
            m_Target = newValue;
            m_Start = newValue;
            m_LerpPosition = 1.0;
            m_Animating = false;
            if (notify) valueChanged.Invoke();
        }

        internal void SetTarget(T newTarget, float animationSpeed)
        {
            if (!AreEqual(m_Target, value)) BeginAnimating(newTarget, value, animationSpeed);
        }

        internal void SkipFading() => StopAnim(target);

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (!GetValue().Equals(target) && !m_Animating)
            {
                Subscribe();
                m_Animating = true;
            }
        }
    }

    /// <summary>The same, for value types compared without boxing.</summary>
    public abstract partial class BaseAnimValueNonAlloc<T> : BaseAnimValue<T> where T : IEquatable<T>
    {
        protected BaseAnimValueNonAlloc(T value) : base(value) { }

        protected BaseAnimValueNonAlloc(T value, UnityAction callback) : base(value, callback) { }

        protected override bool AreEqual(T a, T b) => a.Equals(b);
    }

    [Serializable]
    public partial class AnimFloat : BaseAnimValueNonAlloc<float>
    {
        [SerializeField] float m_Value;

        public AnimFloat(float value) : base(value) { }

        public AnimFloat(float value, UnityAction callback) : base(value, callback) { }

        protected override float GetValue()
        {
            m_Value = Mathf.Lerp(start, target, lerpPosition);
            return m_Value;
        }
    }

    [Serializable]
    public partial class AnimVector3 : BaseAnimValueNonAlloc<Vector3>
    {
        [SerializeField] Vector3 m_Value;

        public AnimVector3() : base(Vector3.zero) { }

        public AnimVector3(Vector3 value) : base(value) { }

        public AnimVector3(Vector3 value, UnityAction callback) : base(value, callback) { }

        protected override Vector3 GetValue()
        {
            m_Value = Vector3.Lerp(start, target, lerpPosition);
            return m_Value;
        }
    }

    [Serializable]
    public partial class AnimBool : BaseAnimValueNonAlloc<bool>
    {
        [SerializeField] float m_Value;

        public AnimBool() : base(false) { }

        public AnimBool(bool value) : base(value) { }

        public AnimBool(UnityAction callback) : base(false, callback) { }

        public AnimBool(bool value, UnityAction callback) : base(value, callback) { }

        /// <summary>How far toward true it is.</summary>
        public float faded
        {
            get
            {
                GetValue();
                return m_Value;
            }
        }

        protected override bool GetValue()
        {
            float from = target ? 0f : 1f;
            float to = 1f - from;
            m_Value = Mathf.Lerp(from, to, lerpPosition);
            return m_Value > 0.5f;
        }

        public float Fade(float from, float to) => Mathf.Lerp(from, to, faded);
    }

    [Serializable]
    public partial class AnimQuaternion : BaseAnimValueNonAlloc<Quaternion>
    {
        [SerializeField] Quaternion m_Value;

        public AnimQuaternion(Quaternion value) : base(value) { }

        public AnimQuaternion(Quaternion value, UnityAction callback) : base(value, callback) { }

        protected override Quaternion GetValue()
        {
            m_Value = Quaternion.Slerp(start, target, lerpPosition);
            return m_Value;
        }
    }
}

namespace UnityEditor
{
    /// <summary>A color preference (Preferences > Colors): its default unless the user saved one
    /// (EditorPrefs, under its name, as "name;r;g;b;a" or with the dark skin's color appended).</summary>
    internal partial class PrefColor : IPrefType
    {
        string m_Name;
        Color m_Color;
        Color m_DefaultColor;
        bool m_SeparateColors;
        Color m_OptionalDarkColor;
        Color m_OptionalDarkDefaultColor;
        bool m_Loaded;

        public PrefColor() { m_Loaded = true; }

        public PrefColor(string name, float defaultRed, float defaultGreen, float defaultBlue, float defaultAlpha)
        {
            m_Name = name;
            m_Color = m_DefaultColor = new Color(defaultRed, defaultGreen, defaultBlue, defaultAlpha);
            m_OptionalDarkColor = m_OptionalDarkDefaultColor = Color.clear;
        }

        public PrefColor(string name, float defaultRed, float defaultGreen, float defaultBlue, float defaultAlpha,
            float defaultRed2, float defaultGreen2, float defaultBlue2, float defaultAlpha2)
        {
            m_Name = name;
            m_Color = m_DefaultColor = new Color(defaultRed, defaultGreen, defaultBlue, defaultAlpha);
            m_SeparateColors = true;
            m_OptionalDarkColor = m_OptionalDarkDefaultColor = new Color(defaultRed2, defaultGreen2, defaultBlue2, defaultAlpha2);
        }

        public Color Color
        {
            get
            {
                Load();
                return m_SeparateColors && EditorGUIUtility.isProSkin ? m_OptionalDarkColor : m_Color;
            }
            set
            {
                Load();
                if (m_SeparateColors && EditorGUIUtility.isProSkin) m_OptionalDarkColor = value;
                else m_Color = value;
            }
        }

        public string Name
        {
            get
            {
                Load();
                return m_Name;
            }
        }

        public void Load()
        {
            if (m_Loaded) return;
            m_Loaded = true;
            var saved = m_Name == null ? null : EditorPrefs.GetString(m_Name, null);
            if (!string.IsNullOrEmpty(saved)) FromUniqueString(saved);
        }

        public static implicit operator Color(PrefColor pcolor) => pcolor.Color;

        public string ToUniqueString()
        {
            Load();
            string F(float v) => v.ToString(CultureInfo.InvariantCulture);
            var text = $"{m_Name};{F(m_Color.r)};{F(m_Color.g)};{F(m_Color.b)};{F(m_Color.a)}";
            if (m_SeparateColors)
                text += $";{F(m_OptionalDarkColor.r)};{F(m_OptionalDarkColor.g)};{F(m_OptionalDarkColor.b)};{F(m_OptionalDarkColor.a)}";
            return text;
        }

        public void FromUniqueString(string s)
        {
            m_Loaded = true;
            var parts = s.Split(';');
            if (parts.Length != 5 && parts.Length != 9)
            {
                Debug.LogError("Parsing PrefColor failed");
                return;
            }
            m_Name = parts[0];
            if (TryColor(parts, 1, out var color)) m_Color = color;
            else Debug.LogError("Parsing PrefColor failed");
            m_SeparateColors = parts.Length == 9;
            if (!m_SeparateColors) m_OptionalDarkColor = Color.clear;
            else if (TryColor(parts, 5, out var dark)) m_OptionalDarkColor = dark;
            else Debug.LogError("Parsing PrefColor failed");
        }

        static bool TryColor(string[] parts, int first, out Color color)
        {
            var c = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[first + i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out c[i]))
                {
                    color = default;
                    return false;
                }
            }
            color = new Color(c[0], c[1], c[2], c[3]);
            return true;
        }

        internal void ResetToDefault()
        {
            Load();
            m_Color = m_DefaultColor;
            m_OptionalDarkColor = m_OptionalDarkDefaultColor;
        }
    }

    internal partial interface IPrefType
    {
        string ToUniqueString();
        void FromUniqueString(string sstr);
        void Load();
    }
}
