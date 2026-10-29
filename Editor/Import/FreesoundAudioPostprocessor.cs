using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    /// <summary>
    /// Applies import presets to sounds the installer has just downloaded. Later reimports are left alone,
    /// so manual tweaks to the import settings are never overwritten.
    /// </summary>
    internal class FreesoundAudioPostprocessor : AssetPostprocessor
    {
        internal static readonly Dictionary<string, SoundResult> Pending = new Dictionary<string, SoundResult>();

        void OnPreprocessAudio()
        {
            if (!Pending.TryGetValue(assetPath, out var sound)) return;

            var importer = (AudioImporter)assetImporter;
            importer.userData = "freesound:" + sound.Id.ToString(CultureInfo.InvariantCulture);

            var settings = FreesoundSettings.Instance;
            if (!settings.applyImportPresets) return;

            bool isShort = sound.Duration > 0f && sound.Duration < settings.shortClipThreshold;
            var sampleSettings = importer.defaultSampleSettings;
            sampleSettings.loadType = isShort ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            importer.defaultSampleSettings = sampleSettings;

            if (isShort && settings.forceMonoShortClips) importer.forceToMono = true;
        }
    }
}
