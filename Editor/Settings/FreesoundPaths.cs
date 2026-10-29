using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    internal static class FreesoundPaths
    {
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static string CacheDirectory => Path.Combine(ProjectRoot, "Library", "FreesoundCache");

        public static string ToFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
        }

        /// <summary>Converts an absolute path to an "Assets/..." path, or null if it is outside the Assets folder.</summary>
        public static string ToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;

            string root = ProjectRoot.Replace('\\', '/').TrimEnd('/');
            string path = Path.GetFullPath(fullPath).Replace('\\', '/').TrimEnd('/');

            if (string.Equals(path, root + "/Assets", StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (path.StartsWith(root + "/Assets/", StringComparison.OrdinalIgnoreCase))
                return path.Substring(root.Length + 1);
            return null;
        }

        public static string NormalizeFolder(string folder)
        {
            return (folder ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        public static bool IsValidAssetFolder(string folder)
        {
            string f = NormalizeFolder(folder);
            if (f != "Assets" && !f.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            if (f.Contains("..") || f.Contains("//")) return false;
            return f.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }

        public static void EnsureAssetFolder(string folder)
        {
            string[] parts = NormalizeFolder(folder).Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
