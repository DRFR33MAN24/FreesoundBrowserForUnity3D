using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FreesoundBrowser
{
    /// <summary>
    /// The built-in AudioSource inspector ignores property drawers, so AudioSources get a button in the
    /// GameObject header and a component context-menu entry instead.
    /// </summary>
    [InitializeOnLoad]
    internal static class AudioSourceHeaderHook
    {
        const string AudioClipPropertyPath = "m_audioClip";

        static AudioSourceHeaderHook()
        {
            Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        static void OnHeaderGUI(Editor editor)
        {
            if (!FreesoundSettings.Instance.showFieldButton) return;
            if (!(editor.target is GameObject)) return;

            var sources = editor.targets
                .OfType<GameObject>()
                .Select(go => go.GetComponent<AudioSource>())
                .ToArray();
            if (sources.Length == 0 || sources.Any(s => s == null)) return;

            var content = new GUIContent("Find AudioSource Clip on Freesound...",
                "Search Freesound and assign the picked sound to the AudioSource clip.");
            if (GUILayout.Button(content, EditorStyles.miniButton))
            {
                Open(sources);
                GUIUtility.ExitGUI();
            }
        }

        [MenuItem("CONTEXT/AudioSource/Find Clip on Freesound...")]
        static void OpenFromContextMenu(MenuCommand command)
        {
            if (command.context is AudioSource source) Open(new[] { source });
        }

        static void Open(AudioSource[] sources)
        {
            var first = sources[0];
            string label = $"{first.gameObject.name} > Audio Source > Clip" +
                           (sources.Length > 1 ? $" (+{sources.Length - 1} more)" : string.Empty);
            string hint = first.clip != null ? first.clip.name : first.gameObject.name;

            FreesoundBrowserWindow.OpenForTargets(sources.Cast<Object>().ToArray(), AudioClipPropertyPath, label, hint);
        }
    }
}
