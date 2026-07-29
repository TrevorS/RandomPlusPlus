using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    /// <summary>
    /// Draws a search panel over the portrait area while a search is rerolling that
    /// pawn, instead of letting the card draw the pawn itself.
    /// </summary>
    /// <remarks>
    /// Mid-search, the pawn on the card is whatever the last rejected candidate left
    /// behind - a fresh age on the previous candidate's traits, a name that only
    /// changes when a candidate survives the age check, a portrait invalidated every
    /// few candidates. The search cannot leave a coherent pawn at every frame without
    /// paying for full generation per candidate, which is exactly the cost it exists
    /// to avoid. So while it runs, the card's portrait area shows a deliberate
    /// display instead: the live reroll count, and the latest candidate's name
    /// sampled at a readable pace rather than at whatever rate candidates fail.
    ///
    /// Skipping the portrait area entirely also skips re-rendering the portrait of a
    /// pawn that is mutating hundreds of times a second.
    /// </remarks>
    [HarmonyPatch(typeof(StartingPawnUtility), "DrawPortraitArea")]
    static class Patch_SearchOverlay
    {
        // Slot machines read at about this pace; candidate churn is far faster.
        private const float SamplePeriodSeconds = 0.12f;

        private static string sampledCandidate = "";
        private static float nextSampleAt;

        [HarmonyPrefix]
        static bool Prefix(Rect rect, int pawnIndex)
        {
            if (!PawnRandomizer.SearchInProgress || pawnIndex != PawnRandomizer.SearchingPawnIndex)
            {
                sampledCandidate = "";
                nextSampleAt = 0f;
                return true;
            }

            if (Time.realtimeSinceStartup >= nextSampleAt)
            {
                nextSampleAt = Time.realtimeSinceStartup + SamplePeriodSeconds;
                var pawn = PawnRandomizer.SearchingPawn;
                string name = pawn?.Name?.ToStringShort;
                sampledCandidate = string.IsNullOrEmpty(name)
                    ? ""
                    : $"{name}, {pawn.ageTracker.AgeBiologicalYears}";
            }

            Widgets.DrawMenuSection(rect);

            var previousAnchor = Text.Anchor;
            var previousFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;

            Text.Font = GameFont.Small;
            Widgets.Label(Band(rect, 0.30f), "RandomPlus.SearchOverlay.Title".Translate());

            Text.Font = GameFont.Medium;
            Widgets.Label(Band(rect, 0.42f),
                $"{PawnRandomizer.RandomRerollCounter():N0} / {PawnRandomizer.PawnFilter.RerollLimit:N0}");

            Text.Font = GameFont.Small;
            Widgets.Label(Band(rect, 0.54f), sampledCandidate);

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(Band(rect, 0.66f), "RandomPlus.SearchOverlay.Hint".Translate());
            GUI.color = Color.white;

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            return false;
        }

        private static Rect Band(Rect rect, float fraction) =>
            new Rect(rect.x, rect.y + (rect.height * fraction), rect.width, 34f);
    }
}
