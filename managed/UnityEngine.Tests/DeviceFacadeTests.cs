using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>UnityEngine.Device answers and acts like UnityEngine outside the Device Simulator
    /// (tests/projects/devicefacade), including after state changes the probe does not make.</summary>
    [Collection("Frames")]
    public class DeviceFacadeTests : IDisposable
    {
        readonly int m_Width = Screen.width, m_Height = Screen.height;

        public void Dispose()
        {
            Screen.Update(m_Width, m_Height);
            Screen.ConsumeSizeChanged();
        }

        [Fact]
        public void ScreenSizeAndSafeAreaFollowAWindowResize()
        {
            Screen.Update(123, 45);
            Assert.Equal((123, 45), (Device.Screen.width, Device.Screen.height));
            Assert.Equal(Screen.safeArea, Device.Screen.safeArea);
            Assert.Equal(new Rect(0, 0, 123, 45), Device.Screen.safeArea);
        }

        [Fact]
        public void SleepTimeoutReadsNeverSleepWhateverIsSet()
        {
            Device.Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Assert.Equal(SleepTimeout.NeverSleep, Screen.sleepTimeout);
            Assert.Equal(SleepTimeout.NeverSleep, Device.Screen.sleepTimeout);
        }

        [Fact]
        public void LogHandlersAddedThroughEitherClassAreTheSameSubscription()
        {
            int seen = 0;
            Application.LogCallback handler = (message, stack, type) => { if (message == "device-facade-test") seen++; };
            Device.Application.logMessageReceived += handler;
            Debug.Log("device-facade-test");
            Application.logMessageReceived -= handler;
            Debug.Log("device-facade-test");
            Assert.Equal(1, seen);
        }

        /// <summary>A Device member is a "not implemented" stub only when its UnityEngine
        /// counterpart is one too, so the facade never answers less than the class it mirrors.</summary>
        [Fact]
        public void DeviceMembersAreStubsOnlyWhereTheirCounterpartsAre()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var stub = typeof(Application).Assembly.GetType("Unbound.StubAttribute", throwOnError: true);
            bool IsStub(MemberInfo member) => member switch
            {
                PropertyInfo p => (p.GetMethod ?? p.SetMethod).IsDefined(stub, false),
                EventInfo e => e.AddMethod.IsDefined(stub, false),
                _ => member.IsDefined(stub, false),
            };
            string Signature(MemberInfo member) => member is MethodInfo m
                ? m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")"
                : member.Name;
            foreach (var (device, engine) in new[] { (typeof(Device.Application), typeof(Application)), (typeof(Device.Screen), typeof(Screen)), (typeof(Device.SystemInfo), typeof(SystemInfo)) })
            {
                var counterparts = engine.GetMembers(BindingFlags.Public | BindingFlags.Static).ToLookup(Signature);
                foreach (var member in device.GetMembers(flags).Where(m => m is not MethodInfo { IsSpecialName: true }))
                {
                    var counterpart = counterparts[Signature(member)].FirstOrDefault();
                    Assert.True(counterpart != null, $"{device.Name}.{Signature(member)} has no counterpart");
                    if (IsStub(member))
                        Assert.True(IsStub(counterpart), $"{device.Name}.{Signature(member)} is a stub, its counterpart is not");
                }
            }
        }
    }
}
