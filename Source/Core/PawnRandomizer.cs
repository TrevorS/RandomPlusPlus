using Verse;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RandomPlus
{
    /// <summary>
    /// The filter currently being applied, and the game hooks the search needs.
    /// </summary>
    public static partial class PawnRandomizer
    {
        // PawnGenerator's generation steps are private, so they have to be reached by
        // reflection. They are bound once to delegates rather than invoked through
        // MethodInfo: a search calls them thousands of times, and MethodInfo.Invoke
        // costs an object[] plus a box of the PawnGenerationRequest struct on every
        // call, on top of being far slower than a direct one.
        delegate void PawnGenStep(Pawn pawn, PawnGenerationRequest request);
        delegate void PawnGeneStep(Pawn pawn, XenotypeDef xenotype, PawnGenerationRequest request);

        static PawnGenStep generateAge;
        static PawnGenStep generateTraits;
        static PawnGenStep generateSkills;
        static PawnGenStep generateHealth;
        static PawnGenStep generateBodyType;
        static PawnGenStep generateGear;
        static PawnGeneStep generateGenes;

        static Func<List<Pawn>> startingAndOptionalPawns;

        /// <summary>
        /// Whether another mod has patched the generation entry points the in-place path
        /// skips. Wired up at startup by GenerationCompatibility; the default keeps this
        /// type free of any dependency on Harmony so it can be tested off the game.
        /// </summary>
        public static Func<bool> ForeignPatchesOnGenerationEntryPoints = () => false;

        /// <summary>
        /// Whether gear generation may be skipped for candidate pawns. False when another
        /// mod has patched it and would expect to see every pawn.
        /// </summary>
        public static Func<bool> GearSuppressionIsSafe = () => false;

        /// <summary>
        /// Read by the Harmony prefix on PawnGenerator.GenerateGearFor. Only ever true
        /// while a candidate pawn is being generated.
        /// </summary>
        public static bool SuppressGearGeneration;

        /// <summary>
        /// Well Met hides pawn traits until a colonist is befriended, so filtering on
        /// them would defeat the point of that mod. Evaluated in <see cref="Init"/>,
        /// which runs when the starting pawn page opens - it used to be set only when
        /// the filter editor window was opened, so a filter loaded from disk still
        /// applied its trait rules if the user never opened that window.
        /// </summary>
        public static bool ModWellMetActive;

        // Set when the pawn in hand was generated with gear suppressed, and so still owes
        // its apparel, weapons and inventory.
        private static bool gearGenerationPending;

        public static int randomRerollCounter = 0;

        public static List<PawnFilter> pawnFilterList = new List<PawnFilter>();

        private static PawnFilter pawnFilter;
        public static PawnFilter PawnFilter
        {
            get { return pawnFilter; }
            set { pawnFilter = value; }
        }

        public static int RandomRerollCounter()
        {
            return randomRerollCounter;
        }

        public static void Init()
        {
            pawnFilter = new PawnFilter();
            ModWellMetActive = ModsConfig.IsActive("Lakuna.WellMet");

            // Bound by name, so a RimWorld update that renames or reshapes one of these is
            // caught here rather than at the call site. Tools/RandomPlus.Verify checks them
            // against real game metadata at build time.
            try
            {
                generateAge = BindStep("GenerateRandomAge");
                generateTraits = BindStep("GenerateTraits");
                generateSkills = BindStep("GenerateSkills");
                generateHealth = BindStep("GenerateInitialHediffs");
                generateBodyType = BindStep("GenerateBodyType");
                generateGear = BindStep("GenerateGearFor");

                var genesMethod = typeof(PawnGenerator)
                    .GetMethod("GenerateGenes", BindingFlags.NonPublic | BindingFlags.Static);
                generateGenes = genesMethod == null
                    ? null
                    : (PawnGeneStep)Delegate.CreateDelegate(typeof(PawnGeneStep), genesMethod);

                var pawnsGetter = typeof(StartingPawnUtility)
                    .GetProperty("StartingAndOptionalPawns", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetGetMethod(true);
                startingAndOptionalPawns = pawnsGetter == null
                    ? null
                    : (Func<List<Pawn>>)Delegate.CreateDelegate(typeof(Func<List<Pawn>>), pawnsGetter);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to initialize reflection methods: {ex.Message}");
            }
        }

        private static PawnGenStep BindStep(string name)
        {
            var method = typeof(PawnGenerator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                ModLog.Warning($"PawnGenerator.{name} not found; that generation step will be skipped.");
                return null;
            }
            return (PawnGenStep)Delegate.CreateDelegate(typeof(PawnGenStep), method);
        }

        public static void ResetRerollCounter()
        {
            randomRerollCounter = 0;
        }
    }
}
