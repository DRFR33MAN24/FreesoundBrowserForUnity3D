using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace FreesoundBrowser
{
    /// <summary>
    /// OAuth2 "authorization code grant" flow (https://freesound.org/docs/api/authentication.html).
    /// The user authorizes in the browser, pastes the code back, and tokens are refreshed automatically afterwards.
    /// </summary>
    public static class FreesoundAuth
    {
        const string AuthorizeEndpoint = "https://freesound.org/apiv2/oauth2/authorize/";
        const string TokenEndpoint = "https://freesound.org/apiv2/oauth2/access_token/";
        static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

        static Task<string> pendingRefresh;

        /// <summary>On Freesound the client secret and the API key are the same value.</summary>
        static string EffectiveClientSecret =>
            string.IsNullOrEmpty(FreesoundUserPrefs.ClientSecret) ? FreesoundUserPrefs.ApiKey : FreesoundUserPrefs.ClientSecret;

        public static bool HasClientCredentials =>
            !string.IsNullOrEmpty(FreesoundUserPrefs.ClientId) && !string.IsNullOrEmpty(EffectiveClientSecret);

        public static bool HasValidAccessToken =>
            !string.IsNullOrEmpty(FreesoundUserPrefs.AccessToken) &&
            FreesoundUserPrefs.AccessTokenExpiryUtc - ExpiryMargin > DateTime.UtcNow;

        public static bool CanRefresh => HasClientCredentials && !string.IsNullOrEmpty(FreesoundUserPrefs.RefreshToken);

        public static bool IsLoggedIn => HasValidAccessToken || CanRefresh;

        public static void OpenAuthorizationPage()
        {
            if (string.IsNullOrEmpty(FreesoundUserPrefs.ClientId))
                throw new FreesoundAuthException("Set the Client ID first.");

            Application.OpenURL(AuthorizeEndpoint +
                                "?client_id=" + Uri.EscapeDataString(FreesoundUserPrefs.ClientId) +
                                "&response_type=code");
        }

        public static async Task ExchangeCodeAsync(string codeOrRedirectUrl)
        {
            string code = ExtractCode(codeOrRedirectUrl);
            if (string.IsNullOrEmpty(code)) throw new FreesoundAuthException("The authorization code is empty.");

            await RequestTokenAsync(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code }
            });

            try
            {
                var me = await FreesoundClient.GetMeAsync();
                FreesoundUserPrefs.Username = me?.Username;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Freesound] Logged in, but could not fetch the user name: " + e.Message);
            }
        }

        public static void Logout()
        {
            FreesoundUserPrefs.ClearTokens();
        }

        public static async Task<string> GetValidAccessTokenAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && HasValidAccessToken) return FreesoundUserPrefs.AccessToken;

            if (!CanRefresh)
                throw new FreesoundAuthException(
                    "Not logged in to Freesound. Log in under Preferences > Freesound Browser (required for original-quality downloads).");

            if (pendingRefresh == null || pendingRefresh.IsCompleted)
                pendingRefresh = RefreshAsync();
            return await pendingRefresh;
        }

        static async Task<string> RefreshAsync()
        {
            try
            {
                return await RequestTokenAsync(new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", FreesoundUserPrefs.RefreshToken }
                });
            }
            catch (FreesoundApiException e) when (e.StatusCode == 400 || e.StatusCode == 401)
            {
                FreesoundUserPrefs.ClearTokens();
                throw new FreesoundAuthException("Your Freesound session expired or was revoked. Log in again in Preferences > Freesound Browser. (" + e.Message + ")");
            }
        }

        static async Task<string> RequestTokenAsync(Dictionary<string, string> fields)
        {
            if (!HasClientCredentials)
                throw new FreesoundAuthException("Client ID and Client Secret / API Key are required for OAuth2 login.");

            var form = new WWWForm();
            form.AddField("client_id", FreesoundUserPrefs.ClientId);
            form.AddField("client_secret", EffectiveClientSecret);
            foreach (var pair in fields) form.AddField(pair.Key, pair.Value);

            using (var request = UnityWebRequest.Post(TokenEndpoint, form))
            {
                request.timeout = 30;
                await request.SendAsync();

                if (request.result != UnityWebRequest.Result.Success)
                    throw FreesoundApiException.FromRequest(request);

                var token = JsonConvert.DeserializeObject<TokenResponse>(request.downloadHandler.text);
                if (token == null || string.IsNullOrEmpty(token.AccessToken))
                    throw new FreesoundAuthException("Freesound did not return an access token.");

                FreesoundUserPrefs.AccessToken = token.AccessToken;
                if (!string.IsNullOrEmpty(token.RefreshToken)) FreesoundUserPrefs.RefreshToken = token.RefreshToken;
                FreesoundUserPrefs.AccessTokenExpiryUtc = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 86399);
                return token.AccessToken;
            }
        }

        /// <summary>Accepts either the raw code or a pasted redirect URL containing "code=...".</summary>
        static string ExtractCode(string input)
        {
            string value = (input ?? string.Empty).Trim();
            int index = value.IndexOf("code=", StringComparison.Ordinal);
            if (index < 0) return value;

            string code = value.Substring(index + 5);
            int end = code.IndexOfAny(new[] { '&', '#' });
            if (end >= 0) code = code.Substring(0, end);
            return Uri.UnescapeDataString(code);
        }
    }
}
