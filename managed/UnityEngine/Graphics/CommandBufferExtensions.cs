namespace UnityEngine.Rendering
{
    public static partial class CommandBufferExtensions
    {
        // Fast GPU memory (ESRAM) exists only on consoles: elsewhere the commands are recorded and do
        // nothing (measured to record: tests/projects/runtimeapis command_buffer.*).
        public static void SwitchIntoFastMemory(this CommandBuffer cmd, RenderTargetIdentifier rid, FastMemoryFlags fastMemoryFlags, float residency, bool copyContents) =>
            cmd.AddWithoutEffect();

        public static void SwitchOutOfFastMemory(this CommandBuffer cmd, RenderTargetIdentifier rid, bool copyContents) =>
            cmd.AddWithoutEffect();
    }
}
