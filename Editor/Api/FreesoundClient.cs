using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace FreesoundBrowser
{
    public class FreesoundApiException : Exception
    {
        public long StatusCode { get; }

        public FreesoundApiException(long statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        internal static FreesoundApiException FromRequest(UnityWebRequest request)
        {
            long code = request.responseCode;
            string detail = ExtractDetail(request);
            string message;
            switch (code)
            {
                case 0: message = "Network error: " + request.error; break;
                case 400: message = "Bad request (400)"; break;
                case 401: message = "Unauthorized (401). Check your API key / login in Preferences > Freesound Browser"; break;
                case 403: message = "Forbidden (403)"; break;
                case 404: message = "Not found (404)"; break;
                case 429: message = "Freesound rate limit exceeded (429). Wait a minute and try again"; break;
                default: message = $"HTTP {code}: {request.error}"; break;
            }
            if (!string.IsNullOrEmpty(detail)) message += " - " + detail;
            return new FreesoundApiException(code, message);
        }

        static string ExtractDetail(UnityWebRequest request)
        {
            try
            {
                string text = request.downloadHandler?.text;
                if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;
                var json = JObject.Parse(text);
                return (string)json["detail"] ?? (string)json["error_description"] ?? (string)json["error"];
            }
            catch
            {
                return null;
            }
        }
    }

    public class FreesoundAuthException : Exception
    {
        public FreesoundAuthException(string message) : base(message) { }
    }

    internal enum AuthMode
    {
        None,
        Read,
        OAuth
    }

    public static class FreesoundClient
    {
        public const string ApiRoot = "https://freesound.org/apiv2";

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static Task<SearchResponse> SearchAsync(SearchQuery query)
        {
            return GetJsonAsync<SearchResponse>(query.BuildUrl(), AuthMode.Read);
        }

        public static Task<MeResponse> GetMeAsync()
        {
            return GetJsonAsync<MeResponse>(ApiRoot + "/me/", AuthMode.OAuth);
        }

        /// <summary>Preview files are served from the Freesound CDN and need no authentication.</summary>
        public static Task<byte[]> DownloadPreviewAsync(string previewUrl)
        {
            return GetBytesAsync(previewUrl, AuthMode.None, 120);
        }

        /// <summary>Original-quality file. Requires an OAuth2 login.</summary>
        public static Task<byte[]> DownloadOriginalAsync(int soundId)
        {
            return GetBytesAsync($"{ApiRoot}/sounds/{soundId}/download/", AuthMode.OAuth, 600);
        }

        static async Task<T> GetJsonAsync<T>(string url, AuthMode mode)
        {
            byte[] bytes = await GetBytesAsync(url, mode, 30);
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), JsonSettings);
        }

        static async Task<byte[]> GetBytesAsync(string url, AuthMode mode, int timeoutSeconds)
        {
            for (int attempt = 0; ; attempt++)
            {
                string authorization = await GetAuthorizationHeaderAsync(mode, forceRefresh: attempt > 0);

                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = timeoutSeconds;
                    if (authorization != null) request.SetRequestHeader("Authorization", authorization);

                    await request.SendAsync();

                    if (request.result == UnityWebRequest.Result.Success)
                        return request.downloadHandler.data;

                    bool usedBearer = authorization != null && authorization.StartsWith("Bearer ", StringComparison.Ordinal);
                    if (request.responseCode == 401 && usedBearer && attempt == 0 && FreesoundAuth.CanRefresh)
                        continue;

                    throw FreesoundApiException.FromRequest(request);
                }
            }
        }

        static async Task<string> GetAuthorizationHeaderAsync(AuthMode mode, bool forceRefresh)
        {
            switch (mode)
            {
                case AuthMode.None:
                    return null;

                case AuthMode.Read:
                    if (FreesoundUserPrefs.HasApiKey && !forceRefresh) return "Token " + FreesoundUserPrefs.ApiKey;
                    if (FreesoundAuth.IsLoggedIn) return "Bearer " + await FreesoundAuth.GetValidAccessTokenAsync(forceRefresh);
                    throw new FreesoundAuthException("No Freesound API key configured. Add it in Preferences > Freesound Browser.");

                default:
                    return "Bearer " + await FreesoundAuth.GetValidAccessTokenAsync(forceRefresh);
            }
        }
    }
}
