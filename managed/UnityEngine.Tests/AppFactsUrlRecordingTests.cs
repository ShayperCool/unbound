using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace UnityEngine.Tests
{
    public class AppFactsUrlRecordingTests
    {
        [Theory]
        [InlineData("absent")]
        [InlineData("incomplete")]
        [InlineData("late")]
        [InlineData("delayed")]
        [InlineData("already complete")]
        public void Only_complete_records_within_real_deadline_succeed(string kind)
        {
            string directory = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "unbound/validation/url-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string log = Path.Combine(directory, "openurl.log");
            File.WriteAllText(log, "old record\n");
            int offset = AppFactsUrlRecording.Read(log).Length;
            Task writer = Task.CompletedTask;
            try
            {
                if (kind == "already complete") File.AppendAllText(log, "xdg-open\turl\n");
                else if (kind != "absent") writer = Task.Run(() =>
                {
                    Thread.Sleep(kind == "late" ? 350 : 250); // beyond the old ~0.1s simulated wait
                    File.AppendAllText(log, "xdg-open\turl");
                    if (kind != "incomplete") File.AppendAllText(log, "\n");
                });
                var deadline = TimeSpan.FromMilliseconds(kind == "late" ? 100 : 1000);
                if (kind == "delayed" || kind == "already complete")
                    Assert.Equal(new[] { "xdg-open\turl" }, AppFactsUrlRecording.Wait(log, offset, deadline));
                else Assert.Throws<TimeoutException>(() => AppFactsUrlRecording.Wait(log, offset, deadline));
            }
            finally
            {
                writer.GetAwaiter().GetResult();
                Directory.Delete(directory, true);
            }
        }
    }
}
