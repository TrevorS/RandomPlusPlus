using System;
using System.Collections.Generic;

namespace RandomPlus
{
    /// <summary>
    /// Detects a second loaded assembly that defines this mod's types, and reports
    /// it as one clear startup error instead of the confusing failures it causes.
    /// </summary>
    /// <remarks>
    /// Two installs produce that state: a stale RandomPlus.dll left next to
    /// RandomPlusPlus.dll by unzipping a new build over an old one (the assembly was
    /// renamed before the first release, and RimWorld loads every dll in an
    /// Assemblies folder), or the original RandomPlus mod enabled alongside this
    /// one, which shares the RandomPlus namespace.
    ///
    /// Nothing fails at the point of duplication. It fails later, obliquely: every
    /// Harmony patch applies twice, and Scribe resolves saved filter presets by type
    /// name across all loaded assemblies, so loading a preset can materialise the
    /// other assembly's PawnFilter and die in a cast the player has no way to
    /// interpret. This check exists so the log names the actual problem.
    /// </remarks>
    internal static class InstallGuard
    {
        internal static void WarnIfDuplicateTypesLoaded()
        {
            try
            {
                var self = typeof(PawnFilter).Assembly;
                string marker = typeof(PawnFilter).FullName;
                var duplicates = new List<string>();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == self)
                        continue;

                    try
                    {
                        if (assembly.GetType(marker, false) != null)
                            duplicates.Add(assembly.GetName().Name);
                    }
                    catch (Exception)
                    {
                        // An assembly that cannot even enumerate its types is some
                        // other mod's problem, not evidence of a duplicate install.
                    }
                }

                if (duplicates.Count == 0)
                    return;

                ModLog.Error(
                    $"Another loaded assembly also defines this mod's types: {string.Join(", ", duplicates)}. " +
                    "This applies every patch twice and breaks loading saved filter presets. " +
                    "If a stale RandomPlus.dll sits next to RandomPlusPlus.dll in this mod's Assemblies folders, " +
                    "delete the RandomPlusPlus folder from Mods and reinstall it from a fresh zip. " +
                    "If the original RandomPlus mod is enabled, disable it - this mod replaces it.");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Could not check for duplicate assemblies: {ex.Message}");
            }
        }
    }
}
