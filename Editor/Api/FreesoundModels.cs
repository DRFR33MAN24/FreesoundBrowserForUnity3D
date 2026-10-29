using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FreesoundBrowser
{
    [Serializable]
    public class SoundPreviews
    {
        [JsonProperty("preview-hq-mp3")] public string HqMp3;
        [JsonProperty("preview-lq-mp3")] public string LqMp3;
        [JsonProperty("preview-hq-ogg")] public string HqOgg;
        [JsonProperty("preview-lq-ogg")] public string LqOgg;
    }

    [Serializable]
    public class SoundImages
    {
        [JsonProperty("waveform_m")] public string WaveformM;
        [JsonProperty("waveform_l")] public string WaveformL;
        [JsonProperty("spectral_m")] public string SpectralM;
        [JsonProperty("spectral_l")] public string SpectralL;
    }

    [Serializable]
    public class SoundResult
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("tags")] public List<string> Tags = new List<string>();
        [JsonProperty("username")] public string Username;
        [JsonProperty("license")] public string License;
        [JsonProperty("duration")] public float Duration;
        [JsonProperty("type")] public string Type;
        [JsonProperty("filesize")] public long FileSize;
        [JsonProperty("samplerate")] public float SampleRate;
        [JsonProperty("channels")] public int Channels;
        [JsonProperty("previews")] public SoundPreviews Previews;
        [JsonProperty("images")] public SoundImages Images;
        [JsonProperty("avg_rating")] public float AvgRating;
        [JsonProperty("num_downloads")] public int NumDownloads;
        [JsonProperty("url")] public string Url;

        public string PageUrl => string.IsNullOrEmpty(Url) ? $"https://freesound.org/s/{Id}/" : Url;
    }

    public class SearchResponse
    {
        [JsonProperty("count")] public int Count;
        [JsonProperty("next")] public string Next;
        [JsonProperty("previous")] public string Previous;
        [JsonProperty("results")] public List<SoundResult> Results = new List<SoundResult>();
    }

    public class TokenResponse
    {
        [JsonProperty("access_token")] public string AccessToken;
        [JsonProperty("refresh_token")] public string RefreshToken;
        [JsonProperty("expires_in")] public int ExpiresIn;
        [JsonProperty("scope")] public string Scope;
    }

    public class MeResponse
    {
        [JsonProperty("username")] public string Username;
        [JsonProperty("email")] public string Email;
    }

    public static class LicenseUtil
    {
        /// <summary>Handles both the documented names ("Attribution") and the license URLs the API actually returns.</summary>
        public static string ShortName(string license)
        {
            if (string.IsNullOrEmpty(license)) return "Unknown license";
            string l = license.ToLowerInvariant();
            if (l.Contains("zero") || l.Contains("creative commons 0") || l.Contains("cc0")) return "CC0";
            if (l.Contains("sampling")) return "Sampling+";
            if (l.Contains("by-nc") || l.Contains("noncommercial")) return "CC BY-NC";
            if (l.Contains("/by/") || l.Contains("attribution")) return "CC BY";
            return license;
        }

        public static bool RequiresAttribution(string license) => ShortName(license) != "CC0";

        public static bool IsNonCommercial(string license) => ShortName(license) == "CC BY-NC";
    }
}
