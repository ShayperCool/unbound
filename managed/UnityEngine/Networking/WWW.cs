using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace UnityEngine
{
    /// <summary>Unity's original web request, on UnityWebRequest: sent when constructed, a yield
    /// instruction that waits for the response.</summary>
    [Obsolete("Use UnityWebRequest, a fully featured replacement which is more efficient and has additional features")]
    public partial class WWW : CustomYieldInstruction, IDisposable
    {
        readonly UnityWebRequest m_Request;
        Texture2D m_Texture;

        public WWW(string url)
        {
            m_Request = UnityWebRequest.Get(url);
            m_Request.SendWebRequest();
        }

        public WWW(string url, WWWForm form)
        {
            m_Request = UnityWebRequest.Post(url, form);
            m_Request.chunkedTransfer = false;
            m_Request.SendWebRequest();
        }

        public WWW(string url, byte[] postData)
        {
            m_Request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST, new DownloadHandlerBuffer(), new UploadHandlerRaw(postData));
            m_Request.SendWebRequest();
        }

        public WWW(string url, byte[] postData, Dictionary<string, string> headers)
        {
            var method = postData == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST;
            m_Request = new UnityWebRequest(url, method, new DownloadHandlerBuffer(), postData != null ? new UploadHandlerRaw(postData) : null);
            if (headers != null)
                foreach (var header in headers) m_Request.SetRequestHeader(header.Key, header.Value);
            m_Request.SendWebRequest();
        }

        public static string EscapeURL(string s) => UnityWebRequest.EscapeURL(s);

        public static string EscapeURL(string s, Encoding e) => UnityWebRequest.EscapeURL(s, e);

        public static string UnEscapeURL(string s) => UnityWebRequest.UnEscapeURL(s);

        public static string UnEscapeURL(string s, Encoding e) => UnityWebRequest.UnEscapeURL(s, e);

        public override bool keepWaiting => !m_Request.isDone;

        public bool isDone => m_Request.isDone;

        public string url => m_Request.url;

        public float progress
        {
            get
            {
                float p = m_Request.downloadProgress;
                return p < 0f ? 0f : p;
            }
        }

        public float uploadProgress
        {
            get
            {
                float p = m_Request.uploadProgress;
                return p < 0f ? 0f : p;
            }
        }

        public int bytesDownloaded => (int)m_Request.downloadedBytes;

        [Obsolete("WWW.size is obsolete. Please use WWW.bytesDownloaded instead", false)]
        public int size => bytesDownloaded;

        /// <summary>The connection error, or the status line of an HTTP error.</summary>
        public string error
        {
            get
            {
                if (!m_Request.isDone) return null;
                if (m_Request.result == UnityWebRequest.Result.ConnectionError || m_Request.result == UnityWebRequest.Result.DataProcessingError) return m_Request.error;
                if (m_Request.responseCode >= 400) return StatusWithoutVersion();
                return null;
            }
        }

        /// <summary>"404 Not Found" from the status line "HTTP/1.1 404 Not Found".</summary>
        string StatusWithoutVersion()
        {
            var line = m_Request.m_StatusLine ?? "";
            int space = line.IndexOf(' ');
            return space >= 0 ? line.Substring(space + 1) : line;
        }

        public Dictionary<string, string> responseHeaders
        {
            get
            {
                if (!m_Request.isDone) return new Dictionary<string, string>();
                var headers = m_Request.GetResponseHeaders() ?? new Dictionary<string, string>();
                if (m_Request.m_StatusLine != null) headers["STATUS"] = m_Request.m_StatusLine;
                return headers;
            }
        }

        public byte[] bytes
        {
            get
            {
                if (!m_Request.isDone) return Array.Empty<byte>();
                return m_Request.downloadHandler?.data ?? Array.Empty<byte>();
            }
        }

        public string text
        {
            get
            {
                if (!m_Request.isDone) return "";
                return m_Request.downloadHandler?.text ?? "";
            }
        }

        [Obsolete("Please use WWW.text instead. (UnityUpgradable) -> text", false)]
        public string data => text;

        public Texture2D texture => m_Texture ??= MakeTexture(true);

        public Texture2D textureNonReadable => MakeTexture(false);

        Texture2D MakeTexture(bool readable)
        {
            var result = new Texture2D(2, 2);
            if (m_Request.isDone) result.LoadImage(bytes, !readable);
            return result;
        }

        public void LoadImageIntoTexture(Texture2D texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (!m_Request.isDone) return;
            texture.LoadImage(bytes);
        }

        public ThreadPriority threadPriority { get; set; } = ThreadPriority.Normal;

        public void Dispose() => m_Request.Dispose();
    }
}
