using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine.Video
{
    /// <summary>
    /// A VideoPlayer's sound: each enabled audio track it controls is decoded by the host (ffmpeg)
    /// into a streamed clip played by a voice of the track's AudioSource (AudioSource output: the
    /// source's settings, volume and filters apply; its clip is left alone) or of a hidden 2D source
    /// (Direct output: the track's direct volume and mute). The sound plays while the pictures play,
    /// is silent while they are paused, starts again at their time when they start, are sought or
    /// loop, and keeps its pitch at another playback speed, skipping what faster pictures leave
    /// behind (measured: tests/projects/videoaudio, Unity 6000.0.71f1). A target source reports
    /// itself playing from the time a track plays through it, also after the player stops (as the
    /// Editor's does). API Only output (an AudioSampleProvider) and sound below normal speed are not
    /// implemented (said once; the latter is silent).
    /// </summary>
    public sealed partial class VideoPlayer
    {
        sealed class AudioTrack
        {
            internal ushort Index;
            internal AudioClip Clip;
            internal ulong Stream;
            internal ulong Voice;
            internal AudioSource Source;
            internal bool VoicePlaying;
            internal bool Playing;
            internal float Speed = 1f;
        }

        readonly List<AudioTrack> m_AudioPlayback = new List<AudioTrack>();
        /// <summary>The time a seek or loop starts the sound again at on its next update.</summary>
        double? m_AudioRestart;
        /// <summary>A start: the sound syncs to the pictures (decoding again only when it drifted).</summary>
        bool m_AudioSync;
        /// <summary>When the last drift check was sent (real time); at most every 0.25 s.</summary>
        float m_AudioSyncedAt = float.NegativeInfinity;
        readonly List<AudioSource> m_ClaimedSources = new List<AudioSource>();
        GameObject m_DirectAudio;
        bool m_ApiOnlyWarned;

        void RestartAudio(double time) => m_AudioRestart = time;

        void SyncAudio() => m_AudioSync = true;

        /// <summary>The rate the pictures move at: the playback speed, scaled by Time.timeScale unless
        /// the player follows unscaled time.</summary>
        float EffectiveAudioSpeed => m_TimeUpdateMode == VideoTimeUpdateMode.UnscaledGameTime ? m_PlaybackSpeed : m_PlaybackSpeed * Time.timeScale;

        /// <summary>The sources this player's sound played through stop reporting themselves playing
        /// when it is destroyed (unmeasured: the Editor's answer then).</summary>
        void ReleaseAudioClaims()
        {
            foreach (var source in m_ClaimedSources)
                if (source != null) source.m_VideoAudioClaimed = false;
            m_ClaimedSources.Clear();
        }

        static void AudioCall(string request, object body)
        {
            try { VideoClip.Call(request, JsonSerializer.Serialize(body)); }
            catch (Exception e) { Debug.LogWarning($"Unbound: a video's sound could not be played: {e.Message}"); }
        }

        /// <summary>The source a track's voice plays through, or none.</summary>
        AudioSource AudioTarget(ushort index)
        {
            if (m_AudioOutputMode == VideoAudioOutputMode.AudioSource) return Track(index).Source;
            if (m_AudioOutputMode != VideoAudioOutputMode.Direct) return null;
            if (m_DirectAudio == null)
            {
                m_DirectAudio = new GameObject("VideoPlayer direct audio") { hideFlags = HideFlags.HideAndDontSave };
                var source = m_DirectAudio.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            return m_DirectAudio.GetComponent<AudioSource>();
        }

        /// <summary>Keeps every controlled track's sound in step with the pictures this update.</summary>
        void UpdateAudio()
        {
            if (m_AudioOutputMode == VideoAudioOutputMode.APIOnly && m_SourceAudioTracks > 0 && m_Playing && !m_ApiOnlyWarned)
            {
                m_ApiOnlyWarned = true;
                Stubs.Ignored("UnityEngine.Video.VideoPlayer API Only audio output", "no AudioSampleProvider receives the video's sound");
            }
            bool output = m_Prepared && m_File != null
                && (m_AudioOutputMode == VideoAudioOutputMode.AudioSource || m_AudioOutputMode == VideoAudioOutputMode.Direct);
            int count = output ? Math.Min((int)controlledAudioTrackCount, m_SourceAudioTracks) : 0;
            bool playing = m_Playing && !m_Paused && !m_StartPending;
            float speed = EffectiveAudioSpeed;
            // Pictures slower than normal (a playback speed or Time.timeScale below 1, a paused
            // game) would leave the sound running ahead of them (unmeasured): it is silent then.
            if (playing && speed < 1f && count > 0)
            {
                Stubs.Ignored("UnityEngine.Video.VideoPlayer sound below normal speed", "the sound is silent while the pictures move slower than normal (playbackSpeed or Time.timeScale below 1)");
                playing = false;
            }
            double? restart = m_AudioRestart;
            m_AudioRestart = null;
            bool sync = m_AudioSync;
            m_AudioSync = false;
            float now = Time.realtimeSinceStartup;
            bool check = playing && now - m_AudioSyncedAt >= 0.25f;
            if (check) m_AudioSyncedAt = now;
            for (ushort i = 0; i < count; i++)
            {
                var settings = Track(i);
                var source = AudioTarget(i);
                AudioTrack track = null;
                foreach (var t in m_AudioPlayback)
                    if (t.Index == i) { track = t; break; }
                if (!settings.Enabled || source == null)
                {
                    if (track != null) CloseAudio(track);
                    continue;
                }
                // A prepared player opens its tracks before it plays, so their sound is decoded
                // when it starts (the Editor's sound starts with the pictures: videoaudio *.timing).
                bool opened = false;
                if (track == null)
                {
                    track = OpenAudio(i);
                    if (track == null) continue;
                    opened = true;
                }
                if (track.Source != source)
                {
                    StopVoice(track);
                    track.Source = source;
                }
                // The source's OnAudioFilterRead scripts filter the track's voice as its own.
                bool hasFilter = AudioFilters.Update(track.Voice, source.gameObject);
                if (source.m_HasFilter != hasFilter) source.m_HasFilter = hasFilter;
                float volume = m_AudioOutputMode == VideoAudioOutputMode.Direct ? (settings.Mute ? 0f : settings.Volume) : source.m_Volume;
                source.SendPacket(track.Voice, track.Clip, volume, false);
                // A seek or loop starts the voice again too: what the mixer read ahead is dropped
                // and the streamed clip's frames count from 0 again.
                if (restart.HasValue) StopVoice(track);
                if (!track.VoicePlaying && playing && source.isActiveAndEnabled)
                {
                    AudioBackend.Current.Command(track.Voice, new AudioSourceCommand { Kind = AudioCommandKind.Play });
                    track.VoicePlaying = true;
                    if (m_AudioOutputMode == VideoAudioOutputMode.AudioSource && !source.m_VideoAudioClaimed)
                    {
                        source.m_VideoAudioClaimed = true;
                        if (!m_ClaimedSources.Contains(source)) m_ClaimedSources.Add(source);
                    }
                }
                // A new decoder starts at the pictures' time, a seek or loop starts it again at its
                // time; a start, and every 0.25 s of playing, syncs it to the pictures, which
                // starts decoding again only when the sound has drifted (video.rs AudioStream).
                double? again = opened ? m_Time : restart;
                double? syncTo = again == null && (sync || check) ? m_Time : (double?)null;
                if (again.HasValue || syncTo.HasValue || playing != track.Playing || speed != track.Speed)
                {
                    AudioCall("video-audio-control", new
                    {
                        id = track.Stream,
                        playing,
                        speed = (double)speed,
                        restart = again,
                        sync = syncTo,
                    });
                    track.Playing = playing;
                    track.Speed = speed;
                }
            }
            for (int k = m_AudioPlayback.Count - 1; k >= 0; k--)
                if (m_AudioPlayback[k].Index >= count) CloseAudio(m_AudioPlayback[k]);
        }

        AudioTrack OpenAudio(ushort index)
        {
            var info = m_SourceAudioTrackInfo != null && index < m_SourceAudioTrackInfo.Length ? m_SourceAudioTrackInfo[index] : default;
            int channels = Math.Max(1, (int)info.Channels);
            int rate = info.SampleRate > 0 ? (int)info.SampleRate : 48000;
            // A streamed clip much longer than any video: its data comes from the decoder.
            int frames = (int)Math.Min(int.MaxValue / 2, (long)rate * 3600 * 6);
            var clip = AudioClip.Create("VideoPlayer audio " + index, frames, channels, rate, true, _ => { });
            clip.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                using var reply = JsonDocument.Parse(VideoClip.Call("video-audio-open", JsonSerializer.Serialize(new
                {
                    clip = clip.m_Handle, path = m_File, track = (int)index, channels, rate,
                })));
                var track = new AudioTrack { Index = index, Clip = clip, Stream = reply.RootElement.GetProperty("id").GetUInt64(), Voice = AudioBackend.NextHandle() };
                m_AudioPlayback.Add(track);
                return track;
            }
            catch (Exception e)
            {
                Object.DestroyImmediate(clip);
                Debug.LogWarning($"Unbound: the sound of video '{m_Name}' could not be played: {e.Message}");
                return null;
            }
        }

        static void StopVoice(AudioTrack track)
        {
            if (!track.VoicePlaying) return;
            AudioBackend.Current.Command(track.Voice, new AudioSourceCommand { Kind = AudioCommandKind.Stop });
            track.VoicePlaying = false;
        }

        void CloseAudio(AudioTrack track)
        {
            StopVoice(track);
            AudioBackend.Current.ReleaseSource(track.Voice);
            AudioFilters.Forget(track.Voice);
            AudioCall("video-audio-close", new { id = track.Stream });
            if (track.Clip != null) Object.DestroyImmediate(track.Clip);
            m_AudioPlayback.Remove(track);
        }

        /// <summary>Stops every track's sound (the player stops, is released or destroyed).</summary>
        void CloseAllAudio()
        {
            for (int k = m_AudioPlayback.Count - 1; k >= 0; k--) CloseAudio(m_AudioPlayback[k]);
            m_AudioRestart = null;
            if (m_DirectAudio != null)
            {
                Object.DestroyImmediate(m_DirectAudio);
                m_DirectAudio = null;
            }
        }
    }
}
