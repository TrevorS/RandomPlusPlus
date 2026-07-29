using Verse;
using System;
using System.Collections.Generic;
using System.IO;

namespace RandomPlus
{
    public static class SaveLoader
    {
        public static readonly string filename = "RandomPlus.xml";

        public static string GetFilePath()
        {
            string configFolder = Path.GetDirectoryName(GenFilePaths.ModsConfigFilePath);
            return Path.Combine(configFolder, filename);
        }

        public static void SaveOverwrite(int index, PawnFilter pawnFilter)
        {
            if (index >= 0)
                PawnRandomizer.pawnFilterList[index] = pawnFilter;

            SaveAll();
        }

        public static void Save(PawnFilter pawnFilter)
        {
            if (!PawnRandomizer.pawnFilterList.Contains(pawnFilter))
                PawnRandomizer.pawnFilterList.Add(pawnFilter);

            SaveAll();
        }

        public static void SaveAll()
        {

            try
            {
                Scribe.saver.InitSaving(GetFilePath(), "RandomPlus");
                Scribe_Collections.Look<PawnFilter>(ref PawnRandomizer.pawnFilterList, "list", LookMode.Deep, null);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to save filters to {GetFilePath()}: {ex}");
                throw;
            }
            finally
            {
                Scribe.saver.FinalizeSaving();
                Scribe.mode = LoadSaveMode.Inactive;
            }
        }

        public static void Load(PawnFilter pawnFilter)
        {
            PawnRandomizer.PawnFilter = pawnFilter;
        }

        /// <summary>
        /// How many saved filters the most recent LoadAll could not materialise.
        /// The dialog reads this to tell the player, because the list silently
        /// looking shorter is indistinguishable from the presets being gone.
        /// </summary>
        public static int LastLoadDropped { get; private set; }

        public static void LoadAll()
        {
            LastLoadDropped = 0;

            string filePath = GetFilePath();
            if (!File.Exists(filePath))
                return;

            try
            {
                Scribe.loader.InitLoading(GetFilePath());
                Scribe_Collections.Look<PawnFilter>(ref PawnRandomizer.pawnFilterList, "list");
            }
            catch (Exception ex)
            {
                // Set the file aside rather than deleting it. A load failure used to
                // destroy every saved filter the user had, with no way back.
                ModLog.Error($"Failed to load filters from {filePath}: {ex}");
                TrySetAside(filePath);
            }
            finally
            {
                Scribe.loader.FinalizeLoading();
                Scribe.mode = LoadSaveMode.Inactive;
            }

            // A file whose list node is missing loads as null, not as empty.
            if (PawnRandomizer.pawnFilterList == null)
            {
                PawnRandomizer.pawnFilterList = new List<PawnFilter>();
                return;
            }

            // Scribe does not throw for an entry it cannot deserialize - it logs and
            // yields null (see SaveableFromNode), so none of this reaches the catch
            // above. Left in the list, each null crashes the dialog on every frame it
            // tries to draw; saved back out, it erases the preset it stood for. Drop
            // them from memory, but first copy the file aside so the presets survive
            // until whatever broke loading - usually a duplicate-assembly install,
            // see InstallGuard - is fixed.
            LastLoadDropped = PawnRandomizer.pawnFilterList.RemoveAll(filter => filter == null);
            if (LastLoadDropped > 0)
            {
                TryBackup(filePath);
                ModLog.Error(
                    $"{LastLoadDropped} saved filter(s) in {filePath} could not be read and were skipped. " +
                    "The error above this one names the cause. The file has been copied to " +
                    $"{filePath}.backup so the presets are not lost if a save overwrites it.");
            }
        }

        private static void TryBackup(string filePath)
        {
            try
            {
                // Never overwrite an existing backup: on the second broken start the
                // live file may already have been rewritten without the unreadable
                // presets, and copying it over the backup would lose them for good.
                string backup = filePath + ".backup";
                if (!File.Exists(backup))
                    File.Copy(filePath, backup);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Could not back up the filter file: {ex.Message}");
            }
        }

        private static void TrySetAside(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                string spoiled = filePath + ".corrupt";
                if (File.Exists(spoiled))
                    File.Delete(spoiled);

                File.Move(filePath, spoiled);
                ModLog.Warning($"Moved the unreadable filter file to {spoiled}.");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Could not set aside the unreadable filter file: {ex.Message}");
            }
        }

        public static void Delete(PawnFilter pawnFilter)
        {
            PawnRandomizer.pawnFilterList.Remove(pawnFilter);
            SaveAll();
        }
    }
}
