using UnityEngine;

namespace FourGuardians.BossFight
{
    // Shared anatomical trajectory: the attached upper body and open mouth rotate
    // around the grounded root. Only the downswing/contact has damage.
    public static class DocheolWholeMotion
    {
        public static float Angle(int side, float downswing)
            => Mathf.Lerp(side * 30f, -side * 30f, Mathf.SmoothStep(0f, 1f, downswing));

        public static Vector2 Center(int side, float downswing, float along)
        {
            Vector2 body = new Vector2(side * .55f, 1f);
            Vector2 mouth = new Vector2(side * 2.6f, 1.65f);
            return Quaternion.Euler(0, 0, Angle(side, downswing)) * Vector2.Lerp(body, mouth, along);
        }
        public static Vector2 Radius(float along) => new Vector2(Mathf.Lerp(1f, 1.4f, along), Mathf.Lerp(.75f, .70f, along));

        public static Bounds Bounds(Vector2 root, int side, float downswing)
        {
            Bounds result = default;
            for (int i = 0; i <= 4; i++)
            {
                float along = i / 4f; Vector2 center = root + Center(side, downswing, along), radius = Radius(along);
                Bounds b = new Bounds(center, new Vector3(radius.x * 2, radius.y * 2, .1f));
                if (i == 0) result = b; else result.Encapsulate(b);
            }
            return result;
        }
        public static bool Intersects(Vector2 root, int side, float downswing, Bounds hurt)
        {
            for (int i = 0; i <= 4; i++)
            {
                float along = i / 4f; Vector2 center = root + Center(side, downswing, along), r = Radius(along);
                float x = Mathf.Clamp(center.x, hurt.min.x, hurt.max.x) - center.x;
                float y = Mathf.Clamp(center.y, hurt.min.y, hurt.max.y) - center.y;
                if (x*x/(r.x*r.x) + y*y/(r.y*r.y) <= 1f) return true;
            }
            return false;
        }
    }
}
