using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FreesoundBrowser
{
    public enum DownloadQuality
    {
        HqPreviewOgg,
        HqPreviewMp3,
        Original
    }

    public enum LicenseFilter
    {
        Cc0Only,
        Cc0AndAttribution,
        Any
    }

    public enum SortOrder
    {
        Relevance,
        MostDownloaded,
        HighestRated,
        Newest,
        ShortestFirst,
        LongestFirst
    }

    internal static class FreesoundLabels
    {
        public static readonly string[] Quality =
        {
            "HQ preview (OGG ~192kbps)",
            "HQ preview (MP3 ~128kbps)",
            "Original file (OAuth2 login)"
        };

        public static readonly string[] License =
        {
            "CC0 only",
            "CC0 + Attribution",
            "Any (incl. NonCommercial)"
        };

        public static readonly string[] Sort =
        {
            "Relevance",
            "Most downloaded",
            "Highest rated",
            "Newest",
            "Shortest first",
            "Longest first"
        };
    }

    public class SearchQuery
    {
        public const string Fields =
            "id,name,tags,username,license,duration,type,filesize,samplerate,channels,previews,images,avg_rating,num_downloads,url";

        public string Text = string.Empty;
        public int Page = 1;
        public int PageSize = 30;
        public SortOrder Sort = SortOrder.Relevance;
        public float MinDuration;
        public float MaxDuration;
        public LicenseFilter License = LicenseFilter.Cc0AndAttribution;

        public string BuildFilter()
        {
            var filters = new List<string>();

            float min = Mathf.Max(0f, MinDuration);
            float max = Mathf.Max(0f, MaxDuration);
            if (min > 0f && max > 0f && min > max)
            {
                float t = min;
                min = max;
                max = t;
            }
            if (min > 0f || max > 0f)
            {
                string lo = min > 0f ? min.ToString("0.###", CultureInfo.InvariantCulture) : "*";
                string hi = max > 0f ? max.ToString("0.###", CultureInfo.InvariantCulture) : "*";
                filters.Add($"duration:[{lo} TO {hi}]");
            }

            switch (License)
            {
                case LicenseFilter.Cc0Only:
                    filters.Add("license:\"Creative Commons 0\"");
                    break;
                case LicenseFilter.Cc0AndAttribution:
                    filters.Add("license:(\"Creative Commons 0\" OR \"Attribution\")");
                    break;
            }

            return string.Join(" ", filters);
        }

        public string BuildUrl()
        {
            var sb = new StringBuilder(FreesoundClient.ApiRoot);
            sb.Append("/search/?query=").Append(Uri.EscapeDataString((Text ?? string.Empty).Trim()));

            string filter = BuildFilter();
            if (filter.Length > 0) sb.Append("&filter=").Append(Uri.EscapeDataString(filter));

            sb.Append("&sort=").Append(SortParameter(Sort));
            sb.Append("&page=").Append(Mathf.Max(1, Page).ToString(CultureInfo.InvariantCulture));
            sb.Append("&page_size=").Append(Mathf.Clamp(PageSize, 1, 150).ToString(CultureInfo.InvariantCulture));
            sb.Append("&fields=").Append(Fields);
            return sb.ToString();
        }

        static string SortParameter(SortOrder sort)
        {
            switch (sort)
            {
                case SortOrder.MostDownloaded: return "downloads_desc";
                case SortOrder.HighestRated: return "rating_desc";
                case SortOrder.Newest: return "created_desc";
                case SortOrder.ShortestFirst: return "duration_asc";
                case SortOrder.LongestFirst: return "duration_desc";
                default: return "score";
            }
        }
    }
}
