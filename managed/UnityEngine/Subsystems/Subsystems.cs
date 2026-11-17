using System;
using System.Collections.Generic;
using UnityEngine.SubsystemsImplementation;

// The managed subsystem API (the Editor with no native subsystem providers: no integrated
// descriptors, and the standalone ones packages register in C#). Measured in the Unity
// 6000.0.71f1 Editor, tests/projects/subsystems.
namespace UnityEngine
{
    public partial interface ISubsystem
    {
        void Start();
        void Stop();
        void Destroy();
        bool running { get; }
    }

    public partial interface ISubsystemDescriptor
    {
        string id { get; }
        ISubsystem Create();
    }

    /// <summary>The descriptors registered and the standalone subsystems created from them, in
    /// that order; no native (integrated) subsystem exists.</summary>
    public static partial class SubsystemManager
    {
        static readonly List<SubsystemWithProvider> s_StandaloneSubsystems = new List<SubsystemWithProvider>();

        public static void GetAllSubsystemDescriptors(List<ISubsystemDescriptor> descriptors) =>
            SubsystemDescriptorStore.GetAllSubsystemDescriptors(descriptors);

        public static void GetSubsystemDescriptors<T>(List<T> descriptors) where T : ISubsystemDescriptor =>
            SubsystemDescriptorStore.GetSubsystemDescriptors(descriptors);

        public static void GetSubsystems<T>(List<T> subsystems) where T : ISubsystem
        {
            if (subsystems == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            subsystems.Clear();
            foreach (var subsystem in s_StandaloneSubsystems)
                if (subsystem is T matching) subsystems.Add(matching);
        }

        [global::System.@ObsoleteAttribute("Use GetSubsystems instead. (UnityUpgradable) -> GetSubsystems<T>(*)", false)]
        public static void GetInstances<T>(List<T> subsystems) where T : ISubsystem => GetSubsystems(subsystems);

        internal static void AddStandaloneSubsystem(SubsystemWithProvider subsystem) => s_StandaloneSubsystems.Add(subsystem);

        internal static bool RemoveStandaloneSubsystem(SubsystemWithProvider subsystem) => s_StandaloneSubsystems.Remove(subsystem);

        internal static SubsystemWithProvider FindStandaloneSubsystemByDescriptor(SubsystemDescriptorWithProvider descriptor)
        {
            foreach (var subsystem in s_StandaloneSubsystems)
                if (subsystem.descriptor == descriptor) return subsystem;
            return null;
        }

        // Raised around a reload of the native subsystem plugins (XR plug-in management); none is
        // loaded (measured: tests/projects/subsystems end.reload_events).
        [Unbound.EditorFaithful("raised only when native subsystem plugins reload; none is loaded (measured: tests/projects/subsystems end.reload_events)")]
        public static event Action beforeReloadSubsystems;

        [Unbound.EditorFaithful("raised only when native subsystem plugins reload; none is loaded (measured: tests/projects/subsystems end.reload_events)")]
        public static event Action afterReloadSubsystems;

        [global::System.Obsolete("Use beforeReloadSubsystems instead. (UnityUpgradable) -> beforeReloadSubsystems", false)]
        public static event Action reloadSubsytemsStarted
        {
            add => beforeReloadSubsystems += value;
            remove => beforeReloadSubsystems -= value;
        }

        [global::System.Obsolete("Use afterReloadSubsystems instead. (UnityUpgradable) -> afterReloadSubsystems", false)]
        public static event Action reloadSubsytemsCompleted
        {
            add => afterReloadSubsystems += value;
            remove => afterReloadSubsystems -= value;
        }
    }
}

namespace UnityEngine.SubsystemsImplementation
{
    /// <summary>The registered standalone descriptors, by id: a descriptor with an id already
    /// registered replaces it with a warning.</summary>
    public static partial class SubsystemDescriptorStore
    {
        static readonly List<SubsystemDescriptorWithProvider> s_StandaloneDescriptors = new List<SubsystemDescriptorWithProvider>();

        public static void RegisterDescriptor(SubsystemDescriptorWithProvider descriptor)
        {
            if (descriptor == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            descriptor.ThrowIfInvalid();
            RegisterDescriptor(descriptor, s_StandaloneDescriptors);
        }

        internal static void RegisterDescriptor<TDescriptor, TBaseTypeInList>(TDescriptor descriptor, List<TBaseTypeInList> storeInList)
            where TDescriptor : TBaseTypeInList where TBaseTypeInList : ISubsystemDescriptor
        {
            for (int i = 0; i < storeInList.Count; i++)
            {
                if (storeInList[i].id != descriptor.id) continue;
                Debug.LogWarning($"Registering subsystem descriptor with duplicate ID '{descriptor.id}' - overwriting previous entry.");
                storeInList[i] = descriptor;
                return;
            }
            storeInList.Add(descriptor);
        }

        internal static void GetAllSubsystemDescriptors(List<ISubsystemDescriptor> descriptors)
        {
            if (descriptors == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            descriptors.Clear();
            foreach (var descriptor in s_StandaloneDescriptors) descriptors.Add(descriptor);
        }

        internal static void GetSubsystemDescriptors<T>(List<T> descriptors) where T : ISubsystemDescriptor
        {
            if (descriptors == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            descriptors.Clear();
            foreach (var descriptor in s_StandaloneDescriptors)
                if (descriptor is T matching) descriptors.Add(matching);
        }
    }

    public abstract partial class SubsystemDescriptorWithProvider : ISubsystemDescriptor
    {
        protected SubsystemDescriptorWithProvider() { }

        public string id { get; set; }

        protected internal Type providerType { get; set; }

        protected internal Type subsystemTypeOverride { get; set; }

        ISubsystem ISubsystemDescriptor.Create() => CreateImpl();

        internal abstract ISubsystem CreateImpl();

        internal abstract void ThrowIfInvalid();
    }

    /// <summary>Creates its subsystem once (later calls answer the same one while it lives): the
    /// provider is made and initialized first, and a provider that fails to initialize makes no
    /// subsystem.</summary>
    public partial class SubsystemDescriptorWithProvider<TSubsystem, TProvider> : SubsystemDescriptorWithProvider
        where TSubsystem : SubsystemWithProvider, new()
        where TProvider : SubsystemProvider<TSubsystem>
    {
        public SubsystemDescriptorWithProvider() { }

        internal override ISubsystem CreateImpl() => Create();

        public TSubsystem Create()
        {
            if (SubsystemManager.FindStandaloneSubsystemByDescriptor(this) is TSubsystem existing) return existing;
            var provider = CreateProvider();
            if (provider == null) return null;
            var subsystem = subsystemTypeOverride != null ? (TSubsystem)Activator.CreateInstance(subsystemTypeOverride) : new TSubsystem();
            subsystem.Initialize(this, provider);
            SubsystemManager.AddStandaloneSubsystem(subsystem);
            return subsystem;
        }

        internal sealed override void ThrowIfInvalid()
        {
            if (providerType == null)
                throw new InvalidOperationException("Invalid descriptor - must supply a valid providerType field!");
            if (!providerType.IsSubclassOf(typeof(TProvider)))
                throw new InvalidOperationException($"Can't create provider - providerType '{providerType}' is not a subclass of '{typeof(TProvider)}'!");
            if (subsystemTypeOverride != null && !subsystemTypeOverride.IsSubclassOf(typeof(TSubsystem)))
                throw new InvalidOperationException($"Can't create provider - subsystemTypeOverride '{subsystemTypeOverride}' is not a subclass of '{typeof(TSubsystem)}'!");
        }

        internal TProvider CreateProvider()
        {
            var provider = (TProvider)Activator.CreateInstance(providerType);
            return provider.TryInitialize() ? provider : null;
        }
    }

    public abstract partial class SubsystemProvider
    {
        internal bool m_Running;

        protected SubsystemProvider() { }

        public bool running => m_Running;
    }

    public abstract partial class SubsystemProvider<TSubsystem> : SubsystemProvider where TSubsystem : SubsystemWithProvider, new()
    {
        protected SubsystemProvider() { }

        /// <summary>A provider initializes unless it overrides this (measured: tests/projects/subsystems
        /// create.calls).</summary>
        [Unbound.EditorFaithful("the base provider always initializes; a provider overrides it (measured: tests/projects/subsystems create, fail.create)")]
        protected internal virtual bool TryInitialize() => true;

        public abstract void Start();

        public abstract void Stop();

        public abstract void Destroy();
    }

    /// <summary>A provider without its subsystem: setting running only marks the provider.</summary>
    public partial class SubsystemProxy<TSubsystem, TProvider>
        where TSubsystem : SubsystemWithProvider, new()
        where TProvider : SubsystemProvider<TSubsystem>
    {
        internal SubsystemProxy(TProvider provider) => this.provider = provider;

        public TProvider provider { get; private set; }

        public bool running
        {
            get => provider.running;
            set => provider.m_Running = value;
        }
    }

    /// <summary>Start and Stop run once each until the other (OnStart/OnStop, then the provider's
    /// running flag); Destroy stops it, and the first Destroy also takes it out of the manager and
    /// runs OnDestroy.</summary>
    public abstract partial class SubsystemWithProvider : ISubsystem
    {
        protected SubsystemWithProvider() { }

        public bool running { get; private set; }

        internal SubsystemProvider providerBase { get; set; }

        internal abstract SubsystemDescriptorWithProvider descriptor { get; }

        public void Start()
        {
            if (running) return;
            OnStart();
            providerBase.m_Running = true;
            running = true;
        }

        public void Stop()
        {
            if (!running) return;
            OnStop();
            providerBase.m_Running = false;
            running = false;
        }

        public void Destroy()
        {
            Stop();
            if (SubsystemManager.RemoveStandaloneSubsystem(this)) OnDestroy();
        }

        protected abstract void OnStart();

        protected abstract void OnStop();

        protected abstract void OnDestroy();

        internal abstract void Initialize(SubsystemDescriptorWithProvider descriptor, SubsystemProvider subsystemProvider);
    }

    public abstract partial class SubsystemWithProvider<TSubsystem, TSubsystemDescriptor, TProvider> : SubsystemWithProvider
        where TSubsystem : SubsystemWithProvider, new()
        where TSubsystemDescriptor : SubsystemDescriptorWithProvider
        where TProvider : SubsystemProvider<TSubsystem>
    {
        protected SubsystemWithProvider() { }

        public TSubsystemDescriptor subsystemDescriptor { get; private set; }

        protected internal TProvider provider { get; private set; }

        internal sealed override SubsystemDescriptorWithProvider descriptor => subsystemDescriptor;

        [Unbound.EditorFaithful("a hook for derived subsystems, called once when the subsystem is created (measured: tests/projects/subsystems create.calls)")]
        protected virtual void OnCreate() { }

        protected override void OnStart() => provider.Start();

        protected override void OnStop() => provider.Stop();

        protected override void OnDestroy() => provider.Destroy();

        internal sealed override void Initialize(SubsystemDescriptorWithProvider descriptor, SubsystemProvider provider)
        {
            providerBase = provider;
            this.provider = (TProvider)provider;
            subsystemDescriptor = (TSubsystemDescriptor)descriptor;
            OnCreate();
        }
    }
}

namespace UnityEngine.SubsystemsImplementation.Extensions
{
    public static partial class SubsystemDescriptorExtensions
    {
        public static SubsystemProxy<TSubsystem, TProvider> CreateProxy<TSubsystem, TProvider>(this SubsystemDescriptorWithProvider<TSubsystem, TProvider> descriptor)
            where TSubsystem : SubsystemWithProvider, new()
            where TProvider : SubsystemProvider<TSubsystem>
        {
            var provider = descriptor.CreateProvider();
            return provider != null ? new SubsystemProxy<TSubsystem, TProvider>(provider) : null;
        }
    }

    public static partial class SubsystemExtensions
    {
        public static TProvider GetProvider<TSubsystem, TDescriptor, TProvider>(this SubsystemWithProvider<TSubsystem, TDescriptor, TProvider> subsystem)
            where TSubsystem : SubsystemWithProvider, new()
            where TDescriptor : SubsystemDescriptorWithProvider<TSubsystem, TProvider>
            where TProvider : SubsystemProvider<TSubsystem>
            => subsystem.provider;
    }
}
