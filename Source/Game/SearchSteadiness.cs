using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    /// <summary>
    /// Keeps the rest of the character creation page steady while a search runs.
    /// </summary>
    /// <remarks>
    /// The search leaves whatever candidate it last tested on the live pawn, so
    /// every widget that reads that pawn - its tile in the pawn list, the team
    /// skills summary - redraws dozens of times a second with different random
    /// data. The portrait area is already replaced outright (see
    /// <see cref="Patch_SearchOverlay"/>); this file calms the two surfaces that
    /// still read the churning pawn, without touching what the search generates.
    ///
    /// The approach is sample-and-hold, display-side only. Nothing here writes to
    /// a pawn: the name and title getters lie only while vanilla is drawing the
    /// pawn list, the portrait is simply not re-rendered until the search ends,
    /// and the team skill summary picks its best pawn from the pawns that are not
    /// churning. Generation code always sees the real data, so the search still
    /// produces exactly the pawns it always did.
    /// </remarks>
    internal static class SearchSample
    {
        /// <summary>Slot machines read at about this pace; candidate churn is far
        /// faster.</summary>
        internal const float PeriodSeconds = 0.12f;

        private static float nextSampleAt;
        private static Pawn lastPawn;

        internal static Name SampledName { get; private set; }
        internal static string SampledLabelShort { get; private set; }
        internal static string SampledTitleCap { get; private set; }
        internal static string SampledTitleShortCap { get; private set; }

        /// <summary>What the search overlay shows: "name, age" of the latest
        /// sampled candidate.</summary>
        internal static string SampledPortraitLine { get; private set; } = "";

        /// <summary>
        /// Call after every pump. While the search runs this refreshes the samples
        /// at a readable pace; once it is over it re-renders the held portrait and
        /// clears. Idle calls are a cheap no-op, so callers need not track state.
        /// </summary>
        internal static void AfterPump()
        {
            if (PawnRandomizer.SearchInProgress)
                Refresh();
            else
                SearchEnded();
        }

        private static void Refresh()
        {
            var pawn = PawnRandomizer.SearchingPawn;
            if (pawn == null)
                return;

            lastPawn = pawn;
            if (SampledName != null && Time.realtimeSinceStartup < nextSampleAt)
                return;

            nextSampleAt = Time.realtimeSinceStartup + PeriodSeconds;

            // Candidates replace the name object rather than mutating it, so holding
            // a reference holds a stable value.
            SampledName = pawn.Name;
            SampledLabelShort = pawn.LabelShort;
            SampledTitleCap = pawn.story?.TitleCap;
            SampledTitleShortCap = pawn.story?.TitleShortCap;

            string shortName = pawn.Name?.ToStringShort;
            SampledPortraitLine = string.IsNullOrEmpty(shortName)
                ? ""
                : $"{shortName}, {pawn.ageTracker.AgeBiologicalYears}";
        }

        private static void SearchEnded()
        {
            SampledName = null;
            SampledLabelShort = null;
            SampledTitleCap = null;
            SampledTitleShortCap = null;
            SampledPortraitLine = "";
            nextSampleAt = 0f;

            if (lastPawn != null)
            {
                // The one re-render the search withheld: show the kept pawn.
                PortraitsCache.SetDirty(lastPawn);
                lastPawn = null;
            }
        }
    }

    /// <summary>
    /// Marks when vanilla is drawing the pawn list, which is the only scope in
    /// which the name and title getters below are allowed to lie.
    /// </summary>
    /// <remarks>
    /// The scope is what makes the substitution safe. An unscoped patch on
    /// Pawn.Name would feed sampled names to generation itself - the bio and name
    /// generator reads existing pawns' names to avoid collisions - and change
    /// which names the search can produce. Inside DrawPawnList nothing generates;
    /// it only draws.
    /// </remarks>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "DrawPawnList")]
    static class Patch_CalmPawnListWhileSearching
    {
        internal static bool InPawnList;

        [HarmonyPrefix]
        static void Prefix()
        {
            InPawnList = PawnRandomizer.SearchInProgress;
        }

        // A finalizer rather than a postfix: the flag has to clear even when
        // DrawPawnList throws, or the getters would keep lying page-wide.
        [HarmonyFinalizer]
        static void Finalizer()
        {
            InPawnList = false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Name), MethodType.Getter)]
    static class Patch_SteadyNameInPawnList
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn __instance, ref Name __result)
        {
            if (!Patch_CalmPawnListWhileSearching.InPawnList
                || SearchSample.SampledName == null
                || __instance != PawnRandomizer.SearchingPawn)
                return true;

            __result = SearchSample.SampledName;
            return false;
        }
    }

    // LabelShort as well as Name: the tile may read either, and LabelShort reads
    // the pawn's name field directly rather than through the patched getter.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.LabelShort), MethodType.Getter)]
    static class Patch_SteadyLabelInPawnList
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn __instance, ref string __result)
        {
            if (!Patch_CalmPawnListWhileSearching.InPawnList
                || SearchSample.SampledLabelShort == null
                || __instance != PawnRandomizer.SearchingPawn)
                return true;

            __result = SearchSample.SampledLabelShort;
            return false;
        }
    }

    // ___pawn is Harmony field injection: the tracker's backing pawn field is
    // private, and this is the sanctioned way to read it from a prefix.
    [HarmonyPatch(typeof(Pawn_StoryTracker), "TitleCap", MethodType.Getter)]
    static class Patch_SteadyTitleInPawnList
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn ___pawn, ref string __result)
        {
            if (!Patch_CalmPawnListWhileSearching.InPawnList
                || SearchSample.SampledTitleCap == null
                || ___pawn != PawnRandomizer.SearchingPawn)
                return true;

            __result = SearchSample.SampledTitleCap;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_StoryTracker), "TitleShortCap", MethodType.Getter)]
    static class Patch_SteadyShortTitleInPawnList
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn ___pawn, ref string __result)
        {
            if (!Patch_CalmPawnListWhileSearching.InPawnList
                || SearchSample.SampledTitleShortCap == null
                || ___pawn != PawnRandomizer.SearchingPawn)
                return true;

            __result = SearchSample.SampledTitleShortCap;
            return false;
        }
    }

    /// <summary>
    /// Holds the searching pawn's cached portrait instead of re-rendering it for
    /// every candidate.
    /// </summary>
    /// <remarks>
    /// Candidate generation dirties the portrait several times a second, and each
    /// dirtying costs a re-render on top of the visible flicker. Suppressing it
    /// keeps the pre-search image in the pawn list tile until the search settles;
    /// <see cref="SearchSample.AfterPump"/> issues the one deferred SetDirty when
    /// it does. Only helps the in-place path, where the pawn object is stable -
    /// the whole-pawn path creates a new pawn per candidate, and a new pawn's
    /// portrait renders on first use regardless.
    /// </remarks>
    [HarmonyPatch(typeof(PortraitsCache), "SetDirty")]
    static class Patch_HoldPortraitWhileSearching
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn pawn)
        {
            return !PawnRandomizer.SearchInProgress || pawn != PawnRandomizer.SearchingPawn;
        }
    }

    /// <summary>
    /// Keeps the team skills summary steady by picking each skill's best pawn from
    /// the pawns that are not being rerolled.
    /// </summary>
    /// <remarks>
    /// Same selection vanilla makes - highest level among the selected pawns,
    /// passion breaking ties, a disabled incumbent always replaced - minus the
    /// churning pawn. The summary stays live and truthful for everyone else and
    /// picks the searching pawn back up on the frame the search ends. For a lone
    /// selected pawn there is nothing stable to show, so vanilla runs unchanged.
    /// </remarks>
    [HarmonyPatch(typeof(StartingPawnUtility), "FindBestSkillOwner")]
    static class Patch_SteadyTeamSkillsWhileSearching
    {
        [HarmonyPrefix]
        static bool Prefix(SkillDef skill, ref Pawn __result)
        {
            if (!PawnRandomizer.SearchInProgress)
                return true;

            var pawns = Find.GameInitData.startingAndOptionalPawns;
            int count = Mathf.Min(Find.GameInitData.startingPawnCount, pawns.Count);
            var searching = PawnRandomizer.SearchingPawn;

            Pawn best = null;
            SkillRecord bestRecord = null;
            for (int i = 0; i < count; i++)
            {
                var pawn = pawns[i];
                if (pawn == searching || pawn?.skills == null)
                    continue;

                var record = pawn.skills.GetSkill(skill);
                if (best == null
                    || bestRecord.TotallyDisabled
                    || record.Level > bestRecord.Level
                    || (record.Level == bestRecord.Level && record.passion > bestRecord.passion))
                {
                    best = pawn;
                    bestRecord = record;
                }
            }

            if (best == null)
                return true;

            __result = best;
            return false;
        }
    }
}
