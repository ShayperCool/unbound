using System;
using System.Collections.Generic;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.SubsystemsImplementation.Extensions;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The managed subsystem API as the Unity 6000.0.71f1 Editor runs it with no provider plug-in
    /// (tests/projects/subsystems): registration by id, the order of the provider's and the
    /// subsystem's callbacks, and what the manager lists, each next to a control.
    /// </summary>
    public sealed class SubsystemsTests : IDisposable
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static bool Initializes = true;
        readonly List<string> m_Logs = new List<string>();

        public SubsystemsTests()
        {
            Application.logMessageReceived += Record;
            Calls.Clear();
            Initializes = true;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= Record;
            var all = new List<TestSubsystem>();
            SubsystemManager.GetSubsystems(all);
            foreach (var subsystem in all) subsystem.Destroy();
        }

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        public class TestSubsystem : SubsystemWithProvider<TestSubsystem, TestDescriptor, TestSubsystem.Provider>
        {
            public abstract class Provider : SubsystemProvider<TestSubsystem> { }

            public TestSubsystem() => Calls.Add("ctor");
            protected override void OnCreate() => Calls.Add("OnCreate");
            protected override void OnStart() { Calls.Add("OnStart"); base.OnStart(); }
            protected override void OnStop() { Calls.Add("OnStop"); base.OnStop(); }
            protected override void OnDestroy() { Calls.Add("OnDestroy"); base.OnDestroy(); }
        }

        public class DerivedSubsystem : TestSubsystem { }

        public class TestProvider : TestSubsystem.Provider
        {
            public TestProvider() => Calls.Add("Provider.ctor");
            protected internal override bool TryInitialize() { Calls.Add("Provider.TryInitialize"); return Initializes; }
            public override void Start() => Calls.Add("Provider.Start");
            public override void Stop() => Calls.Add("Provider.Stop");
            public override void Destroy() => Calls.Add("Provider.Destroy");
        }

        public class TestDescriptor : SubsystemDescriptorWithProvider<TestSubsystem, TestSubsystem.Provider>
        {
            public static TestDescriptor Make(string id, Type provider, Type subsystemOverride = null) =>
                new TestDescriptor { id = id, providerType = provider, subsystemTypeOverride = subsystemOverride };
        }

        static List<TestDescriptor> Registered()
        {
            var list = new List<TestDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(list);
            return list;
        }

        [Fact]
        public void DescriptorsRegisterByIdAndInvalidOnesThrow()
        {
            var descriptor = TestDescriptor.Make("tests.register", typeof(TestProvider));
            SubsystemDescriptorStore.RegisterDescriptor(descriptor);
            int count = Registered().Count;
            Assert.Contains(descriptor, Registered());
            Assert.Empty(m_Logs);
            // The same id replaces the entry in place, with a warning.
            var replacement = TestDescriptor.Make("tests.register", typeof(TestProvider));
            SubsystemDescriptorStore.RegisterDescriptor(replacement);
            Assert.Equal(new[] { "Warning: Registering subsystem descriptor with duplicate ID 'tests.register' - overwriting previous entry." }, m_Logs);
            Assert.Equal(count, Registered().Count);
            Assert.Contains(replacement, Registered());
            Assert.DoesNotContain(descriptor, Registered());

            Assert.Equal("Invalid descriptor - must supply a valid providerType field!",
                Assert.Throws<InvalidOperationException>(() => SubsystemDescriptorStore.RegisterDescriptor(TestDescriptor.Make("tests.none", null))).Message);
            Assert.Equal("Can't create provider - providerType 'System.String' is not a subclass of 'UnityEngine.Tests.SubsystemsTests+TestSubsystem+Provider'!",
                Assert.Throws<InvalidOperationException>(() => SubsystemDescriptorStore.RegisterDescriptor(TestDescriptor.Make("tests.wrong", typeof(string)))).Message);
            Assert.Equal("Can't create provider - subsystemTypeOverride 'System.String' is not a subclass of 'UnityEngine.Tests.SubsystemsTests+TestSubsystem'!",
                Assert.Throws<InvalidOperationException>(() => SubsystemDescriptorStore.RegisterDescriptor(TestDescriptor.Make("tests.override", typeof(TestProvider), typeof(string)))).Message);
            Assert.Throws<NullReferenceException>(() => SubsystemDescriptorStore.RegisterDescriptor(null));
            Assert.Equal(count, Registered().Count);
        }

        [Fact]
        public void SubsystemsRunTheirProvidersInOrder()
        {
            var descriptor = TestDescriptor.Make("tests.lifecycle", typeof(TestProvider));
            SubsystemDescriptorStore.RegisterDescriptor(descriptor);
            var subsystem = descriptor.Create();
            Assert.Equal(new[] { "Provider.ctor", "Provider.TryInitialize", "ctor", "OnCreate" }, Calls);
            Assert.False(subsystem.running);
            Assert.Same(descriptor, subsystem.subsystemDescriptor);
            Assert.IsType<TestProvider>(subsystem.GetProvider());
            Assert.Same(subsystem, descriptor.Create());
            Assert.Same(subsystem, ((ISubsystemDescriptor)descriptor).Create());

            Calls.Clear();
            subsystem.Start();
            subsystem.Start();
            Assert.Equal(new[] { "OnStart", "Provider.Start" }, Calls);
            Assert.True(subsystem.running && subsystem.GetProvider().running);
            var listed = new List<ISubsystem>();
            SubsystemManager.GetSubsystems(listed);
            Assert.Contains(subsystem, listed);

            Calls.Clear();
            subsystem.Destroy();
            subsystem.Destroy();
            Assert.Equal(new[] { "OnStop", "Provider.Stop", "OnDestroy", "Provider.Destroy" }, Calls);
            Assert.False(subsystem.running || subsystem.GetProvider().running);
            SubsystemManager.GetSubsystems(listed);
            Assert.DoesNotContain(subsystem, listed);
            // Started again after it was destroyed, Destroy only stops it.
            subsystem.Start();
            Calls.Clear();
            subsystem.Destroy();
            Assert.Equal(new[] { "OnStop", "Provider.Stop" }, Calls);
            Assert.False(subsystem.running || subsystem.GetProvider().running);
            Assert.Throws<NullReferenceException>(() => SubsystemManager.GetSubsystems<ISubsystem>(null));
            // A destroyed subsystem is no longer the descriptor's: Create makes another.
            Assert.NotSame(subsystem, descriptor.Create());
        }

        [Fact]
        public void ProvidersThatFailMakeNoSubsystemAndProxiesOnlyMarkRunning()
        {
            var failing = TestDescriptor.Make("tests.fail", typeof(TestProvider));
            SubsystemDescriptorStore.RegisterDescriptor(failing);
            Initializes = false;
            Assert.Null(failing.Create());
            Assert.Equal(new[] { "Provider.ctor", "Provider.TryInitialize" }, Calls);
            Initializes = true;
            // Control: the same descriptor creates its subsystem once the provider initializes.
            Assert.NotNull(failing.Create());

            var derived = TestDescriptor.Make("tests.derived", typeof(TestProvider), typeof(DerivedSubsystem));
            SubsystemDescriptorStore.RegisterDescriptor(derived);
            Assert.IsType<DerivedSubsystem>(derived.Create());

            var proxied = TestDescriptor.Make("tests.proxy", typeof(TestProvider));
            SubsystemDescriptorStore.RegisterDescriptor(proxied);
            Calls.Clear();
            var proxy = proxied.CreateProxy();
            Assert.Equal(new[] { "Provider.ctor", "Provider.TryInitialize" }, Calls);
            proxy.running = true;
            Assert.True(proxy.provider.running);
            Assert.Equal(2, Calls.Count);
        }
    }
}
