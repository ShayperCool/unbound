using System;
using System.Collections.Generic;
using Unity.Profiling.LowLevel;
using UnityEngine;

namespace Unity.Profiling
{
    /// <summary>A profiler category: Unity's built-in ones and categories made by name.</summary>
    public readonly partial struct ProfilerCategory
    {
        readonly ushort m_CategoryId;

        static readonly List<string> s_Names = new List<string>
        {
            "Render", "Scripts", "GUI", "Physics", "Physics 2D", "Animation", "AI", "Audio", "Video", "Particles",
            "Lighting", "Network", "Loading", "VR", "Input", "Memory", "Virtual Texturing", "File IO", "Internal", "Any", "GPU",
        };

        public ProfilerCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) throw new ArgumentException("A category needs a name", nameof(categoryName));
            lock (s_Names)
            {
                int index = s_Names.IndexOf(categoryName);
                if (index < 0)
                {
                    s_Names.Add(categoryName);
                    index = s_Names.Count - 1;
                }
                m_CategoryId = (ushort)index;
            }
        }

        internal ProfilerCategory(ushort category)
        {
            m_CategoryId = category;
        }

        public string Name
        {
            get
            {
                lock (s_Names) return m_CategoryId < s_Names.Count ? s_Names[m_CategoryId] : "";
            }
        }

        public Color32 Color => new Color32(128, 128, 128, 255);

        public override string ToString() => Name;

        public static implicit operator ushort(ProfilerCategory category) => category.m_CategoryId;

        public static ProfilerCategory Render => new ProfilerCategory(0);
        public static ProfilerCategory Scripts => new ProfilerCategory(1);
        public static ProfilerCategory Gui => new ProfilerCategory(2);
        public static ProfilerCategory Physics => new ProfilerCategory(3);
        public static ProfilerCategory Physics2D => new ProfilerCategory(4);
        public static ProfilerCategory Animation => new ProfilerCategory(5);
        public static ProfilerCategory Ai => new ProfilerCategory(6);
        public static ProfilerCategory Audio => new ProfilerCategory(7);
        public static ProfilerCategory Video => new ProfilerCategory(8);
        public static ProfilerCategory Particles => new ProfilerCategory(9);
        public static ProfilerCategory Lighting => new ProfilerCategory(10);
        public static ProfilerCategory Network => new ProfilerCategory(11);
        public static ProfilerCategory Loading => new ProfilerCategory(12);
        public static ProfilerCategory Vr => new ProfilerCategory(13);
        public static ProfilerCategory Input => new ProfilerCategory(14);
        public static ProfilerCategory Memory => new ProfilerCategory(15);
        public static ProfilerCategory VirtualTexturing => new ProfilerCategory(16);
        public static ProfilerCategory FileIO => new ProfilerCategory(17);
        public static ProfilerCategory Internal => new ProfilerCategory(18);
        internal static ProfilerCategory Any => new ProfilerCategory(19);
        internal static ProfilerCategory GPU => new ProfilerCategory(20);
    }

    /// <summary>
    /// A named profiler sample. Unbound has no profiler to record into: markers keep their names
    /// (for code that reads them back) and Begin/End cost nothing.
    /// </summary>
    public unsafe partial struct ProfilerMarker
    {
        internal readonly IntPtr m_Ptr;

        static readonly List<string> s_Markers = new List<string>();

        static IntPtr Register(string name)
        {
            lock (s_Markers)
            {
                s_Markers.Add(name ?? "");
                return (IntPtr)s_Markers.Count;
            }
        }

        public ProfilerMarker(string name)
        {
            m_Ptr = Register(name);
        }

        public ProfilerMarker(char* name, int nameLen)
        {
            m_Ptr = Register(new string(name, 0, nameLen));
        }

        public ProfilerMarker(ProfilerCategory category, string name)
        {
            m_Ptr = Register(name);
        }

        public ProfilerMarker(ProfilerCategory category, char* name, int nameLen)
        {
            m_Ptr = Register(new string(name, 0, nameLen));
        }

        public ProfilerMarker(ProfilerCategory category, string name, MarkerFlags flags)
        {
            m_Ptr = Register(name);
        }

        public ProfilerMarker(ProfilerCategory category, char* name, int nameLen, MarkerFlags flags)
        {
            m_Ptr = Register(new string(name, 0, nameLen));
        }

        public IntPtr Handle => m_Ptr;

        public void Begin() { }

        public void Begin(UnityEngine.Object contextUnityObject) { }

        public void End() { }

        internal void GetName(ref string name)
        {
            int index = (int)m_Ptr - 1;
            lock (s_Markers) name = index >= 0 && index < s_Markers.Count ? s_Markers[index] : "";
        }

        public AutoScope Auto() => new AutoScope(m_Ptr);

        public partial struct AutoScope : IDisposable
        {
            internal readonly IntPtr m_Ptr;

            internal AutoScope(IntPtr markerPtr)
            {
                m_Ptr = markerPtr;
            }

            public void Dispose() { }
        }
    }
}
