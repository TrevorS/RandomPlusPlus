using Verse;
using System;
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

        public static void LoadAll()
        {
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
