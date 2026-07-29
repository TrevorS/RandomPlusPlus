using System;
using UnityEngine;
using Verse;

namespace RandomPlus
{
    [StaticConstructorOnStartup]
    public static class Textures
    {
        public static Texture2D TexturePassionMajor;
        public static Texture2D TexturePassionMinor;
        public static Texture2D TextureFieldAtlas;
        public static Texture2D TextureButtonPrevious;
        public static Texture2D TextureButtonNext;
        public static Texture2D TexturePassionNone;
        public static Texture2D TextureButtonDelete;
        public static Texture2D TextureButtonReset;
        public static Texture2D TextureButtonClearSkills;
        public static Texture2D TextureAlertSmall;
        public static Texture2D TextureButtonAdd;
        public static Texture2D TextureRadioButtonOff;
        public static Texture2D TextureAlternateRow;
        public static Texture2D TextureSkillBarFill;

        public static Texture2D TextureHighlightRow;

        static Textures()
        {
            LoadTextures();
        }

        private static void LoadTextures()
        {
            TexturePassionMajor = ContentFinder<Texture2D>.Get("UI/Icons/PassionMajor", true);
            TexturePassionMinor = ContentFinder<Texture2D>.Get("UI/Icons/PassionMinor", true);
            TextureRadioButtonOff = ContentFinder<Texture2D>.Get("UI/Widgets/RadioButOff", true);
            TextureFieldAtlas = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/FieldAtlas", true);
            TextureButtonPrevious = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonPrevious", true);
            TextureButtonNext = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonNext", true);
            TexturePassionNone = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/NoPassion", true);
            TextureButtonDelete = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonDelete", true);
            TextureButtonReset = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonReset", true);
            TextureButtonClearSkills = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonClear", true);
            TextureAlertSmall = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/AlertSmall", true);
            TextureButtonAdd = ContentFinder<Texture2D>.Get("EdB/PrepareCarefully/ButtonAdd", true);

            TextureAlternateRow = SolidColorMaterials.NewSolidColorTexture(new Color(1, 1, 1, 0.05f));
            TextureSkillBarFill = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.1f));

            TextureHighlightRow = SolidColorMaterials.NewSolidColorTexture(new Color(1, 1, 1, 0.2f));
        }
    }
}

