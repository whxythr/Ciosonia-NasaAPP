using System.Globalization;
using SpaceSim.Audio;
using SpaceSim.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// «Звук» tab: music volume, mute, crossfade length, and every soundtrack entry with the file the game expects for it, whether
    /// it was found, and a button to its official SoundCloud page (the game ships no copyrighted audio — see <see cref="MusicDirector"/>).
    /// </summary>
    public sealed partial class SettingsView
    {
        SettingSlider musicVolume, musicFade;
        VisualElement muteSwitch;

        void BuildAudio(VisualElement rows)
        {
            headingTag.text = Loc.T("МУЗЫКА");
            var vol = SettingRow(rows, Loc.T("Громкость музыки"), null);
            musicVolume = new SettingSlider(0f, 1f, 0.05f, GameSettings.MusicVolume,
                v => Mathf.RoundToInt(v * 100f) + " %", v => GameSettings.MusicVolume = v);
            vol.Add(musicVolume);
            SliderKeys(vol, musicVolume);

            var mute = SettingRow(rows, Loc.T("Выключить музыку"), null);
            muteSwitch = Switch(mute, GameSettings.MusicMuted, () => GameSettings.MusicMuted = !GameSettings.MusicMuted);

            var fade = SettingRow(rows, Loc.T("Плавный переход"), Loc.T("Длительность смены трека при перелёте между зонами"));
            musicFade = new SettingSlider(GameSettings.MinMusicCrossfade, GameSettings.MaxMusicCrossfade, 0.5f, GameSettings.MusicCrossfade,
                v => v.ToString("0.0", CultureInfo.InvariantCulture) + Loc.T(" с"), v => GameSettings.MusicCrossfade = v);
            fade.Add(musicFade);
            SliderKeys(fade, musicFade);

            var folder = SettingRow(rows, Loc.T("Папка с музыкой"), MusicDirector.UserMusicFolder);
            folder.Add(ButtonEl(Loc.T("ОТКРЫТЬ"), () => Application.OpenURL("file:///" + MusicDirector.UserMusicFolder.Replace('\\', '/'))));
            folder.Add(ButtonEl(Loc.T("ОБНОВИТЬ"), () =>
            {
                MusicDirector.Instance?.Rescan();
                BuildPage();
            }));

            rows.Add(Text(Loc.T("ТРЕКИ"), "bind-group", "t-mono"));
            MusicLibrary library = MusicDirector.Instance != null ? MusicDirector.Instance.Library : MusicLibrary.CreateDefault();
            foreach (MusicTrack track in library.Tracks)
            {
                string file = MusicDirector.FindTrackFile(track.Id);
                string state = file != null ? Loc.T("найден: ") + System.IO.Path.GetFileName(file) : Loc.T("нет файла — ожидается ") + track.Id + ".ogg / .mp3 / .wav";
                var row = SettingRow(rows, track.DisplayName, state + "  ·  " + Where(library, track.Id));
                MusicTrack t = track;
                row.Add(ButtonEl("SOUNDCLOUD", () => MusicDirector.OpenOnSoundCloud(t)));
            }
        }

        /// <summary>Where a track plays, from the rule table (ids are data, so this text is too).</summary>
        static string Where(MusicLibrary library, string trackId)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (MusicRule r in library.Rules)
                if (r.TrackId == trackId) parts.Add(Loc.T(string.IsNullOrEmpty(r.Label) ? r.Id : r.Label));
            if (trackId == library.UnassignedSoiTrackId || string.IsNullOrEmpty(library.UnassignedSoiTrackId) && trackId == library.RuleOf(MusicZoneKind.DeepSpace)?.TrackId)
                parts.Add(Loc.T("тела без своей музыки"));
            return string.Join(", ", parts);
        }

        void RefreshAudio()
        {
            if (page != Page.Audio) return;
            musicVolume?.SetValueWithoutNotify(GameSettings.MusicVolume);
            musicFade?.SetValueWithoutNotify(GameSettings.MusicCrossfade);
            muteSwitch?.EnableInClassList("switch--on", GameSettings.MusicMuted);
        }

        void AudioInfo()
        {
            infoEyebrow.text = Loc.T("МУЗЫКА");
            infoTitle.style.display = DisplayStyle.Flex;
            MusicDirector d = MusicDirector.Instance;
            MusicTrack now = d != null ? d.Library.Track(d.RequestedTrack) : null;
            infoTitle.text = now != null ? now.DisplayName : Loc.T("Тишина");
            infoText.text = Loc.T("Музыка зависит от того, где вы: у какой планеты или спутника, у звезды или в открытом космосе. ") +
                            Loc.T("Треки защищены авторским правом и в игру не входят: положите свои копии (.ogg, .mp3 или .wav) в папку с музыкой под именами из списка и нажмите «Обновить». ") +
                            Loc.T("Без файла трек не звучит — кнопка SoundCloud открывает его официальную страницу в браузере.");
        }
    }
}
