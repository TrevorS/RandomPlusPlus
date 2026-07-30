using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    [StaticConstructorOnStartup]
    class HarmonyPatches
    {
        static HarmonyPatches()
        {
            InstallGuard.WarnIfDuplicateTypesLoaded();

            var harmony = new Harmony(GenerationCompatibility.HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            GenerationCompatibility.Install();
        }
    }

    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "PreOpen")]
    class Patch_InitPawnRandomizer
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            PawnRandomizer.Init();
            PawnRandomizer.ResetRerollCounter();
        }
    }

    [HarmonyPatch(typeof(StartingPawnUtility), "RandomizePawn")]
    class Patch_RandomizeMethod
    {
        // How long one frame may spend searching. A frame is ~16ms at 60fps, so this
        // costs visible frame rate while a search runs - but the window keeps drawing
        // and taking input, which is the point. Short searches still finish inside
        // the click that started them.
        internal const int SearchBudgetMillis = 25;

        [HarmonyPrefix]
        static bool Prefix(int pawnIndex)
        {
            if (!TutorSystem.AllowAction((EventPack)nameof(StartingPawnUtility.RandomizePawn)))
                return false;

            // One search at a time. A click during a search is dropped, not queued.
            if (PawnRandomizer.SearchInProgress)
                return false;

            PawnRandomizer.BeginReroll(pawnIndex);
            PawnRandomizer.PumpSearch(SearchBudgetMillis);
            SearchSample.AfterPump();

            // Vanilla notifies after its single roll. Ours may still be running, but
            // the tutor event only records that the action was used, which is true
            // from the moment the search starts.
            TutorSystem.Notify_Event((EventPack)nameof(StartingPawnUtility.RandomizePawn));

            return false;
        }
    }

    // Advances a search that outlived its click, one time slice per frame, so the
    // game keeps rendering and taking input while a long search runs. Previously
    // the whole search ran inside the click's GUI event, and a large reroll limit
    // froze the window for seconds - a beachball on macOS. The session aborts
    // itself if the window it was started from closes, so a pawn can never enter
    // the game half-rerolled.
    [HarmonyPatch(typeof(WindowStack), "WindowStackOnGUI")]
    class Patch_PumpRerollSearch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            // OnGUI runs several times per frame - layout, repaint, one call per
            // input event. Repaint happens exactly once, so gating on it pumps the
            // search once per frame.
            if (Event.current.type != EventType.Repaint)
                return;

            if (PawnRandomizer.SearchInProgress)
                PawnRandomizer.PumpSearch(Patch_RandomizeMethod.SearchBudgetMillis);

            // Unconditional: this is also what settles the display after the search
            // ends, however it ends - completion, the stop button, a closed window.
            SearchSample.AfterPump();
        }
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "DrawCharacterCard")]
    class Patch_RandomEditButton
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int startIndex = -1;

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_1 &&
                    codes[i + 1].opcode == OpCodes.Brfalse)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                var methodInfo = typeof(Patch_RandomEditButton)
                    .GetMethod("InjectCustomUI", BindingFlags.Public | BindingFlags.Static);
                codes.Insert(startIndex + 2, new CodeInstruction(OpCodes.Call, methodInfo));
            }

            return codes;
        }

        public static void InjectCustomUI()
        {
            // Coordinates here are in RimWorld's own UI units. Prefs.UIScale is applied to
            // the whole GUI matrix before any of this draws, so scaling these by it again
            // would move them off the card.
            Rect editButtonRect = new Rect(540f, 6f, 50f, 30f);
            if (ModsConfig.IsActive("hahkethomemah.simplepersonalities"))
                editButtonRect.x -= 130f;

            if (Widgets.ButtonText(editButtonRect, "RandomPlus.FilterButton".Translate(), true, false, true))
            {
                var page = new Page_RandomEditor();
                Find.WindowStack.Add(page);
            }

            Rect rerollLabelRect = new Rect(640f, 4f, 200f, 30f);
            if (ModsConfig.IdeologyActive)
                rerollLabelRect.y += 40f;
            if (ModsConfig.BiotechActive)
                rerollLabelRect.y += 60f;

            if (PawnRandomizer.PawnFilter == null)
                PawnRandomizer.Init();

            string labelText = "RandomPlus.RerollLabel".Translate() + PawnRandomizer.RandomRerollCounter() + "/" + PawnRandomizer.PawnFilter.RerollLimit;

            var tmpSave = GUI.color;
            if (PawnRandomizer.RandomRerollCounter() >= PawnRandomizer.PawnFilter.RerollLimit)
                GUI.color = Color.red;
            Widgets.Label(rerollLabelRect, labelText);
            GUI.color = tmpSave;
        }
    }

    // A dev-mode shortcut: hold the keybinding on the main menu to skip straight
    // through world creation to the starting-pawns page, with the filter editor open.
    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    class Patch_DoMainMenuControls
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int startIndex = -1;

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Newobj &&
                    codes[i + 1].opcode == OpCodes.Stloc_2)
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex != -1)
            {
                var newCode = new List<CodeInstruction>();
                newCode.Add(new CodeInstruction(OpCodes.Ldloc_2));
                var methodInfo = typeof(Patch_DoMainMenuControls)
                    .GetMethod("AddQuickGoToConfigPawnPage", BindingFlags.Public | BindingFlags.Static);
                newCode.Add(new CodeInstruction(OpCodes.Call, methodInfo));
                codes.InsertRange(startIndex + 2, newCode);
            }

            return codes;
        }

        public static void AddQuickGoToConfigPawnPage(List<ListableOption> optList)
        {
            if (Event.current.type == EventType.KeyDown)
            {
                KeyBindingDef quickKey = DefDatabase<KeyBindingDef>.GetNamed("Dev_QuickGoToConfigPawnPage");
                if (quickKey.JustPressed)
                {
                    Patch_DoMainMenuControls.GoToConfigPawnPage();
                }
            }
        }

        public static void GoToConfigPawnPage()
        {
            try
            {
                var page_select_scenario = new Page_SelectScenario();
                Find.WindowStack.Add(page_select_scenario);

                var methodInfo0 = typeof(Page_SelectScenario).GetMethod("CanDoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                methodInfo0?.Invoke(page_select_scenario, new object[0]);
                var methodInfo1 = typeof(Page_SelectScenario).GetMethod("DoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                methodInfo1?.Invoke(page_select_scenario, new object[0]);

                var page_storyteller = (Page_SelectStoryteller)page_select_scenario.next;

                var page_storyteller_methodInfo0 = typeof(Page_SelectStoryteller).GetMethod("CanDoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                page_storyteller_methodInfo0?.Invoke(page_storyteller, new object[0]);
                var page_storyteller_methodInfo1 = typeof(Page_SelectStoryteller).GetMethod("DoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                page_storyteller_methodInfo1?.Invoke(page_storyteller, new object[0]);

                var page_create_world = (Page_CreateWorldParams)page_storyteller.next;

                var prop = typeof(Page_CreateWorldParams).GetField("planetCoverage", BindingFlags.NonPublic | BindingFlags.Instance);
                prop?.SetValue(page_create_world, 0.1f);

                var page_create_world_methodInfo0 = typeof(Page_CreateWorldParams).GetMethod("CanDoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                page_create_world_methodInfo0?.Invoke(page_create_world, new object[0]);

                var page_select_site = (Page_SelectStartingSite)page_create_world.next;

                LongEventHandler.QueueLongEvent(() =>
                {
                    while (Find.World == null) System.Threading.Thread.Sleep(100);
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try
                        {
                            Find.WorldInterface.SelectedTile = RimWorld.Planet.TileFinder.RandomStartingTile();
                        }
                        catch
                        {
                            // Fallback if API changed
                            Find.WorldInterface.SelectedTile = Rand.Range(0, Find.WorldGrid.TilesCount);
                        }

                        var page_select_site_methodInfo0 = typeof(Page_SelectStartingSite).GetMethod("CanDoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                        page_select_site_methodInfo0?.Invoke(page_select_site, new object[0]);
                        var page_create_world_methodInfo1 = typeof(Page_SelectStartingSite).GetMethod("DoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                        page_create_world_methodInfo1?.Invoke(page_select_site, new object[0]);

                        if (ModsConfig.IdeologyActive)
                        {
                            var page_ideo = (Page_ChooseIdeoPreset)page_select_site.next;
                            var allIdeo = DefDatabase<IdeoPresetDef>.AllDefs;
                            var page_ideo_select_field = typeof(Page_ChooseIdeoPreset).GetField("selectedIdeo", BindingFlags.NonPublic | BindingFlags.Instance);
                            page_ideo_select_field?.SetValue(page_ideo, allIdeo.RandomElement());

                            var page_ideo_methodInfo0 = typeof(Page_ChooseIdeoPreset).GetMethod("CanDoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                            page_ideo_methodInfo0?.Invoke(page_ideo, new object[0]);
                            var page_ideo_methodInfo1 = typeof(Page_ChooseIdeoPreset).GetMethod("DoNext", BindingFlags.NonPublic | BindingFlags.Instance);
                            page_ideo_methodInfo1?.Invoke(page_ideo, new object[0]);
                        }

                        var page = new Page_RandomEditor();
                        Find.WindowStack.Add(page);
                    });
                }, null, true, null, false);
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to launch quick config page: {ex.Message}");
            }
        }
    }
}
