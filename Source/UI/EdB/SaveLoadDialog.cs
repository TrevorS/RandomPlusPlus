using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    public class SaveLoadDialog : Window
    {
        protected const float DeleteButtonSpace = 5;
        protected const float MapDateExtraLeftMargin = 220;

        private static readonly Color ManualSaveTextColor = new Color(1, 1, 0.6f);

        protected const float MapEntrySpacing = 8;
        protected const float BoxMargin = 20;
        protected const float MapNameExtraLeftMargin = 15;
        protected const float MapEntryMargin = 6;

        private Vector2 scrollPosition = Vector2.zero;

        //protected string interactButLabel = "Error";
        //protected float bottomAreaHeight;

        protected static string Filename = "";
        private bool focusedColonistNameArea;

        private int selectedIndex = -1;

        public SaveLoadDialog()
        {
            this.closeOnCancel = true;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.forcePause = true;

            SaveLoader.LoadAll();

            // Without this, a preset that failed to load is just missing from the
            // list, which reads as "my preset is gone" rather than "something broke".
            if (SaveLoader.LastLoadDropped > 0)
            {
                Messages.Message(
                    "RandomPlus.SaveLoadDialog.SkippedUnreadable".Translate(SaveLoader.LastLoadDropped),
                    MessageTypeDefOf.RejectInput);
            }
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(600, 400);
            }
        }

        public override void PostClose()
        {
            GUI.FocusControl(null);
        }

        public override void DoWindowContents(Rect inRect)
        {
            int padding = 16;
            int scrollBarWidth = 20;
            Vector2 buttonSize = new Vector2(150, 30);
            Vector2 rowSize = new Vector2(inRect.width - buttonSize.x - padding - scrollBarWidth, 36);

            List<PawnFilter> list = PawnRandomizer.pawnFilterList.ToList();
            float listHeight = list.Count * rowSize.y;
            Rect listViewRect = new Rect(0, 0, rowSize.x, listHeight);

            inRect.height -= 40;

            Rect outRect = new Rect(0, 0, inRect.width - buttonSize.x - padding, inRect.height - buttonSize.y - padding - 20);
            Widgets.BeginScrollView(outRect, ref this.scrollPosition, listViewRect);
            try
            {
                float num2 = 0;
                int num3 = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    PawnFilter current = list[i];
                    Rect rect = new Rect(0, num2, rowSize.x, rowSize.y);
                    if (selectedIndex == i)
                    {
                        GUI.DrawTexture(rect, Textures.TextureHighlightRow);
                    }
                    else if (num3 % 2 == 0)
                    {
                        GUI.DrawTexture(rect, Textures.TextureAlternateRow);
                    }

                    Color color = selectedIndex == i ? new Color(0.7f, 0.7f, 0.7f, 1) : Color.white;
                    if (Widgets.ButtonText(rect, "", false, true, color))
                    {
                        selectedIndex = i;
                        Filename = current?.name ?? "";
                    }

                    Rect innerRect = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);

                    GUI.BeginGroup(innerRect);
                    try
                    {
                        GUI.color = ManualSaveTextColor;
                        Rect rect2 = new Rect(15, 0, rowSize.x, rowSize.y);
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Text.Font = GameFont.Small;
                        // Null-tolerant even though LoadAll scrubs failed entries: one
                        // null here used to abort the whole draw, leaving a window
                        // with a list and no buttons, field or footer.
                        Widgets.Label(rect2, current?.name ?? "");
                        GUI.color = Color.white;
                    }
                    finally
                    {
                        GUI.EndGroup();
                    }
                    num2 += rowSize.y;
                    num3++;
                }
            }
            finally
            {
                Widgets.EndScrollView();
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }

            // Start below the window's X: doCloseX draws it in the top-right corner,
            // over the first rows of this column, and a click there landed on Load.
            int buttonAreaTop = 36;
            Rect buttonAreaRect = new Rect(listViewRect.x + listViewRect.width + padding + scrollBarWidth, buttonAreaTop, buttonSize.x, outRect.height - buttonAreaTop);
            GUI.BeginGroup(buttonAreaRect);
            try
            {
                // Bounds, not just -1: the selection is an index into this frame's
                // copy of the list, and acting on it must never read past whatever
                // the list has shrunk to.
                bool hasSelection = selectedIndex >= 0 && selectedIndex < list.Count;
                if (!hasSelection)
                    GUI.enabled = false;

                Rect loadButtonRect = new Rect(0, 0, buttonSize.x, buttonSize.y);
                if (Widgets.ButtonText(loadButtonRect, "RandomPlus.SaveLoadDialog.LoadButton".Translate(), true, false, true) && hasSelection)
                {
                    SaveLoader.Load(list[selectedIndex]);
                    Close();
                }

                Rect deleteButtonRect = loadButtonRect.OffsetBy(new Vector2(0, buttonSize.y + padding));
                if (Widgets.ButtonText(deleteButtonRect, "RandomPlus.SaveLoadDialog.DeleteButton".Translate(), true, false, true) && hasSelection)
                {
                    SaveLoader.Delete(list[selectedIndex]);
                    selectedIndex = -1;
                }

                if (!hasSelection)
                    GUI.enabled = true;
            }
            finally
            {
                GUI.EndGroup();
            }


            this.DrawFooter(inRect.AtZero());
        }

        protected void DrawFooter(Rect inRect)
        {
            GUI.BeginGroup(inRect);
            bool flag = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            float top = inRect.height - 52;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.SetNextControlName("ColonistNameField");
            Rect rect = new Rect(5, top, 400, 35);
            string text = Widgets.TextField(rect, Filename);
            if (GenText.IsValidFilename(text))
            {
                Filename = text;
                var matchingIndex = PawnRandomizer.pawnFilterList.FindIndex(i => i?.name == Filename);
                if (matchingIndex >= 0)
                {
                    selectedIndex = matchingIndex;
                }
                else
                {
                    selectedIndex = -1;
                }
            }
            if (!this.focusedColonistNameArea)
            {
                GUI.FocusControl("ColonistNameField");
                this.focusedColonistNameArea = true;
            }


            Rect butRect = new Rect(420, top, inRect.width - 400 - 20, 35);

            GUI.SetNextControlName("SaveButton");
            string buttonName = selectedIndex >= 0 ? "RandomPlus.SaveLoadDialog.OverwriteButton".Translate() : "RandomPlus.SaveLoadDialog.SaveButton".Translate();
            if (Widgets.ButtonText(butRect, buttonName, true, false, true) || flag)
            {
                if (Filename.Length == 0)
                {
                    Messages.Message("NeedAName".Translate(), MessageTypeDefOf.RejectInput);
                }
                else if (selectedIndex >= 0)
                {
                    PawnRandomizer.PawnFilter.name = text;
                    SaveLoader.SaveOverwrite(selectedIndex, PawnRandomizer.PawnFilter);
                    Close(true);
                }
                else
                {
                    PawnRandomizer.PawnFilter.name = text;
                    SaveLoader.Save(PawnRandomizer.PawnFilter);
                    Close(true);
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.EndGroup();
        }

    }
}
