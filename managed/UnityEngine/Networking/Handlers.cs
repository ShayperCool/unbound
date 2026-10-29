using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace UnityEngine.Networking
{
    /// <summary>Receives a response body. The request gives it the body on the main thread when the
    /// response is complete: its length, the data (in chunks of a script handler's preallocated
    /// buffer), then CompleteContent.</summary>
    public partial class DownloadHandler : IDisposable
    {
        internal bool m_IsDone;
        internal string m_Error;
        internal string m_ContentType;
        NativeArray<byte> m_Native;

        internal DownloadHandler() { }

        public bool isDone => m_IsDone;

        public string error => m_Error;

        public byte[] data => GetData();

        public string text => GetText();

        public NativeArray<byte>.ReadOnly nativeData => GetNativeData().AsReadOnly();

        /// <summary>The bytes received, for the handlers that keep them.</summary>
        internal virtual byte[] Bytes() => null;

        protected virtual NativeArray<byte> GetNativeData()
        {
            var bytes = Bytes();
            if (bytes == null) return default;
            if (!m_Native.IsCreated || m_Native.Length != bytes.Length)
            {
                if (m_Native.IsCreated) m_Native.Dispose();
                m_Native = new NativeArray<byte>(bytes, Allocator.Persistent);
            }
            return m_Native;
        }

        protected virtual byte[] GetData() => Bytes();

        /// <summary>The data decoded with the charset of the response's Content-Type, UTF-8 by default.</summary>
        protected virtual string GetText()
        {
            var bytes = GetData();
            if (bytes == null) return null;
            return EncodingOf(m_ContentType).GetString(bytes);
        }

        static Encoding EncodingOf(string contentType)
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                int at = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    var name = contentType.Substring(at + 8).Trim().Trim('"');
                    int end = name.IndexOf(';');
                    if (end >= 0) name = name.Substring(0, end);
                    try { return Encoding.GetEncoding(name); }
                    catch (ArgumentException) { }
                }
            }
            return Encoding.UTF8;
        }

        [Unbound.EditorFaithful("as Unity: the base handler accepts the data (script handlers override it)")]
        protected virtual bool ReceiveData(byte[] data, int dataLength) => true;

        protected virtual void ReceiveContentLengthHeader(ulong contentLength)
        {
#pragma warning disable CS0618
            ReceiveContentLength((int)Math.Min(contentLength, int.MaxValue));
#pragma warning restore CS0618
        }

        [Unbound.EditorFaithful("as Unity: an overridable notification, empty in the base handler")]
        [Obsolete("Use ReceiveContentLengthHeader", false)]
        protected virtual void ReceiveContentLength(int contentLength) { }

        [Unbound.EditorFaithful("as Unity: an overridable notification, empty in the base handler")]
        protected virtual void CompleteContent() { }

        protected virtual float GetProgress() => m_IsDone ? 1f : 0f;

        protected static T GetCheckedDownloader<T>(UnityWebRequest www) where T : DownloadHandler
        {
            if (www == null) throw new NullReferenceException("Cannot get content from a null UnityWebRequest object");
            if (!www.isDone) throw new InvalidOperationException("Cannot get content from an unfinished UnityWebRequest object");
            if (www.result == UnityWebRequest.Result.ProtocolError) throw new InvalidOperationException(www.error);
            return (T)www.downloadHandler;
        }

        /// <summary>Takes a chunk of the body; false stops the download.</summary>
        internal virtual bool Accept(byte[] data, int length) => ReceiveData(data, length);

        internal virtual void Deliver(byte[] body, string contentType, bool received)
        {
            m_ContentType = contentType;
            if (received)
            {
                ReceiveContentLengthHeader((ulong)(body?.Length ?? 0));
                if (body != null && body.Length > 0 && !Accept(body, body.Length))
                    m_Error = "Download handler stopped the download";
            }
            CompleteContent();
            m_IsDone = true;
        }

        public virtual void Dispose()
        {
            if (m_Native.IsCreated) m_Native.Dispose();
            m_Native = default;
        }
    }

    /// <summary>Keeps the body in memory.</summary>
    public sealed partial class DownloadHandlerBuffer : DownloadHandler
    {
        readonly System.IO.MemoryStream m_Stream = new System.IO.MemoryStream();
        byte[] m_Bytes;

        public DownloadHandlerBuffer() { }

        internal override bool Accept(byte[] data, int length)
        {
            m_Stream.Write(data, 0, length);
            m_Bytes = null;
            return true;
        }

        internal override byte[] Bytes() => m_Bytes ??= m_Stream.ToArray();

        protected override NativeArray<byte> GetNativeData() => base.GetNativeData();

        public override void Dispose()
        {
            base.Dispose();
            m_Stream.Dispose();
        }

        public static string GetContent(UnityWebRequest www) => GetCheckedDownloader<DownloadHandlerBuffer>(www).text;
    }

    /// <summary>Writes the body to a file.</summary>
    public sealed partial class DownloadHandlerFile : DownloadHandler
    {
        readonly string m_Path;
        readonly bool m_Append;
        bool m_Opened;

        public DownloadHandlerFile(string path) : this(path, false) { }

        public DownloadHandlerFile(string path, bool append)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path), "Path cannot be null or empty");
            m_Path = path;
            m_Append = append;
        }

        public bool removeFileOnAbort { get; set; }

        internal override bool Accept(byte[] data, int length)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(m_Path);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
                using var file = new System.IO.FileStream(m_Path, m_Append || m_Opened ? System.IO.FileMode.Append : System.IO.FileMode.Create);
                file.Write(data, 0, length);
                m_Opened = true;
                return true;
            }
            catch (Exception e)
            {
                m_Error = e.Message;
                return false;
            }
        }

        internal override void Deliver(byte[] body, string contentType, bool received)
        {
            if (!received && removeFileOnAbort && m_Opened)
            {
                try { System.IO.File.Delete(m_Path); }
                catch (Exception) { }
            }
            if (received && (body == null || body.Length == 0) && !m_Append)
            {
                // An empty response still leaves an (empty) file.
                try { System.IO.File.WriteAllBytes(m_Path, Array.Empty<byte>()); }
                catch (Exception e) { m_Error = e.Message; }
            }
            base.Deliver(body, contentType, received);
        }

        protected override NativeArray<byte> GetNativeData() => throw new NotSupportedException("Raw data access is not supported");

        protected override byte[] GetData() => throw new NotSupportedException("Raw data access is not supported");

        protected override string GetText() => throw new NotSupportedException("String access is not supported");
    }

    /// <summary>Base of scripted download handlers: subclasses receive the body through ReceiveData.</summary>
    public partial class DownloadHandlerScript : DownloadHandler
    {
        readonly byte[] m_Preallocated;

        public DownloadHandlerScript() { }

        public DownloadHandlerScript(byte[] preallocatedBuffer)
        {
            if (preallocatedBuffer == null || preallocatedBuffer.Length < 1)
                throw new ArgumentException("Cannot create a preallocated-buffer DownloadHandlerScript backed by a null or zero-length array");
            m_Preallocated = preallocatedBuffer;
        }

        /// <summary>With a preallocated buffer, the data comes through it, a buffer's worth at a time.</summary>
        internal override bool Accept(byte[] data, int length)
        {
            if (m_Preallocated == null) return ReceiveData(data, length);
            for (int offset = 0; offset < length; offset += m_Preallocated.Length)
            {
                int n = Math.Min(m_Preallocated.Length, length - offset);
                Array.Copy(data, offset, m_Preallocated, 0, n);
                if (!ReceiveData(m_Preallocated, n)) return false;
            }
            return true;
        }
    }

    /// <summary>Supplies a request body.</summary>
    public partial class UploadHandler : IDisposable
    {
        internal byte[] m_Data;
        string m_ContentType;
        internal float m_Progress;

        internal UploadHandler() { }

        public byte[] data => m_Data;

        public string contentType
        {
            get => m_ContentType;
            set => m_ContentType = value;
        }

        public float progress => m_Progress;

        /// <summary>The bytes to send, read when the request is sent.</summary>
        internal virtual byte[] Payload() => m_Data;

        [Unbound.EditorFaithful("the payload is managed memory: nothing native to release")]
        public virtual void Dispose() { }
    }

    public sealed partial class UploadHandlerRaw : UploadHandler
    {
        public UploadHandlerRaw(byte[] data)
        {
            if (data != null && data.Length == 0) data = null;
            m_Data = data;
        }

        public UploadHandlerRaw(NativeArray<byte> data, bool transferOwnership)
        {
            m_Data = data.IsCreated && data.Length > 0 ? data.ToArray() : null;
            if (transferOwnership && data.IsCreated) data.Dispose();
        }

        public UploadHandlerRaw(NativeArray<byte>.ReadOnly data)
        {
            m_Data = data.IsCreated && data.Length > 0 ? data.ToArray() : null;
        }

        [Unbound.EditorFaithful("the payload is managed memory: nothing native to release")]
        public override void Dispose() { }
    }

    public sealed partial class UploadHandlerFile : UploadHandler
    {
        readonly string m_Path;

        public UploadHandlerFile(string filePath)
        {
            m_Path = filePath;
        }

        internal override byte[] Payload() => m_Data = System.IO.File.ReadAllBytes(m_Path);
    }

    /// <summary>Decides whether to trust a server's certificate (only when one is set).</summary>
    public partial class CertificateHandler : IDisposable
    {
        protected CertificateHandler() { }

        [Unbound.EditorFaithful("as Unity: the base handler trusts no certificate (script handlers override it)")]
        protected virtual bool ValidateCertificate(byte[] certificateData) => false;

        internal bool Validate(byte[] certificateData)
        {
            try { return ValidateCertificate(certificateData); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        [Unbound.EditorFaithful("nothing native to release")]
        public void Dispose() { }
    }

    public partial interface IMultipartFormSection
    {
        string sectionName { get; }
        byte[] sectionData { get; }
        string fileName { get; }
        string contentType { get; }
    }

    public partial class MultipartFormDataSection : IMultipartFormSection
    {
        readonly string m_Name;
        readonly byte[] m_Data;
        readonly string m_ContentType;

        public MultipartFormDataSection(string name, byte[] data, string contentType)
        {
            if (data == null || data.Length < 1) throw new ArgumentException("Cannot create a multipart form data section without body data");
            m_Name = name;
            m_Data = data;
            m_ContentType = contentType;
        }

        public MultipartFormDataSection(string name, byte[] data) : this(name, data, null) { }

        public MultipartFormDataSection(byte[] data) : this(null, data) { }

        public MultipartFormDataSection(string name, string data, Encoding encoding, string contentType)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentException("Cannot create a multipart form data section without body data");
            encoding ??= Encoding.UTF8;
            m_Name = name;
            m_Data = encoding.GetBytes(data);
            m_ContentType = contentType != null && !contentType.Contains("encoding=", StringComparison.Ordinal)
                ? contentType.Trim(' ', ';') + "; encoding=" + encoding.WebName
                : contentType;
        }

        public MultipartFormDataSection(string name, string data, string contentType) : this(name, data, Encoding.UTF8, contentType) { }

        public MultipartFormDataSection(string name, string data) : this(name, data, "text/plain") { }

        public MultipartFormDataSection(string data) : this(null, data) { }

        public string sectionName => m_Name;

        public byte[] sectionData => m_Data;

        [Unbound.EditorFaithful("as Unity: a data section has no file name")]
        public string fileName => null;

        public string contentType => m_ContentType;
    }

    public partial class MultipartFormFileSection : IMultipartFormSection
    {
        readonly string m_Name;
        readonly byte[] m_Data;
        readonly string m_FileName;
        readonly string m_ContentType;

        public MultipartFormFileSection(string name, byte[] data, string fileName, string contentType)
        {
            if (data == null || data.Length < 1) throw new ArgumentException("Cannot create a multipart form file section without body data");
            m_Name = string.IsNullOrEmpty(name) ? "file" : name;
            m_Data = data;
            m_FileName = string.IsNullOrEmpty(fileName) ? "file.dat" : fileName;
            m_ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType;
        }

        public MultipartFormFileSection(byte[] data) : this(null, data, null, null) { }

        public MultipartFormFileSection(string fileName, byte[] data) : this(null, data, fileName, null) { }

        public MultipartFormFileSection(string name, string data, Encoding dataEncoding, string fileName)
            : this(name, (dataEncoding ?? Encoding.UTF8).GetBytes(data ?? ""), string.IsNullOrEmpty(fileName) ? "file.txt" : fileName,
                "text/plain; charset=" + (dataEncoding ?? Encoding.UTF8).WebName)
        {
        }

        public MultipartFormFileSection(string data, Encoding dataEncoding, string fileName) : this(null, data, dataEncoding, fileName) { }

        public MultipartFormFileSection(string data, string fileName) : this(data, null, fileName) { }

        public string sectionName => m_Name;

        public byte[] sectionData => m_Data;

        public string fileName => m_FileName;

        public string contentType => m_ContentType;
    }
}

namespace UnityEngine
{
    /// <summary>A form to post: url-encoded fields, or multipart data once a binary field is added.</summary>
    public partial class WWWForm
    {
        readonly List<(string Name, byte[] Data, string FileName, string MimeType)> m_Fields = new List<(string, byte[], string, string)>();
        readonly byte[] m_Boundary = Networking.UnityWebRequest.GenerateBoundary();
        bool m_ContainsFiles;

        public WWWForm() { }

        public void AddField(string fieldName, string value) => AddField(fieldName, value, Encoding.UTF8);

        public void AddField(string fieldName, string value, Encoding e)
        {
            m_Fields.Add((fieldName, (e ?? Encoding.UTF8).GetBytes(value ?? ""), null, "text/plain; charset=\"" + (e ?? Encoding.UTF8).WebName + "\""));
        }

        public void AddField(string fieldName, int i) => AddField(fieldName, i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void AddBinaryData(string fieldName, byte[] contents) => AddBinaryData(fieldName, contents, null, null);

        public void AddBinaryData(string fieldName, byte[] contents, string fileName) => AddBinaryData(fieldName, contents, fileName, null);

        public void AddBinaryData(string fieldName, byte[] contents, string fileName, string mimeType)
        {
            m_ContainsFiles = true;
            bool image = contents != null && contents.Length > 8 && contents[0] == 0x89 && contents[1] == 0x50 && contents[2] == 0x4e && contents[3] == 0x47;
            m_Fields.Add((fieldName, contents ?? Array.Empty<byte>(), fileName ?? fieldName + (image ? ".png" : ".dat"), mimeType ?? (image ? "image/png" : "application/octet-stream")));
        }

        public Dictionary<string, string> headers
        {
            get
            {
                var result = new Dictionary<string, string>();
                result["Content-Type"] = m_ContainsFiles
                    ? "multipart/form-data; boundary=\"" + Encoding.UTF8.GetString(m_Boundary) + "\""
                    : "application/x-www-form-urlencoded";
                return result;
            }
        }

        public byte[] data
        {
            get
            {
                if (!m_ContainsFiles)
                {
                    var sb = new StringBuilder();
                    foreach (var entry in m_Fields)
                    {
                        if (sb.Length > 0) sb.Append('&');
                        sb.Append(Networking.WebEncoding.DataEncode(entry.Name)).Append('=')
                            .Append(Networking.WebEncoding.URLEncode(Encoding.UTF8.GetString(entry.Data), Encoding.UTF8));
                    }
                    return Encoding.UTF8.GetBytes(sb.ToString());
                }
                var boundary = Encoding.UTF8.GetString(m_Boundary);
                using var stream = new System.IO.MemoryStream();
                void Write(string s) => stream.Write(Encoding.UTF8.GetBytes(s));
                foreach (var entry in m_Fields)
                {
                    Write("--" + boundary + "\r\n");
                    Write("Content-Type: " + entry.MimeType + "\r\n");
                    Write("Content-disposition: form-data; name=\"" + entry.Name + "\"");
                    if (entry.FileName != null) Write("; filename=\"" + entry.FileName + "\"");
                    Write("\r\n\r\n");
                    stream.Write(entry.Data);
                    Write("\r\n");
                }
                Write("--" + boundary + "--\r\n");
                return stream.ToArray();
            }
        }
    }
}
