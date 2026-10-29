using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    internal static class FreesoundSettingsProvider
    {
        public const string ProjectPath = "Project/Freesound Browser";
        public const string PreferencesPath = "Preferences/Freesound Browser";

        const string ApplyUrl = "https://freesound.org/apiv2/apply";

        static readonly HashSet<string> Keywords = new HashSet<string> { "freesound", "audio", "sfx", "sound", "api", "token" };

        static bool showSecrets;
        static string authCode = string.Empty;
        static string status;
        static MessageType statusType = MessageType.Info;
        static bool busy;

        public static void OpenPreferences() => SettingsService.OpenUserPreferences(PreferencesPath);

        public static void OpenProjectSettings() => SettingsService.OpenProjectSettings(ProjectPath);

        [SettingsProvider]
        public static SettingsProvider CreateProjectProvider()
        {
            return new SettingsProvider(ProjectPath, SettingsScope.Project)
            {
                label = "Freesound Browser",
                guiHandler = _ => DrawProjectSettings(),
                keywords = Keywords
            };
        }

        [SettingsProvider]
        public static SettingsProvider CreatePreferencesProvider()
        {
            return new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "Freesound Browser",
                guiHandler = _ => DrawPreferences(),
                keywords = Keywords
            };
        }

        // ---------------------------------------------------------------- Project settings

        static void DrawProjectSettings()
        {
            var s = FreesoundSettings.Instance;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 220;

            EditorGUILayout.HelpBox(
                "Shared with your team (ProjectSettings/FreesoundBrowserSettings.json). API keys and logins are per user: Preferences > Freesound Browser.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Installation", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s.installFolder = EditorGUILayout.TextField(new GUIContent("Install Folder", "Downloaded sounds are saved here."), s.installFolder);
                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Freesound install folder", Application.dataPath, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        string assetPath = FreesoundPaths.ToAssetPath(picked);
                        if (assetPath != null)
                        {
                            s.installFolder = assetPath;
                            GUI.changed = true;
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Freesound Browser", "The install folder must be inside this project's Assets folder.", "OK");
                        }
                    }
                }
            }
            if (!FreesoundPaths.IsValidAssetFolder(s.installFolder))
                EditorGUILayout.HelpBox("The install folder must be \"Assets\" or a folder inside it.", MessageType.Error);

            s.defaultQuality = (DownloadQuality)EditorGUILayout.Popup(new GUIContent("Default Install Quality"), (int)s.defaultQuality, ToContents(FreesoundLabels.Quality));
            s.writeCredits = EditorGUILayout.Toggle(new GUIContent("Write FREESOUND_CREDITS.md", "Appends author/license info for every installed sound (needed for CC BY attribution)."), s.writeCredits);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import Presets (new downloads only)", EditorStyles.boldLabel);
            s.applyImportPresets = EditorGUILayout.Toggle("Apply Import Presets", s.applyImportPresets);
            using (new EditorGUI.DisabledScope(!s.applyImportPresets))
            {
                s.shortClipThreshold = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Short Clip Threshold (s)", "Clips shorter than this use Decompress On Load. Longer clips use Compressed In Memory."), s.shortClipThreshold));
                s.forceMonoShortClips = EditorGUILayout.Toggle("Force Mono On Short Clips", s.forceMonoShortClips);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Browser Defaults", EditorStyles.boldLabel);
            s.showFieldButton = EditorGUILayout.Toggle(new GUIContent("Show Button On AudioClip Fields", "Adds a Freesound search button next to every AudioClip field in the Inspector."), s.showFieldButton);
            s.closeWindowAfterPick = EditorGUILayout.Toggle("Close Browser After Pick", s.closeWindowAfterPick);
            s.autoPlayOnSelect = EditorGUILayout.Toggle("Auto-play On Select", s.autoPlayOnSelect);
            s.defaultMinDuration = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Min Duration (s)", "0 = no minimum"), s.defaultMinDuration));
            s.defaultMaxDuration = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Max Duration (s)", "0 = no maximum"), s.defaultMaxDuration));
            s.defaultLicenseFilter = (LicenseFilter)EditorGUILayout.Popup(new GUIContent("License Filter"), (int)s.defaultLicenseFilter, ToContents(FreesoundLabels.License));
            s.defaultSortOrder = (SortOrder)EditorGUILayout.Popup(new GUIContent("Sort Order"), (int)s.defaultSortOrder, ToContents(FreesoundLabels.Sort));
            s.pageSize = EditorGUILayout.IntSlider(new GUIContent("Results Per Page"), s.pageSize, 5, 150);

            if (EditorGUI.EndChangeCheck()) s.Save();

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset To Defaults", GUILayout.Width(140)) &&
                EditorUtility.DisplayDialog("Freesound Browser", "Reset all project settings to defaults?", "Reset", "Cancel"))
            {
                s.ResetToDefaults();
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        // ---------------------------------------------------------------- Preferences (credentials)

        static void DrawPreferences()
        {
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 140;

            EditorGUILayout.HelpBox(
                "Credentials are stored in EditorPrefs on this machine only and are never written to the project.",
                MessageType.None);

            showSecrets = EditorGUILayout.ToggleLeft("Show secrets", showSecrets);

            using (new EditorGUI.DisabledScope(busy))
            {
                DrawApiKeySection();
                EditorGUILayout.Space(10);
                DrawOAuthSection();
            }

            if (busy) EditorGUILayout.HelpBox("Working...", MessageType.None);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Open Project Settings (install folder, defaults)", GUILayout.Width(320)))
                OpenProjectSettings();

            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        static void DrawApiKeySection()
        {
            EditorGUILayout.LabelField("1. API Key (search, preview, HQ preview install)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string key = SecretField("API Key", FreesoundUserPrefs.ApiKey);
            if (EditorGUI.EndChangeCheck()) FreesoundUserPrefs.ApiKey = key;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                if (GUILayout.Button("Get API credentials...", GUILayout.Width(160))) Application.OpenURL(ApplyUrl);
                using (new EditorGUI.DisabledScope(!FreesoundUserPrefs.HasApiKey))
                {
                    if (GUILayout.Button("Test API Key", GUILayout.Width(110))) TestApiKey();
                }
            }
        }

        static void DrawOAuthSection()
        {
            EditorGUILayout.LabelField("2. OAuth2 Login (optional, needed for original-quality downloads)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string clientId = EditorGUILayout.TextField(new GUIContent("Client ID", "The 'Client id' column on freesound.org/apiv2/apply (not the secret)."), FreesoundUserPrefs.ClientId);
            string clientSecret = SecretField("Client Secret", FreesoundUserPrefs.ClientSecret);
            if (EditorGUI.EndChangeCheck())
            {
                FreesoundUserPrefs.ClientId = clientId;
                FreesoundUserPrefs.ClientSecret = clientSecret;
            }

            if (string.IsNullOrEmpty(FreesoundUserPrefs.ClientSecret))
                EditorGUILayout.HelpBox("Client Secret is empty, so the API Key above is used (on Freesound they are the same value).", MessageType.None);

            if (FreesoundAuth.IsLoggedIn)
            {
                string user = string.IsNullOrEmpty(FreesoundUserPrefs.Username) ? "(unknown user)" : FreesoundUserPrefs.Username;
                DateTime expiry = FreesoundUserPrefs.AccessTokenExpiryUtc;
                string expiryText = expiry > DateTime.UtcNow
                    ? $"access token valid until {expiry.ToLocalTime():g}"
                    : "access token expired, it will be refreshed automatically";
                EditorGUILayout.HelpBox($"Logged in as {user}, {expiryText}.", MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth);
                    using (new EditorGUI.DisabledScope(!FreesoundAuth.CanRefresh))
                    {
                        if (GUILayout.Button("Refresh Token Now", GUILayout.Width(140))) RefreshNow();
                    }
                    if (GUILayout.Button("Log Out", GUILayout.Width(80)))
                    {
                        FreesoundAuth.Logout();
                        SetStatus("Logged out.", MessageType.Info);
                    }
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Step A");
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(FreesoundUserPrefs.ClientId)))
                {
                    if (GUILayout.Button("Open Freesound authorization page", GUILayout.Width(240)))
                        FreesoundAuth.OpenAuthorizationPage();
                }
            }

            authCode = EditorGUILayout.TextField(new GUIContent("Step B: Code", "Paste the code (or the whole redirect URL) shown after clicking 'Authorize!'. Codes expire after 10 minutes."), authCode);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                bool canExchange = !string.IsNullOrWhiteSpace(authCode) && FreesoundAuth.HasClientCredentials;
                using (new EditorGUI.DisabledScope(!canExchange))
                {
                    if (GUILayout.Button("Complete Login", GUILayout.Width(140))) ExchangeCode();
                }
            }
        }

        static string SecretField(string label, string value)
        {
            return showSecrets
                ? EditorGUILayout.TextField(label, value)
                : EditorGUILayout.PasswordField(label, value);
        }

        static GUIContent[] ToContents(string[] labels)
        {
            var result = new GUIContent[labels.Length];
            for (int i = 0; i < labels.Length; i++) result[i] = new GUIContent(labels[i]);
            return result;
        }

        static void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            SettingsService.RepaintAllSettingsWindow();
        }

        static async void TestApiKey()
        {
            busy = true;
            SetStatus(null, MessageType.None);
            try
            {
                var response = await FreesoundClient.SearchAsync(new SearchQuery { Text = "beep", PageSize = 1, License = LicenseFilter.Any });
                SetStatus($"API key works. Freesound returned {response.Count:N0} results for \"beep\".", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus("API key test failed: " + e.Message, MessageType.Error);
            }
            finally
            {
                busy = false;
                SettingsService.RepaintAllSettingsWindow();
            }
        }

        static async void ExchangeCode()
        {
            busy = true;
            SetStatus(null, MessageType.None);
            try
            {
                await FreesoundAuth.ExchangeCodeAsync(authCode);
                authCode = string.Empty;
                SetStatus($"Login successful{(string.IsNullOrEmpty(FreesoundUserPrefs.Username) ? "" : " as " + FreesoundUserPrefs.Username)}. Original-quality downloads are now available.", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus("Login failed: " + e.Message + "\nAuthorization codes expire after 10 minutes and can be used only once; repeat Step A to get a new one.", MessageType.Error);
            }
            finally
            {
                busy = false;
                SettingsService.RepaintAllSettingsWindow();
            }
        }

        static async void RefreshNow()
        {
            busy = true;
            SetStatus(null, MessageType.None);
            try
            {
                await FreesoundAuth.GetValidAccessTokenAsync(forceRefresh: true);
                SetStatus("Access token refreshed.", MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus("Refresh failed: " + e.Message, MessageType.Error);
            }
            finally
            {
                busy = false;
                SettingsService.RepaintAllSettingsWindow();
            }
        }
    }
}
