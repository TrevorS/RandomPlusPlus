using UnityEngine;

namespace RandomPlus
{
    public static class ExtensionsRect
    {
        public static float MiddleY(this Rect rect)
        {
            return rect.y + rect.height * 0.5f;
        }
        public static float HalfHeight(this Rect rect)
        {
            return rect.height * 0.5f;
        }
        public static Rect OffsetBy(this Rect rect, Vector2 offset)
        {
            return new Rect(rect.position + offset, rect.size);
        }
        public static Rect OffsetBy(this Rect rect, float x, float y)
        {
            return new Rect(rect.position + new Vector2(x, y), rect.size);
        }
        public static Rect MoveTo(this Rect rect, Vector2 position)
        {
            return new Rect(position, rect.size);
        }
        public static Rect MoveTo(this Rect rect, float x, float y)
        {
            return new Rect(new Vector2(x, y), rect.size);
        }
        public static Rect InsetBy(this Rect rect, float left, float top, float right, float bottom)
        {
            return new Rect(rect.x + left, rect.y + top, rect.width - left - right, rect.height - top - bottom);
        }
        public static Rect InsetBy(this Rect rect, Vector2 topLeft, Vector2 bottomRight)
        {
            return new Rect(rect.x + topLeft.x, rect.y + topLeft.y, rect.width - topLeft.x - bottomRight.x, rect.height - topLeft.y - bottomRight.y);
        }
        public static Rect InsetBy(this Rect rect, float amount)
        {
            return rect.InsetBy(amount, amount, amount, amount);
        }
        public static Rect InsetBy(this Rect rect, Vector2 amount)
        {
            return rect.InsetBy(amount, amount);
        }
        public static Rect InsetBy(this Rect rect, float xAmount, float yAmount)
        {
            return rect.InsetBy(new Vector2(xAmount, yAmount), new Vector2(xAmount, yAmount));
        }
    }
}
