using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RandomPlus.Tests
{
    public static class World
    {
        // RimWorld's own skill order. What matters for these tests is that a pawn's
        // skill list is built in SkillDef database order, so index 0 is whichever
        // SkillDef sorts first - not necessarily Shooting.
        public static readonly string[] StandardSkills =
        {
            "Shooting", "Melee", "Construction", "Mining", "Cooking", "Plants",
            "Animals", "Crafting", "Artistic", "Medicine", "Social", "Intellectual",
        };

        private static bool initialised;

        // PawnFilter caches skill counts in static readonly fields on first access, so
        // the def database has to be complete before anything touches PawnFilter.
        public static void EnsureDefs()
        {
            if (initialised) return;
            foreach (var name in StandardSkills)
                DefDatabase<SkillDef>.Add(new SkillDef { defName = name, label = name.ToLowerInvariant() });
            initialised = true;
        }

        public static SkillDef Skill(string name) =>
            DefDatabase<SkillDef>.AllDefsListForReading.First(d => d.defName == name);

        public static TraitDef Trait(string name)
        {
            var existing = DefDatabase<TraitDef>.AllDefsListForReading.FirstOrDefault(d => d.defName == name);
            if (existing != null) return existing;
            var def = new TraitDef { defName = name, label = name.ToLowerInvariant() };
            DefDatabase<TraitDef>.Add(def);
            return def;
        }

        public static void Reset()
        {
            EnsureDefs();
            ModsConfig.BiotechActive = false;
            ModsConfig.IdeologyActive = false;
            ModsConfig.Active.Clear();
            Verse.StartingPawnUtility.Pawns = new List<Pawn>();
            Verse.StartingPawnUtility.RandomizeInPlaceCount = 0;
            Verse.StartingPawnUtility.RandomizeInPlaceHook = p => p;
            Find.WindowStack.currentlyDrawnWindow = null;
            Find.WindowStack.Windows.Clear();
            PawnRandomizer.AbortSearch();
            Verse.PawnGenerator.GearGenerationsSkipped = 0;
            PawnRandomizer.SuppressGearGeneration = false;
            PawnRandomizer.ForeignPatchesOnGenerationEntryPoints = () => false;
            PawnRandomizer.GearSuppressionIsSafe = () => true;
            PawnRandomizer.ResetRerollCounter();
            PawnRandomizer.Init();
        }
    }

    public class PawnBuilder
    {
        private readonly Pawn pawn = new Pawn();
        private readonly List<string> skillOrder = new List<string>(World.StandardSkills);

        public PawnBuilder Gender(Gender g) { pawn.gender = g; return this; }
        public PawnBuilder Age(int years) { pawn.ageTracker.AgeBiologicalYears = years; return this; }

        /// <summary>Puts a skill first in the pawn's skill list, as a mod-added
        /// SkillDef sorting ahead of Shooting would.</summary>
        public PawnBuilder SkillListStartsWith(string name)
        {
            skillOrder.Remove(name);
            skillOrder.Insert(0, name);
            return this;
        }

        /// <summary>Leaves a skill off the pawn entirely, as a pawn kind that cannot
        /// have it does, or as a mod removing a SkillDef would.</summary>
        public PawnBuilder WithoutSkill(string name)
        {
            skillOrder.Remove(name);
            return this;
        }

        public PawnBuilder Trait(string name, int degree = 0)
        {
            pawn.story.traits.allTraits.Add(new Trait(World.Trait(name), degree));
            return this;
        }

        public PawnBuilder Hediff(Hediff h) { pawn.health.hediffSet.hediffs.Add(h); return this; }
        public PawnBuilder DisabledWork(WorkTags tags) { pawn.story.DisabledWorkTagsBackstoryAndTraits = tags; return this; }

        private readonly Dictionary<string, (int Level, Passion Passion)> levels =
            new Dictionary<string, (int, Passion)>();

        public PawnBuilder Skill(string name, int level, Passion passion = Passion.None)
        {
            levels[name] = (level, passion);
            return this;
        }

        public Pawn Build()
        {
            pawn.skills.skills.Clear();
            foreach (var name in skillOrder)
            {
                levels.TryGetValue(name, out var v);
                pawn.skills.skills.Add(new SkillRecord
                {
                    def = World.Skill(name),
                    Level = v.Level,
                    passion = v.Passion,
                });
            }
            return pawn;
        }
    }

    public static class Assert
    {
        public static int Passed, Failed;

        public static void True(bool condition, string what)
        {
            if (condition) { Passed++; Console.WriteLine($"  PASS  {what}"); }
            else { Failed++; Console.WriteLine($"  FAIL  {what}"); }
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            { Passed++; Console.WriteLine($"  PASS  {what}"); }
            else
            { Failed++; Console.WriteLine($"  FAIL  {what}  (expected {expected}, got {actual})"); }
        }
    }
}
