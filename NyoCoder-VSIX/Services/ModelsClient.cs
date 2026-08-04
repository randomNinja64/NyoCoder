using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Thrown when listing models from an OpenAI-compatible <c>/v1/models</c> endpoint fails.
    /// </summary>
    public class ModelsException : Exception
    {
        public ModelsException(string message) : base(message) { }
        public ModelsException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Minimal client for OpenAI-compatible <c>/v1/models</c>. Mirrors the TLS enablement and
    /// curl HTTPS fallback used by <see cref="EmbeddingsClient"/>.
    /// </summary>
    public static class ModelsClient
    {
        static ModelsClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls
                    | (SecurityProtocolType)768 | (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch { }
        }

        /// <summary>
        /// Fetches model ids from <c>{baseUrl}/v1/models</c>. Throws <see cref="ModelsException"/>
        /// if the server is unreachable or the response is invalid.
        /// </summary>
        public static IList<string> ListModels(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ModelsException("LLM server URL is empty.");

            string url = baseUrl.Trim().TrimEnd('/') + "/v1/models";
            string key = apiKey ?? string.Empty;
            string body = GetModelsJson(url, key);
            return ParseModelIds(body);
        }

        private static string GetModelsJson(string url, string apiKey)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Accept = "application/json";
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.Add("Authorization", "Bearer " + apiKey);
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new ModelsException("Models request failed with HTTP " + (int)response.StatusCode + ".");
                    return reader.ReadToEnd();
                }
            }
            catch (ModelsException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string httpBody = TryReadWebExceptionBody(ex);

                string curlPath = CurlClient.GetCurlPath();
                if (url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(curlPath) &&
                    ShouldFallbackToCurl(ex))
                {
                    int exitCode;
                    string body = CurlClient.GetJson(url, apiKey, out exitCode);
                    if (exitCode == 0 && !string.IsNullOrEmpty(body))
                        return body;
                    throw new ModelsException(
                        "Unable to reach /v1/models (curl): " + (body ?? httpBody ?? ex.Message),
                        ex);
                }

                if (!string.IsNullOrEmpty(httpBody))
                    throw new ModelsException("Unable to reach /v1/models: " + ex.Message + " — " + httpBody, ex);
                throw new ModelsException("Unable to reach /v1/models: " + ex.Message, ex);
            }
        }

        private static IList<string> ParseModelIds(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new ModelsException("Models response was empty.");

            JObject root;
            try
            {
                root = JObject.Parse(body);
            }
            catch (Exception ex)
            {
                throw new ModelsException("Models response was not valid JSON.", ex);
            }

            JArray data = root["data"] as JArray;
            if (data == null)
                throw new ModelsException("Models response missing 'data' array.");

            List<string> ids = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken item in data)
            {
                JObject obj = item as JObject;
                if (obj == null)
                    continue;
                JToken idToken = obj["id"];
                if (idToken == null || idToken.Type != JTokenType.String)
                    continue;
                string id = ((string)idToken).Trim();
                if (id.Length == 0 || seen.Contains(id))
                    continue;
                seen.Add(id);
                ids.Add(id);
            }

            if (ids.Count == 0)
                throw new ModelsException("Models response contained no model ids.");

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private static bool ShouldFallbackToCurl(Exception ex)
        {
            WebException webEx = ex as WebException;
            if (webEx != null)
                return webEx.Status == WebExceptionStatus.SecureChannelFailure
                    || webEx.Status == WebExceptionStatus.TrustFailure
                    || webEx.Status == WebExceptionStatus.ConnectFailure
                    || webEx.Status == WebExceptionStatus.ConnectionClosed
                    || webEx.Status == WebExceptionStatus.SendFailure
                    || webEx.Status == WebExceptionStatus.ReceiveFailure
                    || webEx.Status == WebExceptionStatus.Timeout
                    || webEx.Status == WebExceptionStatus.ServerProtocolViolation
                    || (webEx.InnerException != null &&
                        webEx.InnerException.GetType().Name.Contains("Authentication"));

            return ex.GetType().Name.Contains("Authentication")
                || ex.GetType().Name.Contains("Security")
                || ex.GetType().Name.Contains("IOException")
                || (ex.Message != null && ex.Message.Contains("connection"));
        }

        private static string TryReadWebExceptionBody(Exception ex)
        {
            WebException webEx = ex as WebException;
            if (webEx == null || webEx.Response == null)
                return null;

            try
            {
                using (Stream stream = webEx.Response.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(body))
                            return null;
                        if (body.Length > 500)
                            return body.Substring(0, 500) + "...";
                        return body;
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
