using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace FreesoundBrowser
{
    public class FreesoundBrowserWindow : EditorWindow
    {
        const string SearchControlName = "FreesoundSearchField";
        const float RowHeight = 56f;
        const float WaveformWidth = 150f;
        const double SearchDebounceSeconds = 0.6;

        static readonly HashSet<string> LabelStopWords = new HashSet<string>
        {
            "clip", "clips", "sound", "sounds", "sfx", "audio", "fx", "element", "source", "the", "on", "m"
        };

        [SerializeField] string query = string.Empty;
        [SerializeField] float minDuration;
        [SerializeField] float maxDuration;
        [SerializeField] LicenseFilter licenseFilter;
        [SerializeField] SortOrder sortOrder;
        [SerializeField] DownloadQuality quality;
        [SerializeField] bool autoPlay;
        [SerializeField] bool initialized;

        [SerializeField] Object[] targets;
        [SerializeField] string propertyPath;
        [SerializeField] string targetLabel;

        [SerializeField] List<SoundResult> results = new List<SoundResult>();
        [SerializeField] int totalCount;
        [SerializeField] int page = 1;
        [SerializeField] int selectedId = -1;
        [SerializeField] bool hasSearched;

        [NonSerialized] string error;
        [NonSerialized] string info;
        [NonSerialized] bool searching;
        [NonSerialized] int searchSequence;
        [NonSerialized] double searchAt = -1;
        [NonSerialized] int installingId = -1;
        [NonSerialized] bool focusSearchField;
        [NonSerialized] int lastPlayingId = -1;
        [NonSerialized] int lastLoadingId = -1;

        Vector2 scroll;
        readonly Dictionary<int, Texture2D> waveforms = new Dictionary<int, Texture2D>();
        readonly HashSet<int> waveformRequested = new HashSet<int>();

        GUIStyle nameStyle;
        GUIStyle metaStyle;

        bool HasTarget => targets != null && targets.Any(t => t != null) && !string.IsNullOrEmpty(propertyPath);

        int PageCount
        {
            get
            {
                int size = Mathf.Max(1, FreesoundSettings.Instance.pageSize);
                return Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)size));
            }
        }

        // ---------------------------------------------------------------- Opening

        [MenuItem("Window/Audio/Freesound Browser")]
        public static void OpenStandalone()
        {
            var window = GetOrCreate();
            window.SetTarget(null, null, null);
            window.focusSearchField = true;
        }

        public static void OpenForProperty(SerializedProperty property)
        {
            string label = DescribeTarget(property.serializedObject.targetObjects, property.displayName);
            OpenForTargets(property.serializedObject.targetObjects, property.propertyPath, label, property.displayName);
        }

        public static void OpenForTargets(Object[] targetObjects, string targetPropertyPath, string label, string queryHint)
        {
            var window = GetOrCreate();
            window.SetTarget(targetObjects, targetPropertyPath, label);

            string guess = QueryFromLabel(queryHint);
            if (!string.IsNullOrEmpty(guess) && guess != window.query)
            {
                window.query = guess;
                window.ScheduleSearch(0);
            }
            window.focusSearchField = true;
        }

        static FreesoundBrowserWindow GetOrCreate()
        {
            var window = GetWindow<FreesoundBrowserWindow>();
            window.Show();
            window.Focus();
            return window;
        }

        void SetTarget(Object[] targetObjects, string targetPropertyPath, string label)
        {
            targets = targetObjects != null ? targetObjects.ToArray() : null;
            propertyPath = targetPropertyPath;
            targetLabel = label;
            info = null;
            Repaint();
        }

        static string DescribeTarget(Object[] targetObjects, string fieldName)
        {
            var first = targetObjects.FirstOrDefault(t => t != null);
            if (first == null) return fieldName;

            string owner = first is Component c
                ? $"{c.gameObject.name} > {ObjectNames.NicifyVariableName(c.GetType().Name)}"
                : $"{first.name} ({ObjectNames.NicifyVariableName(first.GetType().Name)})";
            string more = targetObjects.Length > 1 ? $" (+{targetObjects.Length - 1} more)" : string.Empty;
            return $"{owner} > {fieldName}{more}";
        }

        static string QueryFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var words = Regex.Split(ObjectNames.NicifyVariableName(label).ToLowerInvariant(), "[^a-z]+")
                .Where(w => w.Length > 1 && !LabelStopWords.Contains(w));
            return string.Join(" ", words);
        }

        // ---------------------------------------------------------------- Lifecycle

        void OnEnable()
        {
            titleContent = new GUIContent("Freesound", EditorGUIUtility.IconContent("AudioSource Icon").image);
            minSize = new Vector2(560, 320);

            if (!initialized)
            {
                var s = FreesoundSettings.Instance;
                minDuration = s.defaultMinDuration;
                maxDuration = s.defaultMaxDuration;
                licenseFilter = s.defaultLicenseFilter;
                sortOrder = s.defaultSortOrder;
                quality = s.defaultQuality;
                autoPlay = s.autoPlayOnSelect;
                initialized = true;
            }
        }

        void OnDisable()
        {
            EditorAudioPlayer.Stop();
        }

        void OnDestroy()
        {
            ClearWaveforms();
        }

        void Update()
        {
            if (searchAt >= 0 && EditorApplication.timeSinceStartup >= searchAt)
            {
                searchAt = -1;
                RunSearch(1);
            }

            int playing = EditorAudioPlayer.PlayingId;
            int loading = EditorAudioPlayer.LoadingId;
            if (playing != lastPlayingId || loading != lastLoadingId)
            {
                lastPlayingId = playing;
                lastLoadingId = loading;
                Repaint();
            }
        }

        // ---------------------------------------------------------------- GUI

        void OnGUI()
        {
            EnsureStyles();
            HandleKeyboard();

            DrawToolbar();
            DrawFilters();
            DrawTargetBar();
            DrawMessages();
            DrawResults();
            DrawFooter();
        }

        void EnsureStyles()
        {
            if (nameStyle != null) return;
            nameStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            metaStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
        }

        void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            bool inSearchField = GUI.GetNameOfFocusedControl() == SearchControlName;
            if (inSearchField && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                searchAt = -1;
                RunSearch(1);
                e.Use();
                return;
            }

            if (inSearchField || EditorGUIUtility.editingTextField) return;

            var selected = results.FirstOrDefault(r => r.Id == selectedId);
            switch (e.keyCode)
            {
                case KeyCode.Space when selected != null:
                    TogglePreview(selected);
                    e.Use();
                    break;
                case KeyCode.Return when selected != null:
                case KeyCode.KeypadEnter when selected != null:
                    Pick(selected);
                    e.Use();
                    break;
                case KeyCode.DownArrow:
                case KeyCode.UpArrow:
                    MoveSelection(e.keyCode == KeyCode.DownArrow ? 1 : -1);
                    e.Use();
                    break;
            }
        }

        void MoveSelection(int delta)
        {
            if (results.Count == 0) return;
            int index = results.FindIndex(r => r.Id == selectedId);
            index = Mathf.Clamp(index < 0 ? 0 : index + delta, 0, results.Count - 1);
            Select(results[index]);

            float rowTop = index * RowHeight;
            float viewHeight = position.height - 140f;
            if (rowTop < scroll.y) scroll.y = rowTop;
            else if (rowTop + RowHeight > scroll.y + viewHeight) scroll.y = rowTop + RowHeight - viewHeight;
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUI.SetNextControlName(SearchControlName);
                EditorGUI.BeginChangeCheck();
                query = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck()) ScheduleSearch(SearchDebounceSeconds);

                if (focusSearchField && Event.current.type == EventType.Repaint)
                {
                    focusSearchField = false;
                    EditorGUI.FocusTextInControl(SearchControlName);
                }

                if (GUILayout.Button("Search", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    searchAt = -1;
                    RunSearch(1);
                }

                if (GUILayout.Button(new GUIContent("Settings", "Credentials: Preferences > Freesound Browser"), EditorStyles.toolbarButton, GUILayout.Width(60)))
                    FreesoundSettingsProvider.OpenPreferences();
            }
        }

        void DrawFilters()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();

                GUILayout.Label(new GUIContent("Duration", "Seconds. 0 = no limit."), GUILayout.Width(52));
                minDuration = Mathf.Max(0f, EditorGUILayout.FloatField(minDuration, GUILayout.Width(38)));
                GUILayout.Label("to", GUILayout.Width(16));
                maxDuration = Mathf.Max(0f, EditorGUILayout.FloatField(maxDuration, GUILayout.Width(38)));
                GUILayout.Label("s", GUILayout.Width(12));

                licenseFilter = (LicenseFilter)EditorGUILayout.Popup((int)licenseFilter, FreesoundLabels.License, GUILayout.Width(150));
                sortOrder = (SortOrder)EditorGUILayout.Popup((int)sortOrder, FreesoundLabels.Sort, GUILayout.Width(110));

                if (EditorGUI.EndChangeCheck()) ScheduleSearch(SearchDebounceSeconds);

                GUILayout.FlexibleSpace();
                quality = (DownloadQuality)EditorGUILayout.Popup((int)quality, FreesoundLabels.Quality, GUILayout.Width(175));
                autoPlay = GUILayout.Toggle(autoPlay, new GUIContent("Auto-play", "Play the preview when a row is selected."), GUILayout.Width(75));
            }
        }

        void DrawTargetBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (HasTarget)
                {
                    GUILayout.Label("Assign to:", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                    GUILayout.Label(targetLabel, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Clear Target", EditorStyles.miniButton, GUILayout.Width(80)))
                        SetTarget(null, null, null);
                }
                else
                {
                    GUILayout.Label($"No target field: picked sounds are installed into {FreesoundSettings.Instance.installFolder}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        void DrawMessages()
        {
            if (!FreesoundUserPrefs.HasApiKey && !FreesoundAuth.IsLoggedIn)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("No Freesound API key configured. Add it in Preferences > Freesound Browser (see README).", MessageType.Warning);
                    if (GUILayout.Button("Open Preferences", GUILayout.Width(120), GUILayout.Height(38)))
                        FreesoundSettingsProvider.OpenPreferences();
                }
            }

            if (quality == DownloadQuality.Original && !FreesoundAuth.IsLoggedIn)
                EditorGUILayout.HelpBox("Original-quality downloads need an OAuth2 login (Preferences > Freesound Browser). Until then the HQ OGG preview is installed instead.", MessageType.Info);

            if (EditorUtility.audioMasterMute)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("Editor audio is muted (Game view 'Mute Audio'), so previews are silent.", MessageType.Warning);
                    if (GUILayout.Button("Unmute", GUILayout.Width(70), GUILayout.Height(38)))
                        EditorUtility.audioMasterMute = false;
                }
            }

            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(info)) EditorGUILayout.HelpBox(info, MessageType.Info);
        }

        void DrawResults()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

            if (searching && results.Count == 0)
                GUILayout.Label("Searching...", EditorStyles.centeredGreyMiniLabel);
            else if (results.Count == 0)
                GUILayout.Label(hasSearched ? "No results. Try other words or relax the filters." : "Type to search Freesound.", EditorStyles.centeredGreyMiniLabel);

            for (int i = 0; i < results.Count; i++)
                DrawRow(results[i], i);

            EditorGUILayout.EndScrollView();
        }

        void DrawRow(SoundResult sound, int index)
        {
            Rect row = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            bool selected = sound.Id == selectedId;

            if (e.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                else if (index % 2 == 1) EditorGUI.DrawRect(row, new Color(0f, 0f, 0f, 0.08f));
                EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), new Color(0f, 0f, 0f, 0.2f));
                RequestWaveform(sound);
            }

            bool isPlaying = EditorAudioPlayer.PlayingId == sound.Id;
            bool isLoading = EditorAudioPlayer.LoadingId == sound.Id;
            var playRect = new Rect(row.x + 6, row.y + (RowHeight - 26) / 2, 44, 26);
            if (GUI.Button(playRect, isLoading ? "..." : isPlaying ? "Stop" : "Play"))
            {
                Select(sound, play: false);
                TogglePreview(sound);
            }

            var waveRect = new Rect(playRect.xMax + 6, row.y + 5, WaveformWidth, RowHeight - 10);
            if (waveforms.TryGetValue(sound.Id, out var texture) && texture != null)
                GUI.DrawTexture(waveRect, texture, ScaleMode.StretchToFill);
            else if (e.type == EventType.Repaint)
                EditorGUI.DrawRect(waveRect, new Color(0f, 0f, 0f, 0.25f));

            var pickRect = new Rect(row.xMax - 76, row.y + 7, 70, 22);
            var webRect = new Rect(row.xMax - 76, row.y + 32, 70, 17);
            using (new EditorGUI.DisabledScope(installingId != -1))
            {
                string pickLabel = installingId == sound.Id ? "Installing..." : HasTarget ? "Pick" : "Install";
                if (GUI.Button(pickRect, new GUIContent(pickLabel, "Download, import and assign (double-click or Enter also works)")))
                    Pick(sound);
            }
            if (GUI.Button(webRect, new GUIContent("Web page", sound.PageUrl), EditorStyles.miniButton))
                Application.OpenURL(sound.PageUrl);

            float textX = waveRect.xMax + 8;
            float textWidth = Mathf.Max(10f, pickRect.x - textX - 6);
            GUI.Label(new Rect(textX, row.y + 3, textWidth, 18), new GUIContent(sound.Name, string.Join(", ", sound.Tags ?? new List<string>())), nameStyle);
            GUI.Label(new Rect(textX, row.y + 21, textWidth, 15), FormatTechnical(sound), metaStyle);
            GUI.Label(new Rect(textX, row.y + 36, textWidth, 15), FormatSocial(sound), metaStyle);

            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                if (e.clickCount == 2) Pick(sound);
                else Select(sound, autoPlay);
                e.Use();
            }
        }

        void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(searching || page <= 1))
                {
                    if (GUILayout.Button("< Prev", EditorStyles.toolbarButton, GUILayout.Width(60))) RunSearch(page - 1);
                }

                GUILayout.FlexibleSpace();
                string status = searching ? "Searching..." : hasSearched ? $"Page {page} of {PageCount}  ({totalCount:N0} results)" : string.Empty;
                GUILayout.Label(status, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(searching || page >= PageCount))
                {
                    if (GUILayout.Button("Next >", EditorStyles.toolbarButton, GUILayout.Width(60))) RunSearch(page + 1);
                }
            }
        }

        static string FormatTechnical(SoundResult s)
        {
            string channels = s.Channels == 1 ? "mono" : s.Channels == 2 ? "stereo" : s.Channels > 0 ? $"{s.Channels}ch" : "?ch";
            string rate = s.SampleRate > 0 ? $"{s.SampleRate / 1000f:0.#} kHz" : "? kHz";
            return $"{s.Duration:0.00}s  |  {(s.Type ?? "?").ToUpperInvariant()}  |  {rate}  |  {channels}  |  {FormatSize(s.FileSize)}";
        }

        static string FormatSocial(SoundResult s)
        {
            string rating = s.AvgRating > 0 ? $"rating {s.AvgRating:0.0}" : "unrated";
            return $"by {s.Username}  |  {LicenseUtil.ShortName(s.License)}  |  {s.NumDownloads:N0} downloads  |  {rating}";
        }

        static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "? KB";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:0} KB";
            return $"{bytes / (1024f * 1024f):0.0} MB";
        }

        // ---------------------------------------------------------------- Actions

        void ScheduleSearch(double delaySeconds)
        {
            searchAt = EditorApplication.timeSinceStartup + delaySeconds;
        }

        async void RunSearch(int targetPage)
        {
            int sequence = ++searchSequence;
            searching = true;
            error = null;
            Repaint();

            var request = new SearchQuery
            {
                Text = query,
                Page = Mathf.Max(1, targetPage),
                PageSize = FreesoundSettings.Instance.pageSize,
                Sort = sortOrder,
                MinDuration = minDuration,
                MaxDuration = maxDuration,
                License = licenseFilter
            };

            try
            {
                var response = await FreesoundClient.SearchAsync(request);
                if (this == null || sequence != searchSequence) return;

                ClearWaveforms();
                results = response.Results ?? new List<SoundResult>();
                totalCount = response.Count;
                page = request.Page;
                hasSearched = true;
                scroll = Vector2.zero;
                if (results.All(r => r.Id != selectedId)) selectedId = -1;
            }
            catch (Exception e)
            {
                if (this == null || sequence != searchSequence) return;
                error = e.Message;
            }
            finally
            {
                if (this != null && sequence == searchSequence)
                {
                    searching = false;
                    Repaint();
                }
            }
        }

        void Select(SoundResult sound, bool play = false)
        {
            selectedId = sound.Id;
            if (play) Preview(sound);
            Repaint();
        }

        void TogglePreview(SoundResult sound)
        {
            if (EditorAudioPlayer.PlayingId == sound.Id || EditorAudioPlayer.LoadingId == sound.Id)
            {
                EditorAudioPlayer.Stop();
                Repaint();
                return;
            }
            Preview(sound);
        }

        async void Preview(SoundResult sound)
        {
            try
            {
                await EditorAudioPlayer.PlayAsync(sound);
            }
            catch (Exception e)
            {
                if (this != null) error = "Preview failed: " + e.Message;
            }
            if (this != null) Repaint();
        }

        async void Pick(SoundResult sound)
        {
            if (installingId != -1) return;
            installingId = sound.Id;
            error = null;
            info = null;
            Repaint();

            try
            {
                var clip = await SoundInstaller.InstallAsync(sound, quality);
                EditorAudioPlayer.Stop();

                bool assigned = AssignToTarget(clip);
                if (!assigned)
                {
                    EditorGUIUtility.PingObject(clip);
                    info = $"Installed {AssetDatabase.GetAssetPath(clip)}";
                }

                if (assigned && FreesoundSettings.Instance.closeWindowAfterPick && this != null)
                {
                    installingId = -1;
                    Close();
                    return;
                }
                if (assigned) info = $"Assigned {clip.name} to {targetLabel}";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = "Install failed: " + e.Message;
            }
            finally
            {
                installingId = -1;
                if (this != null) Repaint();
            }
        }

        bool AssignToTarget(AudioClip clip)
        {
            if (!HasTarget) return false;

            var live = targets.Where(t => t != null).ToArray();
            var serializedObject = new SerializedObject(live);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                Debug.LogWarning($"[Freesound] Could not find field '{propertyPath}' on the target anymore; the clip was installed but not assigned.");
                return false;
            }

            property.objectReferenceValue = clip;
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        // ---------------------------------------------------------------- Waveforms

        async void RequestWaveform(SoundResult sound)
        {
            string url = sound.Images?.WaveformM;
            if (string.IsNullOrEmpty(url) || !waveformRequested.Add(sound.Id)) return;

            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url))
                {
                    await request.SendAsync();
                    if (request.result != UnityWebRequest.Result.Success) return;

                    var texture = DownloadHandlerTexture.GetContent(request);
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    if (this == null || !waveformRequested.Contains(sound.Id))
                    {
                        DestroyImmediate(texture);
                        return;
                    }
                    waveforms[sound.Id] = texture;
                    Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Freesound] Waveform download failed: " + e.Message);
            }
        }

        void ClearWaveforms()
        {
            foreach (var texture in waveforms.Values)
                if (texture != null) DestroyImmediate(texture);
            waveforms.Clear();
            waveformRequested.Clear();
        }
    }
}
