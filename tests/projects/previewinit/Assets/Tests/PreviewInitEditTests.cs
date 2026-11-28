using NUnit.Framework;

public sealed class PreviewInitEditTests
{
    [Test]
    public void CapturePreviewInitialization() => PreviewInitProbe.RunEditTest();
}
