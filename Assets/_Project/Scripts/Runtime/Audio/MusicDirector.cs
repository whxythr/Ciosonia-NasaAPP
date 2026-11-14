using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpaceSim.Audio;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Networking;

namespace SpaceSim.Runtime
{
    /// <summary>Whether the player has supplied the audio file of a track.</summary>
    public enum MusicFileStatus { Missing, Found, Loading, Loaded, Failed }

    /// <summary>
    /// The soundtrack player: one persistent object (DontDestroyOnLoad, survives main menu ⇄ flight) with two AudioSources for an
    /// equal-power crossfade. What to play is decided by the pure <see cref="MusicSelector"/> (Core) from the vehicle position;
    /// this class only builds the context, loads files and fades.
    ///
    /// LEGAL / TECHNICAL LIMITATION: the tracks are copyrighted and published on SoundCloud only. The game does not bundle,
    /// download or stream them (Unity has no web view, and SoundCloud's API needs a registered client). The player supplies
    /// their own files: <c>&lt;persistentDataPath&gt;/Music/&lt;trackId&gt;.ogg|.mp3|.wav</c> (or StreamingAssets/Music). A missing
    /// track is silent; a notice names it and offers its official SoundCloud page in the system browser.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class MusicDirector : MonoBehaviour
    {
        public const string MusicFolderName = "Music";
        const float EvaluateInterval = 0.25f, ListenerCheckInterval = 1f, NoticeSeconds = 7f, MissingNoticeSeconds = 12f;
        const int MaxCachedClips = 3;

        static MusicDirector instance;

        readonly AudioSource[] sources = new AudioSource[2];
        readonly float[] levels = new float[2], fadeFrom = new float[2];
        readonly bool[] fadingIn = new bool[2], fadingOut = new bool[2];
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<string> clipOrder = new List<string>();
        readonly Dictionary<string, MusicFileStatus> loadStatus = new Dictionary<string, MusicFileStatus>();
        float fadeT = 1f;
        int current = -1;
        string playingTrack, requestedTrack, loadingTrack;
        bool hasRequested;

        SpaceSimBootstrap game;
        bool inMenu;
        PatchedConicSystem worldFor;
        MusicWorld world;
        float nextEvaluate, nextListenerCheck, lastEvaluateTime;
        AudioListener ownListener;

        string noticeTrack;
        float noticeUntil;

        /// <summary>The single instance (null before the first <see cref="Ensure"/> or outside Play mode).</summary>
        public static MusicDirector Instance => instance;

        /// <summary>The soundtrack table (tracks, rules, thresholds). Plain data; may be edited at runtime.</summary>
        public MusicLibrary Library { get; private set; } = MusicLibrary.CreateDefault();
        public MusicSelectorState Selection { get; private set; } = MusicSelectorState.Initial;
        public MusicWorld World => world;
        /// <summary>Track id currently audible (null = silence).</summary>
        public string PlayingTrack => playingTrack;
        /// <summary>Track id the selector wants (may be silent because its file is missing).</summary>
        public string RequestedTrack => requestedTrack;
        public bool InMenu => inMenu;

        /// <summary>The persistent director, created on first use. Null outside Play mode (edit-mode tests).</summary>
        public static MusicDirector Ensure()
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            var go = new GameObject("MusicDirector");
            return go.AddComponent<MusicDirector>();
        }

        /// <summary>The main-menu scene is up (called by <c>MainMenuController</c>).</summary>
        public void EnterMenu()
        {
            inMenu = true;
            game = null;
            nextEvaluate = 0f;
        }

        /// <summary>A flight scene is up (called by <see cref="SpaceSimBootstrap"/>).</summary>
        public void AttachGame(SpaceSimBootstrap bootstrap)
        {
            game = bootstrap;
            inMenu = false;
            nextEvaluate = 0f;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 2; i++)
            {
                AudioSource s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.volume = 0f;
                s.ignoreListenerPause = true;
                sources[i] = s;
            }
            try
            {
                Directory.CreateDirectory(UserMusicFolder); // so the player finds where to put files
            }
            catch (Exception)
            {
                // read-only or sandboxed storage: the settings screen still names the path
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ─────────────────────────────── files ───────────────────────────────

        /// <summary>Folder for the player's own copies of the tracks.</summary>
        public static string UserMusicFolder => Path.Combine(Application.persistentDataPath, MusicFolderName);
        public static string StreamingMusicFolder => Path.Combine(Application.streamingAssetsPath, MusicFolderName);

        /// <summary>First existing file for a track (user folder first, then StreamingAssets); null if none.</summary>
        public static string FindTrackFile(string trackId)
        {
            if (string.IsNullOrEmpty(trackId)) return null;
            foreach (string folder in new[] { UserMusicFolder, StreamingMusicFolder })
            {
                foreach (string name in MusicLibrary.ExpectedFileNames(trackId))
                {
                    try
                    {
                        string path = Path.Combine(folder, name);
                        if (File.Exists(path)) return path;
                    }
                    catch (Exception)
                    {
                        // invalid path on this platform
                    }
                }
            }
            return null;
        }

        public MusicFileStatus StatusOf(string trackId)
        {
            if (loadStatus.TryGetValue(trackId, out MusicFileStatus s) && s != MusicFileStatus.Missing) return s;
            return FindTrackFile(trackId) != null ? MusicFileStatus.Found : MusicFileStatus.Missing;
        }

        /// <summary>Forgets missing/failed results so newly added files are picked up (settings "rescan").</summary>
        public void Rescan()
        {
            var keys = new List<string>(loadStatus.Keys);
            foreach (string k in keys)
                if (loadStatus[k] == MusicFileStatus.Missing || loadStatus[k] == MusicFileStatus.Failed) loadStatus.Remove(k);
            if (requestedTrack != null && playingTrack != requestedTrack) StartTrack(requestedTrack, force: true);
        }

        static AudioType TypeOf(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp3" ? AudioType.MPEG : ext == ".wav" ? AudioType.WAV : AudioType.OGGVORBIS;
        }

        IEnumerator Load(string trackId, string path)
        {
            loadingTrack = trackId;
            loadStatus[trackId] = MusicFileStatus.Loading;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, TypeOf(path)))
            {
                if (req.downloadHandler is DownloadHandlerAudioClip handler) handler.compressed = true; // decompress on play: less memory
                yield return req.SendWebRequest();
                if (loadingTrack == trackId) loadingTrack = null;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    loadStatus[trackId] = MusicFileStatus.Failed;
                    Debug.LogWarning("Music: could not load " + path + ": " + req.error);
                    if (requestedTrack == trackId) FadeToSilence();
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                {
                    loadStatus[trackId] = MusicFileStatus.Failed;
                    if (requestedTrack == trackId) FadeToSilence();
                    yield break;
                }
                clip.name = trackId;
                Cache(trackId, clip);
                loadStatus[trackId] = MusicFileStatus.Loaded;
                if (requestedTrack == trackId && playingTrack != trackId) CrossfadeTo(trackId, clip);
            }
        }

        void Cache(string trackId, AudioClip clip)
        {
            clips[trackId] = clip;
            clipOrder.Remove(trackId);
            clipOrder.Add(trackId);
            for (int i = 0; clipOrder.Count > MaxCachedClips && i < clipOrder.Count;)
            {
                string old = clipOrder[i];
                if (old == playingTrack || old == requestedTrack || IsOnSource(clips[old]))
                {
                    i++;
                    continue;
                }
                Destroy(clips[old]);
                clips.Remove(old);
                clipOrder.RemoveAt(i);
                loadStatus.Remove(old);
            }
        }

        bool IsOnSource(AudioClip clip) => sources[0] != null && sources[0].clip == clip || sources[1] != null && sources[1].clip == clip;

        // ─────────────────────────────── selection ───────────────────────────────

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextListenerCheck)
            {
                nextListenerCheck = now + ListenerCheckInterval;
                EnsureListener();
            }
            if (now >= nextEvaluate)
            {
                nextEvaluate = now + EvaluateInterval;
                float dt = lastEvaluateTime > 0f ? now - lastEvaluateTime : 0f;
                lastEvaluateTime = now;
                Evaluate(dt);
            }
            UpdateFade(Time.unscaledDeltaTime);
        }

        void Evaluate(float realSeconds)
        {
            MusicContext ctx = BuildContext();
            if (ctx.InMainMenu && world == null) world = new MusicWorld();
            if (world == null) return;
            Selection = MusicSelector.Step(Library, world, ctx, Selection, realSeconds);
            if (Selection.Started && (!hasRequested || Selection.TrackId != requestedTrack)) StartTrack(Selection.TrackId, force: false);
        }

        MusicContext BuildContext()
        {
            if (inMenu) return MusicContext.Menu();
            if (game == null) return MusicContext.Unknown();
            FlightBody body = game.Body;
            FreeFlightSimulation sim = body != null ? body.Simulation : null;
            PatchedConicSystem sys = sim != null ? sim.Navigation : null;
            if (sys == null) return MusicContext.Unknown();
            if (!ReferenceEquals(sys, worldFor))
            {
                worldFor = sys;
                world = MusicWorld.FromSystem(sys, Library);
                foreach (string note in world.Notes) Debug.Log("Music: " + note);
            }
            double t = sim.State.Time;
            Vector3d v = sim.State.Position + sys.Position(sim.FrameBody, t);
            var d = new double[sys.Count];
            for (int i = 0; i < sys.Count; i++) d[i] = (v - sys.Position(i, t)).Magnitude;
            return new MusicContext { HasPosition = true, HeliocentricDistance = v.Magnitude, BodyDistances = d };
        }

        /// <summary>Makes sure exactly one AudioListener hears the music (flight cameras are created without one).</summary>
        void EnsureListener()
        {
            AudioListener[] all = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            bool other = false;
            foreach (AudioListener l in all)
                if (l != ownListener && l.enabled && l.gameObject.activeInHierarchy) other = true;
            if (other)
            {
                if (ownListener != null) ownListener.enabled = false;
                return;
            }
            if (ownListener == null) ownListener = gameObject.AddComponent<AudioListener>();
            ownListener.enabled = true;
        }

        // ─────────────────────────────── playback ───────────────────────────────

        /// <summary>Plays a track (null = silence). Same id as now → nothing restarts.</summary>
        public void StartTrack(string trackId, bool force)
        {
            if (!force && hasRequested && trackId == requestedTrack) return;
            hasRequested = true;
            bool changed = trackId != requestedTrack;
            requestedTrack = trackId;
            if (changed && trackId != null) ShowNotice(trackId);
            if (trackId == null)
            {
                FadeToSilence();
                return;
            }
            if (trackId == playingTrack) return;
            if (clips.TryGetValue(trackId, out AudioClip cached) && cached != null)
            {
                CrossfadeTo(trackId, cached);
                return;
            }
            string path = FindTrackFile(trackId);
            if (path == null)
            {
                loadStatus[trackId] = MusicFileStatus.Missing;
                FadeToSilence(); // silent fallback; the notice offers SoundCloud
                return;
            }
            // The current track keeps playing until the new file is loaded, then they crossfade.
            if (loadingTrack != trackId) StartCoroutine(Load(trackId, path));
        }

        void CrossfadeTo(string trackId, AudioClip clip)
        {
            int next = current < 0 ? 0 : 1 - current;
            // A source still fading out from an earlier change is cut now (it is the quieter one).
            sources[next].Stop();
            sources[next].clip = clip;
            sources[next].time = 0f;
            sources[next].Play();
            levels[next] = 0f;
            fadingIn[next] = true;
            fadingOut[next] = false;
            if (current >= 0)
            {
                fadingOut[current] = true;
                fadingIn[current] = false;
                fadeFrom[current] = levels[current];
            }
            current = next;
            playingTrack = trackId;
            fadeT = 0f;
        }

        void FadeToSilence()
        {
            playingTrack = null;
            if (current < 0) return;
            for (int i = 0; i < 2; i++)
            {
                if (!sources[i].isPlaying) continue;
                fadingOut[i] = true;
                fadingIn[i] = false;
                fadeFrom[i] = levels[i];
            }
            fadeT = 0f;
        }

        void UpdateFade(float dt)
        {
            float duration = Mathf.Max(0.05f, GameSettings.MusicCrossfade);
            if (fadeT < 1f) fadeT = Mathf.Min(1f, fadeT + dt / duration);
            MusicCrossfade.Gains(fadeT, out double outGain, out double inGain);
            float master = GameSettings.EffectiveMusicVolume;
            for (int i = 0; i < 2; i++)
            {
                AudioSource s = sources[i];
                if (s == null) continue;
                if (fadingIn[i]) levels[i] = (float)inGain;
                else if (fadingOut[i]) levels[i] = fadeFrom[i] * (float)outGain;
                if (fadeT >= 1f)
                {
                    if (fadingOut[i])
                    {
                        s.Stop();
                        s.clip = null;
                        levels[i] = 0f;
                    }
                    fadingIn[i] = fadingOut[i] = false;
                }
                s.volume = master * levels[i];
            }
        }

        // ─────────────────────────────── "now playing" notice ───────────────────────────────

        void ShowNotice(string trackId)
        {
            noticeTrack = trackId;
            bool missing = FindTrackFile(trackId) == null && !clips.ContainsKey(trackId);
            noticeUntil = Time.unscaledTime + (missing ? MissingNoticeSeconds : NoticeSeconds);
        }

        /// <summary>Opens a track's official page in the system browser (the only way the game reaches SoundCloud).</summary>
        public static void OpenOnSoundCloud(MusicTrack track)
        {
            if (track != null && !string.IsNullOrEmpty(track.SoundCloudUrl) && track.SoundCloudUrl.StartsWith("https://soundcloud.com/", StringComparison.Ordinal))
                Application.OpenURL(track.SoundCloudUrl);
        }

        void OnGUI()
        {
            if (noticeTrack == null || Time.unscaledTime > noticeUntil || GameSettings.MusicMuted) return;
            MusicTrack track = Library.Track(noticeTrack);
            if (track == null) return;
            bool missing = StatusOf(track.Id) == MusicFileStatus.Missing || StatusOf(track.Id) == MusicFileStatus.Failed;
            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            float w = 420f, h = missing ? 92f : 46f;
            var r = new Rect((screen.x - w) * 0.5f, 12f, w, h);
            HudStyle.Card(r);
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, w - 24f, 18f), Loc.T("СЕЙЧАС ИГРАЕТ") + (missing ? Loc.T(" · файл не найден") : ""), HudStyle.Small);
            GUI.Label(new Rect(r.x + 12f, r.y + 22f, w - 24f, 20f), track.DisplayName, HudStyle.Label);
            if (!missing) return;
            GUI.Label(new Rect(r.x + 12f, r.y + 44f, w - 24f, 18f), Loc.T("Положите файл ") + MusicFolderName + "/" + track.Id + ".ogg / .mp3 / .wav", HudStyle.Small);
            if (GUI.Button(new Rect(r.x + 12f, r.y + 64f, 190f, 22f), Loc.T("Открыть в SoundCloud"), HudStyle.Button)) OpenOnSoundCloud(track);
            if (GUI.Button(new Rect(r.x + w - 102f, r.y + 64f, 90f, 22f), Loc.T("Скрыть"), HudStyle.Button)) noticeUntil = 0f;
        }
    }
}
