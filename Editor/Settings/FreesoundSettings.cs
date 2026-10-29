using System;
using System.IO;
using UnityEngine;

namespace FreesoundBrowser
{
    /// <summary>
    /// Project-wide settings shared with the team (stored in ProjectSettings/FreesoundBrowserSettings.json).
    /// Never put secrets here; those live in <see cref="FreesoundUserPrefs"/>.
    /// </summary>
    [Serializable]
    public class FreesoundSettings
    {
        const string RelativeFilePath = "ProjectSettings/FreesoundBrowserSettings.json";
        public const string DefaultInstallFolder = "Assets/Audio/Freesound";

        public string installFolder = DefaultInstallFolder;
        public DownloadQuality defaultQuality = DownloadQuality.HqPreviewOgg;
        public bool showFieldButton = true;
        public bool closeWindowAfterPick = true;
        public bool autoPlayOnSelect = true;
        public bool writeCredits = true;

        public float defaultMinDuration = 0f;
        public float defaultMaxDuration = 5f;
        public LicenseFilter defaultLicenseFilter = LicenseFilter.Cc0AndAttribution;
        public SortOrder defaultSortOrder = SortOrder.Relevance;
        public int pageSize = 30;

        public bool applyImportPresets = true;
        public float shortClipThreshold = 2f;
        public bool forceMonoShortClips = true;

        static FreesoundSettings instance;

        public static FreesoundSettings Instance
        {
            get
            {
                if (instance == null) instance = Load();
                return instance;
            }
        }

        static string FullPath => Path.Combine(FreesoundPaths.ProjectRoot, RelativeFilePath);

        static FreesoundSettings Load()
        {
            var settings = new FreesoundSettings();
            try
            {
                if (File.Exists(FullPath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FullPath), settings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Freesound] Could not read {RelativeFilePath}, using defaults. {e.Message}");
            }
            return settings;
        }

        public void Save()
        {
            pageSize = Mathf.Clamp(pageSize, 1, 150);
            File.WriteAllText(FullPath, JsonUtility.ToJson(this, true));
        }

        public void ResetToDefaults()
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new FreesoundSettings()), this);
            Save();
        }

        /// <summary>The install folder normalized to an "Assets/..." path. Throws if the setting is invalid.</summary>
        public string GetValidatedInstallFolder()
        {
            string folder = FreesoundPaths.NormalizeFolder(installFolder);
            if (!FreesoundPaths.IsValidAssetFolder(folder))
                throw new InvalidOperationException(
                    $"Install folder \"{installFolder}\" is invalid. It must be inside Assets (Project Settings > Freesound Browser).");
            return folder;
        }
    }
}
