using System;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Project release")]
    public class DeclarationAttributeStateTests
    {
        [Fact]
        public void RayTracingHelpUrlUsesTheSelectedReleaseAndRejectsUnmeasuredProfiles()
        {
            string saved = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                var attribute = new RayTracingShaderHelpURLAttribute();
                string expected = attribute.URL;
                Assert.Equal("https://docs.unity3d.com//6000.0/Documentation/ScriptReference/Rendering.RayTracingShader.html", expected);
                ProjectInfo.UnityVersion = "2022.3.10f1";
                Assert.Throws<NotSupportedException>(() => attribute.URL);
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Assert.Equal(expected, attribute.URL);
            }
            finally { ProjectInfo.UnityVersion = saved; }
        }
    }
}
