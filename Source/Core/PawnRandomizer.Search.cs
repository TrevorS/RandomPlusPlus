using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RandomPlus
{
    /// <summary>
    /// The reroll search: how a pawn matching the filter is found.
    /// </summary>
    public static partial class PawnRandomizer
    {
        /// <summary>How many times to re-roll a candidate's health when the roll leaves it
        /// dead or downed. Only a backstop against a scenario that always does.</summary>
        private const int MaxHealthGenerationAttempts = 100;

        /// <summary>Any constant does; ErrorOnce keys on it to log this at most once.</summary>
        private const int HealthGenerationFailureKey = 0x52502B48;

        /// <summary>
        /// Rerolls the starting pawn at <paramref name="pawnIndex"/> until it satisfies the
        /// filter, or until the reroll limit is spent.
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
        /// </remarks>
        public static void Reroll(int pawnIndex)
        {
            List<Pawn> pawnList = startingAndOptionalPawns();
            Pawn pawn = pawnList[pawnIndex];

            int index = StartingPawnUtility.PawnIndex(pawn);
            PawnGenerationRequest request = StartingPawnUtility.GetGenerationRequest(index);
            request.ValidateAndFix();

            gearGenerationPending = false;
            bool suppressGear = generateGear != null && GearSuppressionIsSafe();

            try
            {
                pawn = RunSearch(pawn, request, suppressGear);
            }
            finally
            {
                // Never leave suppression on, whatever happened in there.
                SuppressGearGeneration = false;
            }

            FinishKeptPawn(pawn, request);
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

        private static Pawn RunSearch(Pawn pawn, PawnGenerationRequest request, bool suppressGear)
        {
            pawn = RerollWholePawn(pawn, suppressGear);

            randomRerollCounter++;

            if (CheckPawnIsSatisfied(pawn))
                return pawn;

            // The faction the bio and name generator should draw from. Hoisted out of the
            // loop because it does not change between candidates.
            Faction faction1;
            Faction faction2 = request.Faction ??
                (!Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction1, false, true)
                    ? Faction.OfAncients : faction1);

            XenotypeDef xenotype = ModsConfig.BiotechActive ? PawnGenerator.GetXenotypeForGeneratedPawn(request) : null;

            bool canRegenerateInPlace = CanRegenerateInPlace();
            bool pawnPartlyRegenerated = false;

            while (randomRerollCounter < PawnFilter.RerollLimit)
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
                        pawnPartlyRegenerated = false;

                        // A whole pawn, so take it if it already satisfies everything.
                        if (CheckPawnIsSatisfied(pawn))
                            return pawn;

                        continue;
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
                        continue;

                    pawn.story.traits = new TraitSet(pawn);
                    pawn.skills = new Pawn_SkillTracker(pawn);

                    PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo(pawn, faction2.def, request, xenotype);

                    generateTraits?.Invoke(pawn, request);
                    generateSkills?.Invoke(pawn, request);

                    if (!CheckSkillsIsSatisfied(pawn) || !CheckTraitsIsSatisfied(pawn))
                        continue;

                    // The backstory decides which work types are disabled, so this is
                    // settled by the step above and is cheap to test. Do it before
                    // generating health, which is the costliest step left.
                    if (!CheckWorkIsSatisfied(pawn))
                        continue;

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
                        continue;

                    // Everything the filter tests has now passed, so it is worth paying for
                    // the rest. Gear comes before Notify_PawnGenerated because RedressPawn
                    // destroys all equipment, apparel and inventory before regenerating,
                    // which would wipe anything a custom scenario granted.
                    PawnGenerator.RedressPawn(pawn, request);
                    gearGenerationPending = false;

                    // Handle custom scenario e.g forced traits
                    Find.Scenario.Notify_PawnGenerated(pawn, request.Context, true);
                    if (!CheckPawnIsSatisfied(pawn))
                        continue;

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

                    return pawn;
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Error during pawn generation (attempt {randomRerollCounter}): {ex.Message}");
                    try
                    {
                        Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                        pawn = RerollWholePawn(pawn, suppressGear);
                        pawnPartlyRegenerated = false;
                    }
                    catch (Exception ex2)
                    {
                        ModLog.Error($"Critical error in pawn cleanup: {ex2.Message}");
                        break; // Exit to prevent infinite loop
                    }
                }
            }

            // Reached when the reroll budget ran out mid-search, or when recovery from a
            // generation error gave up. If the last attempt left a pawn part-way through a
            // reroll, finish it, rather than handing back one wearing a rejected
            // candidate's gear and with no work priorities.
            if (pawnPartlyRegenerated)
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

            return pawn;
        }

        /// <summary>
        /// Whether a pawn's parts can be regenerated directly instead of rerolling the
        /// whole pawn.
        /// </summary>
        private static bool CanRegenerateInPlace()
        {
            // Humanoid Alien Races routes its races through its own generation; driving
            // PawnGenerator's steps directly skips that and produces broken pawns.
            if (ModsConfig.IsActive("erdelf.HumanoidAlienRaces"))
                return false;

            // The wanderers dialog works with whole pawns.
            if (Find.WindowStack.currentlyDrawnWindow is Dialog_ChooseNewWanderers)
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
    }
}
