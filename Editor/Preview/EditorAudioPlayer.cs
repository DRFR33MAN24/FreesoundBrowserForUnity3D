using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FreesoundBrowser
{
    /// <summary>
    /// Plays low-quality previews in edit mode through a hidden AudioSource. Preview files are cached in Library/FreesoundCache.
    /// </summary>
    internal static class EditorAudioPlayer
    {
        static GameObject host;
        static AudioSource source;
        static int playingId = -1;
        static int loadingId = -1;
        static int ticket;

        public static int PlayingId => source != null && source.isPlaying ? playingId : -1;

        public static int LoadingId => loadingId;

        static string PreviewCacheDirectory => Path.Combine(FreesoundPaths.CacheDirectory, "previews");

        [InitializeOnLoadMethod]
        static void RegisterCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DestroyHost;
            EditorApplication.quitting += DestroyHost;
        }

        public static async Task PlayAsync(SoundResult sound)
        {
            Stop();
            int myTicket = ++ticket;
            loadingId = sound.Id;

            try
            {
                var previews = sound.Previews;
                bool useOgg = !string.IsNullOrEmpty(previews?.LqOgg);
                string url = useOgg ? previews.LqOgg : previews?.LqMp3;
                if (string.IsNullOrEmpty(url)) throw new FreesoundApiException(0, "This sound has no preview.");

                string path = Path.Combine(PreviewCacheDirectory, $"{sound.Id}-lq.{(useOgg ? "ogg" : "mp3")}");
                if (!File.Exists(path))
                {
                    byte[] bytes = await FreesoundClient.DownloadPreviewAsync(url);
                    Directory.CreateDirectory(PreviewCacheDirectory);
                    File.WriteAllBytes(path, bytes);
                }
                if (myTicket != ticket) return;

                var audioType = useOgg ? AudioType.OGGVORBIS : AudioType.MPEG;
                using (var request = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, audioType))
                {
                    await request.SendAsync();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        File.Delete(path);
                        throw new FreesoundApiException(0, "Could not decode preview: " + request.error);
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(request);
                    if (myTicket != ticket)
                    {
                        Object.DestroyImmediate(clip);
                        return;
                    }

                    clip.name = sound.Name;
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    EnsureSource();
                    source.clip = clip;
                    source.Play();
                    playingId = sound.Id;
                }
            }
            finally
            {
                if (myTicket == ticket) loadingId = -1;
            }
        }

        public static void Stop()
        {
            ticket++;
            loadingId = -1;
            playingId = -1;
            if (source == null) return;

            source.Stop();
            var clip = source.clip;
            source.clip = null;
            if (clip != null) Object.DestroyImmediate(clip);
        }

        static void EnsureSource()
        {
            if (source != null) return;
            if (host == null)
                host = EditorUtility.CreateGameObjectWithHideFlags("FreesoundPreviewPlayer", HideFlags.HideAndDontSave, typeof(AudioSource));

            source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = false;
        }

        static void DestroyHost()
        {
            Stop();
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            source = null;
        }
    }
}
