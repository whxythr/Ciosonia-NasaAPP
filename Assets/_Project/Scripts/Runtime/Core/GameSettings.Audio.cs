using System;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Music settings (volume, mute, crossfade length); persisted with the other player settings.</summary>
    public static partial class GameSettings
    {
        public const float DefaultMusicVolume = 0.7f;
        public const float MinMusicCrossfade = 0.5f, MaxMusicCrossfade = 10f, DefaultMusicCrossfade = 3f;

        public const string MusicVolumeKey = "SpaceSim.Settings.MusicVolume";
        public const string MusicMutedKey = "SpaceSim.Settings.MusicMuted";
        public const string MusicCrossfadeKey = "SpaceSim.Settings.MusicCrossfade";

        static float musicVolume = DefaultMusicVolume, musicCrossfade = DefaultMusicCrossfade;
        static bool musicMuted;

        /// <summary>Master music volume, 0…1.</summary>
        public static float MusicVolume
        {
            get
            {
                EnsureLoaded();
                return musicVolume;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp01(value);
                if (Mathf.Abs(v - musicVolume) < 1e-4f) return;
                musicVolume = v;
                Store.Set(MusicVolumeKey, Format(v));
                Commit();
            }
        }

        public static bool MusicMuted
        {
            get
            {
                EnsureLoaded();
                return musicMuted;
            }
            set
            {
                EnsureLoaded();
                if (value == musicMuted) return;
                musicMuted = value;
                Store.Set(MusicMutedKey, value ? "1" : "0");
                Commit();
            }
        }

        /// <summary>Length of the crossfade between two tracks [s].</summary>
        public static float MusicCrossfade
        {
            get
            {
                EnsureLoaded();
                return musicCrossfade;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, MinMusicCrossfade, MaxMusicCrossfade);
                if (Mathf.Abs(v - musicCrossfade) < 1e-4f) return;
                musicCrossfade = v;
                Store.Set(MusicCrossfadeKey, Format(v));
                Commit();
            }
        }

        /// <summary>Volume the music should play at now (0 when muted).</summary>
        public static float EffectiveMusicVolume => MusicMuted ? 0f : MusicVolume;

        public static void ResetAudio()
        {
            EnsureLoaded();
            musicVolume = DefaultMusicVolume;
            musicMuted = false;
            musicCrossfade = DefaultMusicCrossfade;
            WriteAudio();
            Commit();
        }

        static void LoadAudio(ISettingsStore s)
        {
            float v = ReadFloat(s, MusicVolumeKey, DefaultMusicVolume);
            musicVolume = Mathf.Clamp01(v);
            musicMuted = s.TryGet(MusicMutedKey, out string m) && m == "1";
            musicCrossfade = Mathf.Clamp(ReadFloat(s, MusicCrossfadeKey, DefaultMusicCrossfade), MinMusicCrossfade, MaxMusicCrossfade);
        }

        static void WriteAudio()
        {
            Store.Set(MusicVolumeKey, Format(musicVolume));
            Store.Set(MusicMutedKey, musicMuted ? "1" : "0");
            Store.Set(MusicCrossfadeKey, Format(musicCrossfade));
        }

        static void CaptureAudio(Snapshot s)
        {
            s.MusicVolume = musicVolume;
            s.MusicMuted = musicMuted;
            s.MusicCrossfade = musicCrossfade;
        }

        static void RestoreAudio(Snapshot s)
        {
            musicVolume = s.MusicVolume;
            musicMuted = s.MusicMuted;
            musicCrossfade = s.MusicCrossfade;
            WriteAudio();
        }
    }
}
