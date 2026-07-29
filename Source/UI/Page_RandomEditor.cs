using RimWorld;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    public class Page_RandomEditor : Page
    {
        PanelSkills panelSkills;
        PanelTraits panelTraits;
        PanelOthers panelOthers;

        private const float WindowWidth = 694f;
        private const float WindowHeight = 40f + 590f;

        private const int ButtonWidth = 100;
        private const int ButtonHeight = 20;

        // Window contents are drawn in RimWorld's own UI units - Prefs.UIScale is applied
        // to the GUI matrix before this runs. These used to be divided by it, which put
        // both buttons in the middle of the window at any scale other than 1.
        private static readonly Rect RectButtonResetAll =
            new Rect(WindowWidth - (ButtonWidth + 50), ButtonHeight - 8, ButtonWidth, ButtonHeight);

        private static readonly Rect RectButtonSaveLoad =
            new Rect(WindowWidth - ((ButtonWidth * 2) + 60), ButtonHeight - 8, ButtonWidth, ButtonHeight);

        public Page_RandomEditor()
        {
            this.closeOnCancel = true;
            this.closeOnAccept = true;
            this.closeOnClickedOutside = true;
            this.doCloseButton = true;
            this.doCloseX = true;
        }

        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);

        public override string PageTitle
        {
            get
            {
                return "RandomPlus.RandomEditor.Header".Translate();
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();

            panelSkills = new PanelSkills();
            panelTraits = new PanelTraits();
            panelOthers = new PanelOthers();
        }

        public override void DoWindowContents(Rect inRect)
        {
            this.DrawPageTitle(inRect);

            // Draw panels
            try
            {
                panelSkills?.Draw();
                panelTraits?.Draw();
                panelOthers?.Draw();
            }
            catch (System.Exception ex)
            {
                ModLog.Error($"Error drawing panels: {ex.Message}");
            }

            try
            {
                if (Widgets.ButtonText(RectButtonSaveLoad, "RandomPlus.RandomEditor.SaveLoadButton".Translate(), true, false, true))
                {
                    Find.WindowStack.Add(new SaveLoadDialog());
                }

                if (Widgets.ButtonText(RectButtonResetAll, "RandomPlus.RandomEditor.ResetAllButton".Translate(), true, true, true))
                {
                    PawnRandomizer.PawnFilter?.ResetAll();
                }
            }
            catch (System.Exception ex)
            {
                ModLog.Error($"Error drawing buttons: {ex.Message}");
            }
        }
    }
}
