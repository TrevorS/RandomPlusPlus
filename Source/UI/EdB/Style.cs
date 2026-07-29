using UnityEngine;

namespace RandomPlus
{
    public static class Style
    {
        public static Color ColorText = new Color(0.80f, 0.80f, 0.80f);
        public static Color ColorTextPanelHeader = new Color(207f / 255f, 207f / 255f, 207f / 255f);

        public static Color ColorPanelBackground = new Color(36f / 255f, 37f / 255f, 38f / 255f);
        public static Color ColorPanelBackgroundItem = new Color(43f / 255f, 44f / 255f, 45f / 255f);

        public static Color ColorButton = new Color(0.623529f, 0.623529f, 0.623529f);
        public static Color ColorButtonHighlight = new Color(0.97647f, 0.97647f, 0.97647f);
        public static Color ColorButtonDisabled = new Color(0.27647f, 0.27647f, 0.27647f);
        public static Color ColorButtonSelected = new Color(1, 1, 1);

        public static Color ColorControlDisabled = new Color(1, 1, 1, 0.27647f);

        public static void SetGUIColorForButton(Rect rect)
        {
            if (rect.Contains(Event.current.mousePosition))
            {
                GUI.color = Style.ColorButtonHighlight;
            }
            else
            {
                GUI.color = Style.ColorButton;
            }
        }
        public static void SetGUIColorForButton(Rect rect, bool selected)
        {
            if (selected)
            {
                GUI.color = Style.ColorButtonSelected;
            }
            else
            {
                if (rect.Contains(Event.current.mousePosition))
                {
                    GUI.color = Style.ColorButtonHighlight;
                }
                else
                {
                    GUI.color = Style.ColorButton;
                }
            }
        }
        public static void SetGUIColorForButton(Rect rect, bool selected, Color color, Color hoverColor, Color selectedColor)
        {
            if (selected)
            {
                GUI.color = selectedColor;
            }
            else
            {
                if (rect.Contains(Event.current.mousePosition))
                {
                    GUI.color = hoverColor;
                }
                else
                {
                    GUI.color = color;
                }
            }
        }
    }
}
