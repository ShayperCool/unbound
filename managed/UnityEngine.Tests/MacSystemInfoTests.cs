using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The machine facts SystemInfo reads from the OS on macOS, against what `sysctl`
    /// reports. The macOS Editor (Unity 6000.0.71f1, Apple M2 Pro) answered processorType and
    /// processorModel <c>Apple M2 Pro</c> (machdep.cpu.brand_string), processorManufacturer
    /// <c>Apple</c>, deviceModel <c>Mac14,9</c> (hw.model), systemMemorySize 16384 (hw.memsize in
    /// MiB) and processorFrequency 2400 in every read (hw.tbfrequency 24 MHz x kern.clockrate's hz
    /// 100). Elsewhere the test has nothing to check.</summary>
    public class MacSystemInfoTests
    {
        static string Sysctl(string name)
        {
            var info = new ProcessStartInfo("/usr/sbin/sysctl", "-n " + name) { RedirectStandardOutput = true, UseShellExecute = false };
            using var process = Process.Start(info);
            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return text;
        }

        [Fact]
        public void MachineFactsAreTheOsAnswers()
        {
            if (!OperatingSystem.IsMacOS()) return;
            Assert.Equal(Sysctl("machdep.cpu.brand_string"), SystemInfo.processorType);
            Assert.Equal(SystemInfo.processorType, SystemInfo.processorModel);
            Assert.Equal(Sysctl("hw.model"), SystemInfo.deviceModel);
            Assert.Equal((int)(long.Parse(Sysctl("hw.memsize")) / (1024 * 1024)), SystemInfo.systemMemorySize);
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                Assert.Equal("Apple", SystemInfo.processorManufacturer);
                // The timebase times the clock rate: 2400 on every Apple silicon Mac so far
                // (24 MHz, hz 100), steady from read to read.
                var clock = Sysctl("kern.clockrate");
                int hz = int.Parse(clock.Substring(clock.IndexOf("hz = ", StringComparison.Ordinal) + 5).Split(',')[0]);
                Assert.Equal((int)(long.Parse(Sysctl("hw.tbfrequency")) * hz / 1_000_000), SystemInfo.processorFrequency);
                Assert.Equal(SystemInfo.processorFrequency, SystemInfo.processorFrequency);
            }
        }
    }
}
