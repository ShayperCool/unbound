using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class MachineIdentityEglTests
    {
        static readonly IntPtr Display = (IntPtr)1, Own = (IntPtr)2, Foreign = (IntPtr)3;
        static MachineIdentity.Binding Previous => new MachineIdentity.Binding
            { Display = (IntPtr)4, Draw = (IntPtr)5, Read = (IntPtr)6, Context = Foreign };

        sealed class Egl : MachineIdentity.EglCalls
        {
            internal readonly List<string> Calls = new List<string>();
            internal bool Restore = true, Release = true, Destroy = true, ApiRestore = true;
            internal bool InitialSnapshotInitialized, ForeignAlive;
            internal IntPtr Current;
            internal override uint QueryApi() { Calls.Add("query-api"); return 0x30A0; }
            internal override bool BindApi(uint api) { Calls.Add("api:" + api); return api != 0x30A0 || ApiRestore; }
            internal override bool Initialize(IntPtr display)
            {
                Assert.Equal(Display, display);
                Calls.Add("initialize");
                // Another consumer can start using a display after an uninitialized snapshot.
                Assert.False(InitialSnapshotInitialized);
                ForeignAlive = true;
                return true;
            }
            internal override IntPtr CreateContext(IntPtr display) { Calls.Add("create-own"); return Own; }
            internal override bool MakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context)
            {
                Calls.Add(context == Own ? "make-own" : context == Foreign ? "restore" : "release");
                if (context == Foreign)
                {
                    Assert.Equal(Previous.Display, display);
                    Assert.Equal(Previous.Draw, draw);
                    Assert.Equal(Previous.Read, read);
                    if (!Restore) return false;
                }
                if (context == IntPtr.Zero && !Release) return false;
                Current = context;
                return true;
            }
            internal override bool DestroyContext(IntPtr display, IntPtr context)
            {
                Calls.Add("destroy-own");
                Assert.Equal(Display, display);
                Assert.Equal(Own, context);
                Assert.NotEqual(Own, Current); // destroying a still-current context only defers deletion
                return Destroy;
            }
        }

        static MachineIdentity.Gpu Read(Egl egl, MachineIdentity.Binding previous) =>
            MachineIdentity.WithOpenGl(egl, () => MachineIdentity.ReadContext(egl, Display, previous,
                () => new MachineIdentity.Gpu { Name = "controlled" }));

        [Fact]
        public void Restore_preserves_binding_api_and_shared_display()
        {
            var egl = new Egl();
            Assert.Equal("controlled", Read(egl, Previous).Name);
            Assert.Equal(new[] { "query-api", "api:12450", "initialize", "create-own", "make-own", "restore", "destroy-own", "api:12448" }, egl.Calls);
            Assert.Equal(Foreign, egl.Current);
            Assert.True(egl.ForeignAlive);
        }

        [Fact]
        public void Failed_restore_releases_before_destroy_and_reports_failure()
        {
            var egl = new Egl { Restore = false };
            var error = Assert.Throws<InvalidOperationException>(() => Read(egl, Previous));
            Assert.Contains("cannot restore", error.Message);
            Assert.Equal(new[] { "query-api", "api:12450", "initialize", "create-own", "make-own", "restore", "release", "destroy-own", "api:12448" }, egl.Calls);
            Assert.Equal(IntPtr.Zero, egl.Current);
            Assert.True(egl.ForeignAlive);
        }

        [Fact]
        public void Failed_release_retains_own_current_context_and_reports_unrestored_state()
        {
            var egl = new Egl { Restore = false, Release = false };
            var error = Assert.Throws<InvalidOperationException>(() => Read(egl, Previous));
            Assert.Contains("release failed; current context retained", error.Message);
            Assert.DoesNotContain("destroy-own", egl.Calls);
            Assert.Equal(Own, egl.Current);
            Assert.Equal("api:12448", egl.Calls[egl.Calls.Count - 1]);
            Assert.True(egl.ForeignAlive);
        }

        [Fact]
        public void No_previous_context_is_released_before_destroy()
        {
            var egl = new Egl();
            Assert.NotNull(Read(egl, default));
            Assert.Equal(IntPtr.Zero, egl.Current);
            Assert.True(egl.Calls.IndexOf("release") < egl.Calls.IndexOf("destroy-own"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Api_restore_and_cleanup_failures_cannot_return_gpu(bool apiFailure)
        {
            var egl = new Egl { ApiRestore = !apiFailure, Destroy = apiFailure };
            var error = Assert.Throws<InvalidOperationException>(() => Read(egl, Previous));
            Assert.Contains(apiFailure ? "EGL API" : "destruction failed", error.Message);
            Assert.True(egl.ForeignAlive);
        }

        [Fact]
        public void Read_exception_still_restores_and_destroys_owned_context()
        {
            var egl = new Egl();
            Assert.Throws<ArgumentException>(() => MachineIdentity.WithOpenGl(egl, () =>
                MachineIdentity.ReadContext(egl, Display, Previous, () => throw new ArgumentException("controlled"))));
            Assert.Equal(Foreign, egl.Current);
            Assert.Contains("destroy-own", egl.Calls);
            Assert.Equal("api:12448", egl.Calls[egl.Calls.Count - 1]);
            Assert.True(egl.ForeignAlive);
        }
    }
}
