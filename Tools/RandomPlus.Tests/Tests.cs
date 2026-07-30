using System;
using System.Linq;
using RimWorld;
using Verse;

namespace RandomPlus.Tests
{
    public static class Tests
    {
        // ------------------------------------------------------------ skill totals

        public static void SkillTotal_CountsEverySkill()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.skillRange = new IntRange(0, 25);

            var pawn = new PawnBuilder()
                .Skill("Shooting", 8).Skill("Melee", 6).Skill("Intellectual", 10)
                .Build();

            // 8 + 6 + 10 = 24, inside 0..25
            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(pawn), "skill total sums all skills (24 within 0..25)");

            filter.skillRange = new IntRange(0, 23);
            Assert.True(!PawnRandomizer.CheckSkillsIsSatisfied(pawn), "skill total rejects when above the range");
        }

        public static void SkillTotal_HighestAttackOnly_StandardOrder()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.countOnlyHighestAttack = true;
            filter.skillRange = new IntRange(18, 18);

            var pawn = new PawnBuilder()
                .Skill("Shooting", 8).Skill("Melee", 6).Skill("Intellectual", 10)
                .Build();

            // max(shooting 8, melee 6) + intellectual 10 = 18
            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(pawn),
                "countOnlyHighestAttack collapses shooting/melee to the higher of the two");
        }

        public static void SkillTotal_HighestAttackOnly_ModdedSkillOrder()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.countOnlyHighestAttack = true;
            filter.skillRange = new IntRange(18, 18);

            // A mod adding a SkillDef that sorts ahead of Shooting shifts the whole
            // list, so positions 0 and 1 are no longer shooting and melee.
            var pawn = new PawnBuilder()
                .SkillListStartsWith("Intellectual")
                .Skill("Shooting", 8).Skill("Melee", 6).Skill("Intellectual", 10)
                .Build();

            // The answer must not depend on list order: still max(8, 6) + 10 = 18.
            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(pawn),
                "countOnlyHighestAttack identifies shooting/melee by def, not list position");
        }

        public static void SkillTotal_CountOnlyPassion()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.countOnlyPassion = true;
            filter.skillRange = new IntRange(18, 18);

            var pawn = new PawnBuilder()
                .Skill("Shooting", 8, Passion.Major)
                .Skill("Melee", 6)
                .Skill("Intellectual", 10, Passion.Minor)
                .Build();

            // Only the two with a passion count: 8 + 10 = 18
            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(pawn), "countOnlyPassion sums only skills with passion");
        }

        public static void SkillMinimum_IsEnforced()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.Skills.First(s => s.SkillDef.defName == "Shooting").MinValue = 10;

            Assert.True(!PawnRandomizer.CheckSkillsIsSatisfied(new PawnBuilder().Skill("Shooting", 9).Build()),
                "per-skill minimum rejects a pawn below it");
            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(new PawnBuilder().Skill("Shooting", 10).Build()),
                "per-skill minimum accepts a pawn at it");
        }

        public static void SkillMinimum_IgnoredWhenThePawnLacksTheSkill()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.Skills.First(s => s.SkillDef.defName == "Shooting").MinValue = 10;
            Log.Messages.Clear();

            var pawn = new PawnBuilder().WithoutSkill("Shooting").Build();

            Assert.True(PawnRandomizer.CheckSkillsIsSatisfied(pawn),
                "a filter on a skill the pawn does not have is ignored, not treated as a rejection");

            // Reached once per candidate, so it has to be ErrorOnce - a reroll limit
            // of 500 would otherwise put 500 lines in the player's log.
            Assert.Equal(1, Log.Messages.Count, "the missing skill is reported");
            Assert.True(Log.Messages[0].Contains("Shooting"), "the report names the skill");
        }

        // -------------------------------------------------------------- trait pool

        public static void TraitPool_RequiredOnly_PoolNotApplicable()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            foreach (var name in new[] { "Kind", "Brawler", "Nimble" })
                filter.AddTrait(new Trait(World.Trait(name)));
            // All three are Required (the default). There are no Optional traits, so
            // the pool requirement has nothing to draw from and must not apply.
            filter.RequiredTraitsInPool = 3;

            var pawn = new PawnBuilder().Trait("Kind").Trait("Brawler").Trait("Nimble").Build();

            Assert.True(PawnRandomizer.CheckTraitsIsSatisfied(pawn),
                "trait pool is measured against optional traits, not every filtered trait");
        }

        public static void TraitPool_OptionalTraits_StillEnforced()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            foreach (var name in new[] { "Kind", "Brawler", "Nimble" })
                filter.AddTrait(new Trait(World.Trait(name)));
            foreach (var t in filter.Traits) t.traitFilter = TraitContainer.TraitFilterType.Optional;
            filter.RequiredTraitsInPool = 2;

            Assert.True(!PawnRandomizer.CheckTraitsIsSatisfied(new PawnBuilder().Trait("Kind").Build()),
                "trait pool rejects a pawn with too few pool traits");
            Assert.True(PawnRandomizer.CheckTraitsIsSatisfied(new PawnBuilder().Trait("Kind").Trait("Nimble").Build()),
                "trait pool accepts a pawn meeting the pool count");
        }

        public static void Traits_RequiredAndExcluded()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.AddTrait(new Trait(World.Trait("Kind")));
            filter.AddTrait(new Trait(World.Trait("Pyromaniac")));
            filter.Traits.First(t => t.trait.def.defName == "Pyromaniac").traitFilter =
                TraitContainer.TraitFilterType.Excluded;

            Assert.True(PawnRandomizer.CheckTraitsIsSatisfied(new PawnBuilder().Trait("Kind").Build()),
                "required trait present and excluded trait absent is accepted");
            Assert.True(!PawnRandomizer.CheckTraitsIsSatisfied(new PawnBuilder().Build()),
                "missing required trait is rejected");
            Assert.True(!PawnRandomizer.CheckTraitsIsSatisfied(new PawnBuilder().Trait("Kind").Trait("Pyromaniac").Build()),
                "excluded trait present is rejected");
        }

        // --------------------------------------------------------- filter activity

        // The card UI shows a filter indicator and the reroll counter only while
        // HasActiveFilters says so; a wrong answer either hides a live filter or
        // decorates an idle one.
        public static void Filter_ReportsWhenAnythingIsActive()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;

            Assert.True(!filter.HasActiveFilters, "a freshly reset filter filters nothing");

            filter.RerollLimit = 50000;
            Assert.True(!filter.HasActiveFilters, "the reroll limit alone is not a filter");

            filter.Gender = Gender.Male;
            Assert.True(filter.HasActiveFilters, "a gender filter counts");
            filter.ResetAll();
            Assert.True(!filter.HasActiveFilters, "ResetAll returns to filtering nothing");

            filter.Skills.First(s => s.SkillDef.defName == "Shooting").MinValue = 5;
            Assert.True(filter.HasActiveFilters, "a per-skill minimum counts");
            filter.ResetAll();

            filter.Skills.First(s => s.SkillDef.defName == "Melee").Passion = Passion.Minor;
            Assert.True(filter.HasActiveFilters, "a required passion counts");
            filter.ResetAll();

            filter.AddTrait(new Trait(World.Trait("Kind")));
            Assert.True(filter.HasActiveFilters, "a trait filter counts");
            filter.ResetAll();

            filter.ageRange = new IntRange(18, 25);
            Assert.True(filter.HasActiveFilters, "an age range counts");
            filter.ResetAll();

            filter.skillRange = new IntRange(10, PawnFilter.SkillMaxDefault);
            Assert.True(filter.HasActiveFilters, "a skill total range counts");
            filter.ResetAll();

            filter.FilterHealthCondition = PawnFilter.HealthOptions.AllowNone;
            Assert.True(filter.HasActiveFilters, "a health option counts");
            filter.ResetAll();

            filter.FilterIncapable = PawnFilter.IncapableOptions.NoDumbLabor;
            Assert.True(filter.HasActiveFilters, "an incapable option counts");
            filter.ResetAll();

            Assert.True(!filter.HasActiveFilters, "everything reset filters nothing again");
        }

        // -------------------------------------------------------------------- age

        public static void Age_RangeBoundaries()
        {
            World.Reset();
            PawnRandomizer.PawnFilter.ageRange = new IntRange(20, 40);

            Assert.True(!PawnRandomizer.CheckAgeIsSatisfied(new PawnBuilder().Age(19).Build()), "age below range rejected");
            Assert.True(PawnRandomizer.CheckAgeIsSatisfied(new PawnBuilder().Age(20).Build()), "age at lower bound accepted");
            Assert.True(PawnRandomizer.CheckAgeIsSatisfied(new PawnBuilder().Age(40).Build()), "age at upper bound accepted");
            Assert.True(!PawnRandomizer.CheckAgeIsSatisfied(new PawnBuilder().Age(41).Build()), "age above range rejected");
        }

        // ----------------------------------------------------------- reroll search

        private static Pawn SatisfyingPawn(Gender gender, int serial)
        {
            var p = new PawnBuilder().Gender(gender).Age(30).Build();
            p.Serial = serial;
            return p;
        }

        public static void Reroll_ConvergesOnGenderFilter()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.Gender = Gender.Female;

            // Every full roll produces a male pawn until the fourth, which is female.
            int rolls = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ =>
            {
                rolls++;
                return SatisfyingPawn(rolls >= 4 ? Gender.Female : Gender.Male, rolls);
            };

            var start = SatisfyingPawn(Gender.Male, 0);
            Verse.StartingPawnUtility.Pawns.Add(start);

            PawnRandomizer.Reroll(0);

            var final = Verse.StartingPawnUtility.Pawns[0];
            Assert.Equal(Gender.Female, final.gender, "fast reroll ends on a pawn matching the gender filter");
            Assert.True(PawnRandomizer.RandomRerollCounter() < filter.RerollLimit,
                $"fast reroll converges without exhausting the budget (used {PawnRandomizer.RandomRerollCounter()}/{filter.RerollLimit})");
        }

        public static void Reroll_RespectsBudgetWhenUnsatisfiable()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 50;
            filter.Gender = Gender.Female;

            // No roll ever produces a female pawn.
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ => SatisfyingPawn(Gender.Male, 0);

            Verse.StartingPawnUtility.Pawns.Add(SatisfyingPawn(Gender.Male, 0));
            PawnRandomizer.Reroll(0);

            Assert.True(PawnRandomizer.RandomRerollCounter() <= filter.RerollLimit,
                $"unsatisfiable filter stops at the reroll limit (used {PawnRandomizer.RandomRerollCounter()}/{filter.RerollLimit})");
        }

        // --------------------------------------------------------- search slicing

        public static void Search_SpansMultiplePumps()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.Gender = Gender.Female;

            int rolls = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ =>
            {
                rolls++;
                return SatisfyingPawn(rolls >= 4 ? Gender.Female : Gender.Male, rolls);
            };
            Verse.StartingPawnUtility.Pawns.Add(SatisfyingPawn(Gender.Male, 0));

            PawnRandomizer.BeginReroll(0);
            Assert.True(PawnRandomizer.SearchInProgress, "a begun search reports itself in progress");
            Assert.Equal(0, PawnRandomizer.SearchingPawnIndex, "the search reports which pawn it is rerolling");

            // A zero budget still advances by one candidate per pump, so the search
            // has to finish, and has to take more than one pump to do it.
            int pumps = 0;
            while (PawnRandomizer.SearchInProgress && pumps < 1000)
            {
                PawnRandomizer.PumpSearch(0);
                pumps++;
            }

            Assert.True(!PawnRandomizer.SearchInProgress, "the sliced search runs to completion");
            Assert.Equal(-1, PawnRandomizer.SearchingPawnIndex, "no searching pawn is reported once the search is over");
            Assert.True(pumps > 1, $"the search spanned multiple pumps ({pumps})");
            Assert.Equal(Gender.Female, Verse.StartingPawnUtility.Pawns[0].gender,
                "a search sliced across pumps still satisfies the filter");
        }

        public static void Search_AbortsWhenOwnerWindowCloses()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 10_000;
            filter.Gender = Gender.Female;

            // Unsatisfiable, so the search would run for a long time if not aborted.
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ => SatisfyingPawn(Gender.Male, 0);
            Verse.StartingPawnUtility.Pawns.Add(SatisfyingPawn(Gender.Male, 0));

            // The search is started from inside a window, which the stack knows about.
            var page = new Window();
            Find.WindowStack.currentlyDrawnWindow = page;
            Find.WindowStack.Windows.Add(page);

            PawnRandomizer.BeginReroll(0);
            PawnRandomizer.PumpSearch(0);
            Assert.True(PawnRandomizer.SearchInProgress, "unsatisfiable search is still running after one pump");

            // The window closes - Back, or Start - mid-search.
            Find.WindowStack.Windows.Clear();
            PawnRandomizer.PumpSearch(0);

            Assert.True(!PawnRandomizer.SearchInProgress, "the search stops when its window closes");
            Assert.Equal(1, Verse.StartingPawnUtility.Pawns[0].GearCount,
                "the abandoned search leaves a finished pawn, gear generated exactly once");
        }

        public static void Search_SecondBeginIsIgnored()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 10_000;
            filter.Gender = Gender.Female;

            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ => SatisfyingPawn(Gender.Male, 0);
            Verse.StartingPawnUtility.Pawns.Add(SatisfyingPawn(Gender.Male, 0));

            PawnRandomizer.BeginReroll(0);
            PawnRandomizer.PumpSearch(0);
            int counted = PawnRandomizer.RandomRerollCounter();
            Assert.True(counted > 0, "the first search made progress");

            // A second click while a search runs starts nothing and resets nothing.
            PawnRandomizer.BeginReroll(0);
            Assert.Equal(counted, PawnRandomizer.RandomRerollCounter(),
                "a second begin during a search does not reset the reroll counter");
            Assert.True(PawnRandomizer.SearchInProgress, "the original search is still the active one");

            PawnRandomizer.AbortSearch();
            Assert.True(!PawnRandomizer.SearchInProgress, "an aborted search is no longer in progress");
        }

        public static void Reroll_FallsBackToWholePawnRerolls()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            // The wanderers dialog is one of the cases where regenerating a pawn's parts
            // is unsafe, so the search falls back to rerolling whole pawns.
            Find.WindowStack.currentlyDrawnWindow = new Dialog_ChooseNewWanderers();
            filter.Gender = Gender.Female;

            int rolls = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ =>
            {
                rolls++;
                return SatisfyingPawn(rolls >= 3 ? Gender.Female : Gender.Male, rolls);
            };

            Verse.StartingPawnUtility.Pawns.Add(SatisfyingPawn(Gender.Male, 0));
            PawnRandomizer.Reroll(0);

            Assert.Equal(Gender.Female, Verse.StartingPawnUtility.Pawns[0].gender,
                "whole-pawn fallback ends on a pawn matching the filter");
        }

        public static void Reroll_GeneratesGearForTheAcceptedPawn()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.ageRange = new IntRange(30, 30);

            // Ages cycle 10, 20, 30: only the third attempt passes the age filter.
            int attempt = 0;
            Verse.PawnGenerator.OnGenerateAge = p =>
            {
                attempt++;
                p.ageTracker.AgeBiologicalYears = attempt < 3 ? 10 * attempt : 30;
            };

            var start = SatisfyingPawn(Gender.Male, 0);
            start.ageTracker.AgeBiologicalYears = 10;
            Verse.StartingPawnUtility.Pawns.Add(start);

            PawnRandomizer.Reroll(0);

            var final = Verse.StartingPawnUtility.Pawns[0];
            Assert.Equal(30, final.ageTracker.AgeBiologicalYears, "fast reroll ends on a pawn passing the age filter");
            Assert.Equal(1, final.RedressCount, "gear is generated exactly once, for the accepted pawn");

            Verse.PawnGenerator.OnGenerateAge = _ => { };
        }

        public static void Reroll_AlienRacesForcesWholePawnRerolls()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.ageRange = new IntRange(30, 30);
            ModsConfig.Active.Add("erdelf.HumanoidAlienRaces");

            // If the search regenerated parts in place it would call this; with Humanoid
            // Alien Races active it must not, and must reroll whole pawns instead.
            int inPlaceAgeCalls = 0;
            Verse.PawnGenerator.OnGenerateAge = _ => inPlaceAgeCalls++;

            int rolls = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ =>
            {
                rolls++;
                var p = new PawnBuilder().Gender(Gender.Male).Age(rolls >= 3 ? 30 : 10).Build();
                return p;
            };

            Verse.StartingPawnUtility.Pawns.Add(new PawnBuilder().Age(10).Build());
            PawnRandomizer.Reroll(0);

            Assert.Equal(0, inPlaceAgeCalls, "alien races present: no in-place regeneration");
            Assert.Equal(30, Verse.StartingPawnUtility.Pawns[0].ageTracker.AgeBiologicalYears,
                "alien races present: whole-pawn rerolls still satisfy the filter");

            Verse.PawnGenerator.OnGenerateAge = _ => { };
        }

        public static void Reroll_InitialisesWorkSettingsOnTheKeptPawn()
        {
            World.Reset();
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.ageRange = new IntRange(30, 30);

            int attempt = 0;
            Verse.PawnGenerator.OnGenerateAge = p =>
            {
                attempt++;
                p.ageTracker.AgeBiologicalYears = attempt < 4 ? 10 : 30;
            };

            var start = new PawnBuilder().Age(10).Build();
            Verse.StartingPawnUtility.Pawns.Add(start);
            PawnRandomizer.Reroll(0);

            var final = Verse.StartingPawnUtility.Pawns[0];
            Assert.Equal(30, final.ageTracker.AgeBiologicalYears, "search lands on a pawn passing the filter");
            Assert.Equal(1, final.workSettings.EnableAndInitializeCount,
                "work priorities are built once, for the kept pawn, not per rejected candidate");

            Verse.PawnGenerator.OnGenerateAge = _ => { };
        }

        // ------------------------------------------------------- generation shortcuts

        /// <summary>Drives the search down the whole-pawn path, matching on the Nth roll.</summary>
        private static Pawn SearchViaWholePawnRerolls(int matchOnRoll)
        {
            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.ageRange = new IntRange(30, 30);

            int rolls = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = _ =>
            {
                rolls++;
                return new PawnBuilder().Age(rolls >= matchOnRoll ? 30 : 10).Build();
            };

            Verse.StartingPawnUtility.Pawns.Add(new PawnBuilder().Age(10).Build());
            PawnRandomizer.Reroll(0);
            return Verse.StartingPawnUtility.Pawns[0];
        }

        public static void Gear_SkippedForCandidates_GeneratedForTheKeptPawn()
        {
            World.Reset();
            // Force the whole-pawn path, so every candidate is a full generation.
            PawnRandomizer.ForeignPatchesOnGenerationEntryPoints = () => true;

            var kept = SearchViaWholePawnRerolls(matchOnRoll: 3);

            Assert.Equal(30, kept.ageTracker.AgeBiologicalYears, "gear suppression does not change which pawn is chosen");
            Assert.Equal(3, Verse.PawnGenerator.GearGenerationsSkipped,
                "every candidate generated skipped its gear");
            Assert.Equal(1, kept.GearCount, "the kept pawn ends up with gear, exactly once");
        }

        public static void Gear_NotSuppressedWhenAnotherModPatchesIt()
        {
            World.Reset();
            PawnRandomizer.ForeignPatchesOnGenerationEntryPoints = () => true;
            // Another mod patched gear generation and expects to see every pawn.
            PawnRandomizer.GearSuppressionIsSafe = () => false;

            var kept = SearchViaWholePawnRerolls(matchOnRoll: 3);

            Assert.Equal(0, Verse.PawnGenerator.GearGenerationsSkipped,
                "gear generation is left alone when another mod has patched it");
            Assert.Equal(1, kept.GearCount, "the kept pawn still has gear");
        }

        public static void ForeignPatchesOnEntryPoints_ForceWholePawnRerolls()
        {
            World.Reset();
            PawnRandomizer.ForeignPatchesOnGenerationEntryPoints = () => true;

            int inPlaceAgeCalls = 0;
            Verse.PawnGenerator.OnGenerateAge = _ => inPlaceAgeCalls++;

            var kept = SearchViaWholePawnRerolls(matchOnRoll: 3);

            Assert.Equal(0, inPlaceAgeCalls,
                "a foreign patch on the generation entry points disables in-place regeneration");
            Assert.Equal(30, kept.ageTracker.AgeBiologicalYears, "and the search still satisfies the filter");

            Verse.PawnGenerator.OnGenerateAge = _ => { };
        }

        public static void NoForeignPatches_UsesInPlaceRegeneration()
        {
            World.Reset();

            int inPlaceAgeCalls = 0;
            Verse.PawnGenerator.OnGenerateAge = p =>
            {
                inPlaceAgeCalls++;
                p.ageTracker.AgeBiologicalYears = inPlaceAgeCalls >= 3 ? 30 : 10;
            };

            var filter = PawnRandomizer.PawnFilter;
            filter.RerollLimit = 100;
            filter.ageRange = new IntRange(30, 30);
            Verse.StartingPawnUtility.Pawns.Add(new PawnBuilder().Age(10).Build());
            PawnRandomizer.Reroll(0);

            Assert.True(inPlaceAgeCalls > 0, "with nothing else patched, the search regenerates in place");
            Assert.Equal(1, Verse.StartingPawnUtility.RandomizeInPlaceCount,
                "and generates only the one whole pawn it starts from");

            Verse.PawnGenerator.OnGenerateAge = _ => { };
        }
    }
}
