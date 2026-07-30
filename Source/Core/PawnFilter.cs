using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;

namespace RandomPlus
{
    public class PawnFilter : IExposable
    {
        public static readonly int PassionMinDefault = 0;
        public static readonly int PassionMaxDefault = DefDatabase<SkillDef>.AllDefsListForReading.Count;

        public static readonly int SkillMinDefault = 0;
        public static readonly int SkillMaxDefault = DefDatabase<SkillDef>.AllDefsListForReading.Count * 8;

        public static readonly int MinAgeDefault = 0;
        public static readonly int MaxAgeDefault = 120;

        public static readonly int DefaultPoolSize = 0;

        public enum RerollLimitOptions { N100 = 100, N250 = 250, N500 = 500, N1000 = 1000, N2500 = 2500, N5000 = 5000, N10000 = 10000, N50000 = 50000 }
        public readonly static string[] RerollLimitOptionValues = new string[] { "100", "250", "500", "1000", "2500", "5000", "10000", "50000" };
        public static readonly RerollLimitOptions DefaultRerollLimit = RerollLimitOptions.N1000;

        public enum HealthOptions
        {
            AllowAll, OnlyStartCondition, NoPain, NoAddiction, AllowNone,
            //OnlyPositiveImplants, 
        }
        public readonly static string[] HealthOptionValues = new string[] {
            "RandomPlus.PanelOthers.HealthOptions.AllowAll",
            "RandomPlus.PanelOthers.HealthOptions.OnlyStartConditions",
            "RandomPlus.PanelOthers.HealthOptions.NoPain",
            "RandomPlus.PanelOthers.HealthOptions.NoAddiction",
            "RandomPlus.PanelOthers.HealthOptions.AllowNone",
            //"RandomPlus.PanelOthers.HealthOptions.OnlyPositiveImplants",
        };

        public enum IncapableOptions { AllowAll, NoDumbLabor, AllowNone }
        public readonly static string[] IncapableOptionValues = new string[] {
            "RandomPlus.PanelOthers.IncapableOptions.AllowAll",
            "RandomPlus.PanelOthers.IncapableOptions.NoDumbLabor",
            "RandomPlus.PanelOthers.IncapableOptions.AllowNone"
        };

        public string name;

        // Exposed as the list itself rather than an iterator. These are read on every
        // candidate pawn during a search, and an iterator property allocates a state
        // machine on each call.
        private List<SkillContainer> skills = new List<SkillContainer>();
        public List<SkillContainer> Skills => skills;

        #region Traits
        private List<TraitContainer> traits = new List<TraitContainer>();
        public List<TraitContainer> Traits => traits;

        public void AddTrait(Trait trait)
        {
            traits.Add(new TraitContainer(trait));
        }

        public void TraitUpdated(int index, Trait trait)
        {
            traits[index].trait = trait;
        }

        public void TraitRemoved(Trait trait)
        {
            var needToRemoveTC = traits.FirstOrDefault(tc => tc.trait == trait);
            traits.Remove(needToRemoveTC);
        }
        #endregion

        private int _RequiredTraitsInPool = DefaultPoolSize;
        public int RequiredTraitsInPool { get => _RequiredTraitsInPool; set => _RequiredTraitsInPool = value; }

        public IntRange passionRange;
        public IntRange skillRange;
        public bool countOnlyHighestAttack;
        public bool countOnlyPassion;

        public IntRange ageRange;

        private Gender gender;
        public Gender Gender
        {
            get => gender;
            set => gender = value;
        }

        private int rerollLimit = (int)DefaultRerollLimit;
        public int RerollLimit
        {
            get => rerollLimit;
            set => rerollLimit = value;
        }

        private HealthOptions filterHealthCondition;
        public HealthOptions FilterHealthCondition
        {
            get => filterHealthCondition;
            set => filterHealthCondition = value;
        }

        private IncapableOptions filterIncapable;
        public IncapableOptions FilterIncapable
        {
            get => filterIncapable;
            set => filterIncapable = value;
        }

        /// <summary>
        /// Whether this filter constrains anything at all. The reroll limit is
        /// deliberately not counted: a limit with nothing to satisfy never rerolls.
        /// The count-only toggles are not counted either - they only change how the
        /// passion and skill ranges are tallied, so they do nothing while those
        /// ranges sit at their defaults, and any range that moved counts by itself.
        /// </summary>
        public bool HasActiveFilters
        {
            get
            {
                foreach (var skill in skills)
                {
                    if (skill.Passion != Passion.None || skill.MinValue > 0)
                        return true;
                }

                return traits.Count > 0
                    || _RequiredTraitsInPool != DefaultPoolSize
                    || passionRange.min != PassionMinDefault
                    || passionRange.max != PassionMaxDefault
                    || skillRange.min != SkillMinDefault
                    || skillRange.max != SkillMaxDefault
                    || ageRange.min != MinAgeDefault
                    || ageRange.max != MaxAgeDefault
                    || gender != Gender.None
                    || filterHealthCondition != HealthOptions.AllowAll
                    || filterIncapable != IncapableOptions.AllowAll;
            }
        }

        public PawnFilter()
        {
            ResetAll();
        }

        public void ResetSkills()
        {
            skills.Clear();
            foreach (var skilldef in DefDatabase<SkillDef>.AllDefs)
            {
                skills.Add(new SkillContainer(skilldef));
            }
            passionRange = new IntRange(PassionMinDefault, PassionMaxDefault);
            skillRange = new IntRange(SkillMinDefault, SkillMaxDefault);
            countOnlyHighestAttack = false;
            countOnlyPassion = false;
        }

        public void ResetTraits()
        {
            traits.Clear();
            RequiredTraitsInPool = DefaultPoolSize;
        }

        public void ResetOther()
        {
            gender = Gender.None;
            rerollLimit = (int)DefaultRerollLimit;
            filterHealthCondition = HealthOptions.AllowAll;
            filterIncapable = IncapableOptions.AllowAll;

            ageRange = new IntRange(MinAgeDefault, MaxAgeDefault);
        }

        public void ResetAll()
        {
            ResetSkills();
            ResetTraits();
            ResetOther();
        }

        public void ExposeData()
        {
            int version = 1;
            Scribe_Values.Look(ref this.name, "name", "");
            Scribe_Values.Look(ref version, "version", 1);
            Scribe_Collections.Look(ref this.skills, "skills", LookMode.Deep, null);
            Scribe_Collections.Look(ref this.traits, "traits", LookMode.Deep, null);

            Scribe_Values.Look(ref _RequiredTraitsInPool, "poolSize", DefaultPoolSize);

            Scribe_Values.Look(ref passionRange.min, "passionRangeMin", PassionMinDefault);
            Scribe_Values.Look(ref passionRange.max, "passionRangeMax", PassionMaxDefault);

            Scribe_Values.Look(ref skillRange.min, "skillRangeMin", SkillMinDefault);
            Scribe_Values.Look(ref skillRange.max, "skillRangeMax", SkillMaxDefault);

            Scribe_Values.Look(ref countOnlyHighestAttack, "countOnlyHighestAttack", false);
            Scribe_Values.Look(ref countOnlyPassion, "countOnlyPassion", false);

            Scribe_Values.Look(ref ageRange.min, "ageRangeMin", MinAgeDefault);
            Scribe_Values.Look(ref ageRange.max, "ageRangeMax", MaxAgeDefault);

            Scribe_Values.Look(ref rerollLimit, "rerollLimit", (int)DefaultRerollLimit);
            Scribe_Values.Look(ref gender, "gender", Gender.None);
            Scribe_Values.Look(ref filterHealthCondition, "healthCondition", HealthOptions.AllowAll);
            Scribe_Values.Look(ref filterIncapable, "incapable", IncapableOptions.AllowAll);

            // A hand-edited or truncated preset can load with a list node missing -
            // Scribe leaves the list null - or with entries whose defs left along
            // with a removed mod. Neither may reach the UI: a null list crashes the
            // card's filter-active check every frame, and a null-def trait matches
            // no pawn ever, so a Required one would burn the whole reroll budget
            // with no explanation.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (skills == null)
                    skills = new List<SkillContainer>();
                if (traits == null)
                    traits = new List<TraitContainer>();

                skills.RemoveAll(s => s == null || s.SkillDef == null);
                int dropped = traits.RemoveAll(t => t == null || t.trait?.def == null);
                if (dropped > 0)
                    ModLog.Warning($"Dropped {dropped} saved trait filter(s) whose trait no longer exists - a mod was removed?");
            }
        }
    }
}
