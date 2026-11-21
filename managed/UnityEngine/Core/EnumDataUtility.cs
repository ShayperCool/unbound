using System;
using System.Collections.Generic;
using System.Reflection;

// Cached enum reflection metadata (declaration order, InspectorName, InspectorOrder, obsolete
// filtering, and integer flag values) shared by UI Toolkit enum controls and Editor SerializedProperty
// enum bindings. Matches Unity 6000.0.71f1 Editor behavior, tests/projects/enumbinding.

namespace UnityEngine
{
    internal partial struct EnumData
    {
        public Enum[] values;
        public int[] flagValues;
        public string[] displayNames;
        public string[] names;
        public string[] tooltip;
        public bool flags;
        public Type underlyingType;
        public bool unsigned;
        public bool serializable;
    }

    internal static partial class EnumDataUtility
    {
        public enum CachedType
        {
            ExcludeObsolete = 0,
            IncludeObsoleteExceptErrors = 1,
            IncludeAllObsolete = 2,
        }

        static readonly Dictionary<(CachedType, Type), EnumData> s_EnumData = new Dictionary<(CachedType, Type), EnumData>();

        public static EnumData GetCachedEnumData(Type enumType, CachedType cachedType = CachedType.IncludeObsoleteExceptErrors, Func<string, string> nicifyName = null)
        {
            var key = (cachedType, enumType);
            if (s_EnumData.TryGetValue(key, out var data)) return data;

            data = new EnumData
            {
                underlyingType = Enum.GetUnderlyingType(enumType),
            };
            data.unsigned = data.underlyingType == typeof(byte)
                || data.underlyingType == typeof(ushort)
                || data.underlyingType == typeof(uint)
                || data.underlyingType == typeof(ulong);

            var rawFields = enumType.GetFields(BindingFlags.Static | BindingFlags.Public);
            var fields = new List<FieldInfo>(rawFields.Length);
            for (int i = 0; i < rawFields.Length; i++)
            {
                if (CheckObsoleteAddition(rawFields[i], cachedType)) fields.Add(rawFields[i]);
            }

            if (fields.Count == 0)
            {
                var empty = new[] { "" };
                data.values = Array.Empty<Enum>();
                data.flagValues = new[] { 0 };
                data.displayNames = empty;
                data.names = empty;
                data.tooltip = empty;
                data.flags = true;
                data.serializable = true;
                return data;
            }

            try
            {
                string location = fields[0].Module.Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                    fields.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            }
            catch
            {
            }

            int count = fields.Count;
            var displayNames = new string[count];
            var tooltips = new string[count];
            var values = new Enum[count];
            var flagValues = new int[count];
            var names = new string[count];
            var seenDisplayNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasDuplicateDisplayName = false;

            for (int i = 0; i < count; i++)
            {
                var f = fields[i];
                string displayName = EnumNameFromEnumField(f, nicifyName);
                displayNames[i] = displayName;
                if (!seenDisplayNames.Add(displayName)) hasDuplicateDisplayName = true;
                tooltips[i] = EnumTooltipFromEnumField(f);
                var val = (Enum)f.GetValue(null);
                values[i] = val;
                flagValues[i] = data.unsigned ? unchecked((int)Convert.ToUInt64(val)) : unchecked((int)Convert.ToInt64(val));
                names[i] = f.Name;
            }

            if (hasDuplicateDisplayName)
                Debug.LogWarning("Enum " + enumType.Name + " has multiple entries with the same display name, this prevents selection in EnumPopup.");

            if (data.underlyingType == typeof(ushort))
            {
                for (int i = 0; i < count; i++)
                {
                    if (flagValues[i] == ushort.MaxValue) flagValues[i] = -1;
                }
            }
            else if (data.underlyingType == typeof(byte))
            {
                for (int i = 0; i < count; i++)
                {
                    if (flagValues[i] == byte.MaxValue) flagValues[i] = -1;
                }
            }

            data.displayNames = displayNames;
            data.tooltip = tooltips;
            data.values = values;
            data.flagValues = flagValues;
            data.names = names;
            data.flags = enumType.IsDefined(typeof(FlagsAttribute), false);
            data.serializable = data.underlyingType != typeof(long) && data.underlyingType != typeof(ulong);

            HandleInspectorOrderAttribute(enumType, ref data);
            s_EnumData[key] = data;
            return data;
        }

        public static int EnumFlagsToInt(EnumData enumData, Enum enumValue)
        {
            if (enumData.unsigned)
            {
                if (enumData.underlyingType == typeof(uint)) return unchecked((int)Convert.ToUInt32(enumValue));
                if (enumData.underlyingType == typeof(ushort))
                {
                    ushort u16 = Convert.ToUInt16(enumValue);
                    return u16 == ushort.MaxValue ? -1 : u16;
                }
                byte u8 = Convert.ToByte(enumValue);
                return u8 == byte.MaxValue ? -1 : u8;
            }
            return Convert.ToInt32(enumValue);
        }

        public static Enum IntToEnumFlags(Type enumType, int value)
        {
            var data = GetCachedEnumData(enumType);
            if (data.unsigned)
            {
                if (data.underlyingType == typeof(uint)) return Enum.Parse(enumType, unchecked((uint)value).ToString()) as Enum;
                if (data.underlyingType == typeof(ushort)) return Enum.Parse(enumType, unchecked((ushort)value).ToString()) as Enum;
                return Enum.Parse(enumType, unchecked((byte)value).ToString()) as Enum;
            }
            return Enum.Parse(enumType, value.ToString()) as Enum;
        }

        public static void HandleInspectorOrderAttribute(Type enumType, ref EnumData enumData)
        {
            if (!(Attribute.GetCustomAttribute(enumType, typeof(InspectorOrderAttribute)) is InspectorOrderAttribute order))
                return;

            int count = enumData.displayNames.Length;
            var indices = new int[count];
            for (int i = 0; i < count; i++) indices[i] = i;

            if (order.m_inspectorSort == InspectorSort.ByValue)
            {
                var keys = new int[count];
                Array.Copy(enumData.flagValues, keys, count);
                Array.Sort(keys, indices);
            }
            else
            {
                var keys = new string[count];
                Array.Copy(enumData.displayNames, keys, count);
                Array.Sort(keys, indices, StringComparer.Ordinal);
            }

            if (order.m_sortDirection == InspectorSortDirection.Descending)
                Array.Reverse(indices);

            var values = new Enum[count];
            var flagValues = new int[count];
            var displayNames = new string[count];
            var names = new string[count];
            var tooltips = new string[count];
            for (int i = 0; i < count; i++)
            {
                int src = indices[i];
                values[i] = enumData.values[src];
                flagValues[i] = enumData.flagValues[src];
                displayNames[i] = enumData.displayNames[src];
                names[i] = enumData.names[src];
                tooltips[i] = enumData.tooltip[src];
            }

            enumData.values = values;
            enumData.flagValues = flagValues;
            enumData.displayNames = displayNames;
            enumData.names = names;
            enumData.tooltip = tooltips;
        }

        static bool CheckObsoleteAddition(FieldInfo field, CachedType cachedType)
        {
            var attrs = field.GetCustomAttributes(typeof(ObsoleteAttribute), false);
            if (attrs.Length == 0) return true;
            switch (cachedType)
            {
                case CachedType.ExcludeObsolete: return false;
                case CachedType.IncludeAllObsolete: return true;
                default: return !((ObsoleteAttribute)attrs[0]).IsError;
            }
        }

        static string EnumTooltipFromEnumField(FieldInfo field)
        {
            var attrs = field.GetCustomAttributes(typeof(TooltipAttribute), false);
            return attrs.Length != 0 ? ((TooltipAttribute)attrs[0]).tooltip : string.Empty;
        }

        static string EnumNameFromEnumField(FieldInfo field, Func<string, string> nicifyName)
        {
            var attrs = field.GetCustomAttributes(typeof(InspectorNameAttribute), false);
            if (attrs.Length != 0) return ((InspectorNameAttribute)attrs[0]).displayName;
            string name = nicifyName == null ? field.Name : nicifyName(field.Name);
            return field.IsDefined(typeof(ObsoleteAttribute), false) ? name + " (Obsolete)" : name;
        }
    }
}

namespace UnityEditor
{
    internal static partial class EnumDataUtility
    {
        internal static UnityEngine.EnumData GetCachedEnumData(Type enumType, bool excludeObsolete = true) =>
            UnityEngine.EnumDataUtility.GetCachedEnumData(
                enumType,
                excludeObsolete ? UnityEngine.EnumDataUtility.CachedType.ExcludeObsolete : UnityEngine.EnumDataUtility.CachedType.IncludeObsoleteExceptErrors,
                ObjectNames.NicifyVariableName);

        internal static UnityEngine.EnumData GetCachedEnumData(Type enumType, UnityEngine.EnumDataUtility.CachedType cachedType) =>
            UnityEngine.EnumDataUtility.GetCachedEnumData(enumType, cachedType, ObjectNames.NicifyVariableName);

        internal static int EnumFlagsToInt(UnityEngine.EnumData enumData, Enum enumValue) =>
            UnityEngine.EnumDataUtility.EnumFlagsToInt(enumData, enumValue);

        internal static Enum IntToEnumFlags(Type enumType, int value) =>
            UnityEngine.EnumDataUtility.IntToEnumFlags(enumType, value);
    }
}
