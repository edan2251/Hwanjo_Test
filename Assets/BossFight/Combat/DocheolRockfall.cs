using System;
using UnityEngine;

namespace FourGuardians.BossFight
{
    public enum RockfallSize { Small, Medium, Large }

    /// <summary>A fixed-position hazard, advanced only by the encounter's scaled tick.</summary>
    public sealed class DocheolRockfall : IDisposable
    {
        private const float FallDuration = 0.25f;
        private const float ImpactDuration = 0.14f;
        private const float DustDuration = 0.30f;
        private readonly float floorY;
        private readonly GameObject root;
        private readonly Sprite ellipseSprite, ringSprite, stoneSprite;
        private readonly Material material;
        private readonly SpriteRenderer shadow, warning, stone, facet;
        private readonly SpriteRenderer[] dust = new SpriteRenderer[5];
        private float elapsed;
        private bool disposed;

        public int AttackId { get; }
        public float LockedX { get; }
        public RockfallSize Size { get; }
        public bool Targeted { get; }
        public float Width { get; }
        public float WarningDuration { get; }
        public float Remaining => disposed ? 0f : Mathf.Max(0f, Lifetime - elapsed);
        public float TimeToImpact => disposed ? 0f : Mathf.Max(0f, ImpactAt - elapsed);
        public float ImpactRemaining => disposed ? 0f : Mathf.Max(0f, ImpactAt + ImpactDuration - elapsed);
        public string Stage { get; private set; } = "warning";
        public Bounds WarningBounds { get; }
        public Bounds ActiveBounds { get; private set; }
        public bool Finished => disposed || Stage == "done";
        public bool ImpactedThisTick { get; private set; }
        private float ImpactAt => WarningDuration + FallDuration;
        private float Lifetime => ImpactAt + ImpactDuration + DustDuration;

        public static float WidthFor(RockfallSize size)
        {
            switch (size)
            {
                case RockfallSize.Small: return 0.8f;
                case RockfallSize.Medium: return 1.25f;
                case RockfallSize.Large: return 1.8f;
                default: throw new ArgumentOutOfRangeException(nameof(size));
            }
        }

        public DocheolRockfall(int attackId, float x, float floorY, RockfallSize size, bool targeted, Transform parent)
        {
            AttackId = attackId;
            LockedX = x;
            this.floorY = floorY;
            Size = size;
            Targeted = targeted;
            Width = WidthFor(size);
            WarningDuration = size == RockfallSize.Small ? 0.95f : size == RockfallSize.Medium ? 1.10f : 1.25f;
            WarningBounds = new Bounds(new Vector3(x, floorY + 0.8f, 0f), new Vector3(Width, 1.6f, 1f));
            root = new GameObject("Docheol_Rockfall_" + attackId);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, floorY, 0f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                material = new Material(shader) { name = "Docheol_Rockfall_Unlit", mainTexture = Texture2D.whiteTexture };
            ellipseSprite = MakeShape("Rockfall_Ellipse", false, false);
            ringSprite = MakeShape("Rockfall_WarningRing", true, false);
            stoneSprite = MakeShape("Rockfall_Stone", false, true);
            shadow = MakeRenderer("FixedShadow", ellipseSprite, 7);
            warning = MakeRenderer("ExactWidthWarning", ringSprite, 9);
            stone = MakeRenderer("FallingStone", stoneSprite, 18);
            facet = MakeRenderer("StoneFacet", stoneSprite, 19);
            for (int i = 0; i < dust.Length; i++)
                dust[i] = MakeRenderer("GroundDust_" + i, ellipseSprite, 10);
            Render();
        }

        public void Tick(float dt)
        {
            ImpactedThisTick = false;
            if (Finished || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return;
            float before = elapsed;
            elapsed = Mathf.Min(Lifetime, elapsed + dt);
            ImpactedThisTick = before < ImpactAt && elapsed >= ImpactAt;
            Stage = elapsed < WarningDuration ? "warning"
                : elapsed < ImpactAt ? "fall"
                : elapsed < ImpactAt + ImpactDuration ? "impact"
                : elapsed < Lifetime ? "dust" : "done";
            // Preserve one hit if a long tick skips the impact, but never extend its trailing edge.
            ActiveBounds = Stage == "impact" || ImpactedThisTick ? WarningBounds : default;
            Render();
        }

        private void Render()
        {
            if (root == null) return;
            root.transform.position = new Vector3(LockedX, floorY, 0f);
            bool tell = Stage == "warning" || Stage == "fall";
            bool impact = ActiveBounds.size.x > 0f;
            bool clearing = Stage == "dust";
            float anticipation = Mathf.Clamp01(elapsed / ImpactAt);
            float fade = clearing ? Mathf.Clamp01((Lifetime - elapsed) / DustDuration) : 1f;
            float pulse = 0.78f + Mathf.Sin(elapsed * 17f) * 0.12f;
            // Neither pulse nor dust changes the advertised left/right limits.
            Draw(shadow, Stage != "done" || impact, 0f, 0.065f, Width, 0.18f,
                new Color(0.13f, 0.10f, 0.07f, (0.28f + anticipation * 0.34f) * fade));
            Draw(warning, tell || impact, 0f, 0.075f, Width, 0.20f,
                Targeted ? new Color(1f, 0.58f, 0.22f, pulse) : new Color(0.92f, 0.68f, 0.32f, pulse));
            float fallProgress = Mathf.Clamp01((elapsed - WarningDuration) / FallDuration);
            float height = Mathf.Min(1.55f, 0.7f + Width * 0.36f);
            float y = height * 0.5f + 7.5f * (1f - fallProgress * fallProgress);
            bool showStone = Stage == "fall" || impact;
            if (impact) y = height * 0.5f;
            Draw(stone, showStone, 0f, y, Width * 0.82f, height, new Color(0.36f, 0.32f, 0.25f));
            Draw(facet, showStone, -Width * 0.07f, y + height * 0.13f, Width * 0.47f, height * 0.47f,
                new Color(0.58f, 0.52f, 0.39f));
            for (int i = 0; i < dust.Length; i++)
            {
                float drift = Mathf.Sin(elapsed * 5f + i * 1.7f) * 0.02f;
                float localX = (i - 2) * Width * 0.19f;
                float dustHeight = tell ? 0.055f + anticipation * 0.06f : 0.22f + (1f - fade) * 0.22f;
                Draw(dust[i], tell || impact || clearing, localX, 0.07f + dustHeight * 0.5f + drift,
                    Width * 0.20f, dustHeight, new Color(0.61f, 0.51f, 0.35f, (tell ? 0.20f + anticipation * 0.22f : 0.65f * fade)));
            }
        }

        private SpriteRenderer MakeRenderer(string name, Sprite sprite, int order)
        {
            var obj = new GameObject(name, typeof(SpriteRenderer));
            obj.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = obj.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            if (material != null) renderer.sharedMaterial = material;
            return renderer;
        }

        private static void Draw(SpriteRenderer renderer, bool visible, float x, float y, float width, float height, Color color)
        {
            renderer.enabled = visible;
            if (!visible) return;
            renderer.transform.localPosition = new Vector3(x, y, 0f);
            renderer.transform.localScale = new Vector3(width, height, 1f);
            renderer.color = color;
        }

        private static Sprite MakeShape(string name, bool ring, bool stone)
        {
            Texture2D white = Texture2D.whiteTexture;
            Sprite sprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height),
                new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            int sides = stone ? 8 : 24;
            Vector2[] vertices = new Vector2[ring ? sides * 2 : sides + 1];
            ushort[] triangles = new ushort[sides * (ring ? 6 : 3)];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * 2f * Mathf.PI / sides;
                float radius = stone && (i & 1) == 1 ? 0.43f : 0.5f;
                Vector2 outer = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (ring)
                {
                    vertices[i * 2] = outer;
                    vertices[i * 2 + 1] = outer * 0.80f;
                    int next = (i + 1) % sides;
                    int index = i * 6;
                    triangles[index] = (ushort)(i * 2);
                    triangles[index + 1] = (ushort)(next * 2);
                    triangles[index + 2] = (ushort)(i * 2 + 1);
                    triangles[index + 3] = (ushort)(next * 2);
                    triangles[index + 4] = (ushort)(next * 2 + 1);
                    triangles[index + 5] = (ushort)(i * 2 + 1);
                }
                else
                {
                    vertices[i + 1] = outer;
                    int index = i * 3;
                    triangles[index] = 0;
                    triangles[index + 1] = (ushort)(i + 1);
                    triangles[index + 2] = (ushort)((i + 1) % sides + 1);
                }
            }
            // https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Sprite.OverrideGeometry.html
            // OverrideGeometry expects pixel coordinates within Sprite.rect, not pivot-relative units.
            // Unity applies the centered pivot and pixels-per-unit conversion after this call.
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector2(
                    Mathf.Clamp01(vertices[i].x + 0.5f) * sprite.rect.width,
                    Mathf.Clamp01(vertices[i].y + 0.5f) * sprite.rect.height);
            }
            sprite.OverrideGeometry(vertices, triangles);
            return sprite;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stage = "done";
            ActiveBounds = default;
            ImpactedThisTick = false;
            if (root != null) root.SetActive(false);
            DestroyOwned(root);
            DestroyOwned(ellipseSprite);
            DestroyOwned(ringSprite);
            DestroyOwned(stoneSprite);
            DestroyOwned(material);
            // Texture2D.whiteTexture is Unity-owned and is intentionally never destroyed.
        }

        private static void DestroyOwned(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
