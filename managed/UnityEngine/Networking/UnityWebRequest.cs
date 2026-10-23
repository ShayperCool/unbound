using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unbound.Runtime;

namespace UnityEngine.Networking
{
    /// <summary>
    /// HTTP requests on .NET's HttpClient, with Unity's semantics: a request is configured, sent once
    /// (SendWebRequest), and completes on the main thread during the player loop, when its download
    /// handler has the response, its async operation's completed event runs and waiting coroutines
    /// resume. Redirects are followed up to redirectLimit; cookies persist for the session.
    /// </summary>
    public partial class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbGET = "GET";
        public const string kHttpVerbHEAD = "HEAD";
        public const string kHttpVerbPOST = "POST";
        public const string kHttpVerbPUT = "PUT";
        public const string kHttpVerbCREATE = "CREATE";
        public const string kHttpVerbDELETE = "DELETE";

        public enum Result
        {
            InProgress = 0,
            Success = 1,
            ConnectionError = 2,
            ProtocolError = 3,
            DataProcessingError = 4,
        }

        static CookieContainer s_Cookies = new CookieContainer();
        static readonly Dictionary<int, HttpClient> s_Clients = new Dictionary<int, HttpClient>();
        static readonly object s_ClientLock = new object();

        string m_Url = "";
        string m_Method = kHttpVerbGET;
        readonly Dictionary<string, string> m_RequestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> m_ResponseHeaders;
        internal UploadHandler m_UploadHandler;
        internal DownloadHandler m_DownloadHandler;
        internal CertificateHandler m_CertificateHandler;
        int m_Timeout;
        int m_RedirectLimit = 32;
        bool m_UseHttpContinue = true;
        bool m_Sent;
        bool m_Disposed;
        volatile bool m_Done;
        long m_ResponseCode;
        Result m_Result = Result.InProgress;
        string m_Error;
        long m_DownloadedBytes;
        long m_ContentLength = -1;
        long m_UploadedBytes;
        CancellationTokenSource m_Cancel;
        UnityWebRequestAsyncOperation m_Operation;

        public UnityWebRequest() { }

        public UnityWebRequest(string url)
        {
            this.url = url;
        }

        public UnityWebRequest(Uri uri)
        {
            this.uri = uri;
        }

        public UnityWebRequest(string url, string method)
        {
            this.url = url;
            this.method = method;
        }

        public UnityWebRequest(Uri uri, string method)
        {
            this.uri = uri;
            this.method = method;
        }

        public UnityWebRequest(string url, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
        {
            this.url = url;
            this.method = method;
            m_DownloadHandler = downloadHandler;
            m_UploadHandler = uploadHandler;
        }

        public UnityWebRequest(Uri uri, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
        {
            this.uri = uri;
            this.method = method;
            m_DownloadHandler = downloadHandler;
            m_UploadHandler = uploadHandler;
        }

        // ------------------------------------------------------------------ configuration

        void CheckModifiable(string what)
        {
            if (m_Sent) throw new InvalidOperationException($"UnityWebRequest has already been sent and its {what} cannot be altered");
        }

        public string url
        {
            get => m_Url;
            set
            {
                CheckModifiable("URL");
                m_Url = NormalizeUrl(value);
            }
        }

        public Uri uri
        {
            get => new Uri(m_Url);
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                CheckModifiable("URL");
                m_Url = value.IsAbsoluteUri ? value.AbsoluteUri : value.OriginalString;
            }
        }

        /// <summary>Unity assumes http:// for URLs without a scheme, and file paths are file:// URLs.</summary>
        static string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (url.Contains("://", StringComparison.Ordinal)) return url;
            if (url.StartsWith("/", StringComparison.Ordinal)) return "file://" + url;
            return "http://" + url;
        }

        public string method
        {
            get => m_Method;
            set
            {
                CheckModifiable("method");
                if (string.IsNullOrEmpty(value)) throw new ArgumentException("Cannot set a UnityWebRequest's method to an empty or null string");
                m_Method = value.ToUpperInvariant();
            }
        }

        public UploadHandler uploadHandler
        {
            get => m_UploadHandler;
            set
            {
                CheckModifiable("upload handler");
                m_UploadHandler = value;
            }
        }

        public DownloadHandler downloadHandler
        {
            get => m_DownloadHandler;
            set
            {
                CheckModifiable("download handler");
                m_DownloadHandler = value;
            }
        }

        public CertificateHandler certificateHandler
        {
            get => m_CertificateHandler;
            set
            {
                CheckModifiable("certificate handler");
                m_CertificateHandler = value;
            }
        }

        /// <summary>Seconds; 0 waits for ever.</summary>
        public int timeout
        {
            get => m_Timeout;
            set
            {
                CheckModifiable("timeout");
                m_Timeout = Math.Max(0, value);
            }
        }

        public int redirectLimit
        {
            get => m_RedirectLimit;
            set
            {
                CheckModifiable("redirect limit");
                m_RedirectLimit = Math.Max(-1, value);
            }
        }

        public bool useHttpContinue
        {
            get => m_UseHttpContinue;
            set
            {
                CheckModifiable("useHttpContinue");
                m_UseHttpContinue = value;
            }
        }

        [Obsolete("HTTP/2 and many HTTP/1.1 servers don't support this; we recommend leaving it set to false (default).", false)]
        public bool chunkedTransfer { get; set; }

        public bool disposeCertificateHandlerOnDispose { get; set; } = true;
        public bool disposeDownloadHandlerOnDispose { get; set; } = true;
        public bool disposeUploadHandlerOnDispose { get; set; } = true;

        public bool isModifiable => !m_Sent;

        public string GetRequestHeader(string name) => m_RequestHeaders.TryGetValue(name, out var value) ? value : null;

        public void SetRequestHeader(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cannot set a Request Header with a null or empty name");
            if (value == null) throw new ArgumentException("Cannot set a Request header with a null");
            CheckModifiable("headers");
            m_RequestHeaders[name] = value;
        }

        // ------------------------------------------------------------------ state

        public bool isDone => m_Done;

        public Result result => m_Result;

        public string error => m_Error;

        public long responseCode => m_ResponseCode;

        [Obsolete("UnityWebRequest.isNetworkError is deprecated. Use (UnityWebRequest.result == UnityWebRequest.Result.ConnectionError) instead.", false)]
        public bool isNetworkError => m_Result == Result.ConnectionError;

        [Obsolete("UnityWebRequest.isHttpError is deprecated. Use (UnityWebRequest.result == UnityWebRequest.Result.ProtocolError) instead.", false)]
        public bool isHttpError => m_Result == Result.ProtocolError;

        [Obsolete("UnityWebRequest.isError has been renamed to isNetworkError for clarity. (UnityUpgradable) -> isNetworkError", false)]
        public bool isError => m_Result == Result.ConnectionError;

        public float downloadProgress
        {
            get
            {
                if (!m_Sent) return -1f;
                if (m_Done) return 1f;
                long length = Interlocked.Read(ref m_ContentLength);
                return length > 0 ? Mathf.Clamp01((float)Interlocked.Read(ref m_DownloadedBytes) / length) : 0f;
            }
        }

        public float uploadProgress
        {
            get
            {
                if (!m_Sent) return -1f;
                if (m_Done) return 1f;
                var data = m_UploadHandler?.m_Data;
                return data == null || data.Length == 0 ? 0f : Mathf.Clamp01((float)Interlocked.Read(ref m_UploadedBytes) / data.Length);
            }
        }

        public ulong downloadedBytes => (ulong)Math.Max(0, Interlocked.Read(ref m_DownloadedBytes));

        public ulong uploadedBytes => (ulong)Math.Max(0, Interlocked.Read(ref m_UploadedBytes));

        public string GetResponseHeader(string name)
        {
            if (m_ResponseHeaders == null || name == null) return null;
            foreach (var pair in m_ResponseHeaders)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        public Dictionary<string, string> GetResponseHeaders() => m_ResponseHeaders == null ? null : new Dictionary<string, string>(m_ResponseHeaders);

        // ------------------------------------------------------------------ sending

        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(UnityWebRequest));
            if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot begin sending the request again");
            m_Sent = true;
            m_Operation = new UnityWebRequestAsyncOperation { m_WebRequest = this };
            m_Cancel = new CancellationTokenSource();
            if (m_Timeout > 0) m_Cancel.CancelAfter(TimeSpan.FromSeconds(m_Timeout));
            var token = m_Cancel.Token;
            Task.Run(() => RunAsync(token));
            return m_Operation;
        }

        [Obsolete("Use SendWebRequest.  It returns a UnityWebRequestAsyncOperation which contains a reference to the WebRequest object.", false)]
        public AsyncOperation Send() => SendWebRequest();

        public void Abort()
        {
            if (!m_Sent || m_Done) return;
            m_Aborted = true;
            try { m_Cancel?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        volatile bool m_Aborted;

        static HttpClient ClientFor(int redirectLimit)
        {
            lock (s_ClientLock)
            {
                if (s_Clients.TryGetValue(redirectLimit, out var client)) return client;
                client = new HttpClient(NewHandler(redirectLimit, null), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
                s_Clients[redirectLimit] = client;
                return client;
            }
        }

        static SocketsHttpHandler NewHandler(int redirectLimit, CertificateHandler certificates)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = redirectLimit != 0,
                MaxAutomaticRedirections = Math.Max(1, redirectLimit < 0 ? 50 : redirectLimit),
                CookieContainer = s_Cookies,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
            };
            if (certificates != null)
            {
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                        certificate != null && certificates.Validate(certificate.GetRawCertData()),
                };
            }
            return handler;
        }

        async Task RunAsync(CancellationToken token)
        {
            HttpClient dedicated = null;
            byte[] body = null;
            string contentType = null;
            try
            {
                var target = new Uri(m_Url, UriKind.Absolute);
                if (target.IsFile)
                {
                    body = await System.IO.File.ReadAllBytesAsync(target.LocalPath, token).ConfigureAwait(false);
                    m_ResponseCode = 0;
                    m_ResponseHeaders = new Dictionary<string, string>();
                    Interlocked.Exchange(ref m_ContentLength, body.Length);
                    Interlocked.Exchange(ref m_DownloadedBytes, body.Length);
                    Finish(Result.Success, null, body, null);
                    return;
                }
                var client = m_CertificateHandler != null
                    ? dedicated = new HttpClient(NewHandler(m_RedirectLimit, m_CertificateHandler), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan }
                    : ClientFor(m_RedirectLimit);
                using var request = new HttpRequestMessage(new HttpMethod(m_Method), target);
                var upload = m_UploadHandler?.Payload();
                if (upload != null)
                {
                    var content = new ByteArrayContent(upload);
                    var type = GetRequestHeader("Content-Type") ?? m_UploadHandler.contentType ?? "application/octet-stream";
                    if (MediaTypeHeaderValue.TryParse(type, out var parsed)) content.Headers.ContentType = parsed;
                    else content.Headers.TryAddWithoutValidation("Content-Type", type);
                    request.Content = content;
                }
                foreach (var pair in m_RequestHeaders)
                {
                    if (string.Equals(pair.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
                        request.Content?.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }
                request.Headers.ExpectContinue = m_UseHttpContinue && upload != null && upload.Length > 0 ? true : null;
                if (!request.Headers.UserAgent.Any()) request.Headers.TryAddWithoutValidation("User-Agent", "UnityPlayer/" + ProjectInfo.UnityVersion + " (UnityWebRequest/1.0, libcurl/8.5.0-DEV)");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                Interlocked.Exchange(ref m_UploadedBytes, upload?.Length ?? 0);
                m_ResponseCode = (long)response.StatusCode;
                var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var h in response.Headers) headers[h.Key] = string.Join(", ", h.Value);
                foreach (var h in response.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);
                m_ResponseHeaders = headers;
                contentType = response.Content.Headers.ContentType?.ToString();
                long length = response.Content.Headers.ContentLength ?? -1;
                Interlocked.Exchange(ref m_ContentLength, length);
                if (m_Method != kHttpVerbHEAD)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    using var buffer = new System.IO.MemoryStream(length > 0 ? (int)Math.Min(length, int.MaxValue) : 4096);
                    var chunk = new byte[64 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        Interlocked.Add(ref m_DownloadedBytes, read);
                    }
                    body = buffer.ToArray();
                }
                int code = (int)response.StatusCode;
                if (code >= 400)
                    Finish(Result.ProtocolError, $"HTTP/{response.Version.Major}.{response.Version.Minor} {code} {response.ReasonPhrase}", body, contentType);
                else
                    Finish(Result.Success, null, body, contentType);
            }
            catch (OperationCanceledException)
            {
                Finish(Result.ConnectionError, m_Aborted ? "Request aborted" : "Request timeout", null, null);
            }
            catch (HttpRequestException e)
            {
                Finish(Result.ConnectionError, ConnectionMessage(e), null, null);
            }
            catch (UriFormatException)
            {
                Finish(Result.ConnectionError, "Malformed URL", null, null);
            }
            catch (System.IO.IOException e) when (e.InnerException is SocketException)
            {
                Finish(Result.ConnectionError, "Cannot connect to destination host", null, null);
            }
            catch (System.IO.FileNotFoundException)
            {
                Finish(Result.ConnectionError, "Cannot connect to destination host", null, null);
            }
            catch (Exception e)
            {
                Finish(Result.ConnectionError, "Unknown Error: " + e.Message, null, null);
            }
            finally
            {
                dedicated?.Dispose();
            }
        }

        static string ConnectionMessage(HttpRequestException e)
        {
            switch (e.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError: return "Cannot resolve destination host";
                case HttpRequestError.ConnectionError: return "Cannot connect to destination host";
                case HttpRequestError.SecureConnectionError: return "Unable to complete SSL connection";
                case HttpRequestError.ProxyTunnelError: return "Cannot resolve proxy";
                case HttpRequestError.ResponseEnded: return "Failed to receive data";
                case HttpRequestError.ConfigurationLimitExceeded: return "Redirect limit exceeded";
                default: return e.InnerException is SocketException ? "Cannot connect to destination host" : "Unknown Error";
            }
        }

        /// <summary>The response is in: the download handler gets it on the main thread, then the
        /// request is done and its operation completes.</summary>
        void Finish(Result result, string error, byte[] body, string contentType)
        {
            PlayerLoopDriver.RunOnMainThread(() =>
            {
                if (m_DownloadHandler != null && !m_Disposed)
                {
                    try { m_DownloadHandler.Deliver(body, contentType, result == Result.Success || result == Result.ProtocolError); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        result = Result.DataProcessingError;
                        error = "Data Processing Error, see Download Handler error";
                    }
                }
                m_Result = result;
                m_Error = error;
                m_Done = true;
                m_Cancel?.Dispose();
                m_Cancel = null;
                m_Operation?.Complete();
            });
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Abort();
            if (disposeDownloadHandlerOnDispose) m_DownloadHandler?.Dispose();
            if (disposeUploadHandlerOnDispose) m_UploadHandler?.Dispose();
            if (disposeCertificateHandlerOnDispose) m_CertificateHandler?.Dispose();
            GC.SuppressFinalize(this);
        }

        public static void ClearCookieCache()
        {
            lock (s_ClientLock)
            {
                s_Cookies = new CookieContainer();
                foreach (var client in s_Clients.Values) client.Dispose();
                s_Clients.Clear();
            }
        }

        public static void ClearCookieCache(Uri uri)
        {
            if (uri == null)
            {
                ClearCookieCache();
                return;
            }
            foreach (Cookie cookie in s_Cookies.GetCookies(uri)) cookie.Expired = true;
        }

        // ------------------------------------------------------------------ factories

        public static UnityWebRequest Get(string uri) => new UnityWebRequest(uri, kHttpVerbGET, new DownloadHandlerBuffer(), null);

        public static UnityWebRequest Get(Uri uri) => new UnityWebRequest(uri, kHttpVerbGET, new DownloadHandlerBuffer(), null);

        public static UnityWebRequest Delete(string uri) => new UnityWebRequest(uri, kHttpVerbDELETE);

        public static UnityWebRequest Delete(Uri uri) => new UnityWebRequest(uri, kHttpVerbDELETE);

        public static UnityWebRequest Head(string uri) => new UnityWebRequest(uri, kHttpVerbHEAD);

        public static UnityWebRequest Head(Uri uri) => new UnityWebRequest(uri, kHttpVerbHEAD);

        public static UnityWebRequest Put(string uri, byte[] bodyData) => new UnityWebRequest(uri, kHttpVerbPUT, new DownloadHandlerBuffer(), new UploadHandlerRaw(bodyData));

        public static UnityWebRequest Put(Uri uri, byte[] bodyData) => new UnityWebRequest(uri, kHttpVerbPUT, new DownloadHandlerBuffer(), new UploadHandlerRaw(bodyData));

        public static UnityWebRequest Put(string uri, string bodyData) => Put(uri, Encoding.UTF8.GetBytes(bodyData ?? ""));

        public static UnityWebRequest Put(Uri uri, string bodyData) => Put(uri, Encoding.UTF8.GetBytes(bodyData ?? ""));

        static UnityWebRequest PostWith(UnityWebRequest request, byte[] data, string contentType)
        {
            request.uploadHandler = new UploadHandlerRaw(data) { contentType = contentType };
            request.downloadHandler = new DownloadHandlerBuffer();
            return request;
        }

        /// <summary>Unity's original string Post: the string is sent url-escaped as a form.</summary>
        [Obsolete("UnityWebRequest.Post with only a string data is obsolete. Use UnityWebRequest.Post with content type argument or UnityWebRequest.PostWwwForm instead (UnityUpgradable) -> [UnityEngine] UnityEngine.Networking.UnityWebRequest.PostWwwForm(*)", false)]
        public static UnityWebRequest Post(string uri, string postData) => PostWwwForm(uri, postData);

        [Obsolete("UnityWebRequest.Post with only a string data is obsolete. Use UnityWebRequest.Post with content type argument or UnityWebRequest.PostWwwForm instead (UnityUpgradable) -> [UnityEngine] UnityEngine.Networking.UnityWebRequest.PostWwwForm(*)", false)]
        public static UnityWebRequest Post(Uri uri, string postData) => PostWwwForm(uri, postData);

        public static UnityWebRequest PostWwwForm(string uri, string form) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), EscapedForm(form), "application/x-www-form-urlencoded");

        public static UnityWebRequest PostWwwForm(Uri uri, string form) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), EscapedForm(form), "application/x-www-form-urlencoded");

        static byte[] EscapedForm(string form) => string.IsNullOrEmpty(form) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(WebEncoding.DataEncode(form));

        public static UnityWebRequest Post(string uri, string postData, string contentType) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), Encoding.UTF8.GetBytes(postData ?? ""), contentType);

        public static UnityWebRequest Post(Uri uri, string postData, string contentType) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), Encoding.UTF8.GetBytes(postData ?? ""), contentType);

        public static UnityWebRequest Post(string uri, WWWForm formData) => PostForm(new UnityWebRequest(uri, kHttpVerbPOST), formData);

        public static UnityWebRequest Post(Uri uri, WWWForm formData) => PostForm(new UnityWebRequest(uri, kHttpVerbPOST), formData);

        static UnityWebRequest PostForm(UnityWebRequest request, WWWForm form)
        {
            if (form == null) return PostWith(request, Array.Empty<byte>(), "application/x-www-form-urlencoded");
            var data = form.data;
            PostWith(request, data.Length > 0 ? data : null, null);
            foreach (var header in form.headers) request.SetRequestHeader(header.Key, header.Value);
            return request;
        }

        public static UnityWebRequest Post(string uri, List<IMultipartFormSection> multipartFormSections) => Post(uri, multipartFormSections, GenerateBoundary());

        public static UnityWebRequest Post(Uri uri, List<IMultipartFormSection> multipartFormSections) => Post(uri, multipartFormSections, GenerateBoundary());

        public static UnityWebRequest Post(string uri, List<IMultipartFormSection> multipartFormSections, byte[] boundary) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), SerializeFormSections(multipartFormSections, boundary),
                "multipart/form-data; boundary=" + Encoding.UTF8.GetString(boundary));

        public static UnityWebRequest Post(Uri uri, List<IMultipartFormSection> multipartFormSections, byte[] boundary) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), SerializeFormSections(multipartFormSections, boundary),
                "multipart/form-data; boundary=" + Encoding.UTF8.GetString(boundary));

        public static UnityWebRequest Post(string uri, Dictionary<string, string> formFields) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), SerializeSimpleForm(formFields), "application/x-www-form-urlencoded");

        public static UnityWebRequest Post(Uri uri, Dictionary<string, string> formFields) =>
            PostWith(new UnityWebRequest(uri, kHttpVerbPOST), SerializeSimpleForm(formFields), "application/x-www-form-urlencoded");

        // ------------------------------------------------------------------ encoding

        public static string EscapeURL(string s) => EscapeURL(s, Encoding.UTF8);

        public static string EscapeURL(string s, Encoding e)
        {
            if (s == null) return null;
            if (s.Length == 0) return "";
            if (e == null) return null;
            return WebEncoding.URLEncode(s, e);
        }

        public static string UnEscapeURL(string s) => UnEscapeURL(s, Encoding.UTF8);

        public static string UnEscapeURL(string s, Encoding e)
        {
            if (s == null) return null;
            if (s.IndexOf('%') < 0 && s.IndexOf('+') < 0) return s;
            return WebEncoding.URLDecode(s, e ?? Encoding.UTF8);
        }

        /// <summary>Forty random characters from Unity's boundary alphabet.</summary>
        public static byte[] GenerateBoundary()
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var bytes = new byte[40];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)alphabet[System.Random.Shared.Next(alphabet.Length)];
            return bytes;
        }

        public static byte[] SerializeSimpleForm(Dictionary<string, string> formFields)
        {
            if (formFields == null) throw new ArgumentNullException(nameof(formFields));
            var sb = new StringBuilder();
            foreach (var pair in formFields)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(WebEncoding.DataEncode(pair.Key)).Append('=').Append(WebEncoding.DataEncode(pair.Value ?? ""));
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static byte[] SerializeFormSections(List<IMultipartFormSection> multipartFormSections, byte[] boundary)
        {
            if (multipartFormSections == null || multipartFormSections.Count == 0) return null;
            if (boundary == null || boundary.Length == 0) throw new ArgumentException("Cannot serialize form sections with an empty boundary");
            var crlf = Encoding.UTF8.GetBytes("\r\n");
            var dashes = Encoding.UTF8.GetBytes("--");
            using var stream = new System.IO.MemoryStream();
            foreach (var section in multipartFormSections)
            {
                stream.Write(crlf);
                stream.Write(dashes);
                stream.Write(boundary);
                stream.Write(crlf);
                var disposition = new StringBuilder("Content-Disposition: form-data");
                if (!string.IsNullOrEmpty(section.sectionName)) disposition.Append("; name=\"").Append(section.sectionName).Append('"');
                if (!string.IsNullOrEmpty(section.fileName)) disposition.Append("; filename=\"").Append(section.fileName).Append('"');
                disposition.Append("\r\n");
                stream.Write(Encoding.UTF8.GetBytes(disposition.ToString()));
                if (!string.IsNullOrEmpty(section.contentType)) stream.Write(Encoding.UTF8.GetBytes("Content-Type: " + section.contentType + "\r\n"));
                stream.Write(crlf);
                var data = section.sectionData;
                if (data != null) stream.Write(data);
            }
            stream.Write(crlf);
            stream.Write(dashes);
            stream.Write(boundary);
            stream.Write(dashes);
            stream.Write(crlf);
            return stream.ToArray();
        }
    }

    /// <summary>Waits for a request: yield it in a coroutine or await it; webRequest is the request.</summary>
    public partial class UnityWebRequestAsyncOperation : AsyncOperation
    {
        internal UnityWebRequest m_WebRequest;

        public UnityWebRequestAsyncOperation() { }

        public UnityWebRequest webRequest
        {
            get => m_WebRequest;
            internal set => m_WebRequest = value;
        }
    }

    /// <summary>Unity's URL and form encodings: escapes as lowercase %xx, spaces as '+'.</summary>
    internal static class WebEncoding
    {
        const string Unreserved = "-_.!*'()";

        internal static string URLEncode(string s, Encoding e)
        {
            var bytes = e.GetBytes(s);
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                char c = (char)b;
                if (b < 128 && (char.IsAsciiLetterOrDigit(c) || Unreserved.IndexOf(c) >= 0)) sb.Append(c);
                else if (c == ' ') sb.Append('+');
                else sb.Append('%').Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>Form fields: as URLEncode.</summary>
        internal static string DataEncode(string s) => URLEncode(s ?? "", Encoding.UTF8);

        internal static string URLDecode(string s, Encoding e)
        {
            var bytes = new List<byte>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '+') bytes.Add((byte)' ');
                else if (c == '%' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
                {
                    bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else bytes.AddRange(e.GetBytes(c.ToString()));
            }
            return e.GetString(bytes.ToArray());
        }
    }
}
