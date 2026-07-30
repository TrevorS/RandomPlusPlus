using System;
using RandomPlus.Tests;

// Runs the mod's filter and reroll logic against the stubs in Stubs/.
// Exit code 0 when every assertion holds, 1 otherwise.

World.EnsureDefs();

var cases = new (string Name, Action Run)[]
{
    ("skill totals",        Tests.SkillTotal_CountsEverySkill),
    ("skill totals",        Tests.SkillTotal_HighestAttackOnly_StandardOrder),
    ("skill totals",        Tests.SkillTotal_HighestAttackOnly_ModdedSkillOrder),
    ("skill totals",        Tests.SkillTotal_CountOnlyPassion),
    ("skill minimums",      Tests.SkillMinimum_IsEnforced),
    ("skill minimums",      Tests.SkillMinimum_IgnoredWhenThePawnLacksTheSkill),
    ("traits",              Tests.Traits_RequiredAndExcluded),
    ("trait pool",          Tests.TraitPool_RequiredOnly_PoolNotApplicable),
    ("trait pool",          Tests.TraitPool_OptionalTraits_StillEnforced),
    ("age",                 Tests.Age_RangeBoundaries),
    ("filter activity",     Tests.Filter_ReportsWhenAnythingIsActive),
    ("reroll search",       Tests.Reroll_ConvergesOnGenderFilter),
    ("reroll search",       Tests.Reroll_RespectsBudgetWhenUnsatisfiable),
    ("search slicing",      Tests.Search_SpansMultiplePumps),
    ("search slicing",      Tests.Search_AbortsWhenOwnerWindowCloses),
    ("search slicing",      Tests.Search_SecondBeginIsIgnored),
    ("reroll search",       Tests.Reroll_GeneratesGearForTheAcceptedPawn),
    ("reroll search",       Tests.Reroll_InitialisesWorkSettingsOnTheKeptPawn),
    ("compatibility",       Tests.Reroll_FallsBackToWholePawnRerolls),
    ("compatibility",       Tests.Reroll_AlienRacesForcesWholePawnRerolls),
    ("compatibility",       Tests.ForeignPatchesOnEntryPoints_ForceWholePawnRerolls),
    ("compatibility",       Tests.NoForeignPatches_UsesInPlaceRegeneration),
    ("gear suppression",    Tests.Gear_SkippedForCandidates_GeneratedForTheKeptPawn),
    ("gear suppression",    Tests.Gear_NotSuppressedWhenAnotherModPatchesIt),
};

string section = null;
foreach (var (name, run) in cases)
{
    if (name != section) { Console.WriteLine($"\n=== {name} ==="); section = name; }
    try
    {
        run();
    }
    catch (Exception ex)
    {
        Assert.Failed++;
        Console.WriteLine($"  FAIL  {run.Method.Name} threw {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine($"\n{Assert.Passed} passed, {Assert.Failed} failed");
return Assert.Failed == 0 ? 0 : 1;
