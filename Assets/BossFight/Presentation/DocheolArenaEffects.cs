using System.Collections.Generic;
using UnityEngine;

namespace FourGuardians.BossFight
{
    /// <summary>Decorative, scene-owned intro and burrow effects. Never creates or changes floor collision.</summary>
    [DisallowMultipleComponent]
    public sealed class DocheolArenaEffects : MonoBehaviour
    {
        private const int PieceLimit = 56;
        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Pickup> pickups = new List<Pickup>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly System.Random random = new System.Random(60129);
        private BossFightSession session;
        private Transform effectRoot, mound;
        private Material material;
        private readonly Dictionary<Texture2D, Material> textureMaterials = new Dictionary<Texture2D, Material>();

        private Sprite leafSprite, rockSprite, dustSprite;
        private bool configured;
        private string introStage = "done", burrowStage = "none";
        private float introProgress, burrowProgress, emissionTimer;
        private Vector2 bossPosition, burrowPosition, mouthPosition;
        private float FloorY => session != null && session.Boss != null ? session.Boss.FloorY : 0f;
        private int Facing => session != null && session.Boss != null ? session.Boss.Facing : -1;

        private sealed class Piece
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector2 Start, Velocity;
            public Vector3 Scale;
            public Color Color;
            public float Age, Lifetime, Spin;
            public bool Suction, Intro;
        }

        private sealed class Pickup
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector2 Offset;
            public Vector3 Scale;
            public float TakeAt, Spin;
        }

        public void Configure(BossFightSession owner)
        {
            session = owner;
            if (configured) return;
            configured = true;
            var root = new GameObject("Docheol_DecorativeEffects");
            root.transform.SetParent(transform, false);
            effectRoot = root.transform;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader) { name = "Docheol_ArenaEffects_Unlit", mainTexture = Texture2D.whiteTexture };
                material.enableInstancing = false;
            }
            leafSprite = MakeSprite("Leaf", 0);
            rockSprite = MakeSprite("Stone", 1);
            dustSprite = MakeSprite("Dust", 2);
            BuildMound();
            BuildPickups();
            ResetEffects();
        }

        public void SetIntro(string stage, float progress, Vector2 bossPos)
        {
            if (!configured) return;
            stage = stage ?? "done";
            bool changed = introStage != stage;
            introStage = stage;
            introProgress = Mathf.Clamp01(progress);
            bossPosition = bossPos;
            // The visual's left-facing mouth is about 1.25 units forward and 1.55 above the floor.
            mouthPosition = new Vector2(bossPos.x + Facing * 1.25f, FloorY + 1.55f);
            if (changed) emissionTimer = 0f;
            if (stage == "done")
            {
                mound.gameObject.SetActive(false);
                foreach (Pickup pickup in pickups) pickup.Transform.gameObject.SetActive(false);
                if (changed) ClearPieces(true);
                return;
            }

            float height = stage == "buried" ? 1.30f
                : stage == "emerge" ? Mathf.Lerp(1.30f, 0.82f, introProgress)
                : stage == "inhale" ? Mathf.Lerp(0.82f, 0.35f, introProgress)
                : stage == "roar" ? Mathf.Lerp(0.35f, 0.12f, introProgress) : 0.35f;
            mound.gameObject.SetActive(true);
            float tremble = stage == "emerge" ? Mathf.Sin(introProgress * 93f) * 0.025f : 0f;
            mound.position = new Vector3(bossPos.x + tremble, FloorY - 0.015f, 0f);
            mound.localScale = new Vector3(1f, height, 1f);
            foreach (Pickup pickup in pickups)
            {
                bool visible = stage == "buried" || stage == "emerge" || stage == "inhale";
                float capture = stage == "inhale" ? Mathf.InverseLerp(pickup.TakeAt, pickup.TakeAt + 0.32f, introProgress) : 0f;
                pickup.Transform.gameObject.SetActive(visible && capture < 1f);
                if (!visible || capture >= 1f) continue;
                Vector2 start = new Vector2(bossPos.x + pickup.Offset.x * -Facing, FloorY + pickup.Offset.y);
                float amount = capture * capture;
                Vector2 control = Vector2.Lerp(start, mouthPosition, 0.45f) + Vector2.up * 0.85f;
                pickup.Transform.position = Bezier(start, control, mouthPosition, amount);
                pickup.Transform.localScale = pickup.Scale * Mathf.Lerp(1f, 0.18f, amount);
                pickup.Transform.localRotation = Quaternion.Euler(0, 0, pickup.Spin * capture);
            }
        }

        public void SetBurrow(string stage, Vector2 position, float progress)
        {
            if (!configured) return;
            stage = stage ?? "none";
            bool changed = burrowStage != stage;
            burrowStage = stage;
            burrowProgress = Mathf.Clamp01(progress);
            // Buried boss root is below ground; dust must stay on the unchanged arena floor.
            burrowPosition = new Vector2(position.x, FloorY + 0.04f);
            if (!changed) return;
            emissionTimer = 0f;
            if (stage == "erupt")
                for (int i = 0; i < 13; i++) EmitDebris(burrowPosition, false, false, 1.1f);
            else if (stage == "submerge")
                for (int i = 0; i < 7; i++) EmitDebris(burrowPosition, false, false, 0.5f);
        }

        public void ResetEffects()
        {
            introStage = "done";
            burrowStage = "none";
            emissionTimer = 0f;
            if (mound != null) mound.gameObject.SetActive(false);
            foreach (Pickup pickup in pickups)
                if (pickup.Transform != null) pickup.Transform.gameObject.SetActive(false);
            ClearPieces(false);
        }

        private void BuildMound()
        {
            // Texture colors are authored in sRGB; no bright, interpolated vertex-color wedge.
            var hill = new GameObject("Intro_Mound_VisualOnly");
            hill.transform.SetParent(effectRoot, false);
            mound = hill.transform;
            const int width = 224, height = 64;
            var pixels = new Color32[width * height];
            var inside = new bool[pixels.Length];
            var surface = new int[width];
            Color32 deep = new Color32(31, 35, 25, 255);
            Color32 soil = new Color32(52, 48, 34, 255);
            Color32 earth = new Color32(70, 61, 42, 255);
            Color32 clay = new Color32(87, 73, 48, 255);
            Color32 moss = new Color32(52, 64, 34, 255);
            Color32 mossLight = new Color32(77, 87, 47, 255);
            for (int x = 0; x < width; x++)
            {
                int blockX = x / 3 * 3;
                float distance = Mathf.Abs((blockX - 108f) / 112f);
                float profile = Mathf.Pow(Mathf.Clamp01(1f - distance), 0.70f);
                int crest = Mathf.Max(1, Mathf.RoundToInt(profile * 54f / 2f) * 2);
                if (crest > 5) crest += ((blockX / 9) % 3 - 1) * 2;
                surface[x] = Mathf.Clamp(crest, 1, height - 1);
                for (int y = 0; y < surface[x]; y++)
                {
                    int index = y * width + x;
                    inside[index] = true;
                    int tile = ((x / 4) * 31 + (y / 3) * 17 + (x / 13) * 7) % 37;
                    Color32 color = y < surface[x] * 0.28f ? deep : y < surface[x] * 0.67f ? soil : earth;
                    if (tile < 3 && y > 5 && y < surface[x] - 5) color = clay;
                    else if (tile > 32 && y < surface[x] - 4) color = deep;
                    // Broken moss shelves follow each stepped crest without outlining the whole hill.
                    bool mossPatch = (x / 11 + x / 29) % 5 != 1;
                    if (mossPatch && y >= surface[x] - (3 + (x / 15) % 3)) color = moss;
                    if (mossPatch && y == surface[x] - 1 && (x / 7) % 3 != 0) color = mossLight;
                    if (y < 3 + (x / 19) % 3) color = deep;
                    pixels[index] = color;
                }
            }

            // Three near roots sit within the soil silhouette: dark underside, woody core, sparse edge light.
            Vector2Int[][] roots =
            {
                new[] { new Vector2Int(51, 28), new Vector2Int(75, 23), new Vector2Int(108, 19), new Vector2Int(128, 8), new Vector2Int(166, 4) },
                new[] { new Vector2Int(123, 49), new Vector2Int(147, 38), new Vector2Int(171, 29), new Vector2Int(184, 12), new Vector2Int(207, 4) },
                new[] { new Vector2Int(113, 43), new Vector2Int(99, 30), new Vector2Int(77, 24), new Vector2Int(63, 14), new Vector2Int(36, 7) }
            };
            foreach (Vector2Int[] root in roots)
            {
                PaintStroke(pixels, width, height, root, 4, new Color32(25, 28, 20, 255), inside);
                PaintStroke(pixels, width, height, root, 2, new Color32(73, 53, 33, 255), inside);
                var rim = new Vector2Int[root.Length];
                for (int i = 0; i < root.Length; i++) rim[i] = root[i] + Vector2Int.up * 2;
                PaintStroke(pixels, width, height, rim, 0, new Color32(101, 77, 46, 255), inside);
            }
            Sprite hillSprite = RegisterSprite("SteppedSoilMound", pixels, width, height, new Vector2(0.5f, 0f), 32f);
            SpriteRenderer renderer = MakeRenderer("Soil_Moss_ForegroundRoots", hillSprite, 18);
            renderer.transform.SetParent(mound, false);
            renderer.transform.localScale = new Vector3(6.8f / (width / 32f), 1f / (height / 32f), 1f);

            // Only two small dead-tree shapes; these share the intro parent's complete cleanup.
            Sprite treeSprite = BuildTreeSprite();
            for (int i = 0; i < 2; i++)
            {
                SpriteRenderer tree = MakeRenderer("Intro_SmallDeadTree", treeSprite, 16);
                tree.transform.SetParent(mound, false);
                tree.transform.localPosition = i == 0 ? new Vector3(-2.17f, 0.37f, 0) : new Vector3(2.13f, 0.35f, 0);
                tree.transform.localScale = i == 0 ? new Vector3(0.68f, 0.70f, 1) : new Vector3(-0.57f, 0.61f, 1);
            }
        }

        private Sprite BuildTreeSprite()
        {
            const int width = 48, height = 64;
            var pixels = new Color32[width * height];
            Color32 bark = new Color32(32, 40, 27, 255);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(21, 0), new Vector2Int(23, 21), new Vector2Int(20, 40), new Vector2Int(26, 57) }, 2, bark);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(22, 24), new Vector2Int(11, 35), new Vector2Int(8, 49) }, 1, bark);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(21, 37), new Vector2Int(34, 43), new Vector2Int(39, 55) }, 1, bark);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(12, 35), new Vector2Int(4, 39) }, 0, bark);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(26, 56), new Vector2Int(34, 61) }, 0, bark);
            PaintStroke(pixels, width, height, new[] { new Vector2Int(19, 3), new Vector2Int(21, 20), new Vector2Int(18, 37) }, 0, new Color32(53, 58, 34, 255));
            return RegisterSprite("SmallDeadTree", pixels, width, height, new Vector2(0.5f, 0f), 64f);
        }

        private Sprite RegisterSprite(string label, Color32[] pixels, int width, int height, Vector2 pivot, float pixelsPerUnit)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Docheol_Fx_" + label, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot, pixelsPerUnit);
            sprite.name = texture.name;
            textures.Add(texture);
            sprites.Add(sprite);
            return sprite;
        }

        private static void PaintStroke(Color32[] pixels, int width, int height, Vector2Int[] points,
            int radius, Color32 color, bool[] clip = null)
        {
            for (int segment = 1; segment < points.Length; segment++)
            {
                Vector2Int a = points[segment - 1], b = points[segment];
                int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                for (int i = 0; i <= steps; i++)
                {
                    float t = steps == 0 ? 0f : i / (float)steps;
                    int cx = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                    int cy = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                    for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius + 1) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || x >= width || y < 0 || y >= height) continue;
                        int index = y * width + x;
                        if (clip == null || clip[index]) pixels[index] = color;
                    }
                }
            }
        }
        private void BuildPickups()
        {
            for (int i = 0; i < 12; i++)
            {
                bool leaf = i < 5;
                bool large = i >= 9;
                SpriteRenderer renderer = MakeRenderer("Intro_" + (leaf ? "Leaf" : large ? "LargeRubble" : "Stone"), leaf ? leafSprite : rockSprite, 19);
                renderer.color = leaf ? new Color(0.43f, 0.45f, 0.22f) : new Color(0.42f, 0.39f, 0.29f);
                float size = large ? Range(0.28f, 0.42f) : leaf ? Range(0.09f, 0.17f) : Range(0.13f, 0.23f);
                Vector3 scale = new Vector3(size * (leaf ? 1.8f : 1.1f), size, 1f);
                float offset = Mathf.Lerp(-7.0f, 3.1f, i / 11f);
                pickups.Add(new Pickup
                {
                    Transform = renderer.transform, Renderer = renderer,
                    Offset = new Vector2(offset, size * 0.48f), Scale = scale,
                    TakeAt = 0.04f + i * 0.048f, Spin = Range(-210f, 210f)
                });
                renderer.transform.localScale = scale;
            }
        }

        private void Update()
        {
            if (!configured || Time.timeScale <= 0f) return;
            float dt = Time.deltaTime;
            emissionTimer -= dt;
            if (introStage == "inhale" && emissionTimer <= 0f)
            {
                emissionTimer = 0.075f;
                Vector2 start = new Vector2(bossPosition.x + Range(-6.0f, 4.0f), FloorY + Range(0.08f, 0.7f));
                EmitSuction(start);
            }
            else if (introStage == "emerge" && emissionTimer <= 0f)
            {
                emissionTimer = 0.09f;
                EmitDebris(new Vector2(bossPosition.x + Range(-2.5f, 2.5f), FloorY + 0.2f), true, false, 0.6f);
            }
            else if ((burrowStage == "travel" || burrowStage == "lock") && emissionTimer <= 0f)
            {
                emissionTimer = burrowStage == "travel" ? 0.065f : 0.09f;
                Vector2 point = burrowPosition + Vector2.right * Range(-0.42f, 0.42f);
                EmitDebris(point, false, true, burrowStage == "lock" ? 0.25f + burrowProgress * 0.16f : 0.25f);
            }
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece piece = pieces[i];
                piece.Age += dt;
                float progress = piece.Age / piece.Lifetime;
                if (piece.Transform == null || progress >= 1f)
                {
                    if (piece.Transform != null) Destroy(piece.Transform.gameObject);
                    pieces.RemoveAt(i);
                    continue;
                }
                if (piece.Suction)
                {
                    float pull = progress * progress;
                    Vector2 control = Vector2.Lerp(piece.Start, mouthPosition, 0.5f) + Vector2.up * 0.7f;
                    piece.Transform.position = Bezier(piece.Start, control, mouthPosition, pull);
                    piece.Transform.localScale = piece.Scale * Mathf.Lerp(1f, 0.16f, pull);
                }
                else
                {
                    piece.Velocity += Vector2.down * (7f * dt);
                    piece.Transform.position += (Vector3)(piece.Velocity * dt);
                    piece.Transform.localScale = piece.Scale * Mathf.Lerp(1f, 0.55f, progress);
                }
                piece.Transform.Rotate(0, 0, piece.Spin * dt);
                Color color = piece.Color;
                color.a *= Mathf.Clamp01((1f - progress) * 3f);
                piece.Renderer.color = color;
            }
        }

        private void EmitSuction(Vector2 start)
        {
            if (pieces.Count >= PieceLimit) return;
            bool leaf = random.Next(3) != 0;
            SpriteRenderer renderer = MakeRenderer("Intro_InhaledDebris", leaf ? leafSprite : rockSprite, 17);
            float size = leaf ? Range(0.045f, 0.10f) : Range(0.07f, 0.14f);
            Color color = leaf ? new Color(0.48f, 0.46f, 0.23f, 0.80f) : new Color(0.46f, 0.43f, 0.32f, 0.8f);
            renderer.color = color;
            renderer.transform.position = start;
            renderer.transform.localScale = new Vector3(size * 1.6f, size, 1f);
            pieces.Add(new Piece
            {
                Transform = renderer.transform, Renderer = renderer, Start = start,
                Scale = renderer.transform.localScale, Color = color,
                Lifetime = Range(0.48f, 0.85f), Spin = Range(-300f, 300f), Suction = true, Intro = true
            });
        }

        private void EmitDebris(Vector2 point, bool intro, bool dust, float strength)
        {
            if (pieces.Count >= PieceLimit) return;
            SpriteRenderer renderer = MakeRenderer(dust ? "Burrow_SurfaceDust" : "Root_Debris", dust ? dustSprite : rockSprite, 17);
            Color color = dust ? new Color(0.51f, 0.44f, 0.29f, 0.30f) : new Color(0.48f, 0.43f, 0.30f, 0.8f);
            float size = dust ? Range(0.10f, 0.19f) : Range(0.07f, 0.16f);
            renderer.color = color;
            renderer.transform.position = point;
            renderer.transform.localScale = new Vector3(size * 1.7f, size, 1f);
            pieces.Add(new Piece
            {
                Transform = renderer.transform, Renderer = renderer, Start = point,
                Scale = renderer.transform.localScale, Color = color,
                Velocity = new Vector2(Range(-2.7f, 2.7f), Range(1.8f, 4.1f)) * strength,
                Lifetime = dust ? 0.23f : Range(0.25f, 0.46f), Spin = Range(-160f, 160f), Intro = intro
            });
        }

        private SpriteRenderer MakeRenderer(string label, Sprite sprite, int order)
        {
            var obj = new GameObject(label, typeof(SpriteRenderer));
            obj.transform.SetParent(effectRoot, false);
            SpriteRenderer renderer = obj.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            if (material != null)
            {
                Texture2D texture = sprite.texture;
                if (!textureMaterials.TryGetValue(texture, out Material spriteMaterial))
                {
                    spriteMaterial = new Material(material) { name = texture.name + "_Material", mainTexture = texture };
                    spriteMaterial.SetTexture("_MainTex", texture);
                    textureMaterials.Add(texture, spriteMaterial);
                }
                renderer.sharedMaterial = spriteMaterial;
                var binding = new MaterialPropertyBlock();
                binding.SetTexture("_MainTex", texture);
                renderer.SetPropertyBlock(binding);
            }
            return renderer;
        }

        private Sprite MakeSprite(string label, int shape)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "Docheol_Fx_" + label, filterMode = FilterMode.Point };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                bool inside = shape == 0 ? Mathf.Abs(nx) + Mathf.Abs(ny) < 0.94f
                    : shape == 1 ? Mathf.Abs(nx) < 0.85f && Mathf.Abs(ny) < 0.8f && Mathf.Abs(nx) + Mathf.Abs(ny) < 1.23f
                    : nx * nx + ny * ny < 0.86f;
                pixels[y * size + x] = inside ? Color.white : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = texture.name;
            textures.Add(texture);
            sprites.Add(sprite);
            return sprite;
        }

        private void ClearPieces(bool onlyIntro)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                if (onlyIntro && !pieces[i].Intro) continue;
                if (pieces[i].Transform != null) Destroy(pieces[i].Transform.gameObject);
                pieces.RemoveAt(i);
            }
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t) =>
            (1f - t) * (1f - t) * a + 2f * (1f - t) * t * b + t * t * c;
        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        private void OnDisable() => ResetEffects();
        private void OnDestroy()
        {
            if (effectRoot != null) Destroy(effectRoot.gameObject);

            if (material != null) Destroy(material);
            foreach (Material spriteMaterial in textureMaterials.Values) if (spriteMaterial != null) Destroy(spriteMaterial);
            foreach (Sprite sprite in sprites) if (sprite != null) Destroy(sprite);
            foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
        }
    }
}