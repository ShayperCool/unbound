using Unbound.Runtime;
using Unbound.Runtime.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The dsp clock without an output device: a frame's real time is counted as the
    /// frame starts, in whole dsp buffers (the rest is owed to the next frame), so that the frame's
    /// scripts read a clock that has already counted a long frame before them, as the Editor's audio
    /// thread has (tests/projects/dspclock); the audio update at the frame's end adds nothing.</summary>
    public sealed class AudioClockTests : System.IDisposable
    {
        readonly VirtualAudioBackend m_Backend = new VirtualAudioBackend();
        readonly double m_RealDelta = Time.s_RealDeltaTime;

        public AudioClockTests()
        {
            AudioSystem.ResetForTests();
            AudioBackend.Use(m_Backend);
            PlayableSystem.ResetClockForTests();
        }

        public void Dispose()
        {
            Time.s_RealDeltaTime = m_RealDelta;
            PlayableSystem.ResetClockForTests();
            AudioSystem.ResetForTests();
        }

        double Step => (double)m_Backend.Configuration.dspBufferSize / m_Backend.Configuration.sampleRate;

        /// <summary>One frame of the player loop's audio: the clock at its start, the update at its end.</summary>
        static void Frame(double realDelta)
        {
            Time.s_RealDeltaTime = realDelta;
            AudioSystem.AdvanceClock();
            AudioSystem.Update();
        }

        [Fact]
        public void ALongFrameIsCountedAsTheNextFrameStarts()
        {
            Frame(0.02);
            double before = AudioSettings.dspTime;
            // The frame after a 0.3 s one: its scripts already read the 0.3 s, in whole buffers
            // (with what the earlier frame owed: 0.32 s is 15 buffers of 1024 at 48000 Hz).
            Time.s_RealDeltaTime = 0.3;
            AudioSystem.AdvanceClock();
            double counted = AudioSettings.dspTime;
            Assert.Equal(15 * Step, counted - before, 12);
            Assert.Equal(System.Math.Round(counted / Step), counted / Step, 9);
            // The audio update at the frame's end does not count it again.
            AudioSystem.Update();
            Assert.Equal(counted, AudioSettings.dspTime);
        }

        /// <summary>One frame as the player loop runs it: the audio clock, DirectorSampleTime, and
        /// the audio update at the frame's end (whose status gives the buffer).</summary>
        static void Sample(double realDelta)
        {
            Time.s_RealDeltaTime = realDelta;
            AudioSystem.AdvanceClock();
            PlayableSystem.SampleTime();
            AudioSystem.Update();
        }

        [Fact]
        public void AGraphsDspClockMovesEveryFrameByItsRealTime()
        {
            Sample(0.0);
            Assert.Equal(0, PlayableSystem.DspDelta);
            double start = PlayableSystem.GraphDspTime;
            for (int i = 1; i <= 30; i++)
            {
                Sample(1.0 / 120);
                // Every frame moves the graphs by the frame's real time, though the audio clock
                // moves only in whole buffers (here never behind it: they started together).
                Assert.Equal(1.0 / 120, PlayableSystem.DspDelta, 9);
                Assert.InRange(PlayableSystem.GraphDspTime, AudioSettings.dspTime, AudioSettings.dspTime + Step);
            }
            Assert.Equal(30.0 / 120, PlayableSystem.GraphDspTime - start, 9);
            // A long frame moves both by its real time.
            double before = PlayableSystem.GraphDspTime;
            Sample(0.3);
            Assert.Equal(0.3, PlayableSystem.GraphDspTime - before, 9);
        }

        [Theory]
        // Made with 1/120 s of real time the clock still owes: left that far behind it.
        [InlineData(1.0 / 120, false)]
        // Made with 15 ms owed, more than half a buffer: pulled up to it as the clock steps.
        [InlineData(0.015, true)]
        public void AGraphMadeWhileTheClockOwesTimeIsPulledUpOnlyFromHalfABufferBehind(double owed, bool pulled)
        {
            Sample(0.0);
            Sample(owed);
            PlayableSystem.ResetClockForTests();
            Time.s_RealDeltaTime = 0;
            PlayableSystem.SampleTime();
            double made = PlayableSystem.GraphDspTime;
            Assert.Equal(AudioSettings.dspTime, made);
            // Frames of 1 ms until the audio clock steps a buffer.
            double dsp = AudioSettings.dspTime;
            int frames = 0;
            while (AudioSettings.dspTime == dsp)
            {
                Sample(0.001);
                frames++;
            }
            double behind = AudioSettings.dspTime - PlayableSystem.GraphDspTime;
            if (pulled) Assert.Equal(0, behind, 12);
            else
            {
                Assert.Equal(made + frames * 0.001, PlayableSystem.GraphDspTime, 9);
                Assert.InRange(behind, 0.008, 0.5 * Step);
            }
        }

        [Fact]
        public void AGraphFollowsADeviceClockThatRunsAheadOrStops()
        {
            Sample(0.0);
            Sample(0.0);
            // A clock that moved much further than real time takes the graphs along to it; one
            // that stops holds them a buffer above it.
            m_Backend.AdvanceClock(1.0);
            Sample(0.0);
            Assert.Equal(AudioSettings.dspTime, PlayableSystem.GraphDspTime, 12);
            double clock = AudioSettings.dspTime;
            Time.s_RealDeltaTime = 0.5;
            PlayableSystem.SampleTime();
            Assert.Equal(clock + Step, PlayableSystem.GraphDspTime, 12);
        }

        [Fact]
        public void BeforeAnyAudioUpdateAGraphKeepsToTheClock()
        {
            // No audio update yet: no buffer known, the graphs take the audio clock as it is.
            Time.s_RealDeltaTime = 0;
            PlayableSystem.SampleTime();
            Time.s_RealDeltaTime = 0.3;
            AudioSystem.AdvanceClock();
            PlayableSystem.SampleTime();
            Assert.Equal(AudioSettings.dspTime, PlayableSystem.GraphDspTime, 12);
        }

        [Fact]
        public void FramesShorterThanABufferAreOwedToTheNextOnes()
        {
            // 1024 samples at 48000 Hz: four frames of 1/120 s make one buffer and a remainder.
            double start = AudioSettings.dspTime;
            Frame(1.0 / 120);
            Frame(1.0 / 120);
            Assert.Equal(start, AudioSettings.dspTime);
            Frame(1.0 / 120);
            Assert.Equal(start + Step, AudioSettings.dspTime, 12);
            Frame(1.0 / 120);
            Assert.Equal(start + Step, AudioSettings.dspTime, 12);
            // A negative delta counts nothing.
            Frame(-1.0);
            Assert.Equal(start + Step, AudioSettings.dspTime, 12);
        }
    }
}
