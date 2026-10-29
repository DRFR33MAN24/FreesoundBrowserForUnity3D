using System.IO;
using System.Text;
using UnityEditor;

namespace FreesoundBrowser
{
    /// <summary>
    /// Keeps a FREESOUND_CREDITS.md table in the install folder. CC BY sounds must be credited in your game.
    /// </summary>
    internal static class AttributionWriter
    {
        public const string FileName = "FREESOUND_CREDITS.md";

        public static void Append(SoundResult sound, string installedAssetPath, string folder)
        {
            string assetPath = $"{folder}/{FileName}";
            string fullPath = FreesoundPaths.ToFullPath(assetPath);
            string sourceUrl = $"https://freesound.org/s/{sound.Id}/";

            bool exists = File.Exists(fullPath);
            if (exists && File.ReadAllText(fullPath).Contains(sourceUrl)) return;

            var sb = new StringBuilder();
            if (!exists)
            {
                sb.AppendLine("# Freesound Credits");
                sb.AppendLine();
                sb.AppendLine("Sounds downloaded from [Freesound](https://freesound.org) with Freesound SFX Browser.");
                sb.AppendLine("Sounds marked **Credit required** (CC BY / CC BY-NC / Sampling+) must be credited in your game or its credits screen.");
                sb.AppendLine("CC BY-NC sounds cannot be used in commercial projects.");
                sb.AppendLine();
                sb.AppendLine("| Sound | Author | License | Credit required | Source | File |");
                sb.AppendLine("|---|---|---|---|---|---|");
            }

            string license = string.IsNullOrEmpty(sound.License)
                ? LicenseUtil.ShortName(sound.License)
                : $"[{LicenseUtil.ShortName(sound.License)}]({(sound.License.StartsWith("http") ? sound.License : sourceUrl)})";

            sb.AppendLine($"| {Escape(sound.Name)} | {Escape(sound.Username)} | {license} | {(LicenseUtil.RequiresAttribution(sound.License) ? "**Yes**" : "No")} | {sourceUrl} | `{Path.GetFileName(installedAssetPath)}` |");

            File.AppendAllText(fullPath, sb.ToString());
            AssetDatabase.ImportAsset(assetPath);
        }

        static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
    }
}
