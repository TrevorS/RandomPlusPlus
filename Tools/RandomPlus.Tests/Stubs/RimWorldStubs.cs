// Minimal stand-ins for the RimWorld types the filter and reroll logic touches.
//
// These exist so everything in Source/Core can be compiled and executed off the
// real game. They are deliberately dumb: just enough shape for
// the mod's code to compile and for a test to build a pawn by hand. Anything the mod
// calls but does not read a result from is a no-op.
//
// The important consequence: these tests prove what the mod's own logic does with a
// given pawn. They prove nothing about how RimWorld actually generates one.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Verse
{
    using RimWorld;

    public enum Gender { None, Male, Female }

    [Flags]
    public enum WorkTags { None = 0, ManualDumb = 1, Violent = 2, Caring = 4 }

    public enum LoadSaveMode { Inactive, Saving, LoadingVars }
    public enum LookMode { Undefined, Value, Deep, Def }
    public enum PawnGenerationContext { PlayerStarter, NonPlayer }

    public class Def
    {
        public string defName;
        public string label;
        public override string ToString() => defName;
    }

    public interface IExposable { void ExposeData(); }

    public struct IntRange
    {
        public int min;
        public int max;
        public IntRange(int min, int max) { this.min = min; this.max = max; }
    }

    public static class Log
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Error(string s) => Messages.Add("ERROR: " + s);
        public static void Warning(string s) => Messages.Add("WARN: " + s);
        public static void Message(string s) => Messages.Add(s);

        // The real one logs a given key at most once per session. Suppressing here
        // would hide calls from tests, which is the opposite of what they want.
        public static void ErrorOnce(string s, int _) => Messages.Add("ERROR: " + s);
    }

    public static class Rand
    {
        private static readonly Random rng = new Random(1234);
        public static int Range(int min, int max) => rng.Next(min, max);
    }

    public static class DefDatabase<T> where T : Def
    {
        private static readonly List<T> defs = new List<T>();
        public static IEnumerable<T> AllDefs => defs;
        public static List<T> AllDefsListForReading => defs;
        public static T GetNamed(string name) => defs.First(d => d.defName == name);
        public static T GetNamedSilentFail(string name) => defs.FirstOrDefault(d => d.defName == name);
        public static void Clear() => defs.Clear();
        public static void Add(T def) => defs.Add(def);
    }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
    }

    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default, bool forceSave = false) { }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> list, string label, LookMode lookMode, params object[] ctorArgs) { }
    }

    public static class Scribe_Defs
    {
        public static void Look<T>(ref T value, string label) where T : Def { }
    }

    public class SkillDef : Def { }
    public class TraitDef : Def
    {
        public float commonalityMale = 1f;
        public float commonalityFemale = 1f;
        public float GetGenderSpecificCommonality(Gender gender)
            => gender == Gender.Female ? commonalityFemale : commonalityMale;
    }
    public class HediffDef : Def { }
    public class HairDef : Def { }
    public class BeardDef : Def { }
    public class TattooDef : Def { }
    public class BodyTypeDef : Def { }
    public class XenotypeDef : Def { }
    public class FactionDef : Def { }

    public class Trait
    {
        public TraitDef def;
        public int Degree;
        public string Label => def == null ? "" : def.label + (Degree == 0 ? "" : "_" + Degree);
        public Trait() { }
        public Trait(TraitDef def, int degree = 0, bool forced = false) { this.def = def; Degree = degree; }
    }

    public class TraitSet
    {
        public List<Trait> allTraits = new List<Trait>();
        public TraitSet() { }
        public TraitSet(Pawn pawn) { }
    }

    public class SkillRecord
    {
        public SkillDef def;
        public Passion passion;
        public int Level;
    }

    public class Pawn_SkillTracker
    {
        public List<SkillRecord> skills = new List<SkillRecord>();
        public Pawn_SkillTracker() { }
        public Pawn_SkillTracker(Pawn pawn) { }
    }

    public class Pawn_StoryTracker
    {
        public TraitSet traits = new TraitSet();
        public WorkTags DisabledWorkTagsBackstoryAndTraits = WorkTags.None;
        public HairDef hairDef;
    }

    public class Gene { }

    public class Hediff
    {
        public HediffDef def = new HediffDef { defName = "Generic" };
        public float PainOffset;
    }

    public class Hediff_Addiction : Hediff { }

    public class Hediff_ChemicalDependency : Hediff
    {
        public Gene LinkedGene;
    }

    public class HediffSet
    {
        public List<Hediff> hediffs = new List<Hediff>();
    }

    public class Pawn_HealthTracker
    {
        public HediffSet hediffSet = new HediffSet();
        public void Reset() => hediffSet.hediffs.Clear();
    }

    public class Pawn_AgeTracker
    {
        public long AgeBiologicalTicks;
        public long AgeChronologicalTicks;
        public int AgeBiologicalYears
        {
            get => (int)(AgeBiologicalTicks / 3600000L);
            set => AgeBiologicalTicks = value * 3600000L;
        }
        public Pawn_AgeTracker() { }
        public Pawn_AgeTracker(Pawn pawn) { }
    }

    public class Pawn_GeneTracker
    {
        public Pawn_GeneTracker(Pawn pawn) { }
    }

    public class Pawn_StyleTracker
    {
        public BeardDef beardDef;
        public TattooDef FaceTattoo;
        public TattooDef BodyTattoo;
        public void SetupTattoos_NoIdeology() { }
    }

    public class Pawn_WorkSettings
    {
        public int EnableAndInitializeCount;
        public void EnableAndInitialize() => EnableAndInitializeCount++;
    }

    public class Pawn_RelationsTracker
    {
        public void ClearAllRelations() { }
    }

    public class RaceProperties
    {
        public bool Humanlike = true;
    }

    public class Pawn
    {
        public Pawn_SkillTracker skills = new Pawn_SkillTracker();
        public Pawn_StoryTracker story = new Pawn_StoryTracker();
        public Pawn_HealthTracker health = new Pawn_HealthTracker();
        public Pawn_AgeTracker ageTracker = new Pawn_AgeTracker();
        public Pawn_StyleTracker style = new Pawn_StyleTracker();
        public Pawn_WorkSettings workSettings = new Pawn_WorkSettings();
        public Pawn_RelationsTracker relations = new Pawn_RelationsTracker();
        public Pawn_GeneTracker genes;
        public Gender gender = Gender.Male;
        public RaceProperties RaceProps = new RaceProperties();
        public bool Dead, Destroyed, Downed;

        // Test bookkeeping, not part of the real API.
        public int Serial;
        public int RedressCount;
        public int GearCount;
    }

    public struct PawnGenerationRequest
    {
        public Faction Faction { get; set; }
        public PawnGenerationContext Context { get; set; }
        public void ValidateAndFix() { }
    }

    public class Faction
    {
        public FactionDef def = new FactionDef { defName = "TestFaction" };
        public static Faction OfAncients { get; } = new Faction();
    }

    public class Window { }

    public static class ModsConfig
    {
        public static bool BiotechActive;
        public static bool IdeologyActive;
        public static readonly HashSet<string> Active = new HashSet<string>();
        public static bool IsActive(string packageId) => Active.Contains(packageId);
    }

    // Reflection targets. PawnRandomizer.Init() looks these up by name with
    // NonPublic|Static, so they must stay private and static to be found.
    public static class PawnGenerator
    {
        public static Action<Pawn> OnGenerateAge = _ => { };
        public static Action<Pawn> OnGenerateTraits = _ => { };
        public static Action<Pawn> OnGenerateSkills = _ => { };
        public static Action<Pawn> OnGenerateHediffs = _ => { };

        private static void GenerateRandomAge(Pawn pawn, PawnGenerationRequest request) => OnGenerateAge(pawn);
        private static void GenerateTraits(Pawn pawn, PawnGenerationRequest request) => OnGenerateTraits(pawn);
        private static void GenerateSkills(Pawn pawn, PawnGenerationRequest request) => OnGenerateSkills(pawn);
        private static void GenerateInitialHediffs(Pawn pawn, PawnGenerationRequest request) => OnGenerateHediffs(pawn);
        private static void GenerateBodyType(Pawn pawn, PawnGenerationRequest request) { }

        // Models vanilla generating gear, and RandomPlus's prefix skipping it.
        public static int GearGenerationsSkipped;
        private static void GenerateGearFor(Pawn pawn, PawnGenerationRequest request)
            => GenerateGear(pawn);

        internal static void GenerateGear(Pawn pawn)
        {
            if (RandomPlus.PawnRandomizer.SuppressGearGeneration) { GearGenerationsSkipped++; return; }
            pawn.GearCount++;
        }
        private static void GenerateGenes(Pawn pawn, XenotypeDef xenotype, PawnGenerationRequest request) { }

        public static void RedressPawn(Pawn pawn, PawnGenerationRequest request)
        {
            pawn.RedressCount++;
            pawn.GearCount = 0; // RedressPawn destroys existing gear first
            GenerateGear(pawn);
        }
        public static XenotypeDef GetXenotypeForGeneratedPawn(PawnGenerationRequest request) => null;
    }

    public static class StartingPawnUtility
    {
        public static List<Pawn> Pawns = new List<Pawn>();

        // The test supplies this, which is what makes the reroll loop drivable.
        public static Func<Pawn, Pawn> RandomizeInPlaceHook = p => p;
        public static int RandomizeInPlaceCount;

        private static List<Pawn> StartingAndOptionalPawns => Pawns;

        public static Pawn RandomizeInPlace(Pawn p)
        {
            RandomizeInPlaceCount++;
            var replacement = RandomizeInPlaceHook(p);
            // A whole pawn is generated with gear, unless the search suppressed it.
            PawnGenerator.GenerateGear(replacement);
            int i = Pawns.IndexOf(p);
            if (i >= 0) Pawns[i] = replacement;
            return replacement;
        }

        public static int PawnIndex(Pawn p) => Math.Max(0, Pawns.IndexOf(p));
        public static PawnGenerationRequest GetGenerationRequest(int index) => new PawnGenerationRequest();
        public static bool WorkTypeRequirementsSatisfied() => true;
    }

    public static class Find
    {
        public static ScenarioStub Scenario = new ScenarioStub();
        public static WorldPawnsStub WorldPawns = new WorldPawnsStub();
        public static FactionManagerStub FactionManager = new FactionManagerStub();
        public static WindowStackStub WindowStack = new WindowStackStub();
    }

    public class ScenarioStub
    {
        public void Notify_NewPawnGenerating(Pawn pawn, PawnGenerationContext context) { }
        public void Notify_PawnGenerated(Pawn pawn, PawnGenerationContext context, bool redressed) { }
    }

    public class WorldPawnsStub
    {
        public void RemoveAndDiscardPawnViaGC(Pawn pawn) { }
    }

    public class FactionManagerStub
    {
        public bool TryGetRandomNonColonyHumanlikeFaction(out Faction faction, bool a, bool b)
        {
            faction = Faction.OfAncients;
            return true;
        }
    }

    public class WindowStackStub
    {
        public Window currentlyDrawnWindow;

        // The open-window list the search session watches its owner in. Left empty
        // by default, which the session reads as "no window to track" - so tests
        // that do not care about windows are unaffected.
        public readonly List<Window> Windows = new List<Window>();
    }
}

namespace RimWorld
{
    using Verse;

    public enum Passion { None, Minor, Major }
    public enum TattooType { Face, Body }

    public class Dialog_ChooseNewWanderers : Window { }

    public static class SpouseRelationUtility
    {
        public static void Notify_PawnRegenerated(Pawn pawn) { }
    }

    public static class PawnBioAndNameGenerator
    {
        public static void GiveAppropriateBioAndNameTo(Pawn pawn, FactionDef faction,
            PawnGenerationRequest request, XenotypeDef xenotype)
        { }
    }

    public static class PawnStyleItemChooser
    {
        public static HairDef RandomHairFor(Pawn pawn) => null;
        public static BeardDef RandomBeardFor(Pawn pawn) => null;
        public static TattooDef RandomTattooFor(Pawn pawn, TattooType type) => null;
    }

    public static class BeardDefOf
    {
        public static BeardDef NoBeard = new BeardDef { defName = "NoBeard" };
    }

    public static class SkillDefOf
    {
        // Cached, like the real DefOf fields. A lookup here would allocate and would
        // show up in the benchmarks as if the mod had caused it.
        private static SkillDef shooting;
        private static SkillDef melee;
        public static SkillDef Shooting => shooting ?? (shooting = RandomPlus.Tests.World.Skill("Shooting"));
        public static SkillDef Melee => melee ?? (melee = RandomPlus.Tests.World.Skill("Melee"));
    }
}
