using System;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RandomPlus
{
    public class ProviderTraits
    {

        /// <summary>One Trait per degree the definition offers, or a single degree-zero
        /// trait when it has none.</summary>
        private static IEnumerable<Trait> ToTraits(TraitDef traitDef)
        {
            List<TraitDegreeData> degreeData = traitDef.degreeDatas;
            int count = degreeData.Count;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    yield return new Trait(traitDef, degreeData[i].degree, true);
                }
            }
            else
            {
                yield return new Trait(traitDef, 0, true);
            }
        }
        private static ProviderTraits _instance;

        private List<Trait> traits = new List<Trait>();
        private List<Trait> sortedTraits = new List<Trait>();

        public static List<Trait> Traits
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProviderTraits();
                }
                return _instance.sortedTraits;
            }
        }
        private ProviderTraits()
        {
            // Get all trait options.  If a traits has multiple degrees, create a separate trait for each degree.
            foreach (TraitDef def in DefDatabase<TraitDef>.AllDefs)
            {
                foreach (var trait in ToTraits(def))
                {
                    traits.Add(trait);
                }
            }

            // Create a sorted version of the trait list.
            sortedTraits = new List<Trait>(traits);
            // Sorted for a human reading the list, so ordering follows the player's
            // locale rather than raw code points.
#pragma warning disable CA1309 // Use ordinal string comparison
            sortedTraits.Sort((t1, t2) => string.Compare(t1.LabelCap, t2.LabelCap, StringComparison.CurrentCulture));
#pragma warning restore CA1309
        }
    }
}
