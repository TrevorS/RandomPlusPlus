using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RandomPlus
{
    /// <summary>
    /// Decides how much of RimWorld's pawn generation the search is allowed to take
    /// shortcuts around, by asking Harmony what other mods have patched.
    /// </summary>
    /// <remarks>
    /// Two different shortcuts, with two different safety questions.
    ///
    /// Regenerating a pawn's parts in place skips the outer generation methods
    /// entirely, so any prefix or postfix another mod put on them never runs, and the
    /// generation steps we do not call never happen. That is only safe if nobody else
    /// has hooked those entry points.
    ///
    /// Suppressing gear generation for candidate pawns still runs every outer method,
    /// in order - it only skips work no filter reads. That is safe unless another mod
    /// has hooked gear generation itself and expects to see every pawn.
    ///
    /// Asking Harmony rather than checking for a known mod by name means this covers
    /// mods that do not exist yet, and stops penalising ones that are actually
    /// harmless.
    /// </remarks>
    public static class GenerationCompatibility
    {
        /// <summary>
        /// Identifies this mod's Harmony patches. It must differ from the original
        /// RandomPlus, which uses "RandomPlus": patch ownership would collide, and
        /// <see cref="AnyForeignPatch"/> excludes patches owned by this id, so it would
        /// mistake the original's patches for its own and take an unsafe shortcut.
        /// </summary>
        public const string HarmonyId = "trevors.randomplusplus";

        /// <summary>Called once at startup, after our own patches are applied.</summary>
        public static void Install()
        {
            PawnRandomizer.ForeignPatchesOnGenerationEntryPoints = AnyForeignPatchOnEntryPoints;
            PawnRandomizer.GearSuppressionIsSafe = GearGenerationIsUnpatched;
        }

        // The methods the in-place path bypasses. A patch on any of them would not run.
        private static IEnumerable<MethodBase> GenerationEntryPoints()
        {
            foreach (var m in MethodsNamed(typeof(PawnGenerator), "GeneratePawn")) yield return m;
            foreach (var m in MethodsNamed(typeof(PawnGenerator), "GenerateNewPawnInternal")) yield return m;
            foreach (var m in MethodsNamed(typeof(StartingPawnUtility), "NewGeneratedStartingPawn")) yield return m;
        }

        // Gear generation, and the generators it delegates to.
        private static IEnumerable<MethodBase> GearGenerationMethods()
        {
            foreach (var m in MethodsNamed(typeof(PawnGenerator), "GenerateGearFor")) yield return m;
            foreach (var m in MethodsNamed(AccessTools.TypeByName("RimWorld.PawnApparelGenerator"), "GenerateStartingApparelFor")) yield return m;
            foreach (var m in MethodsNamed(AccessTools.TypeByName("RimWorld.PawnWeaponGenerator"), "TryGenerateWeaponFor")) yield return m;
            foreach (var m in MethodsNamed(AccessTools.TypeByName("RimWorld.PawnInventoryGenerator"), "GenerateInventoryFor")) yield return m;
        }

        private static IEnumerable<MethodBase> MethodsNamed(Type type, string name)
        {
            if (type == null) yield break;

            var methods = type.GetMethods(AccessTools.all);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name)
                    yield return methods[i];
            }
        }

        private static bool AnyForeignPatchOnEntryPoints() => AnyForeignPatch(GenerationEntryPoints());

        private static bool GearGenerationIsUnpatched() => !AnyForeignPatch(GearGenerationMethods());

        /// <summary>
        /// Whether any Harmony patch by someone other than us is installed on the given
        /// methods. Errs towards "yes" - if the question cannot be answered, assume the
        /// shortcut is unsafe.
        /// </summary>
        private static bool AnyForeignPatch(IEnumerable<MethodBase> methods)
        {
            try
            {
                foreach (var method in methods)
                {
                    var patches = Harmony.GetPatchInfo(method);
                    if (patches == null)
                        continue;

                    foreach (var owner in patches.Owners)
                    {
                        if (owner != HarmonyId)
                            return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Could not inspect Harmony patches, taking the safe path: {ex.Message}");
                return true;
            }
        }
    }

    /// <summary>
    /// Skips generating apparel, weapons and inventory for pawns the search is only
    /// going to test and throw away.
    /// </summary>
    /// <remarks>
    /// No filter reads any of it, and it is among the most expensive parts of
    /// generating a pawn. Every other generation step still runs, in its normal order,
    /// so this does not change which pawns the search can produce - only what a
    /// discarded one costs. The pawn that is kept has its gear generated before it is
    /// handed back.
    ///
    /// The prefix is always installed but does nothing unless PawnRandomizer turned
    /// suppression on for the current search, which it only does when nothing else has
    /// patched gear generation.
    /// </remarks>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateGearFor")]
    static class Patch_SuppressGearWhileSearching
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            return !PawnRandomizer.SuppressGearGeneration;
        }
    }
}
