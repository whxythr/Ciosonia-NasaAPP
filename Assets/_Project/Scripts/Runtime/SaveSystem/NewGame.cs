using System;
using SpaceSim.Economy;
using SpaceSim.Localization;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Starting a new game from the main menu: the only place a game mode is chosen. The live profile (autosave) is first copied
    /// into a manual save slot, so starting over never destroys a game (a Career cannot be lost — or switched to Sandbox — by
    /// accident: the old save keeps its own mode and can be loaded back from «Сохранения»). Then a fresh profile in the chosen mode
    /// becomes the live profile and the flight scene starts from it.
    /// </summary>
    public static class NewGame
    {
        /// <summary>
        /// Writes a new profile in <paramref name="mode"/> as the live profile. Returns the name of the slot the previous live profile
        /// was kept in ("" when there was none or it could not be read).
        /// </summary>
        public static string Start(GameMode mode, string homeBodyId = SpaceSim.Planets.HomeSystemCatalog.HomeWorldId)
        {
            string backup = "";
            PlayerProfile previous = PlayerSession.ReadProfileFile();
            if (previous != null)
            {
                try
                {
                    backup = Loc.T("До новой игры") + " · " + GameModeRules.Name(previous.gameMode);
                    SaveSlots.Save(previous, backup, "", null);
                }
                catch (Exception e)
                {
                    // Never start over without a copy of the old game.
                    Debug.LogWarning("[SpaceSim] New game cancelled: the current profile could not be backed up (" + e.Message + ").");
                    throw;
                }
            }
            else if (System.IO.File.Exists(PlayerSession.SavePath))
            {
                // Unreadable here (damaged, or from a newer build): keep the raw file next to it instead of a slot.
                string copy = PlayerSession.SavePath + ".before-new-game-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                System.IO.File.Copy(PlayerSession.SavePath, copy, true);
                backup = System.IO.Path.GetFileName(copy);
            }
            PlayerSession.WriteProfileFile(PlayerProfile.CreateNew(homeBodyId, mode));
            return backup;
        }
    }
}
