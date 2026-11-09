using System;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public unsafe class AudioCallbackAllocationTests
    {
        sealed class Filter : MonoBehaviour
        {
            internal int Calls;
            void OnAudioFilterRead(float[] data, int channels) { Calls++; data[0] += 0.125f; }
        }

        [Fact]
        public void PreparedClosingBuffersAllocateNothingOnFirstAndRepeatedWorkerRenders()
        {
            const ulong token = 900001;
            var source = new GameObject("source");
            var listener = new GameObject("listener");
            var sourceFilter = source.AddComponent<Filter>();
            var listenerFilter = listener.AddComponent<Filter>();
            AudioFilters.Update(9, source);
            AudioFilters.Update(AudioFilters.ListenerKey, listener);
            try
            {
                var samples = new float[2048];
                for (int reset = 0; reset < 3; reset++)
                {
                    AudioFilters.Dispatch(AudioCallbackKind.PrepareRenderBuffer, token, null, 2048, 1, 0);
                    AudioFilters.Dispatch(AudioCallbackKind.PrepareRenderBuffer, token, null, 512, 1, 0);
                    long allocated = -1;
                    Exception failure = null;
                    var worker = new Thread(() =>
                    {
                        try
                        {
                            AudioFilters.Dispatch(AudioCallbackKind.RenderBufferScope, token, null, 0, 0, 1);
                            fixed (float* data = samples)
                            {
                                long before = GC.GetAllocatedBytesForCurrentThread();
                                for (int i = 0; i < 10; i++)
                                {
                                    AudioFilters.Dispatch(AudioCallbackKind.SourceFilter, 9, data, 1024, 2, 0);
                                    AudioFilters.Dispatch(AudioCallbackKind.ListenerFilter, 0, data, 256, 2, 0);
                                }
                                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                            }
                        }
                        catch (Exception error) { failure = error; }
                        finally { AudioFilters.Dispatch(AudioCallbackKind.RenderBufferScope, token, null, 0, 0, 0); }
                    });
                    worker.Start(); worker.Join();
                    Assert.Null(failure);
                    Assert.Equal(0, allocated);
                    Assert.Equal((reset + 1) * 10, sourceFilter.Calls);
                    Assert.Equal((reset + 1) * 10, listenerFilter.Calls);
                    AudioFilters.Dispatch(AudioCallbackKind.ReleaseRenderBuffers, token, null, 0, 0, 0);
                }
                Assert.Equal(7.5f, samples[0]);
            }
            finally
            {
                AudioFilters.Dispatch(AudioCallbackKind.ReleaseRenderBuffers, token, null, 0, 0, 0);
                AudioFilters.Update(9, null);
                AudioFilters.Update(AudioFilters.ListenerKey, null);
                Object.DestroyImmediate(source); Object.DestroyImmediate(listener);
            }
        }
    }
}
