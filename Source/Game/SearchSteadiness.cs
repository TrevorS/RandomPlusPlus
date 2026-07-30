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
    /// a pawn: the name, title and skill getters lie only while the Create
    /// characters page is drawing its contents, and the portrait is simply not
    /// re-rendered until the search ends. All of it reads the same 0.12s samples,
    /// so the whole page ticks together at one readable pace. Generation code
    /// always sees the real data, so the search still produces exactly the pawns
    /// it always did.
    /// </remarks>
    internal static class SearchSample
    {
        /// <summary>Slot machines read at about this pace; candidate churn is far
        /// faster.</summary>
        internal const float PeriodSeconds = 0.12f;

        private static float nextSampleAt;
        private static Pawn lastPawn;

        /// <summary>True only while the Create characters page draws its own
        /// contents - the sole scope in which the getter patches below may
        /// substitute sampled data. See <see cref="Patch_CalmCreateCharactersPage"/>
        /// for why the scoping is what keeps them safe.</summary>
        internal static bool InSteadyScope;

        /// <summary>The one gate every substitution goes through. Pumping is
        /// excluded because a click on Randomize starts a pump inside the page's
        /// own draw call, and generation must always read real data.</summary>
        internal static bool SubstitutionActive => InSteadyScope && !PawnRandomizer.Pumping;

        /// <summary>The searching pawn's name object as of this frame, refreshed on
        /// every pump rather than at the sample cadence. The ToStringShort patches
        /// compare against this to recognise the churning name wherever it surfaces,
        /// because candidates replace the object and a stale reference would miss
        /// it.</summary>
        internal static Name LiveName { get; private set; }

        internal static Name SampledName { get; private set; }
        internal static string SampledLabelShort { get; private set; }
        internal static string SampledTitleCap { get; private set; }
        internal static string SampledTitleShortCap { get; private set; }

        /// <summary>The sampled candidate's skill tracker. Candidates replace the
        /// tracker object rather than mutating it, so holding the reference holds
        /// stable levels and passions.</summary>
        internal static Pawn_SkillTracker SampledSkills { get; private set; }

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
            LiveName = pawn.Name;
            if (SampledName != null && Time.realtimeSinceStartup < nextSampleAt)
                return;

            nextSampleAt = Time.realtimeSinceStartup + PeriodSeconds;

            // Candidates replace the name and skill objects rather than mutating
            // them, so holding references holds stable values.
            SampledName = pawn.Name;
            SampledLabelShort = pawn.LabelShort;
            SampledTitleCap = pawn.story?.TitleCap;
            SampledTitleShortCap = pawn.story?.TitleShortCap;
            SampledSkills = pawn.skills;

            // Not the pawn's own age tracker: that regenerates for every candidate,
            // while the name only regenerates for candidates that pass the age
            // check, so reading it live could caption an old name with a newer
            // candidate's age. The coherent age belongs to the sampled name.
            string shortName = pawn.Name?.ToStringShort;
            int age = PawnRandomizer.SearchingPawnCoherentAge;
            if (age < 0)
                age = pawn.ageTracker.AgeBiologicalYears;
            SampledPortraitLine = string.IsNullOrEmpty(shortName)
                ? ""
                : $"{shortName}, {age}";
        }

        /// <summary>
        /// Shared body of the ToStringShort/ToStringFull patches: serve the sampled
        /// name's text when the churning name is about to be displayed inside the
        /// steady scope. True when <paramref name="result"/> was substituted.
        /// </summary>
        internal static bool TrySteadyNameText(Name instance, bool full, ref string result)
        {
            var sampled = SampledName;
            if (!SubstitutionActive
                || sampled == null
                || instance == sampled
                || instance != LiveName)
                return false;

            result = full ? sampled.ToStringFull : sampled.ToStringShort;
            return true;
        }

        private static void SearchEnded()
        {
            SampledName = null;
            LiveName = null;
            SampledLabelShort = null;
            SampledTitleCap = null;
            SampledTitleShortCap = null;
            SampledSkills = null;
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
    /// Marks when the Create characters page is drawing its own contents - the
    /// steady scope, in which the getters below may serve sampled data.
    /// </summary>
    /// <remarks>
    /// The scope is what makes the substitution safe. An unscoped patch on a name
    /// getter would feed sampled names to generation itself - the bio and name
    /// generator reads existing pawns' names to avoid collisions - and change
    /// which names the search can produce. The page's draw call only draws, with
    /// one exception: a click on Randomize pumps the search from inside it, which
    /// is why <see cref="SearchSample.SubstitutionActive"/> also excludes pumping.
    ///
    /// The whole page rather than its pawn-list and team-skills parts: the page
    /// reads the searching pawn's name through more surfaces than can be safely
    /// enumerated from reference assemblies, and every one of them should tick
    /// with the sample.
    /// </remarks>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "DoWindowContents")]
    static class Patch_CalmCreateCharactersPage
    {
        [HarmonyPrefix]
        static void Prefix()
        {
            SearchSample.InSteadyScope = PawnRandomizer.SearchInProgress;
        }

        // A finalizer rather than a postfix: the flag has to clear even when the
        // draw throws, or the getters would keep lying beyond the page.
        [HarmonyFinalizer]
        static void Finalizer()
        {
            SearchSample.InSteadyScope = false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Name), MethodType.Getter)]
    static class Patch_SteadyNameInPawnList
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn __instance, ref Name __result)
        {
            if (!SearchSample.SubstitutionActive
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
            if (!SearchSample.SubstitutionActive
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
            if (!SearchSample.SubstitutionActive
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
            if (!SearchSample.SubstitutionActive
                || SearchSample.SampledTitleShortCap == null
                || ___pawn != PawnRandomizer.SearchingPawn)
                return true;

            __result = SearchSample.SampledTitleShortCap;
            return false;
        }
    }

    /// <summary>
    /// The name interception that cannot be bypassed. Mono's JIT inlines Pawn's
    /// trivial Name getter into some of its callers, and an inlined call never
    /// reaches the patch on that getter - which is why the pawn list tile's name
    /// kept churning while its title, whose getter is too big to inline, sampled
    /// correctly. Every path that turns a Name into text ends at this virtual
    /// property, and virtual calls are dispatched through the object, never
    /// inlined - so patching the overrides catches whatever the tile reads.
    /// </summary>
    [HarmonyPatch(typeof(NameTriple), nameof(NameTriple.ToStringShort), MethodType.Getter)]
    static class Patch_SteadyNameTripleText
    {
        [HarmonyPrefix]
        static bool Prefix(NameTriple __instance, ref string __result)
        {
            return !SearchSample.TrySteadyNameText(__instance, false, ref __result);
        }
    }

    [HarmonyPatch(typeof(NameSingle), nameof(NameSingle.ToStringShort), MethodType.Getter)]
    static class Patch_SteadyNameSingleText
    {
        [HarmonyPrefix]
        static bool Prefix(NameSingle __instance, ref string __result)
        {
            return !SearchSample.TrySteadyNameText(__instance, false, ref __result);
        }
    }

    // ToStringFull as well: LabelCap and the colored-name properties end in it,
    // so without this a surface using the full name would keep churning.
    [HarmonyPatch(typeof(NameTriple), nameof(NameTriple.ToStringFull), MethodType.Getter)]
    static class Patch_SteadyNameTripleFullText
    {
        [HarmonyPrefix]
        static bool Prefix(NameTriple __instance, ref string __result)
        {
            return !SearchSample.TrySteadyNameText(__instance, true, ref __result);
        }
    }

    [HarmonyPatch(typeof(NameSingle), nameof(NameSingle.ToStringFull), MethodType.Getter)]
    static class Patch_SteadyNameSingleFullText
    {
        [HarmonyPrefix]
        static bool Prefix(NameSingle __instance, ref string __result)
        {
            return !SearchSample.TrySteadyNameText(__instance, true, ref __result);
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
    /// Serves the sampled candidate's skills while the team skills summary is
    /// being drawn.
    /// </summary>
    /// <remarks>
    /// The summary reads its numbers through GetSkill - both directly and inside
    /// FindBestSkillOwner - so redirecting the searching pawn's tracker to the
    /// sampled one steadies the whole panel at once: best-pawn selection, level
    /// and passion all agree on the same snapshot, and update together at the
    /// sample cadence. Outside the steady scope every caller sees the real
    /// tracker, so work-priority and generation logic are untouched.
    /// </remarks>
    [HarmonyPatch(typeof(Pawn_SkillTracker), "GetSkill")]
    static class Patch_SteadySkillsInTeamSummary
    {
        [HarmonyPrefix]
        static bool Prefix(Pawn_SkillTracker __instance, Pawn ___pawn, SkillDef skillDef, ref SkillRecord __result)
        {
            var sampled = SearchSample.SampledSkills;
            if (!SearchSample.SubstitutionActive
                || sampled == null
                || __instance == sampled
                || ___pawn != PawnRandomizer.SearchingPawn)
                return true;

            __result = sampled.GetSkill(skillDef);
            return false;
        }
    }
}
