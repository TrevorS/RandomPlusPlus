using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RandomPlus
{
    /// <summary>
    /// The reroll search: how a pawn matching the filter is found.
    /// </summary>
    /// <remarks>
    /// There is one search. Each attempt regenerates only the parts of the pawn the
    /// filter actually looks at - age, then traits and skills, then health - and gives
    /// up on a candidate the moment one of them fails, so a rejected pawn costs a
    /// fraction of a full generation. The expensive finishing work (gear, genes, body
    /// type, styling) happens once, for the pawn that is kept.
    ///
    /// Regenerating parts in place bypasses generation hooks other mods install, so
    /// where that is unsafe the same loop rerolls the whole pawn instead. That is a
    /// slower path through one algorithm, not a second one.
    ///
    /// The search runs in time slices rather than to completion. A large reroll limit
    /// can mean tens of thousands of candidates - seconds of work - and running that
    /// inside one GUI event freezes the window for the duration, which macOS marks
    /// with a beachball and Windows with "not responding". So the search is a session:
    /// <see cref="BeginReroll"/> starts one, and <see cref="PumpSearch"/> advances it
    /// under a millisecond budget, once per frame, until it finishes. The candidates
    /// are generated on the same thread in the same order as they always were - a
    /// slice boundary falls only between candidates - so slicing changes when the
    /// work happens, never what is generated.
    /// </remarks>
    public static partial class PawnRandomizer
    {
        /// <summary>How many times to re-roll a candidate's health when the roll leaves it
        /// dead or downed. Only a backstop against a scenario that always does.</summary>
        private const int MaxHealthGenerationAttempts = 100;

        /// <summary>Any constant does; ErrorOnce keys on it to log this at most once.</summary>
        private const int HealthGenerationFailureKey = 0x52502B48;

        /// <summary>Vanilla wants the starting group to cover its required work types.
        /// Rerolling this pawn can only help so many times before it is another pawn's
        /// problem.</summary>
        private const int MaxWorkTypeRetries = 20;

        private static Session activeSearch;
        private static bool pumping;

        /// <summary>Whether a reroll search is currently running.</summary>
        public static bool SearchInProgress => activeSearch != null;

        /// <summary>Which starting pawn the active search is rerolling, or -1. The UI
        /// uses this to draw a search panel over that pawn's card instead of the
        /// half-rerolled pawn itself.</summary>
        public static int SearchingPawnIndex => activeSearch?.PawnIndex ?? -1;

        /// <summary>The pawn the active search is currently rerolling, if any. Between
        /// frames it sits at a candidate boundary - readable, though not a finished
        /// pawn, and possibly a chimera: see <see cref="SearchingPawnCoherentAge"/>.</summary>
        public static Pawn SearchingPawn => activeSearch?.CurrentPawn;

        /// <summary>
        /// The biological age belonging to the same candidate as the searching pawn's
        /// current name and skills, or -1 when no search is running.
        /// </summary>
        /// <remarks>
        /// Age is regenerated for every candidate because it is tested first; the
        /// name, title and skills are only regenerated for candidates that pass the
        /// age filter. So between frames the pawn's own age tracker can be one or
        /// more candidates ahead of its name. The display reads this instead, so a
        /// sampled "name, age" is always one person.
        /// </remarks>
        public static int SearchingPawnCoherentAge => activeSearch?.CoherentAgeYears ?? -1;

        /// <summary>
        /// Starts a search for the starting pawn at <paramref name="pawnIndex"/>. One
        /// search runs at a time; starting another while one is active does nothing.
        /// </summary>
        public static void BeginReroll(int pawnIndex)
        {
            if (activeSearch != null)
                return;

            ResetRerollCounter();
            activeSearch = new Session(pawnIndex);
        }

        /// <summary>
        /// Advances the active search for at most <paramref name="budgetMillis"/>,
        /// always by at least one candidate. Call once per frame.
        /// </summary>
        public static void PumpSearch(int budgetMillis)
        {
            var search = activeSearch;
            if (search == null || pumping)
                return;

            pumping = true;
            try
            {
                if (search.Pump(budgetMillis))
                    activeSearch = null;
            }
            finally
            {
                pumping = false;
            }
        }

        /// <summary>
        /// Stops the active search, leaving its pawn finished - gear generated, work
        /// priorities initialised - rather than part-way through a reroll.
        /// </summary>
        public static void AbortSearch()
        {
            activeSearch?.Finish();
            activeSearch = null;
        }

        /// <summary>
        /// Rerolls the starting pawn at <paramref name="pawnIndex"/> until it satisfies
        /// the filter or the reroll limit is spent, synchronously. The interactive path
        /// goes through <see cref="BeginReroll"/> and <see cref="PumpSearch"/> instead.
        /// </summary>
        public static void Reroll(int pawnIndex)
        {
            BeginReroll(pawnIndex);
            while (SearchInProgress)
                PumpSearch(int.MaxValue);
        }

        /// <summary>
        /// Generates a whole new pawn, skipping gear when that is safe. The result still
        /// owes its gear, which <see cref="FinishKeptPawn"/> settles if it is the one kept.
        /// </summary>
        private static Pawn RerollWholePawn(Pawn pawn, bool suppressGear)
        {
            SpouseRelationUtility.Notify_PawnRegenerated(pawn);

            SuppressGearGeneration = suppressGear;
            try
            {
                pawn = StartingPawnUtility.RandomizeInPlace(pawn);
            }
            finally
            {
                SuppressGearGeneration = false;
            }

            gearGenerationPending = suppressGear;
            return pawn;
        }

        /// <summary>
        /// Gives the pawn being handed back anything the search skipped on its behalf.
        /// </summary>
        /// <remarks>
        /// Gear is added with GenerateGearFor rather than RedressPawn. RedressPawn destroys
        /// all equipment, apparel and inventory first, which would throw away anything a
        /// custom scenario granted during generation; the pawn has no gear to replace here
        /// anyway, because generating it is exactly what was suppressed.
        /// </remarks>
        private static void FinishKeptPawn(Pawn pawn, PawnGenerationRequest request)
        {
            if (!gearGenerationPending)
                return;

            gearGenerationPending = false;

            try
            {
                generateGear?.Invoke(pawn, request);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Failed to generate gear for the chosen pawn: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether a pawn's parts can be regenerated directly instead of rerolling the
        /// whole pawn.
        /// </summary>
        private static bool CanRegenerateInPlace(bool inWanderersDialog)
        {
            // Humanoid Alien Races routes its races through its own generation; driving
            // PawnGenerator's steps directly skips that and produces broken pawns.
            if (ModsConfig.IsActive("erdelf.HumanoidAlienRaces"))
                return false;

            // The wanderers dialog works with whole pawns.
            if (inWanderersDialog)
                return false;

            // Anyone else hooking the generation entry points would be bypassed.
            if (ForeignPatchesOnGenerationEntryPoints())
                return false;

            return true;
        }

        public static void GeneratePawnStyle(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                try
                {
                    pawn.story.hairDef = PawnStyleItemChooser.RandomHairFor(pawn);
                    if (pawn.style != null)
                    {
                        pawn.style.beardDef = pawn.gender == Gender.Male ? PawnStyleItemChooser.RandomBeardFor(pawn) : BeardDefOf.NoBeard;
                        if (ModsConfig.IdeologyActive)
                        {
                            pawn.style.FaceTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Face);
                            pawn.style.BodyTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Body);
                        }
                        else
                        {
                            pawn.style.SetupTattoos_NoIdeology();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Failed to generate pawn style: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// One search, from click to finished pawn: the whole-pawn/in-place candidate
        /// loop, and vanilla's outer retry until the starting group covers its required
        /// work types. Written as an iterator so it can stop between candidates and
        /// resume on a later frame.
        /// </summary>
        private sealed class Session
        {
            private enum Candidate { Rejected, Accepted, CleanupFailed }

            private readonly int pawnIndex;
            private readonly IEnumerator<object> steps;

            // The window the search was started from. If it closes, the search stops:
            // its pawn may be discarded entirely, and on Start it must not enter the
            // game half-rerolled. Tracked only when the stack knows the window, so a
            // caller outside a window (the tests) is simply not tracked.
            private readonly Window owner;
            private readonly bool ownerIsWanderers;
            private readonly bool trackOwner;

            private bool done;

            internal int PawnIndex => pawnIndex;
            internal Pawn CurrentPawn => pawn;
            internal int CoherentAgeYears => coherentAge?.AgeBiologicalYears ?? -1;

            // Search state, shared between the iterator and the candidate attempts.
            private Pawn pawn;

            // The age tracker of the last candidate that regenerated its identity
            // fields - name, title, traits, skills - not merely its age. Candidates
            // replace the tracker object, so this reference pins the age matching
            // the pawn's current name even after later candidates fail the age
            // check and overwrite pawn.ageTracker.
            private Pawn_AgeTracker coherentAge;

            private PawnGenerationRequest request;
            private bool suppressGear;
            private bool canRegenerateInPlace;
            private bool pawnPartlyRegenerated;
            private bool accepted;
            private Faction biosFaction;
            private XenotypeDef xenotype;

            internal Session(int pawnIndex)
            {
                this.pawnIndex = pawnIndex;
                owner = Find.WindowStack.currentlyDrawnWindow;
                ownerIsWanderers = owner is Dialog_ChooseNewWanderers;
                trackOwner = owner != null && Find.WindowStack.Windows.Contains(owner);
                steps = Steps();
            }

            /// <summary>Runs candidates until the budget is spent - always at least
            /// one, so a zero budget still makes progress. True when the search is
            /// over.</summary>
            internal bool Pump(int budgetMillis)
            {
                if (done)
                    return true;

                if (trackOwner && !Find.WindowStack.Windows.Contains(owner))
                {
                    Finish();
                    return true;
                }

                var timer = Stopwatch.StartNew();
                try
                {
                    do
                    {
                        if (!steps.MoveNext())
                        {
                            done = true;
                            break;
                        }
                    }
                    while (timer.ElapsedMilliseconds < budgetMillis);
                }
                catch (Exception ex)
                {
                    // Escaped the per-candidate recovery, so do not keep feeding it.
                    ModLog.Error($"Search stopped by an unexpected error: {ex.Message}");
                    Finish();
                }
                finally
                {
                    // Never leave suppression on between slices, whatever happened.
                    SuppressGearGeneration = false;
                }

                return done;
            }

            /// <summary>Ends the search early, leaving the pawn whole.</summary>
            internal void Finish()
            {
                if (done)
                    return;

                done = true;
                try
                {
                    if (pawn != null)
                    {
                        if (!accepted && pawnPartlyRegenerated)
                            FinishInterruptedPawn();
                        FinishKeptPawn(pawn, request);
                    }
                }
                finally
                {
                    SuppressGearGeneration = false;
                }
            }

            /// <summary>
            /// The search itself. Each `yield return` is a point where a slice may end;
            /// they fall only between whole candidates, so resuming cannot observe a
            /// pawn mid-generation.
            /// </summary>
            private IEnumerator<object> Steps()
            {
                for (int attempt = 0; ; attempt++)
                {
                    List<Pawn> pawnList = startingAndOptionalPawns();
                    pawn = pawnList[pawnIndex];

                    int index = StartingPawnUtility.PawnIndex(pawn);
                    request = StartingPawnUtility.GetGenerationRequest(index);
                    request.ValidateAndFix();

                    gearGenerationPending = false;
                    suppressGear = generateGear != null && GearSuppressionIsSafe();
                    accepted = false;
                    pawnPartlyRegenerated = false;

                    pawn = RerollWholePawn(pawn, suppressGear);
                    coherentAge = pawn.ageTracker;
                    randomRerollCounter++;

                    if (CheckPawnIsSatisfied(pawn))
                    {
                        accepted = true;
                    }
                    else
                    {
                        // The faction the bio and name generator should draw from.
                        // Hoisted out of the loop because it does not change between
                        // candidates.
                        Faction faction1;
                        biosFaction = request.Faction ??
                            (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction1, false, true)
                                ? Faction.OfAncients : faction1);

                        xenotype = ModsConfig.BiotechActive ? PawnGenerator.GetXenotypeForGeneratedPawn(request) : null;
                        canRegenerateInPlace = CanRegenerateInPlace(ownerIsWanderers);

                        while (randomRerollCounter < PawnFilter.RerollLimit)
                        {
                            var result = AttemptOneCandidate();
                            if (result == Candidate.Accepted)
                            {
                                accepted = true;
                                break;
                            }
                            if (result == Candidate.CleanupFailed)
                                break;

                            yield return null;
                        }

                        // Reached when the reroll budget ran out mid-search, or when
                        // recovery from a generation error gave up. If the last attempt
                        // left a pawn part-way through a reroll, finish it, rather than
                        // handing back one wearing a rejected candidate's gear and with
                        // no work priorities.
                        if (!accepted && pawnPartlyRegenerated)
                            FinishInterruptedPawn();
                    }

                    FinishKeptPawn(pawn, request);

                    if (attempt >= MaxWorkTypeRetries || StartingPawnUtility.WorkTypeRequirementsSatisfied())
                        yield break;

                    yield return null;
                }
            }

            /// <summary>One candidate: regenerate, test against the filter, and either
            /// keep it, reject it, or recover from a generation error.</summary>
            private Candidate AttemptOneCandidate()
            {
                try
                {
                    randomRerollCounter++;

                    // Gender is fixed once a pawn exists - none of the steps below change
                    // it - so a gender mismatch can only be resolved by rerolling the whole
                    // pawn. This is also the path taken when regenerating in place is
                    // unsafe, and it covers the recovery reroll in the catch block, which
                    // can equally come back with the wrong gender.
                    if (!canRegenerateInPlace || !CheckGenderIsSatisfied(pawn))
                    {
                        pawn = RerollWholePawn(pawn, suppressGear);
                        coherentAge = pawn.ageTracker;
                        pawnPartlyRegenerated = false;

                        // A whole pawn, so take it if it already satisfies everything.
                        return CheckPawnIsSatisfied(pawn) ? Candidate.Accepted : Candidate.Rejected;
                    }

                    pawnPartlyRegenerated = true;

                    // Age first: it is the cheapest thing to generate and to test.
                    pawn.ageTracker = new Pawn_AgeTracker(pawn);
                    if (generateAge != null)
                    {
                        generateAge(pawn, request);
                    }
                    else
                    {
                        // Fallback if reflection fails
                        pawn.ageTracker.AgeBiologicalTicks = (long)(Rand.Range(16, 65) * 3600000L);
                        pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
                    }

                    if (!CheckAgeIsSatisfied(pawn))
                        return Candidate.Rejected;

                    pawn.story.traits = new TraitSet(pawn);
                    pawn.skills = new Pawn_SkillTracker(pawn);

                    PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, biosFaction.def, request, xenotype);

                    generateTraits?.Invoke(pawn, request);
                    generateSkills?.Invoke(pawn, request);

                    // This candidate now owns every identity field, so its age is the
                    // one a sampled display should pair with the pawn's name.
                    coherentAge = pawn.ageTracker;

                    if (!CheckSkillsIsSatisfied(pawn) || !CheckTraitsIsSatisfied(pawn))
                        return Candidate.Rejected;

                    // The backstory decides which work types are disabled, so this is
                    // settled by the step above and is cheap to test. Do it before
                    // generating health, which is the costliest step left.
                    if (!CheckWorkIsSatisfied(pawn))
                        return Candidate.Rejected;

                    // A scenario or hediff roll can leave the pawn dead or downed, which is
                    // not a candidate at all, so this rolls again. The cap only stops a
                    // scenario that always produces one from hanging the game.
                    bool healthGenSuccess = false;
                    Exception healthFailure = null;
                    for (int i = 0; i < MaxHealthGenerationAttempts && !healthGenSuccess; i++)
                    {
                        pawn.health.Reset();
                        try
                        {
                            // Internally, this method only adds custom Scenario health
                            Find.Scenario.Notify_NewPawnGenerating(pawn, request.Context);
                            generateHealth?.Invoke(pawn, request);

                            healthGenSuccess = !(pawn.Dead || pawn.Destroyed || pawn.Downed);
                        }
                        catch (Exception ex)
                        {
                            healthFailure = ex;
                        }
                    }

                    // Reported after the loop and only once: this is inside the reroll loop,
                    // so logging each failure could put fifty thousand lines in the log.
                    if (healthFailure != null && !healthGenSuccess)
                    {
                        ModLog.ErrorOnce(
                            $"Health generation threw on every attempt, so this pawn's health is whatever survived: {healthFailure.Message}",
                            HealthGenerationFailureKey);
                    }

                    if (!CheckHealthIsSatisfied(pawn))
                        return Candidate.Rejected;

                    // Everything the filter tests has now passed, so it is worth paying for
                    // the rest. Gear comes before Notify_PawnGenerated because RedressPawn
                    // destroys all equipment, apparel and inventory before regenerating,
                    // which would wipe anything a custom scenario granted.
                    PawnGenerator.RedressPawn(pawn, request);
                    gearGenerationPending = false;

                    // Handle custom scenario e.g forced traits
                    Find.Scenario.Notify_PawnGenerated(pawn, request.Context, true);
                    if (!CheckPawnIsSatisfied(pawn))
                        return Candidate.Rejected;

                    if (ModsConfig.BiotechActive)
                    {
                        pawn.genes = new Pawn_GeneTracker(pawn);
                        generateGenes?.Invoke(pawn, xenotype, request);
                    }

                    generateBodyType?.Invoke(pawn, request);
                    GeneratePawnStyle(pawn);

                    // Only the kept pawn needs a work priority table. CheckWorkIsSatisfied
                    // reads the disabled work tags off the backstory and does not need one.
                    pawn.workSettings?.EnableAndInitialize();

                    return Candidate.Accepted;
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Error during pawn generation (attempt {randomRerollCounter}): {ex.Message}");
                    try
                    {
                        Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                        pawn = RerollWholePawn(pawn, suppressGear);
                        coherentAge = pawn.ageTracker;
                        pawnPartlyRegenerated = false;
                        return Candidate.Rejected;
                    }
                    catch (Exception ex2)
                    {
                        ModLog.Error($"Critical error in pawn cleanup: {ex2.Message}");
                        return Candidate.CleanupFailed; // Exit to prevent infinite loop
                    }
                }
            }

            private void FinishInterruptedPawn()
            {
                try
                {
                    PawnGenerator.RedressPawn(pawn, request);
                    gearGenerationPending = false;
                    pawn.workSettings?.EnableAndInitialize();
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Failed to finish pawn after the reroll limit: {ex.Message}");
                }
            }
        }
    }
}
