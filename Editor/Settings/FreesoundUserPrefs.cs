using System;
using System.Globalization;
using UnityEditor;

namespace FreesoundBrowser
{
    /// <summary>
    /// Per-machine credentials stored in EditorPrefs, so they never end up in version control.
    /// </summary>
    public static class FreesoundUserPrefs
    {
        const string Prefix = "FreesoundBrowser.";

        public static string ApiKey
        {
            get => Get("ApiKey");
            set => Set("ApiKey", value);
        }

        public static string ClientId
        {
            get => Get("ClientId");
            set => Set("ClientId", value);
        }

        public static string ClientSecret
        {
            get => Get("ClientSecret");
            set => Set("ClientSecret", value);
        }

        public static string AccessToken
        {
            get => Get("AccessToken");
            set => Set("AccessToken", value);
        }

        public static string RefreshToken
        {
            get => Get("RefreshToken");
            set => Set("RefreshToken", value);
        }

        public static string Username
        {
            get => Get("Username");
            set => Set("Username", value);
        }

        public static DateTime AccessTokenExpiryUtc
        {
            get
            {
                return long.TryParse(Get("AccessTokenExpiry"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
                    ? new DateTime(ticks, DateTimeKind.Utc)
                    : DateTime.MinValue;
            }
            set => Set("AccessTokenExpiry", value.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        public static bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

        public static void ClearTokens()
        {
            EditorPrefs.DeleteKey(Prefix + "AccessToken");
            EditorPrefs.DeleteKey(Prefix + "RefreshToken");
            EditorPrefs.DeleteKey(Prefix + "AccessTokenExpiry");
            EditorPrefs.DeleteKey(Prefix + "Username");
        }

        static string Get(string key) => EditorPrefs.GetString(Prefix + key, string.Empty);

        static void Set(string key, string value) => EditorPrefs.SetString(Prefix + key, (value ?? string.Empty).Trim());
    }
}
