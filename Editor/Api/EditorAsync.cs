using System.Threading.Tasks;
using UnityEngine.Networking;

namespace FreesoundBrowser
{
    internal static class EditorAsync
    {
        /// <summary>
        /// Awaitable SendWebRequest. Continuations resume on the editor main thread via Unity's SynchronizationContext.
        /// </summary>
        public static Task SendAsync(this UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            if (operation.isDone) tcs.TrySetResult(true);
            else operation.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
