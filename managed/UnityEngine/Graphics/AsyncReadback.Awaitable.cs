using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering
{
    public static partial class AsyncGPUReadback
    {
        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(ComputeBuffer src)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(ComputeBuffer src, int size, int offset)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, size, offset, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(GraphicsBuffer src)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(GraphicsBuffer src, int size, int offset)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, size, offset, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex = 0)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex, TextureFormat dstFormat)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, dstFormat, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex, GraphicsFormat dstFormat)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, dstFormat, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, x, width, y, height, z, depth, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, x, width, y, height, z, depth, dstFormat, request => completion.SetResult(request));
            return completion.Awaitable;
        }

        public static Awaitable<AsyncGPUReadbackRequest> RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat)
        {
            var completion = new AwaitableCompletionSource<AsyncGPUReadbackRequest>();
            Request(src, mipIndex, x, width, y, height, z, depth, dstFormat, request => completion.SetResult(request));
            return completion.Awaitable;
        }
    }
}
