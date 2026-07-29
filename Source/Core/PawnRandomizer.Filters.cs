using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RandomPlus
{
    /// <summary>
    /// Whether a given pawn satisfies the filter. Every method here is a pure
    /// question about a pawn, which is what makes them straightforward to test.
    /// </summary>
    public static partial class PawnRandomizer
    {
        public static bool CheckPawnIsSatisfied(Pawn pawn)
        {
            if (RandomRerollCounter() >= PawnFilter.RerollLimit)
            {
                return true;
            }
            // Ordered cheapest first, so a candidate that is going to be rejected is
            // rejected for the least work. Gender, age and work are a handful of integer
            // and flag comparisons; skills walks every skill on the pawn.
            if (!CheckGenderIsSatisfied(pawn))
                return false;
            if (!CheckAgeIsSatisfied(pawn))
                return false;
            if (!CheckWorkIsSatisfied(pawn))
                return false;
            if (!CheckHealthIsSatisfied(pawn))
                return false;
            if (!CheckTraitsIsSatisfied(pawn))
                return false;
            if (!CheckSkillsIsSatisfied(pawn))
                return false;
            return true;
        }

        public static bool CheckAgeIsSatisfied(Pawn pawn)
        {
            if (pawnFilter.ageRange.min != PawnFilter.MinAgeDefault ||
                pawnFilter.ageRange.max != PawnFilter.MaxAgeDefault)
            {
                if (pawnFilter.ageRange.min > pawn.ageTracker.AgeBiologicalYears ||
                    (pawnFilter.ageRange.max != PawnFilter.MaxAgeDefault && pawnFilter.ageRange.max < pawn.ageTracker.AgeBiologicalYears))
                    return false;
            }
            return true;
        }

        public static bool CheckGenderIsSatisfied(Pawn pawn)
        {
            if (pawnFilter.Gender != Gender.None && pawn.gender != Gender.None)
                if (pawnFilter.Gender != pawn.gender)
                    return false;
            return true;
        }

        public static bool CheckSkillsIsSatisfied(Pawn pawn)
        {
            List<SkillRecord> skillList = pawn.skills.skills;

            // Indexed loops throughout: this runs on every candidate pawn, and the LINQ
            // equivalents allocate a closure and a boxed list enumerator each time.
            var skillFilters = pawnFilter.Skills;
            for (int f = 0; f < skillFilters.Count; f++)
            {
                var skillFilter = skillFilters[f];
                if (skillFilter.Passion == Passion.None && skillFilter.MinValue <= 0)
                    continue;

                SkillRecord skillRecord = null;
                for (int i = 0; i < skillList.Count; i++)
                {
                    if (skillList[i].def == skillFilter.SkillDef)
                    {
                        skillRecord = skillList[i];
                        break;
                    }
                }

                // A pawn without a skill the filter names - a mod removed the skill,
                // or this pawn kind does not have it. Skipping the check is the only
                // sensible reading of a filter on a skill that does not exist here.
                if (skillRecord == null)
                {
                    ModLog.ErrorOnce(
                        $"Pawn has no {skillFilter.SkillDef?.defName} skill; that part of the filter is being ignored.",
                        StringComparer.Ordinal.GetHashCode(skillFilter.SkillDef?.defName ?? ""));
                    continue;
                }

                if (skillRecord.passion < skillFilter.Passion ||
                    skillRecord.Level < skillFilter.MinValue)
                {
                    return false;
                }
            }

            // handle total passion range
            if (pawnFilter.passionRange.min > PawnFilter.PassionMinDefault ||
                pawnFilter.passionRange.max < PawnFilter.PassionMaxDefault)
            {
                int totalPassions = 0;
                for (int i = 0; i < skillList.Count; i++)
                {
                    if (skillList[i].passion > 0)
                        totalPassions++;
                }
                if (totalPassions < pawnFilter.passionRange.min ||
                    totalPassions > pawnFilter.passionRange.max)
                {
                    return false;
                }
            }

            // handle total skill range
            if (pawnFilter.skillRange.min != PawnFilter.SkillMinDefault ||
                pawnFilter.skillRange.max != PawnFilter.SkillMaxDefault)
            {
                // Find shooting and melee by def rather than by list position. A pawn's
                // skills are listed in SkillDef database order, so any mod adding a
                // SkillDef that sorts ahead of shooting shifts every index along and the
                // wrong two skills get collapsed.
                SkillRecord shooting = null;
                SkillRecord melee = null;
                if (PawnFilter.countOnlyHighestAttack)
                {
                    for (int i = 0; i < skillList.Count; i++)
                    {
                        if (skillList[i].def == SkillDefOf.Shooting)
                            shooting = skillList[i];
                        else if (skillList[i].def == SkillDefOf.Melee)
                            melee = skillList[i];
                    }
                }

                int skillTotalCounter = 0;
                for (int i = 0; i < skillList.Count; i++)
                {
                    var skill = skillList[i];

                    // Counted together below, as the higher of the two.
                    if (skill == shooting || skill == melee)
                        continue;

                    if (PawnFilter.countOnlyPassion)
                    {
                        if (skill.passion > 0)
                            skillTotalCounter += skill.Level;
                    }
                    else
                    {
                        skillTotalCounter += skill.Level;
                    }
                }

                // Deliberately ignores countOnlyPassion, matching the original behaviour:
                // the higher attack skill counts whether or not it carries a passion.
                if (shooting != null || melee != null)
                {
                    int shootingLevel = shooting != null ? shooting.Level : 0;
                    int meleeLevel = melee != null ? melee.Level : 0;
                    skillTotalCounter += shootingLevel > meleeLevel ? shootingLevel : meleeLevel;
                }

                if (pawnFilter.skillRange.min > skillTotalCounter ||
                pawnFilter.skillRange.max < skillTotalCounter)
                    return false;
            }

            return true;
        }

        public static bool CheckTraitsIsSatisfied(Pawn pawn)
        {
            if (ModWellMetActive)
                return true;

            // handle required and exclude traits
            var traitFilterList = pawnFilter.Traits;
            int traitPoolSize = 0;
            for (int f = 0; f < traitFilterList.Count; f++)
            {
                var traitContainer = traitFilterList[f];

                switch (traitContainer.traitFilter)
                {
                    case TraitContainer.TraitFilterType.Required:
                        if (!HasTrait(pawn, traitContainer.trait))
                            return false;
                        break;
                    case TraitContainer.TraitFilterType.Excluded:
                        if (HasTrait(pawn, traitContainer.trait))
                            return false;
                        break;
                    case TraitContainer.TraitFilterType.Optional:
                        traitPoolSize++;
                        break;
                }
            }

            // handle trait pool (optional)
            // The pool is drawn from the optional traits alone, so the requirement has to
            // be measured against how many of those there are. Counting every filtered
            // trait let a pool larger than the optional set through, and no pawn could
            // ever satisfy it - the filter silently spent the whole reroll budget on
            // every randomize and handed back a pawn that ignored it.
            if (pawnFilter.RequiredTraitsInPool > 0 &&
                pawnFilter.RequiredTraitsInPool <= traitPoolSize)
            {
                int pawnHasTraitCounter = 0;
                for (int f = 0; f < traitFilterList.Count; f++)
                {
                    var traitContainer = traitFilterList[f];
                    if (traitContainer.traitFilter != TraitContainer.TraitFilterType.Optional)
                        continue;

                    if (HasTrait(pawn, traitContainer.trait))
                    {
                        pawnHasTraitCounter++;
                        if (pawnFilter.RequiredTraitsInPool == pawnHasTraitCounter)
                            break;
                    }
                }
                if (pawnHasTraitCounter < pawnFilter.RequiredTraitsInPool)
                    return false;
            }

            return true;
        }

        private static bool IsGeneAffectedHealth(Hediff hediff)
        {
            if (!ModsConfig.BiotechActive)
                return false;

            if (hediff is Hediff_ChemicalDependency chemicalDependency && chemicalDependency.LinkedGene != null)
                return true;

            return false;
        }

        public static bool CheckHealthIsSatisfied(Pawn pawn)
        {
            var option = pawnFilter.FilterHealthCondition;
            if (option == PawnFilter.HealthOptions.AllowAll)
                return true;

            // One indexed pass, rather than a LINQ query per option. Each of these runs on
            // every candidate that gets as far as having health generated.
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                var hediff = hediffs[i];
                if (IsGeneAffectedHealth(hediff))
                    continue;

                switch (option)
                {
                    case PawnFilter.HealthOptions.OnlyStartCondition:
                        if (hediff.def.defName != "CryptosleepSickness" &&
                            hediff.def.defName != "Malnutrition")
                            return false;
                        break;
                    case PawnFilter.HealthOptions.NoPain:
                        if (hediff.PainOffset > 0f)
                            return false;
                        break;
                    case PawnFilter.HealthOptions.NoAddiction:
                        if (hediff is Hediff_Addiction)
                            return false;
                        break;
                    case PawnFilter.HealthOptions.AllowNone:
                        return false;
                }
            }
            return true;
        }

        public static bool CheckWorkIsSatisfied(Pawn pawn)
        {
            // handle work options
            switch (pawnFilter.FilterIncapable)
            {
                case PawnFilter.IncapableOptions.AllowAll:
                    break;
                case PawnFilter.IncapableOptions.NoDumbLabor:
                    if ((pawn.story.DisabledWorkTagsBackstoryAndTraits & WorkTags.ManualDumb) == WorkTags.ManualDumb)
                        return false;
                    break;
                case PawnFilter.IncapableOptions.AllowNone:
                    if (pawn.story.DisabledWorkTagsBackstoryAndTraits != WorkTags.None)
                        return false;
                    break;
            }
            return true;
        }

        /// <summary>
        /// Whether <paramref name="pawn"/> has the given trait, matched on definition and
        /// degree.
        /// </summary>
        /// <remarks>
        /// This used to compare the traits' display labels. Definition and degree identify
        /// a trait exactly, without building and comparing strings for every filtered trait
        /// on every candidate pawn.
        /// </remarks>
        public static bool HasTrait(Pawn pawn, Trait trait)
        {
            var pawnTraits = pawn.story.traits.allTraits;
            for (int i = 0; i < pawnTraits.Count; i++)
            {
                var t = pawnTraits[i];
                if (t == null || trait == null)
                {
                    if (t == null && trait == null)
                        return true;
                    continue;
                }

                if (t.def == trait.def && t.Degree == trait.Degree)
                    return true;
            }
            return false;
        }

        public static void SetGenderFilter(Gender gender)
        {
            pawnFilter.Gender = gender;
        }

        private static float _cacheTraitCommonalityMale;
        private static float _cacheTraitCommonalityFemale;

        private static float GetTotalTraitCommonality(Gender gender)
        {
            if (gender == Gender.Male && _cacheTraitCommonalityMale > 0)
                return _cacheTraitCommonalityMale;
            if (gender == Gender.Female && _cacheTraitCommonalityFemale > 0)
                return _cacheTraitCommonalityFemale;

            float total = 0;
            foreach (var trait in DefDatabase<TraitDef>.AllDefsListForReading)
            {
                total += trait.GetGenderSpecificCommonality(gender);
            }

            if (gender == Gender.Male)
                _cacheTraitCommonalityMale = total;
            if (gender == Gender.Female)
                _cacheTraitCommonalityFemale = total;

            return total;
        }

        public static float GetTraitRollChance(TraitDef traitDef, Gender gender = Gender.Male)
        {
            float total = GetTotalTraitCommonality(gender);
            return traitDef.GetGenderSpecificCommonality(gender) * 100 / total;
        }

        public static string GetTraitRollChanceText(TraitDef traitDef)
        {
            string pecentMale = GetTraitRollChance(traitDef, Gender.Male).ToString("0.0");
            string pecentFemale = GetTraitRollChance(traitDef, Gender.Female).ToString("0.0");

            if (traitDef.GetGenderSpecificCommonality(Gender.Male) == traitDef.GetGenderSpecificCommonality(Gender.Female))
                return $"({pecentMale}%)";
            return $"(♂:{pecentMale}%,♀:{pecentFemale}%)";
        }

    }
}
