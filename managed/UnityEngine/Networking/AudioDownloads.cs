using System;
using System.IO;

namespace UnityEngine.Networking
{
    /// <summary>
    /// Keeps a downloaded audio file and makes it an AudioClip, as the Unity 6000.0.71f1 Editor does
    /// (tests/projects/networkcaching):
    ///
    /// The clip is made on the first access to audioClip (or GetContent) and kept: a nameless
    /// clip, DecompressOnLoad, preloaded and loaded, whatever the flags. Only the file's own
    /// <c>AudioType</c> decodes (<c>UNKNOWN</c> takes any; unbound_assets::audio_download); a file
    /// that does not decode gives a clip without data and an error. With streamAudio the clip's
    /// samples cannot be read back (GetData fails as for any streamed clip); with compressed they
    /// cannot be read back for the formats played compressed (MPEG and IMA ADPCM WAV), the others
    /// being decoded anyway; setting either flag while the other is set warns ("Audio
    /// compression is not valid as streamAudio is set to true") and still sets it. Read before the
    /// download completes, the clip is one without data, kept after it completes; with
    /// streamAudio it is a clip of the length a WAV file's header announces, its samples filled
    /// in when the download completes. Reading audioClip before the request is sent, or after it
    /// failed, throws. data is the file (null after a failure); text throws.
    ///
    /// Unbound's choices (unmeasured): a streamed clip plays only once its download is complete;
    /// streamed Ogg and MPEG files read before then give a clip without data, like other clips
    /// read early; a compressed IMA ADPCM WAV keeps its decoded samples for playback.
    /// </summary>
    public sealed partial class DownloadHandlerAudioClip : DownloadHandler
    {
        readonly MemoryStream m_Stream = new MemoryStream();
        readonly AudioType m_Type;
        bool m_StreamAudio;
        bool m_Compressed;
        bool m_Started;
        bool m_Complete;
        bool m_Failed;
        AudioClip m_Clip;
        bool m_FillAtEnd;

        public DownloadHandlerAudioClip(string url, AudioType audioType)
        {
            m_Type = audioType;
        }

        public DownloadHandlerAudioClip(Uri uri, AudioType audioType) : this(uri?.AbsoluteUri, audioType) { }

        public bool streamAudio
        {
            get => m_StreamAudio;
            set
            {
                if (m_Compressed) WarnBoth();
                m_StreamAudio = value;
            }
        }

        public bool compressed
        {
            get => m_Compressed;
            set
            {
                if (m_StreamAudio) WarnBoth();
                m_Compressed = value;
            }
        }

        static void WarnBoth() => Debug.LogWarning("Audio compression is not valid as streamAudio is set to true");

        public AudioClip audioClip
        {
            get
            {
                if (!m_Started) throw new InvalidOperationException("Cannot access the .audioClip property of DownloadHandlerAudioClip before the download is initiated");
                if (m_Failed) throw new InvalidOperationException("Cannot access the .audioClip property of an aborted DownloadHandlerAudioClip");
                if (m_Clip == null) m_Clip = MakeClip();
                return m_Clip;
            }
        }

        AudioClip MakeClip()
        {
            var data = m_Stream.GetBuffer().AsSpan(0, (int)m_Stream.Length);
            if (m_StreamAudio)
            {
                var clip = AudioClip.FromDownload(data, m_Type, AudioClip.DownloadedForm.Streamed, headerOnly: !m_Complete);
                m_FillAtEnd = !m_Complete && clip.m_Channels > 0;
                return clip;
            }
            if (!m_Complete) return AudioClip.FromDownload(default, m_Type, AudioClip.DownloadedForm.Decompressed, headerOnly: true);
            var form = m_Compressed && KeptCompressed(data) ? AudioClip.DownloadedForm.Compressed : AudioClip.DownloadedForm.Decompressed;
            return AudioClip.FromDownload(data, m_Type, form, headerOnly: false);
        }

        /// <summary>Whether a compressed clip of this file stays compressed: MPEG audio and IMA
        /// ADPCM WAV files do, other formats are decoded.</summary>
        static bool KeptCompressed(ReadOnlySpan<byte> data)
        {
            const ushort ImaAdpcm = 0x11;
            if (data.Length >= 12 && data.Slice(0, 4).SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8))
            {
                for (int at = 12; at + 8 <= data.Length;)
                {
                    int size = BitConverter.ToInt32(data.Slice(at + 4, 4));
                    if (data.Slice(at, 4).SequenceEqual("fmt "u8)) return at + 10 <= data.Length && BitConverter.ToUInt16(data.Slice(at + 8, 2)) == ImaAdpcm;
                    if (size < 0) break;
                    at += 8 + size + (size & 1);
                }
                return false;
            }
            int start = 0;
            if (data.Length >= 10 && data.Slice(0, 3).SequenceEqual("ID3"u8))
                start = 10 + (data[6] << 21 | data[7] << 14 | data[8] << 7 | data[9]);
            return data.Length >= start + 2 && data[start] == 0xff && (data[start + 1] & 0xe0) == 0xe0;
        }

        internal override bool StartRequest()
        {
            m_Started = true;
            return false;
        }

        internal override bool Accept(byte[] data, int length)
        {
            m_Stream.Write(data, 0, length);
            return true;
        }

        internal override void End(bool received)
        {
            m_Complete = received;
            m_Failed = !received;
            if (received && m_FillAtEnd) m_Clip.FillDownload(m_Stream.GetBuffer().AsSpan(0, (int)m_Stream.Length), m_Type);
            m_FillAtEnd = false;
            base.End(received);
        }

        internal override byte[] Bytes() => m_Failed ? null : m_Stream.ToArray();

        protected override string GetText() => throw new NotSupportedException("String access is not supported for audio clips");

        public override void Dispose()
        {
            base.Dispose();
            m_Stream.Dispose();
        }

        public static AudioClip GetContent(UnityWebRequest www) => GetCheckedDownloader<DownloadHandlerAudioClip>(www).audioClip;
    }

    public static partial class UnityWebRequestMultimedia
    {
        public static UnityWebRequest GetAudioClip(string uri, AudioType audioType) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType), null);

        public static UnityWebRequest GetAudioClip(Uri uri, AudioType audioType) =>
            new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType), null);
    }
}
