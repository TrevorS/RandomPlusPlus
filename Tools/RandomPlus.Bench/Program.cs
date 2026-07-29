using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RandomPlus;
using RandomPlus.Tests;
using RimWorld;
using Verse;

// Measures the filter and search code, compiled from the real mod sources and run
// against the stubs in RandomPlus.Tests/Stubs.
//
// What this measures: the mod's own per-candidate cost - the filter predicates, and
// the bookkeeping of the search loop around them.
//
// What it does NOT measure: RimWorld generating a pawn. That is stubbed out to
// nothing here, and in the real game it dwarfs everything below. These numbers are
// useful for one thing - showing that testing a candidate is cheap and allocates
// nothing, so the cost of a search is generation, which is what the search is built
// to avoid paying.
//
// Pass an iteration multiplier as the first argument (default 1).

double scale = args.Length > 0 && double.TryParse(args[0], out var s) ? s : 1.0;

World.EnsureDefs();

Console.WriteLine("RandomPlus benchmarks");
Console.WriteLine($"  {Environment.OSVersion}, {Environment.ProcessorCount} cores, .NET {Environment.Version}");
Console.WriteLine($"  GC: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}, scale x{scale}, best of 5 rounds");
Console.WriteLine();
Console.WriteLine($"{"benchmark",-52} {"ns/op",10} {"B/op",10}");
Console.WriteLine(new string('-', 74));

// ---------------------------------------------------------------- filter predicates

RunFilter("accept-all filter (no criteria set)", 400_000, _ => { });

RunFilter("per-skill minimums (3 skills)", 400_000, f =>
{
    f.Skills.First(x => x.SkillDef.defName == "Shooting").MinValue = 5;
    f.Skills.First(x => x.SkillDef.defName == "Melee").MinValue = 5;
    f.Skills.First(x => x.SkillDef.defName == "Medicine").MinValue = 5;
});

RunFilter("skill total range", 400_000, f => f.skillRange = new IntRange(0, 200));

RunFilter("skill total, highest attack only", 400_000, f =>
{
    f.skillRange = new IntRange(0, 200);
    f.countOnlyHighestAttack = true;
});

RunFilter("passion count range", 400_000, f => f.passionRange = new IntRange(1, 8));

RunFilter("traits: 2 required, 3 pool", 400_000, f =>
{
    foreach (var name in new[] { "Kind", "Nimble" })
        f.AddTrait(new Trait(World.Trait(name)));
    foreach (var name in new[] { "Ascetic", "Tough", "Jogger" })
    {
        f.AddTrait(new Trait(World.Trait(name)));
        f.Traits.Last().traitFilter = TraitContainer.TraitFilterType.Optional;
    }
    f.RequiredTraitsInPool = 2;
});

RunFilter("health: no addiction", 400_000, f => f.FilterHealthCondition = PawnFilter.HealthOptions.NoAddiction);

RunFilter("everything at once", 400_000, f =>
{
    f.Skills.First(x => x.SkillDef.defName == "Shooting").MinValue = 5;
    f.skillRange = new IntRange(0, 200);
    f.passionRange = new IntRange(1, 8);
    f.ageRange = new IntRange(18, 60);
    f.FilterHealthCondition = PawnFilter.HealthOptions.NoAddiction;
    f.FilterIncapable = PawnFilter.IncapableOptions.NoDumbLabor;
    foreach (var name in new[] { "Kind", "Nimble" })
        f.AddTrait(new Trait(World.Trait(name)));
});

// -------------------------------------------------------------------- search loop

RunSearch("search, match on 1st candidate", 20_000, 1);
RunSearch("search, match on 10th candidate", 20_000, 10);
RunSearch("search, match on 100th candidate", 4_000, 100);

Console.WriteLine();
Console.WriteLine("Candidate testing is the cheap half. In the real game each rejected");
Console.WriteLine("candidate also costs a pawn generation, which is what the search avoids");
Console.WriteLine("by regenerating only the parts the filter reads.");
return 0;

// --------------------------------------------------------------------------- helpers

void RunFilter(string label, int iterations, Action<PawnFilter> configure)
{
    World.Reset();
    configure(PawnRandomizer.PawnFilter);

    var pawn = new PawnBuilder()
        .Gender(Gender.Male).Age(30)
        .Skill("Shooting", 6, Passion.Major).Skill("Melee", 7)
        .Skill("Medicine", 8, Passion.Minor).Skill("Intellectual", 5)
        .Trait("Kind").Trait("Ascetic")
        .Build();

    Report(label, Measure((int)(iterations * scale), () => PawnRandomizer.CheckPawnIsSatisfied(pawn)));
}

void RunSearch(string label, int iterations, int rejectsBeforeMatch)
{
    World.Reset();
    var filter = PawnRandomizer.PawnFilter;
    filter.RerollLimit = 100_000;
    filter.ageRange = new IntRange(30, 30);

    int attempt = 0;
    Verse.PawnGenerator.OnGenerateAge = p =>
    {
        attempt++;
        p.ageTracker.AgeBiologicalYears = attempt % rejectsBeforeMatch == 0 ? 30 : 10;
    };

    var pawns = Verse.StartingPawnUtility.Pawns;
    pawns.Add(new PawnBuilder().Age(10).Build());

    Report(label, Measure((int)(iterations * scale), () =>
    {
        PawnRandomizer.ResetRerollCounter();
        attempt = 0;
        // The previous search left this pawn passing the filter; put it back so the
        // search actually runs instead of returning on its first check.
        pawns[0].ageTracker.AgeBiologicalYears = 10;
        PawnRandomizer.Reroll(0);
    }));

    Verse.PawnGenerator.OnGenerateAge = _ => { };
}

// Reports the fastest of several rounds rather than a single timing. A shared or
// busy machine can make one round several times slower than another for identical
// code - measured here at over 3x - and the minimum is the round least disturbed by
// whatever else the machine was doing. Allocation is deterministic and needs no such
// treatment; it is taken from the last round.
(double NsPerOp, double BytesPerOp) Measure(int iterations, Action body)
{
    const int rounds = 5;
    if (iterations < 1) iterations = 1;

    for (int i = 0; i < Math.Max(1, iterations / 10); i++) body();

    double best = double.MaxValue;
    double bytesPerOp = 0;

    for (int round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) body();
        sw.Stop();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        double nsPerOp = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
        if (nsPerOp < best) best = nsPerOp;
        bytesPerOp = (allocatedAfter - allocatedBefore) / (double)iterations;
    }

    return (best, bytesPerOp);
}

void Report(string label, (double NsPerOp, double BytesPerOp) r)
    => Console.WriteLine($"{label,-52} {r.NsPerOp,10:F1} {r.BytesPerOp,10:F1}");
