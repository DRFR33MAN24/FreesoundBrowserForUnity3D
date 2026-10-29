using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    /// <summary>
    /// Downloads a Freesound sound into the configured install folder, imports it and returns the AudioClip asset.
    /// </summary>
    public static class SoundInstaller
    {
        static readonly HashSet<string> UnityAudioExtensions = new HashSet<string> { "wav", "aif", "aiff", "ogg", "mp3" };
        static readonly string[] NameExtensionsToStrip = { ".wav", ".wave", ".aif", ".aiff", ".ogg", ".mp3", ".flac", ".m4a" };

        public static async Task<AudioClip> InstallAsync(SoundResult sound, DownloadQuality quality)
        {
            var settings = FreesoundSettings.Instance;
            string folder = settings.GetValidatedInstallFolder();

            string existing = FindExisting(folder, sound.Id);
            if (existing != null)
            {
                var existingClip = AssetDatabase.LoadAssetAtPath<AudioClip>(existing);
                if (existingClip != null) return existingClip;
            }

            var (bytes, extension) = await DownloadAsync(sound, quality);

            FreesoundPaths.EnsureAssetFolder(folder);
            string assetPath = $"{folder}/{SanitizeName(sound.Name)}_{sound.Id}.{extension}";
            File.WriteAllBytes(FreesoundPaths.ToFullPath(assetPath), bytes);

            FreesoundAudioPostprocessor.Pending[assetPath] = sound;
            try
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                FreesoundAudioPostprocessor.Pending.Remove(assetPath);
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null)
                throw new InvalidOperationException($"The downloaded file could not be imported as an AudioClip: {assetPath}");

            if (settings.writeCredits) AttributionWriter.Append(sound, assetPath, folder);
            return clip;
        }

        static async Task<(byte[] bytes, string extension)> DownloadAsync(SoundResult sound, DownloadQuality quality)
        {
            if (quality == DownloadQuality.Original)
            {
                string type = (sound.Type ?? string.Empty).ToLowerInvariant();
                if (!FreesoundAuth.IsLoggedIn)
                {
                    Debug.LogWarning($"[Freesound] Not logged in with OAuth2, installing the HQ OGG preview of '{sound.Name}' instead of the original file.");
                }
                else if (!UnityAudioExtensions.Contains(type))
                {
                    Debug.LogWarning($"[Freesound] The original of '{sound.Name}' is '{type}', which Unity cannot import. Installing the HQ OGG preview instead.");
                }
                else
                {
                    return (await FreesoundClient.DownloadOriginalAsync(sound.Id), type);
                }
                quality = DownloadQuality.HqPreviewOgg;
            }

            var previews = sound.Previews;
            if (previews == null) throw new InvalidOperationException($"'{sound.Name}' has no downloadable previews.");

            bool wantMp3 = quality == DownloadQuality.HqPreviewMp3;
            string url = wantMp3 ? previews.HqMp3 : previews.HqOgg;
            string extension = wantMp3 ? "mp3" : "ogg";
            if (string.IsNullOrEmpty(url))
            {
                url = wantMp3 ? previews.HqOgg : previews.HqMp3;
                extension = wantMp3 ? "ogg" : "mp3";
            }
            if (string.IsNullOrEmpty(url)) throw new InvalidOperationException($"'{sound.Name}' has no HQ preview.");

            return (await FreesoundClient.DownloadPreviewAsync(url), extension);
        }

        /// <summary>Finds an already-installed file for this sound id ("*_{id}.ext") so it is reused instead of downloaded again.</summary>
        static string FindExisting(string folder, int soundId)
        {
            string fullFolder = FreesoundPaths.ToFullPath(folder);
            if (!Directory.Exists(fullFolder)) return null;

            string file = Directory.GetFiles(fullFolder, $"*_{soundId}.*")
                .FirstOrDefault(f => UnityAudioExtensions.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant()));
            return file == null ? null : $"{folder}/{Path.GetFileName(file)}";
        }

        static string SanitizeName(string name)
        {
            string result = (name ?? string.Empty).Trim();
            foreach (string ext in NameExtensionsToStrip)
            {
                if (result.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - ext.Length);
                    break;
                }
            }

            var sb = new StringBuilder(result.Length);
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            foreach (char c in result)
                sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) || c == '#' || c == '%' ? '_' : c);

            result = Regex.Replace(sb.ToString(), "_+", "_").Trim('_', '.', '-');
            if (result.Length > 48) result = result.Substring(0, 48).TrimEnd('_', '.', '-');
            return result.Length == 0 ? "freesound" : result;
        }
    }
}
