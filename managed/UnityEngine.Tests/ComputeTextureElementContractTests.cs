using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>
    /// ComputeShader.SetTexture and CommandBuffer.SetComputeTextureParam with a
    /// RenderTextureSubElement (6000.0 Scripting API: the element indicates which data, Color, Depth
    /// or Stencil, is set from the render texture). Unbound binds a kernel's render texture as with
    /// Default whatever the element: Default and Color are that binding; a Depth or Stencil request
    /// is reported through Stubs.Ignored instead of being dropped silently.
    ///
    /// What is asserted: the binding a dispatch would send, and that each Depth/Stencil binding is
    /// counted (Stubs.HitReport) with at most one logged warning per member for the process. The
    /// message text past the member key, and when Unity itself would report anything, are not
    /// asserted. The CommandBuffer report comes when the recorded command runs: that is Unbound's
    /// route (the identifier is resolved then), not a Unity timing claim.
    /// </summary>
    public class ComputeTextureElementContractTests
    {
        const string DepthMember = "ComputeShader.SetTexture(RenderTextureSubElement.Depth)";
        const string StencilMember = "ComputeShader.SetTexture(RenderTextureSubElement.Stencil)";

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>Forwards everything to the original handler, counting the warnings that carry a
        /// member key and every exception (a recorded command's failure is logged, not thrown).</summary>
        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal int DepthWarnings, StencilWarnings, Exceptions;
            internal Capture(ILogHandler inner) { m_Inner = inner; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0
                    ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Warning && message != null)
                {
                    if (message.Contains(DepthMember)) DepthWarnings++;
                    if (message.Contains(StencilMember)) StencilWarnings++;
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                Exceptions++;
                m_Inner.LogException(exception, context);
            }
        }

        /// <summary>Runs <paramref name="body"/> with the capture installed at the producer's logger;
        /// the logger's handler, enabled flag and filter are restored after.</summary>
        static Capture Captured(Action body)
        {
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var capture = new Capture(handler);
            logger.logHandler = capture;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                body();
            }
            finally
            {
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
            }
            return capture;
        }

        static ComputeShader Shader(string name) =>
            ComputeShader.FromFile("Tests/" + name + ".compute", name, "#pragma kernel CSMain\n#pragma kernel CSOther\n");

        /// <summary>A target with colour and a depth/stencil buffer, so every element exists.</summary>
        static RenderTexture Target()
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24) { enableRandomWrite = true });
            rt.EnsureCreated();
            return rt;
        }

        /// <summary>What a dispatch of the kernel would bind (copied: the list is reused).</summary>
        static (string Name, byte Kind, ulong Handle, int Int)[] Bound(ComputeShader cs, int kernel) =>
            cs.Properties(kernel, null).Select(p => (p.Name, p.Kind, p.Handle, p.Int)).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();

        // ------------------------------------------------------------------ ComputeShader.SetTexture

        public static TheoryData<RenderTextureSubElement> Supported => new() { RenderTextureSubElement.Default, RenderTextureSubElement.Color };

        /// <summary>Default and Color: the plain overload's binding (mip included), by id and by
        /// name, with no element report.</summary>
        [Theory]
        [MemberData(nameof(Supported))]
        public void DefaultAndColorBindAsThePlainOverloadWithoutAReport(RenderTextureSubElement element)
        {
            var rt = Target();
            try
            {
                var plain = Shader("ElementPlain");
                var withElement = Shader("ElementSupported");
                int kernel = plain.FindKernel("CSMain");
                int id = UnityEngine.Shader.PropertyToID("_ElementSupportedTex");
                plain.SetTexture(kernel, id, rt, 1);
                plain.SetTexture(kernel, "_ElementSupportedByName", rt, 0);
                int depthBefore = Hits(DepthMember), stencilBefore = Hits(StencilMember);
                var capture = Captured(() =>
                {
                    withElement.SetTexture(kernel, id, rt, 1, element);
                    withElement.SetTexture(kernel, "_ElementSupportedByName", rt, 0, element);
                });
                Assert.Equal(Bound(plain, kernel), Bound(withElement, kernel));
                Assert.Contains(Bound(withElement, kernel), p => p.Name == "_ElementSupportedTex" && p.Kind == GfxStream.ImageKind && p.Int == 1);
                Assert.Equal(depthBefore, Hits(DepthMember));
                Assert.Equal(stencilBefore, Hits(StencilMember));
                Assert.Equal(0, capture.DepthWarnings + capture.StencilWarnings);
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Depth by id: reported once per call; the binding stays the plain one.</summary>
        [Fact]
        public void ADepthElementIsReportedAndKeepsThePlainBinding()
        {
            var rt = Target();
            try
            {
                var plain = Shader("DepthPlain");
                var cs = Shader("DepthElement");
                int kernel = cs.FindKernel("CSMain");
                int id = UnityEngine.Shader.PropertyToID("_ElementDepthTex");
                plain.SetTexture(kernel, id, rt, 0);
                int before = Hits(DepthMember), stencilBefore = Hits(StencilMember);
                var capture = Captured(() =>
                {
                    cs.SetTexture(kernel, id, rt, 0, RenderTextureSubElement.Depth);
                    cs.SetTexture(kernel, id, rt, 0, RenderTextureSubElement.Depth);
                });
                Assert.Equal(before + 2, Hits(DepthMember));
                Assert.Equal(stencilBefore, Hits(StencilMember));
                // Logged on the member's first use in the process only.
                Assert.Equal(before == 0 ? 1 : 0, capture.DepthWarnings);
                Assert.Equal(Bound(plain, kernel), Bound(cs, kernel));
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Stencil by name: reported under its own member; the binding stays the plain one.</summary>
        [Fact]
        public void AStencilElementByNameIsReportedAndKeepsThePlainBinding()
        {
            var rt = Target();
            try
            {
                var plain = Shader("StencilPlain");
                var cs = Shader("StencilElement");
                int kernel = cs.FindKernel("CSOther");
                plain.SetTexture(kernel, "_ElementStencilTex", rt, 0);
                int before = Hits(StencilMember), depthBefore = Hits(DepthMember);
                var capture = Captured(() => cs.SetTexture(kernel, "_ElementStencilTex", rt, 0, RenderTextureSubElement.Stencil));
                Assert.Equal(before + 1, Hits(StencilMember));
                Assert.Equal(depthBefore, Hits(DepthMember));
                Assert.Equal(before == 0 ? 1 : 0, capture.StencilWarnings);
                Assert.Equal(Bound(plain, kernel), Bound(cs, kernel));
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>An invalid kernel throws as the plain overload does, before any report.</summary>
        [Fact]
        public void AnInvalidKernelThrowsWithoutAReport()
        {
            var rt = Target();
            try
            {
                var cs = Shader("ElementInvalidKernel");
                int before = Hits(DepthMember);
                Assert.Throws<ArgumentException>(() => cs.SetTexture(5, UnityEngine.Shader.PropertyToID("_ElementInvalidTex"), rt, 0, RenderTextureSubElement.Depth));
                Assert.Equal(before, Hits(DepthMember));
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        // ------------------------------------------------------------------ CommandBuffer.SetComputeTextureParam

        /// <summary>Default and Color through a command buffer: the plain overload's binding, by id
        /// and by name, with no element report.</summary>
        [Theory]
        [MemberData(nameof(Supported))]
        public void CommandDefaultAndColorBindAsThePlainOverloadWithoutAReport(RenderTextureSubElement element)
        {
            var rt = Target();
            try
            {
                var plain = Shader("CommandPlain");
                var cs = Shader("CommandSupported");
                int kernel = cs.FindKernel("CSMain");
                int id = UnityEngine.Shader.PropertyToID("_CommandSupportedTex");
                using var plainBuffer = new CommandBuffer();
                plainBuffer.SetComputeTextureParam(plain, kernel, id, rt, 1);
                plainBuffer.SetComputeTextureParam(plain, kernel, "_CommandSupportedByName", rt, 0);
                Graphics.ExecuteCommandBuffer(plainBuffer);
                using var buffer = new CommandBuffer();
                int depthBefore = Hits(DepthMember), stencilBefore = Hits(StencilMember);
                var capture = Captured(() =>
                {
                    buffer.SetComputeTextureParam(cs, kernel, id, rt, 1, element);
                    buffer.SetComputeTextureParam(cs, kernel, "_CommandSupportedByName", rt, 0, element);
                    Graphics.ExecuteCommandBuffer(buffer);
                });
                Assert.Equal(0, capture.Exceptions);
                Assert.Equal(Bound(plain, kernel), Bound(cs, kernel));
                Assert.Equal(depthBefore, Hits(DepthMember));
                Assert.Equal(stencilBefore, Hits(StencilMember));
                Assert.Equal(0, capture.DepthWarnings + capture.StencilWarnings);
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Depth and Stencil through a command buffer: nothing is bound or reported while
        /// the commands are only recorded (Unbound resolves the identifier when the command runs);
        /// each execution reports each command and binds as the plain overload.</summary>
        [Fact]
        public void CommandDepthAndStencilAreReportedWhenTheCommandRuns()
        {
            var rt = Target();
            try
            {
                var plain = Shader("CommandElementPlain");
                var cs = Shader("CommandElement");
                int kernel = cs.FindKernel("CSMain");
                int depthId = UnityEngine.Shader.PropertyToID("_CommandDepthTex");
                plain.SetTexture(kernel, depthId, rt, 0);
                plain.SetTexture(kernel, "_CommandStencilTex", rt, 0);
                using var buffer = new CommandBuffer();
                int depthBefore = Hits(DepthMember), stencilBefore = Hits(StencilMember);
                int depthRecorded = -1, stencilRecorded = -1, boundRecorded = -1;
                var capture = Captured(() =>
                {
                    buffer.SetComputeTextureParam(cs, kernel, depthId, rt, 0, RenderTextureSubElement.Depth);
                    buffer.SetComputeTextureParam(cs, kernel, "_CommandStencilTex", rt, 0, RenderTextureSubElement.Stencil);
                    depthRecorded = Hits(DepthMember);
                    stencilRecorded = Hits(StencilMember);
                    boundRecorded = Bound(cs, kernel).Length;
                    Graphics.ExecuteCommandBuffer(buffer);
                    Graphics.ExecuteCommandBuffer(buffer);
                });
                Assert.Equal(0, capture.Exceptions);
                Assert.Equal(depthBefore, depthRecorded);
                Assert.Equal(stencilBefore, stencilRecorded);
                Assert.Equal(0, boundRecorded);
                Assert.Equal(depthBefore + 2, Hits(DepthMember));
                Assert.Equal(stencilBefore + 2, Hits(StencilMember));
                Assert.Equal(depthBefore == 0 ? 1 : 0, capture.DepthWarnings);
                Assert.Equal(stencilBefore == 0 ? 1 : 0, capture.StencilWarnings);
                Assert.Equal(Bound(plain, kernel), Bound(cs, kernel));
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>An identifier that resolves to a texture other than a render texture has no
        /// element to select: it is bound as itself, as by the plain overload, and not reported.</summary>
        [Fact]
        public void CommandElementOfANonRenderTextureBindsItWithoutAReport()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                var plain = Shader("CommandTexturePlain");
                var cs = Shader("CommandTextureElement");
                int kernel = cs.FindKernel("CSMain");
                int id = UnityEngine.Shader.PropertyToID("_CommandTexture2D");
                using var plainBuffer = new CommandBuffer();
                plainBuffer.SetComputeTextureParam(plain, kernel, id, texture, 0);
                Graphics.ExecuteCommandBuffer(plainBuffer);
                using var buffer = new CommandBuffer();
                int depthBefore = Hits(DepthMember);
                var capture = Captured(() =>
                {
                    buffer.SetComputeTextureParam(cs, kernel, id, texture, 0, RenderTextureSubElement.Depth);
                    Graphics.ExecuteCommandBuffer(buffer);
                });
                Assert.Equal(0, capture.Exceptions);
                Assert.Equal(depthBefore, Hits(DepthMember));
                Assert.Equal(0, capture.DepthWarnings);
                Assert.Equal(Bound(plain, kernel), Bound(cs, kernel));
                Assert.Contains(Bound(cs, kernel), p => p.Name == "_CommandTexture2D" && p.Kind == GfxStream.TextureKind);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
