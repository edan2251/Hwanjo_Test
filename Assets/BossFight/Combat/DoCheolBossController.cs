using System;
using System.Collections.Generic;
using FourGuardians.CourseContent.Combat;
using UnityEngine;

namespace FourGuardians.BossFight
{
    public enum BossState { Dormant, Idle, Telegraph, Active, Recovery, Transition, Dead }
    public enum BossPattern { None, DT01Slam, DT02Sweep, DT03Bite, DT04Inhale, DT05BodySlam }

    /// <summary>Ground-root boss. Damage is explicit, never contact-based; every tell locks its target.</summary>
    public sealed class DoCheolBossController : MonoBehaviour
    {
        public event Action<string, Vector2, float> Cued;
        public BossState State { get; private set; } = BossState.Dormant;
        public BossPattern Pattern { get; private set; }
        public float StateProgress => duration <= 0f ? 0f : Mathf.Clamp01(timer / duration);
        public int Facing { get; private set; } = -1;
        public int AttackId { get; private set; }
        public float TargetX { get; private set; }
        public Bounds WarningBounds { get; private set; }
        public Bounds ActiveBounds { get; private set; }
        public int Phase { get; private set; } = 1;
        public Health2D Health => health;
        public float RecoveryRemaining => State == BossState.Recovery ? Mathf.Max(0f, duration - timer) : 0f;
        public bool IsComboFollowup { get; private set; }
        public bool FightActive => fightActive;
        public float ArenaLeft = -9.5f;
        public float ArenaRight = 9.5f;
        public float FloorY = 0f;

        private BossFightPlayerAdapter player;
        private Health2D health;
        private BoxCollider2D hurtCollider;
        private Transform hurtRoot;
        private SpriteRenderer warningLine, attackFill, weakPoint;
        private readonly SpriteRenderer[] rangeTicks = new SpriteRenderer[11];
        private readonly SpriteRenderer[] rangeCaps = new SpriteRenderer[2];
        private readonly SpriteRenderer[] dustTrail = new SpriteRenderer[5];
        private static Sprite rootWaveSprite, dustSprite;
        private readonly Dictionary<Texture2D, Material> markerMaterials = new Dictionary<Texture2D, Material>();
        private GameObject markers;
        private static Sprite solidSprite;
        private float timer, duration;
        private bool fightActive, landed, forceSingle;
        private int sequence;
        private Vector3 riseOrigin;
        private float sweepFrom, sweepTo;
        private const float SweepWidth = 0.9f;
        private float riseHeight = 3.1f;
        private BossPattern previousPattern;
        private static int nextAttackId = 200000;

        public void Configure(BossFightPlayerAdapter newPlayer, Health2D newHealth)
        {
            if (health != null) health.Died -= OnDied;
            player = newPlayer;
            health = newHealth;
            if (health != null)
            {
                health.Configure(40, CombatTeam.Enemy);
                health.ConfigureInvulnerability(0.12f);
                health.Died += OnDied;
                health.DamageEnabled = false;
            }
            CreatePresentation();
            UpdateHurtbox();
        }

        public void SetFightActive(bool active)
        {
            fightActive = active && health != null && !health.IsDead;
            if (!fightActive)
            {
                SetState(health != null && health.IsDead ? BossState.Dead : BossState.Dormant, 0f);
                HideMarkers();
                if (health != null) health.DamageEnabled = false;
            }
            else if (State == BossState.Dormant) SetState(BossState.Idle, 1.4f);
            UpdateHurtbox();
        }

        public void ResetFight(Vector2 position)
        {
            fightActive = false;
            transform.position = new Vector3(position.x, FloorY, transform.position.z);
            Facing = -1;
            Phase = 1;
            sequence = 0;
            Pattern = BossPattern.None;
            previousPattern = BossPattern.None;
            IsComboFollowup = false;
            AttackId = 0;
            forceSingle = false;
            WarningBounds = default;
            ActiveBounds = default;
            if (health != null) { health.ResetHealth(); health.DamageEnabled = false; }
            SetState(BossState.Dormant, 0f);
            HideMarkers();
            UpdateHurtbox();
        }

        /// <summary>Explicit QA hook; inert until called, and never drives the release encounter itself.</summary>
        public bool ForcePattern(BossPattern pattern)
        {
            if (!fightActive || health == null || health.IsDead || player == null || player.IsDead) return false;
            if (pattern == BossPattern.None || pattern == BossPattern.DT04Inhale) return false;
            transform.position = new Vector3(transform.position.x, FloorY, transform.position.z);
            forceSingle = true;
            IsComboFollowup = false;
            BeginPattern(pattern);
            return true;
        }

        private void FixedUpdate()
        {
            if (!fightActive || player == null || health == null || health.IsDead) return;
            if (player.IsDead) { SetFightActive(false); return; }
            timer += Time.fixedDeltaTime;
            switch (State)
            {
                case BossState.Idle:
                    FacePlayer();
                    float separation = Mathf.Abs(player.transform.position.x - transform.position.x);
                    if (separation > 3.8f)
                    {
                        float x = Mathf.MoveTowards(transform.position.x, player.transform.position.x - Facing * 3.5f, 1.8f * Time.fixedDeltaTime);
                        transform.position = new Vector3(Mathf.Clamp(x, ArenaLeft + 3.0f, ArenaRight - 3.0f), FloorY, transform.position.z);
                    }
                    if (timer >= duration && (separation <= 3.8f || timer >= 3.2f)) SelectPattern();
                    break;
                case BossState.Telegraph:
                    AnimateWarning();
                    if (Pattern == BossPattern.DT05BodySlam)
                    {
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / 1.25f));
                        transform.position = new Vector3(Mathf.Lerp(riseOrigin.x, TargetX, p), FloorY + riseHeight * p, riseOrigin.z);
                    }
                    if (timer >= duration) BeginActive();
                    break;
                case BossState.Active:
                    TickAttack();
                    if (timer >= duration) FinishAttack();
                    break;
                case BossState.Recovery:
                    if (timer >= duration)
                    {
                        if (Phase == 1 && health.CurrentHealth * 2 <= health.MaxHealth)
                        {
                            Phase = 2;
                            Pattern = BossPattern.None;
                            SetState(BossState.Transition, 2.1f);
                            Cue("phase", transform.position, 1f);
                        }
                        else SetState(BossState.Idle, Phase == 2 ? 0.65f : 0.95f);
                    }
                    break;
                case BossState.Transition:
                    if (timer >= duration) SetState(BossState.Idle, 0.8f);
                    break;
            }
            UpdateHurtbox();
        }

        private void SelectPattern()
        {
            forceSingle = false;
            IsComboFollowup = false;
            BossPattern selected;
            if (sequence == 0) selected = BossPattern.DT01Slam;
            else if (sequence == 1) selected = BossPattern.DT05BodySlam;
            else
            {
                // Deterministic variation makes learning repeatable without consecutive body slams.
                BossPattern[] cycle = { BossPattern.DT02Sweep, BossPattern.DT03Bite, BossPattern.DT01Slam, BossPattern.DT05BodySlam, BossPattern.DT03Bite, BossPattern.DT02Sweep };
                selected = cycle[(sequence - 2) % cycle.Length];
                if (selected == BossPattern.DT03Bite && !CanBite()) selected = BossPattern.DT01Slam;
                if (selected == previousPattern) selected = BossPattern.DT05BodySlam;
            }
            sequence++;
            BeginPattern(selected);
        }

        private bool CanBite()
        {
            float space = Facing < 0 ? transform.position.x - ArenaLeft : ArenaRight - transform.position.x;
            return space >= 6.4f;
        }

        private void FacePlayer()
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f) Facing = dx < 0f ? -1 : 1;
        }

        private void BeginPattern(BossPattern pattern)
        {
            HideMarkers();
            FacePlayer();
            Pattern = pattern;
            AttackId = ++nextAttackId;
            landed = false;
            TargetX = player.transform.position.x;
            float tell;
            switch (pattern)
            {
                case BossPattern.DT01Slam:
                    // Forepaw lands in front of the body; the idle root-slide brings it into reach first.
                    TargetX = transform.position.x + Facing * 2.65f;
                    WarningBounds = Box(TargetX, FloorY + 0.8f, 2.8f, 1.6f);
                    tell = 1.25f;
                    break;
                case BossPattern.DT02Sweep:
                    sweepFrom = transform.position.x + Facing * 0.8f;
                    sweepTo = Mathf.Clamp(transform.position.x + Facing * 6.2f, ArenaLeft, ArenaRight);
                    WarningBounds = Box((sweepFrom + sweepTo) * 0.5f, FloorY + 0.21f, Mathf.Abs(sweepTo - sweepFrom) + SweepWidth, 0.42f);
                    TargetX = sweepTo;
                    tell = IsComboFollowup ? 1.2f : 1.05f;
                    break;
                case BossPattern.DT03Bite:
                    TargetX = transform.position.x + Facing * 2.9f;
                    WarningBounds = Box(TargetX, FloorY + 1.0f, 4.0f, 2.0f);
                    tell = 1.55f;
                    break;
                default:
                    TargetX = Mathf.Clamp(TargetX, ArenaLeft + 3.0f, ArenaRight - 3.0f);
                    WarningBounds = Box(TargetX, FloorY + 2.0f, 5.2f, 4.0f);
                    riseOrigin = transform.position;
                    tell = 1.9f;
                    Cue("rise", transform.position, riseHeight);
                    break;
            }
            SetState(BossState.Telegraph, tell);
            ShowWarning();
            Cue("telegraph", new Vector2(TargetX, FloorY), tell);
        }

        private void BeginActive()
        {
            warningLine.color = new Color(1f, 0.19f, 0.08f, 1f);
            float activeDuration = Pattern == BossPattern.DT05BodySlam ? 0.58f : Pattern == BossPattern.DT02Sweep ? 0.66f : 0.22f;
            SetState(BossState.Active, activeDuration);
            Cue("release", new Vector2(TargetX, FloorY), 1f);
            if (Pattern == BossPattern.DT01Slam) Cue("impact", new Vector2(TargetX, FloorY), 0.5f);
            else if (Pattern == BossPattern.DT03Bite) Cue("bite", new Vector2(TargetX, FloorY + 1f), 0.4f);
            else if (Pattern == BossPattern.DT02Sweep) Cue("sweep", new Vector2(sweepFrom, FloorY), 0.3f);
        }

        private void TickAttack()
        {
            Bounds hit;
            if (Pattern == BossPattern.DT05BodySlam)
            {
                float fall = Mathf.Clamp01(timer / 0.40f);
                float bottom = FloorY + riseHeight * (1f - fall * fall);
                transform.position = new Vector3(TargetX, bottom, transform.position.z);
                // Sample the descending body bounds once per FixedUpdate; landing retains this attack ID.
                hit = Box(TargetX, bottom + 1.6f, 5.2f, 3.2f);
                if (fall >= 1f)
                {
                    hit = Box(TargetX, FloorY + 0.8f, 5.2f, 1.6f);
                    if (!landed) { landed = true; Cue("land", new Vector2(TargetX, FloorY), 1f); }
                }
            }
            else if (Pattern == BossPattern.DT02Sweep)
            {
                float x = Mathf.Lerp(sweepFrom, sweepTo, StateProgress);
                hit = Box(x, FloorY + 0.21f, SweepWidth, 0.42f);
            }
            else hit = WarningBounds;
            ActiveBounds = hit;
            DrawAttackEffect(hit);
            Bounds target = player.HurtBounds;
            // Z is irrelevant in this 2D encounter.
            bool overlap = hit.min.x < target.max.x && hit.max.x > target.min.x && hit.min.y < target.max.y && hit.max.y > target.min.y;
            if (overlap)
            {
                float push = player.transform.position.x < TargetX ? -1f : 1f;
                player.ReceiveAttack(AttackId, 1, new Vector2(push * 4f, 2.7f), gameObject);
            }
        }

        private void FinishAttack()
        {
            transform.position = new Vector3(transform.position.x, FloorY, transform.position.z);
            HideMarkers();
            ActiveBounds = default;
            previousPattern = Pattern;
            if (Phase == 2 && Pattern == BossPattern.DT01Slam && !IsComboFollowup && !forceSingle)
            {
                IsComboFollowup = true;
                Cue("chain", transform.position, 1.2f);
                BeginPattern(BossPattern.DT02Sweep);
                return;
            }
            SetState(BossState.Recovery, Pattern == BossPattern.DT05BodySlam ? 3.0f : 2.65f);
            Cue("recover", transform.position, duration);
        }

        private void SetState(BossState next, float seconds)
        {
            State = next;
            timer = 0f;
            duration = seconds;
        }

        private void OnDied()
        {
            fightActive = false;
            SetState(BossState.Dead, 0f);
            HideMarkers();
            UpdateHurtbox();
            Cue("death", transform.position, 1f);
        }

        private void UpdateHurtbox()
        {
            if (hurtRoot == null) return;
            hurtRoot.localPosition = new Vector3(Facing * 1.65f, 0.95f, 0f);
            bool grounded = transform.position.y <= FloorY + 0.25f;
            bool vulnerable = fightActive && State != BossState.Transition && State != BossState.Dead && grounded;
            hurtCollider.enabled = vulnerable;
            if (health != null) health.DamageEnabled = vulnerable;
            weakPoint.enabled = vulnerable && State == BossState.Recovery;
            weakPoint.color = new Color(1f, 0.81f, 0.29f, 0.25f + Mathf.Sin(Time.time * 9f) * 0.07f);
        }

        private void CreatePresentation()
        {
            if (hurtRoot != null) return;
            GameObject weak = new GameObject("MouthAndForepaws_Hurtbox");
            weak.transform.SetParent(transform, false);
            hurtRoot = weak.transform;
            hurtCollider = weak.AddComponent<BoxCollider2D>();
            hurtCollider.isTrigger = true;
            hurtCollider.size = new Vector2(2.9f, 1.85f);
            weak.AddComponent<Hurtbox2D>().Configure(health);
            weakPoint = NewSprite("RecoveryOpening", hurtRoot, 12);
            weakPoint.transform.localScale = new Vector3(2.6f, 0.07f, 1f);
            weakPoint.transform.localPosition = new Vector3(0f, -0.91f, 0f);
            markers = new GameObject("DoCheol_AttackMarkers");

            warningLine = NewSprite("DangerFloorLine", markers.transform, 8);
            attackFill = NewSprite("RootCrest", markers.transform, 15);
            if (rootWaveSprite == null) rootWaveSprite = CreateRootSprite();
            if (dustSprite == null) dustSprite = CreateDustSprite();
            attackFill.sprite = rootWaveSprite;
            BindMarkerTexture(attackFill, rootWaveSprite.texture);
            for (int i = 0; i < rangeTicks.Length; i++)
                rangeTicks[i] = NewSprite("GroundNotch_" + i, markers.transform, 8);
            for (int i = 0; i < rangeCaps.Length; i++)
                rangeCaps[i] = NewSprite("LockedBoundary_" + i, markers.transform, 9);
            for (int i = 0; i < dustTrail.Length; i++)
            {
                dustTrail[i] = NewSprite("EarthDust_" + i, markers.transform, 14);
                dustTrail[i].sprite = dustSprite;
                BindMarkerTexture(dustTrail[i], dustSprite.texture);
            }
            HideMarkers();
        }

        private SpriteRenderer NewSprite(string label, Transform parent, int sorting)
        {
            if (solidSprite == null)
                solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            GameObject go = new GameObject(label);
            go.transform.SetParent(parent, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = solidSprite;
            BindMarkerTexture(renderer, solidSprite.texture);
            renderer.sortingOrder = sorting;
            return renderer;
        }

        private void BindMarkerTexture(SpriteRenderer renderer, Texture2D texture)
        {
            // Runtime sprite textures must never borrow another renderer's last binding.
            if (!markerMaterials.TryGetValue(texture, out Material material))
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                material = new Material(shader) { name = "DoCheol_Marker_" + texture.name };
                material.mainTexture = texture;
                material.SetTexture("_MainTex", texture);
                material.enableInstancing = false;
                markerMaterials.Add(texture, material);
            }
            renderer.sharedMaterial = material;
            MaterialPropertyBlock binding = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(binding);
            binding.SetTexture("_MainTex", texture);
            renderer.SetPropertyBlock(binding);
        }

        private void ShowWarning()
        {
            // The continuous baseline and its end caps mark the entire locked damage footprint.
            // No filled vertical rectangle obscures the creature or implies an extra collider.
            Color color = Pattern == BossPattern.DT02Sweep
                ? new Color(0.96f, 0.72f, 0.26f, 0.95f)
                : new Color(0.94f, 0.45f, 0.18f, 0.95f);
            DrawRect(warningLine, new Vector3(WarningBounds.center.x, FloorY + 0.045f),
                new Vector3(WarningBounds.size.x, 0.055f, 1f), color);
            for (int i = 0; i < rangeCaps.Length; i++)
            {
                float x = i == 0 ? WarningBounds.min.x + 0.025f : WarningBounds.max.x - 0.025f;
                DrawRect(rangeCaps[i], new Vector3(x, FloorY + 0.18f), new Vector3(0.05f, 0.36f, 1f), color);
            }
            for (int i = 0; i < rangeTicks.Length; i++)
            {
                float x = Mathf.Lerp(WarningBounds.min.x, WarningBounds.max.x, (i + 1f) / (rangeTicks.Length + 1f));
                float height = i % 2 == 0 ? 0.13f : 0.08f;
                DrawRect(rangeTicks[i], new Vector3(x, FloorY + height * 0.5f), new Vector3(0.035f, height, 1f), color);
            }
        }

        private void AnimateWarning()
        {
            float pulse = 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(timer * 7f));
            Color color = warningLine.color;
            color.a = pulse;
            warningLine.color = color;
            for (int i = 0; i < rangeCaps.Length; i++) rangeCaps[i].color = color;
            for (int i = 0; i < rangeTicks.Length; i++) rangeTicks[i].color = color;
        }

        private void DrawAttackEffect(Bounds hit)
        {
            // The wave crest occupies the exact horizontal/vertical damage extent, with a
            // narrow solid earthen foot and uneven spikes instead of a debug rectangle.
            if (Pattern == BossPattern.DT02Sweep)
            {
                DrawRect(attackFill, hit.center, hit.size, Color.white);
                attackFill.flipX = Facing < 0;
                for (int i = 0; i < dustTrail.Length; i++)
                {
                    float fraction = (i + 1f) / (dustTrail.Length + 1f);
                    float x = hit.center.x - Facing * (0.45f + fraction * 0.52f);
                    x = Mathf.Clamp(x, WarningBounds.min.x + 0.08f, WarningBounds.max.x - 0.08f);
                    float y = FloorY + 0.055f + fraction * 0.09f + Mathf.Sin(timer * 18f + i) * 0.025f;
                    float size = 0.16f - fraction * 0.07f;
                    DrawRect(dustTrail[i], new Vector3(x, y), new Vector3(size, size * 0.65f, 1f),
                        new Color(0.55f, 0.40f, 0.24f, (1f - fraction) * 0.50f));
                }
            }
            else if (Pattern != BossPattern.DT05BodySlam || landed)
            {
                // A low fracture crest reinforces the already locked footprint on impact;
                // body and jaw animation communicate the strike's height.
                float height = Pattern == BossPattern.DT03Bite ? 0.17f : 0.38f;
                DrawRect(attackFill, new Vector3(hit.center.x, FloorY + height * 0.5f),
                    new Vector3(hit.size.x, height, 1f), new Color(1f, 0.85f, 0.65f, 0.75f));
            }
            else attackFill.enabled = false;
        }

        private static Sprite CreateRootSprite()
        {
            const int width = 48, height = 24;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "DoCheol_RootWave_Code";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float first = 14f * Mathf.Max(0f, 1f - Mathf.Abs(x - 7f) / 8f);
                float middle = 24f * Mathf.Max(0f, 1f - Mathf.Abs(x - 22f) / 12f);
                float last = 18f * Mathf.Max(0f, 1f - Mathf.Abs(x - 39f) / 9f);
                int top = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(3f, first, middle, last)), 3, height);
                for (int y = 0; y < top; y++)
                {
                    bool ridge = y >= top - 2 || (x + y * 2) % 13 < 2;
                    pixels[y * width + x] = ridge
                        ? new Color32(225, 176, 85, 255)
                        : y < 3 ? new Color32(102, 65, 38, 255) : new Color32(152, 98, 48, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            // DrawRect normalizes the native aspect ratio to the exact attack bounds.
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width, 0, SpriteMeshType.FullRect);
            // A rectangular texture requires matching PPU dimensions at draw time.
            return sprite;
        }

        private static Sprite CreateDustSprite()
        {
            const int size = 12;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "DoCheol_EarthDust_Code";
            texture.filterMode = FilterMode.Point;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = new Vector2(x - 5.5f, y - 5.5f).magnitude;
                pixels[y * size + x] = radius < 4.9f && (x + y * 3) % 11 != 0
                    ? new Color32(255, 255, 255, 180) : new Color32(0, 0, 0, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }
        private static void DrawRect(SpriteRenderer renderer, Vector3 position, Vector3 size, Color color)
        {
            if (renderer == null) return;
            renderer.enabled = true;
            renderer.transform.position = new Vector3(position.x, position.y, 0f);
            Vector2 nativeSize = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(Mathf.Max(0.03f, size.x) / nativeSize.x, Mathf.Max(0.03f, size.y) / nativeSize.y, 1f);
            renderer.color = color;
        }

        private static Bounds Box(float x, float y, float width, float height) => new Bounds(new Vector3(x, y, 0f), new Vector3(width, height, 2f));
        private void Cue(string key, Vector2 point, float strength) => Cued?.Invoke(key, point, strength);
        private void HideMarkers()
        {

            if (warningLine != null) warningLine.enabled = false;
            if (attackFill != null) attackFill.enabled = false;
            if (weakPoint != null) weakPoint.enabled = false;
            for (int i = 0; i < rangeTicks.Length; i++) if (rangeTicks[i] != null) rangeTicks[i].enabled = false;
            for (int i = 0; i < rangeCaps.Length; i++) if (rangeCaps[i] != null) rangeCaps[i].enabled = false;
            for (int i = 0; i < dustTrail.Length; i++) if (dustTrail[i] != null) dustTrail[i].enabled = false;
        }
        private void OnDestroy()
        {
            if (health != null) health.Died -= OnDied;
            if (markers != null) Destroy(markers);
            foreach (Material material in markerMaterials.Values) if (material != null) Destroy(material);
            markerMaterials.Clear();
        }
    }
}
