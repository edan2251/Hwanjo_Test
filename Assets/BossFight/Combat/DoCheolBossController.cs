using System;
using System.Collections.Generic;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;
using UnityEngine;
namespace FourGuardians.BossFight
{
    public enum BossState { Dormant, Idle, Telegraph, Active, Recovery, Transition, Dead, Link }
    public enum BossPattern { None, DT01Slam, DT02Sweep, DT03Bite, DT04Inhale, DT05BodySlam, DT06Burrow, ARockfall, BBurrowLeap }
    public sealed class DoCheolBossController : MonoBehaviour
    {
        public event Action<string, Vector2, float> Cued;
        public event Action<int, int, Vector2> ArmorBroken;
        // Fired only after the player accepts damage; lets QA distinguish rocks from the current basic.
        public event Action<int> PlayerDamagedByAttack;
        public event Action<Vector2, int, bool> LocalHit;
        public BossState State { get; private set; } = BossState.Dormant;
        public BossPattern Pattern { get; private set; }
        public float StateProgress => duration <= 0f ? 0f : Mathf.Clamp01(timer / duration);
        public string AttackStage { get; private set; } = "dormant";
        public float AttackStageProgress { get; private set; }
        public float TransitionProgress => State == BossState.Transition ? StateProgress : Phase == 2 ? 1f : 0f;
        public bool TargetLocked { get; private set; }
        public bool IsUnderground => Pattern == BossPattern.BBurrowLeap && (AttackStage == "travel" || (AttackStage == "submerge" && AttackStageProgress > 0.3f));
        public int ArmorMask { get; private set; } = 15;
        public int ArmorCount => ((ArmorMask >> 0) & 1) + ((ArmorMask >> 1) & 1) + ((ArmorMask >> 2) & 1) + ((ArmorMask >> 3) & 1);
        public Vector2 LastHitPoint { get; private set; }
        public int LastHitRegion { get; private set; } = -1;
        public float LockedTellDuration { get; private set; }
        public float BurrowCooldownRemaining => Mathf.Max(0f, nextBurrowTime - encounterTime);
        public float RockfallCooldownRemaining => Mathf.Max(0f, nextRockfallTime - encounterTime);
        public int Facing { get; private set; } = -1;
        public int AttackId { get; private set; }
        public float TargetX { get; private set; }
        public Bounds WarningBounds { get; private set; }
        public Bounds ActiveBounds { get; private set; }
        public Bounds SecondaryActiveBounds { get; private set; }
        public int Phase { get; private set; } = 1;
        public Health2D Health => health;
        public float RecoveryRemaining => State == BossState.Recovery ? Mathf.Max(0f, duration - timer) : 0f;
        public string ComboCode { get; private set; } = "";
        public int ComboIndex { get; private set; }
        public int ComboLength => combo.Length;
        public bool IsComboFollowup => ComboIndex > 0 && combo.Length > 1;
        public bool FightActive => fightActive;
        public bool RockfallActive => rockfallEnd > encounterTime || rocks.Count > 0;
        public float RockfallRemaining => Mathf.Max(0f, rockfallEnd - encounterTime);
        public IReadOnlyList<DocheolRockfall> Rockfalls => rocks.AsReadOnly();
        public int RejectedRockfallPlacements { get; private set; }
        public Bounds RockfallEscapeCorridor { get; private set; }
        // Samples are (hurtbox center X, hurtbox bottom Y, seconds from the preview origin).
        public IReadOnlyList<Vector3> RockfallEscapeRoute => escapeRoute.AsReadOnly();
        public float EscapeRouteOriginTime { get; private set; }
        public bool EscapePreviewAvailable => player != null && !PlayerMovementCommitted();
        public float ArenaLeft = -9.5f, ArenaRight = 9.5f, FloorY;
        [Header("Basic attack timing")]
        [SerializeField] private float biteTell = 0.95f, biteStrike = 0.16f, biteRecovery = 0.30f;
        [SerializeField] private float sweepTell = 0.50f, sweepFollowupTell = 0.40f, sweepStrike = 0.16f, sweepRecovery = 0.34f;
        [SerializeField] private float slamTell = 1.08f, slamStrike = 0.20f, slamRecovery = 1.35f, slamAdvance = 0.90f;
        [SerializeField] private float comboLink = 0.08f, reactionMargin = 0.20f;
        [Header("A: grounded roar and persistent rockfall")]
        [SerializeField] private int rockfallUnlockHealth = 28;
        [SerializeField] private float rockfallCooldown = 18f, rockfallDuration = 5.5f, rockfallInterval = 0.58f;
        [SerializeField] private float breatheDuration = 0.55f, roarDuration = 0.60f, escapeCorridorWidth = 2.2f;
        [Header("B: underground travel and forward landing")]
        [SerializeField] private float burrowCooldown = 12f, submergeDuration = 0.48f, travelDuration = 0.65f;
        [SerializeField] private float riseDuration = 0.48f, flightDuration = 0.85f, leapHeight = 4.2f;
        [SerializeField] private float landingDuration = 0.16f, landingWidth = 4.4f, waveDelay = 0.32f, waveDuration = 0.65f, leapRecovery = 1.15f;
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
        private float timer, duration, encounterTime, nextBurrowTime, nextRockfallTime, rockfallEnd, nextRockTime, leapFlightTime, safetyTellFloor;
        private bool fightActive, landed, phasePending, threshold75, threshold60;
        private Vector3 riseOrigin;
        private float motionEndX, emergenceX;
        private readonly List<DocheolRockfall> rocks = new List<DocheolRockfall>();
        private readonly List<Vector2> blockedIntervals = new List<Vector2>();
        private readonly Queue<string> recentStarts = new Queue<string>();
        private readonly List<Vector3> escapeRoute = new List<Vector3>();
        private readonly List<TimedDanger> routeDangers = new List<TimedDanger>();
        private struct TimedDanger
        {
            public Bounds bounds;
            public float start, end;
            public bool sweep, basic;
        }
        private BossPattern[] combo = Array.Empty<BossPattern>();
        private int rockSequence, completedSequences;
        private static int nextAttackId = 200000;
        private static readonly string[] PhaseOneCombos = { "3", "2", "3-2" };
        private static readonly string[] PhaseTwoCombos = { "3", "2", "1", "3-2", "2-1", "3-2-1" };
        public void Configure(BossFightPlayerAdapter newPlayer, Health2D newHealth)
        {
            if (health != null) { health.Died -= OnDied; health.DamageTaken -= OnDamageTaken; }
            player = newPlayer; health = newHealth;
            if (health != null)
            {
                health.Configure(40, CombatTeam.Enemy); health.ConfigureInvulnerability(0.12f);
                health.Died += OnDied; health.DamageTaken += OnDamageTaken; health.DamageEnabled = false;
            }
            CreatePresentation(); UpdateHurtbox();
        }
        public void StartEncounter() => SetFightActive(true);
        public void SetEncounterEnabled(bool enabled) => SetFightActive(enabled);
        public void SetFightActive(bool active)
        {
            fightActive = active && health != null && !health.IsDead;
            if (!fightActive)
            {
                ClearRockfall(); HideMarkers(); ClearBounds();
                SetState(health != null && health.IsDead ? BossState.Dead : BossState.Dormant, 0f);
                if (health != null) health.DamageEnabled = false;
            }
            else if (State == BossState.Dormant) SetState(BossState.Idle, 1.2f);
            UpdateHurtbox();
        }
        public void ResetFight(Vector2 position)
        {
            fightActive = false; ClearRockfall();
            transform.position = new Vector3(position.x, FloorY, transform.position.z);
            Facing = -1; Phase = 1; ArmorMask = 15; threshold75 = threshold60 = phasePending = false;
            LastHitRegion = -1; LastHitPoint = position; encounterTime = nextBurrowTime = nextRockfallTime = 0f;
            rockSequence = completedSequences = RejectedRockfallPlacements = 0;
            recentStarts.Clear(); ClearCombo(); Pattern = BossPattern.None; AttackId = 0;
            LockedTellDuration = 0f; TargetLocked = false; ClearBounds();
            if (health != null) { health.ResetHealth(); health.DamageEnabled = false; }
            SetState(BossState.Dormant, 0f); HideMarkers(); UpdateHurtbox();
        }
        /// <summary>QA hook bypasses scheduler/cooldown, but honors phase and environment exclusions.</summary>
        public bool ForcePattern(BossPattern pattern)
        {
            if (!CanForce()) return false;
            switch (pattern)
            {
                case BossPattern.DT01Slam: return ForceCombo("1");
                case BossPattern.DT02Sweep: return ForceCombo("2");
                case BossPattern.DT03Bite: return ForceCombo("3");
                case BossPattern.ARockfall:
                    if (health.CurrentHealth > rockfallUnlockHealth || RockfallActive) return false;
                    BeginSpecial(pattern); return true;
                case BossPattern.BBurrowLeap:
                    if (RockfallActive || Pattern == BossPattern.ARockfall && State != BossState.Recovery) return false;
                    BeginSpecial(pattern); return true;
                default: return false;
            }
        }
        public bool ForceCombo(string code)
        {
            if (!CanForce() || Array.IndexOf(Phase == 1 ? PhaseOneCombos : PhaseTwoCombos, code) < 0) return false;
            transform.position = new Vector3(ClampBodyX(transform.position.x), FloorY, transform.position.z);
            BeginCombo(code); return true;
        }
        private bool CanForce() => fightActive && health != null && !health.IsDead && player != null && !player.IsDead && State != BossState.Transition;
        private void FixedUpdate()
        {
            if (!fightActive || player == null || health == null || health.IsDead) return;
            if (player.IsDead) { SetFightActive(false); return; }
            float dt = Time.fixedDeltaTime; timer += dt; encounterTime += dt; TickRockfall(dt);
            // Damage callbacks may synchronously end the encounter and clear all hazards.
            if (!fightActive || player.IsDead || health.IsDead) return;
            if (phasePending && State != BossState.Transition) BeginTransition();
            switch (State)
            {
                case BossState.Idle:
                    FacePlayer();
                    float separation = Mathf.Abs(player.transform.position.x - transform.position.x);
                    if (separation > 3.6f)
                        transform.position = new Vector3(ClampBodyX(Mathf.MoveTowards(transform.position.x, player.transform.position.x - Facing * 3.2f, 2.4f * dt)), FloorY, transform.position.z);
                    if (separation <= 3.6f || timer >= duration) SelectPattern();
                    break;
                case BossState.Telegraph: TickTelegraph(); if (timer >= duration) BeginActive(); break;
                case BossState.Active:
                    TickAttack();
                    if (fightActive && State == BossState.Active && timer >= duration) FinishAttack();
                    break;
                case BossState.Link:
                    SetStage("link", StateProgress);
                    if (timer >= duration) { ComboIndex++; BeginBasic(combo[ComboIndex]); }
                    break;
                case BossState.Recovery:
                    SetStage("recover", StateProgress);
                    if (timer >= duration)
                    {
                        if (Mathf.Abs(player.transform.position.x - transform.position.x) > 5.2f && !RockfallActive) SetState(BossState.Idle, 0.70f);
                        else SelectPattern();
                    }
                    break;
                case BossState.Transition:
                    SetStage("transition", StateProgress);
                    if (StateProgress >= 0.4f) for (int i = 0; i < 4; i++) BreakArmor(i, GetArmorWorldPoint(i));
                    if (timer >= duration)
                    {
                        Phase = 2; phasePending = false;
                        if (player.FightActive && !player.IsDead) player.Health.DamageEnabled = true;
                        SelectPattern();
                    }
                    break;
            }
            UpdateHurtbox();
        }
        private float ClampBodyX(float x) => Mathf.Clamp(x, ArenaLeft + 3f, ArenaRight - 3f);
        private void FacePlayer()
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f) Facing = dx < 0f ? -1 : 1;
        }
        private void SelectPattern()
        {
            if (phasePending) { BeginTransition(); return; }
            FacePlayer(); float distance = Mathf.Abs(player.transform.position.x - transform.position.x);
            string[] candidates = Phase == 1 ? PhaseOneCombos : PhaseTwoCombos;
            float[] weights = new float[candidates.Length + 2]; float total = 0f;
            for (int i = 0; i < candidates.Length; i++)
            {
                string code = candidates[i];
                float weight = code[0] == '3' ? (distance >= 1.6f && distance <= 4.3f ? 1.5f : 0.60f)
                    : code[0] == '2' ? (distance < 4.6f ? 1.25f : 0.55f) : (distance > 2.5f ? 1.4f : 0.75f);
                float frontSpace = Facing > 0 ? ArenaRight - transform.position.x : transform.position.x - ArenaLeft;
                if (frontSpace < 4.8f && code[0] == '3') weight *= 0.45f;
                foreach (string prior in recentStarts) if (prior.Length > 0 && prior[0] == code[0]) weight *= 0.48f;
                if (RockfallActive)
                {
                    if (code.Length > 1) weight *= 0.65f;
                    // Choose the whole chain only when the existing rocks leave a timed escape.
                    if (EscapePreviewAvailable && !FindTimedEscape(default, false, 0f, code, false)) weight = 0f;
                }
                weights[i] = weight; total += weight;
            }
            if (!RockfallActive && health.CurrentHealth <= rockfallUnlockHealth && RockfallCooldownRemaining <= 0f && completedSequences >= 2) weights[candidates.Length] = 1.1f;
            if (!RockfallActive && BurrowCooldownRemaining <= 0f && completedSequences >= 2) weights[candidates.Length + 1] = distance > 4.8f ? 2f : 0.5f;
            foreach (string prior in recentStarts)
            {
                if (prior == "A") weights[candidates.Length] *= 0.20f;
                if (prior == "B") weights[candidates.Length + 1] *= 0.20f;
            }
            total += weights[candidates.Length] + weights[candidates.Length + 1];
            if (total <= 0.0001f)
            {
                // Never force a sequence rejected by the hazard planner. Start a genuine,
                // clearly visible sweep tell whose strike follows the existing rocks' clearance.
                // The boss stays grounded and hittable; this is not a fake recovery or canceled hit.
                float clearance = 0f;
                foreach (DocheolRockfall rock in rocks) clearance = Mathf.Max(clearance, rock.ImpactRemaining);
                safetyTellFloor = Mathf.Max(sweepTell, clearance + reactionMargin);
                BeginCombo("2"); return;
            }
            float draw = UnityEngine.Random.value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                draw -= weights[i]; if (draw > 0f && i < weights.Length - 1) continue;
                if (i < candidates.Length) BeginCombo(candidates[i]);
                else BeginSpecial(i == candidates.Length ? BossPattern.ARockfall : BossPattern.BBurrowLeap);
                return;
            }
        }
        private void RememberStart(string code) { recentStarts.Enqueue(code); while (recentStarts.Count > 3) recentStarts.Dequeue(); }
        private void ClearCombo() { combo = Array.Empty<BossPattern>(); ComboCode = ""; ComboIndex = 0; }
        private void BeginCombo(string code)
        {
            // Entire chain is selected before its first tell. Inputs never create or cancel followups.
            ComboCode = code; ComboIndex = 0;
            string[] steps = code.Split('-'); combo = new BossPattern[steps.Length];
            for (int i = 0; i < steps.Length; i++) combo[i] = steps[i] == "3" ? BossPattern.DT03Bite : steps[i] == "2" ? BossPattern.DT02Sweep : BossPattern.DT01Slam;
            FacePlayer(); RememberStart(code); BeginBasic(combo[0]);
        }
        private void BeginBasic(BossPattern pattern)
        {
            HideMarkers(); ClearBounds(); Pattern = pattern; AttackId = ++nextAttackId;
            TargetLocked = true; landed = false; riseOrigin = transform.position; motionEndX = transform.position.x;
            float tell;
            if (pattern == BossPattern.DT03Bite)
            {
                TargetX = transform.position.x + Facing * 2.2f;
                WarningBounds = Box(TargetX, FloorY + 0.9f, 2.6f, 1.8f); tell = biteTell;
            }
            else if (pattern == BossPattern.DT02Sweep)
            {
                TargetX = transform.position.x + Facing * 2.3f;
                WarningBounds = Box(TargetX, FloorY + 0.30f, 3.8f, 0.60f);
                tell = IsComboFollowup ? sweepFollowupTell : sweepTell;
            }
            else
            {
                motionEndX = ClampBodyX(transform.position.x + Facing * slamAdvance);
                TargetX = motionEndX + Facing * 3.4f;
                WarningBounds = Box(TargetX, FloorY + 0.8f, 2.8f, 1.6f);
                float innerExit = motionEndX + Facing * (2f - player.HurtBounds.extents.x - 0.10f);
                float distanceInside = Mathf.Max(0f, (player.transform.position.x - innerExit) * Facing);
                tell = Mathf.Max(slamTell, reactionMargin + distanceInside / Mathf.Max(1f, player.Movement.MoveSpeed));
            }
            tell = Mathf.Max(tell, safetyTellFloor); safetyTellFloor = 0f;
            LockedTellDuration = tell; SetState(BossState.Telegraph, tell); SetStage("tell", 0f);
            ShowWarning(); Cue("telegraph", new Vector2(TargetX, FloorY), tell); UpdateHurtbox();
        }
        private void BeginSpecial(BossPattern pattern)
        {
            ClearCombo(); FacePlayer(); HideMarkers(); ClearBounds();
            transform.position = new Vector3(ClampBodyX(transform.position.x), FloorY, transform.position.z);
            Pattern = pattern; AttackId = ++nextAttackId; riseOrigin = transform.position;
            TargetLocked = false; LockedTellDuration = 0f; landed = false;
            RememberStart(pattern == BossPattern.ARockfall ? "A" : "B");
            if (pattern == BossPattern.ARockfall)
            {
                TargetX = transform.position.x; nextRockfallTime = encounterTime + rockfallCooldown;
                SetState(BossState.Telegraph, breatheDuration); SetStage("breathe", 0f); Cue("breathe", transform.position, breatheDuration);
            }
            else
            {
                TargetX = ClampBodyX(player.transform.position.x); nextBurrowTime = encounterTime + burrowCooldown;
                SetState(BossState.Telegraph, submergeDuration + travelDuration + riseDuration);
                SetStage("submerge", 0f); Cue("submerge", transform.position, submergeDuration);
            }
            UpdateHurtbox();
        }
        private void TickTelegraph()
        {
            if (Pattern == BossPattern.ARockfall) { SetStage("breathe", StateProgress); return; }
            if (Pattern != BossPattern.BBurrowLeap)
            {
                SetStage("tell", StateProgress);
                if (Pattern == BossPattern.DT01Slam)
                    transform.position = new Vector3(Mathf.Lerp(riseOrigin.x, motionEndX, Mathf.SmoothStep(0f, 1f, StateProgress)), FloorY, riseOrigin.z);
                AnimateWarning(); return;
            }
            if (timer < submergeDuration)
            {
                SetStage("submerge", timer / submergeDuration);
                transform.position = new Vector3(riseOrigin.x, FloorY - 4.7f * Mathf.SmoothStep(0f, 1f, AttackStageProgress), riseOrigin.z);
            }
            else if (timer < submergeDuration + travelDuration)
            {
                if (AttackStage != "travel") Cue("travel", new Vector2(transform.position.x, FloorY), travelDuration);
                SetStage("travel", (timer - submergeDuration) / travelDuration);
                TargetX = ClampBodyX(player.transform.position.x);
                float proposedEmergence = ClampBodyX(TargetX - Facing * 3f);
                transform.position = new Vector3(Mathf.Lerp(riseOrigin.x, proposedEmergence, AttackStageProgress), FloorY - 4.7f, riseOrigin.z);
                DrawBurrowTrace();
            }
            else
            {
                if (!TargetLocked) LockLeapLanding();
                SetStage("rise", (timer - submergeDuration - travelDuration) / riseDuration);
                float y = Mathf.Lerp(-4.7f, leapHeight, Mathf.SmoothStep(0f, 1f, AttackStageProgress));
                transform.position = new Vector3(emergenceX, FloorY + y, riseOrigin.z);
                AnimateWarning();
            }
        }
        private void LockLeapLanding()
        {
            // Final target sample precedes the visible eruption and the entire forward flight.
            TargetX = ClampBodyX(player.transform.position.x);
            float leftRoom = TargetX - (ArenaLeft + 3f), rightRoom = ArenaRight - 3f - TargetX;
            if (Facing > 0 && leftRoom < 2f) Facing = -1;
            else if (Facing < 0 && rightRoom < 2f) Facing = 1;
            emergenceX = ClampBodyX(TargetX - Facing * 3f);
            WarningBounds = Box(TargetX, FloorY + 0.7f, landingWidth, 1.4f);
            TargetLocked = true;
            float radius = Mathf.Max(0.35f, player.HurtBounds.extents.x);
            float leftExit = WarningBounds.min.x - radius - 0.1f, rightExit = WarningBounds.max.x + radius + 0.1f;
            float escape = float.PositiveInfinity, px = player.transform.position.x;
            if (leftExit >= ArenaLeft + radius) escape = Mathf.Min(escape, Mathf.Max(0f, px - leftExit));
            if (rightExit <= ArenaRight - radius) escape = Mathf.Min(escape, Mathf.Max(0f, rightExit - px));
            if (px + radius <= WarningBounds.min.x || px - radius >= WarningBounds.max.x) escape = 0f;
            float required = reactionMargin + escape / Mathf.Max(1f, player.Movement.MoveSpeed);
            LockedTellDuration = Mathf.Max(riseDuration + flightDuration, required);
            leapFlightTime = LockedTellDuration - riseDuration;
            ShowWarning(); Cue("rise", new Vector2(emergenceX, FloorY), riseDuration);
        }
        private void BeginActive()
        {
            if (Pattern == BossPattern.ARockfall)
            {
                SetState(BossState.Active, roarDuration); SetStage("roar", 0f);
                rockfallEnd = encounterTime + rockfallDuration; nextRockTime = encounterTime;
                Cue("roar", transform.position, roarDuration); return;
            }
            if (Pattern == BossPattern.BBurrowLeap)
            {
                if (!TargetLocked) LockLeapLanding();
                SetState(BossState.Active, leapFlightTime + landingDuration + (Phase == 2 ? waveDelay + waveDuration : 0f));
                SetStage("flight", 0f); Cue("flight", new Vector2(TargetX, FloorY), leapFlightTime); return;
            }
            SetState(BossState.Active, Pattern == BossPattern.DT01Slam ? slamStrike : Pattern == BossPattern.DT02Sweep ? sweepStrike : biteStrike);
            SetStage("strike", 0f); Cue("release", new Vector2(TargetX, FloorY), 1f);
            Cue(Pattern == BossPattern.DT01Slam ? "impact" : Pattern == BossPattern.DT02Sweep ? "sweep" : "bite", new Vector2(TargetX, FloorY), 0.5f);
        }
        private void TickAttack()
        {
            ActiveBounds = SecondaryActiveBounds = default;
            if (Pattern == BossPattern.ARockfall) { SetStage("roar", StateProgress); return; }
            if (Pattern != BossPattern.BBurrowLeap)
            {
                SetStage("strike", StateProgress); ActiveBounds = WarningBounds;
                DrawAttackEffect(ActiveBounds); DamageVolume(ActiveBounds, AttackId); return;
            }
            if (timer < leapFlightTime)
            {
                float p = Mathf.Clamp01(timer / leapFlightTime); SetStage("flight", p);
                transform.position = new Vector3(Mathf.Lerp(emergenceX, TargetX, p), FloorY + leapHeight * (1f - p * p), riseOrigin.z);
                AnimateWarning(); return;
            }
            transform.position = new Vector3(TargetX, FloorY, riseOrigin.z);
            float elapsed = timer - leapFlightTime;
            if (!landed) { landed = true; Cue("land", new Vector2(TargetX, FloorY), 1f); }
            if (elapsed < landingDuration)
            {
                SetStage("land", elapsed / landingDuration);
                ActiveBounds = Box(TargetX, FloorY + 0.7f, landingWidth, 1.4f);
                DrawAttackEffect(ActiveBounds); DamageVolume(ActiveBounds, AttackId); return;
            }
            if (Phase != 2) return;
            if (elapsed < landingDuration + waveDelay)
            {
                if (AttackStage != "wave_delay")
                {
                    HideMarkers(); WarningBounds = Box((ArenaLeft + ArenaRight) * 0.5f, FloorY + 0.21f, ArenaRight - ArenaLeft, 0.42f);
                    ShowWarning(); Cue("wave_delay", new Vector2(TargetX, FloorY), waveDelay);
                }
                SetStage("wave_delay", (elapsed - landingDuration) / waveDelay); AnimateWarning(); return;
            }
            if (AttackStage != "wave") Cue("wave", new Vector2(TargetX, FloorY), waveDuration);
            float progress = Mathf.Clamp01((elapsed - landingDuration - waveDelay) / waveDuration); SetStage("wave", progress);
            float travel = Mathf.Max(TargetX - ArenaLeft, ArenaRight - TargetX) * progress;
            ActiveBounds = WaveBounds(TargetX - travel);
            SecondaryActiveBounds = WaveBounds(TargetX + travel);
            // Landing and both low waves share one ID: one B package can deal at most one hit.
            DamageVolume(ActiveBounds, AttackId); DamageVolume(SecondaryActiveBounds, AttackId);
            if (!fightActive || player.IsDead || health.IsDead) return;
            DrawRect(attackFill, ActiveBounds.center, ActiveBounds.size, Color.white);
            DrawRect(dustTrail[0], SecondaryActiveBounds.center, SecondaryActiveBounds.size, new Color(0.8f, 0.6f, 0.3f));
        }
        private Bounds WaveBounds(float center)
        {
            // The wave ends at the advertised arena limits instead of keeping a hidden tail outside.
            float left = Mathf.Max(ArenaLeft, center - 0.325f);
            float right = Mathf.Min(ArenaRight, center + 0.325f);
            return right > left ? Box((left + right) * 0.5f, FloorY + 0.21f, right - left, 0.42f) : default;
        }
        private void DamageVolume(Bounds volume, int attack)
        {
            Bounds target = player.HurtBounds;
            if (volume.size.x <= 0f || volume.min.x >= target.max.x || volume.max.x <= target.min.x || volume.min.y >= target.max.y || volume.max.y <= target.min.y) return;
            float push = player.transform.position.x < volume.center.x ? -1f : 1f;
            if (player.ReceiveAttack(attack, 1, new Vector2(push * 4f, 2.7f), gameObject))
                PlayerDamagedByAttack?.Invoke(attack);
        }
        private void FinishAttack()
        {
            transform.position = new Vector3(transform.position.x, FloorY, transform.position.z);
            HideMarkers(); ClearBounds();
            if (Pattern == BossPattern.ARockfall) { completedSequences++; SelectPattern(); return; }
            if (combo.Length > 0 && ComboIndex + 1 < combo.Length)
            {
                SetState(BossState.Link, comboLink); SetStage("link", 0f); Cue("link", transform.position, comboLink); return;
            }
            completedSequences++;
            float recovery = Pattern == BossPattern.BBurrowLeap ? leapRecovery : Pattern == BossPattern.DT01Slam ? slamRecovery : Pattern == BossPattern.DT02Sweep ? sweepRecovery : biteRecovery;
            SetState(BossState.Recovery, recovery); Cue("recover", transform.position, recovery);
        }
        private void TickRockfall(float dt)
        {
            for (int i = rocks.Count - 1; i >= 0; i--)
            {
                DocheolRockfall rock = rocks[i]; rock.Tick(dt);
                if (rock.ImpactedThisTick) Cue("rockfall_impact", new Vector2(rock.LockedX, FloorY), rock.Width);
                if (rock.ActiveBounds.size.x > 0f) DamageVolume(rock.ActiveBounds, rock.AttackId);
                if (!fightActive || player.IsDead || health.IsDead) return;
                if (rock.Finished) { rock.Dispose(); rocks.RemoveAt(i); }
            }
            if (encounterTime < rockfallEnd - 1.8f && encounterTime >= nextRockTime)
            {
                // A locked action postpones this placement instead of consuming the full interval.
                nextRockTime = encounterTime + (PlayerMovementCommitted() ? Time.fixedDeltaTime : rockfallInterval);
                SpawnRock();
            }
            RockfallEscapeCorridor = GetRockfallEscapeCorridor(out Bounds corridor) ? corridor : default;
        }
        private void SpawnRock()
        {
            // Original movement does not expose its remaining action lock. Do not add a new
            // hazard based on an invented dash/attack trajectory; existing rocks still resolve.
            if (PlayerMovementCommitted()) return;
            bool targeted = rockSequence % 3 == 0;
            RockfallSize size = (RockfallSize)(rockSequence % 3); rockSequence++;
            float width = DocheolRockfall.WidthFor(size);
            float x = targeted ? player.transform.position.x : Mathf.Lerp(ArenaLeft + 1f, ArenaRight - 1f, Mathf.Repeat(rockSequence * 0.381966f, 1f));
            for (int attempt = 0; attempt < 8; attempt++)
            {
                x = Mathf.Clamp(x, ArenaLeft + width * 0.5f, ArenaRight - width * 0.5f);
                Bounds proposed = Box(x, FloorY + 0.8f, width, 1.6f); bool overlap = false;
                foreach (DocheolRockfall other in rocks)
                    if (other.Stage != "dust" && proposed.min.x < other.WarningBounds.max.x + 0.25f && proposed.max.x > other.WarningBounds.min.x - 0.25f) { overlap = true; break; }
                float impactTime = 0.95f + (int)size * 0.15f + 0.25f;
                if (!overlap && FindEscapeCorridor(proposed, true, out _) && FindTimedEscape(proposed, true, impactTime, null, true))
                {
                    DocheolRockfall rock = new DocheolRockfall(++nextAttackId, x, FloorY, size, targeted, markers.transform);
                    rocks.Add(rock); Cue("rockfall_warning", new Vector2(x, FloorY), rock.WarningDuration); return;
                }
                RejectedRockfallPlacements++;
                targeted = false; x = Mathf.Lerp(ArenaLeft + 1f, ArenaRight - 1f, Mathf.Repeat(rockSequence * 0.381966f + (attempt + 1) * 0.173f, 1f));
            }
            // Skip a rock instead of filling every escape corridor.
        }
        public bool HasRockfallEscapeRoute() => GetRockfallEscapeCorridor(out _);
        public bool GetRockfallEscapeCorridor(out Bounds corridor) => FindEscapeCorridor(default, false, out corridor);
        private bool FindEscapeCorridor(Bounds proposed, bool includeProposed, out Bounds corridor)
        {
            blockedIntervals.Clear();
            foreach (DocheolRockfall rock in rocks) if (rock.Stage != "dust") blockedIntervals.Add(new Vector2(rock.WarningBounds.min.x, rock.WarningBounds.max.x));
            if (includeProposed) blockedIntervals.Add(new Vector2(proposed.min.x, proposed.max.x));
            if ((State == BossState.Telegraph || State == BossState.Active) && WarningBounds.size.x > 0f && Pattern != BossPattern.ARockfall)
                blockedIntervals.Add(new Vector2(WarningBounds.min.x, WarningBounds.max.x));
            blockedIntervals.Sort((a, b) => a.x.CompareTo(b.x));
            float cursor = ArenaLeft, bestDistance = float.PositiveInfinity;
            corridor = default; float px = player == null ? 0f : player.transform.position.x;
            for (int i = 0; i <= blockedIntervals.Count; i++)
            {
                float end = i < blockedIntervals.Count ? Mathf.Clamp(blockedIntervals[i].x, ArenaLeft, ArenaRight) : ArenaRight;
                if (end - cursor >= escapeCorridorWidth)
                {
                    float center = Mathf.Clamp(px, cursor + escapeCorridorWidth * 0.5f, end - escapeCorridorWidth * 0.5f);
                    float distance = Mathf.Abs(px - center);
                    if (distance < bestDistance) { bestDistance = distance; corridor = Box(center, FloorY + 0.9f, escapeCorridorWidth, 1.8f); }
                }
                if (i < blockedIntervals.Count) cursor = Mathf.Max(cursor, blockedIntervals[i].y);
            }
            return corridor.size.x > 0f && bestDistance <= (player == null ? 5f : player.Movement.MoveSpeed) * (0.95f - reactionMargin);
        }
        /// <summary>
        /// Conservative movement preview, not an immunity or an executed input. It includes the
        /// complete preselected combo, each rock's impact interval, wall limits, hurtbox size,
        /// reaction delay, walking speed and ballistic jumps. QA can follow the returned samples.
        /// </summary>
        public bool TryGetRockfallEscapeRoute(out Vector3[] samples)
        {
            bool found = FindTimedEscape(default, false, 0f, null, true);
            samples = escapeRoute.ToArray();
            return found;
        }
        private bool FindTimedEscape(Bounds proposed, bool includeProposed, float proposedImpact, string plannedCode, bool save)
        {
            if (PlayerMovementCommitted())
            {
                if (save) { escapeRoute.Clear(); EscapeRouteOriginTime = encounterTime; }
                return false;
            }
            routeDangers.Clear();
            foreach (DocheolRockfall rock in rocks)
                if (rock.ImpactRemaining > 0f)
                    routeDangers.Add(new TimedDanger { bounds = rock.WarningBounds, start = rock.TimeToImpact, end = rock.ImpactRemaining });
            if (includeProposed)
                routeDangers.Add(new TimedDanger { bounds = proposed, start = proposedImpact, end = proposedImpact + 0.14f });
            float forecastX = transform.position.x, cursor = 0f;
            if (plannedCode != null)
            {
                string[] steps = plannedCode.Split('-');
                for (int i = 0; i < steps.Length; i++)
                {
                    BossPattern step = steps[i] == "3" ? BossPattern.DT03Bite : steps[i] == "2" ? BossPattern.DT02Sweep : BossPattern.DT01Slam;
                    AddFutureDanger(step, i > 0, ref forecastX, ref cursor);
                }
            }
            else if (combo.Length > 0 && (State == BossState.Telegraph || State == BossState.Active || State == BossState.Link))
            {
                int first = ComboIndex;
                if (State == BossState.Link) { cursor = Mathf.Max(0f, duration - timer); first++; }
                else
                {
                    float strike = Pattern == BossPattern.DT01Slam ? slamStrike : Pattern == BossPattern.DT02Sweep ? sweepStrike : biteStrike;
                    float start = State == BossState.Telegraph ? Mathf.Max(0f, duration - timer) : 0f;
                    float end = State == BossState.Telegraph ? start + strike : Mathf.Max(0f, duration - timer);
                    routeDangers.Add(new TimedDanger { bounds = WarningBounds, start = start, end = end, sweep = Pattern == BossPattern.DT02Sweep, basic = true });
                    cursor = end + comboLink; forecastX = motionEndX; first++;
                }
                for (int i = first; i < combo.Length; i++) AddFutureDanger(combo[i], true, ref forecastX, ref cursor);
            }
            // State changes reset their timer on fixed ticks. Widen later basic intervals for
            // accumulated tell/strike/link quantization, rather than treating nominal sums as exact.
            int basicOrdinal = 0;
            for (int i = 0; i < routeDangers.Count; i++)
            {
                TimedDanger danger = routeDangers[i];
                if (danger.basic) { basicOrdinal++; danger.end += basicOrdinal * Time.fixedDeltaTime * 4f; }
                routeDangers[i] = danger;
            }
            float horizon = 0.5f, jumpAt = float.PositiveInfinity;
            foreach (TimedDanger danger in routeDangers)
            {
                horizon = Mathf.Max(horizon, danger.end + 0.1f);
                if (danger.sweep) jumpAt = Mathf.Min(jumpAt, Mathf.Max(reactionMargin, danger.start - 0.16f));
            }
            float speed = player.Movement.MoveSpeed;
            float inputDelay = reactionMargin;
            jumpAt = Mathf.Max(jumpAt, inputDelay);
            float radius = Mathf.Max(0.35f, player.HurtBounds.extents.x) + 0.04f;
            float initialX = player.HurtBounds.center.x;
            // A physical wall may lie just outside the scheduling arena. Never snap a wall-side
            // player inward in the preview; that would invent free escape distance.
            float routeLeft = Mathf.Min(ArenaLeft + radius, initialX), routeRight = Mathf.Max(ArenaRight - radius, initialX);
            float height = player.HurtBounds.size.y;
            float gravity = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(3f, player.Body.gravityScale);
            const float dt = 0.02f;
            // Walking both ways, holding for a jump, and crossing the boss's inside opening are
            // tested separately. No damage ledger or invulnerability is consulted by this planner.
            for (int strategy = 0; strategy < 7; strategy++)
            {
                List<Vector3> route = save ? new List<Vector3>() : null;
                float x = player.HurtBounds.center.x, y = Mathf.Max(FloorY + 0.025f, player.HurtBounds.min.y);
                float vertical = player.Body.linearVelocity.y;
                bool safe = true, jumped = false;
                for (float t = 0f; t <= horizon + dt * 0.5f; t += dt)
                {
                    if (t > 0f && t < inputDelay)
                        x = Mathf.Clamp(x + player.Body.linearVelocity.x * dt, routeLeft, routeRight);
                    else if (t > 0f)
                    {
                        float direction = strategy == 0 ? -1f : strategy == 1 ? 1f : strategy == 2 ? 0f : strategy == 3 ? Facing : -Facing;
                        if (strategy >= 5)
                        {
                            float target = transform.position.x + Facing * (strategy == 5 ? 5.0f : -0.9f);
                            // Once the slow slam's tell begins, the inside route becomes available.
                            foreach (TimedDanger danger in routeDangers)
                                if (danger.bounds.size.y > 1.5f && danger.bounds.size.x > 2.7f && danger.start > t && danger.start - t <= slamTell)
                                    target = transform.position.x - Facing * 0.9f;
                            direction = Mathf.Abs(target - x) < speed * dt ? 0f : Mathf.Sign(target - x);
                        }
                        x = Mathf.Clamp(x + direction * speed * dt, routeLeft, routeRight);
                    }
                    if (!jumped && t >= jumpAt && y <= FloorY + 0.03f)
                    {
                        vertical = player.Movement.JumpSpeed; jumped = true;
                    }
                    if (t > 0f)
                    {
                        vertical -= gravity * dt; y += vertical * dt;
                        if (y < FloorY + 0.025f) { y = FloorY + 0.025f; vertical = 0f; }
                    }
                    foreach (TimedDanger danger in routeDangers)
                    {
                        if (t + dt < danger.start || t - dt > danger.end) continue;
                        Bounds b = danger.bounds;
                        if (x + radius > b.min.x && x - radius < b.max.x && y + height > b.min.y && y < b.max.y)
                        { safe = false; break; }
                    }
                    if (!safe) break;
                    route?.Add(new Vector3(x, y, t));
                }
                if (!safe) continue;
                if (save) { escapeRoute.Clear(); escapeRoute.AddRange(route); EscapeRouteOriginTime = encounterTime; }
                return true;
            }
            if (save) { escapeRoute.Clear(); EscapeRouteOriginTime = encounterTime; }
            return false;
        }
        private bool PlayerMovementCommitted()
        {
            WarriorAction action = player.Movement.Action;
            return action is WarriorAction.BasicAttack or WarriorAction.Attack1 or WarriorAction.Attack2
                or WarriorAction.JumpAttack or WarriorAction.DashAttack or WarriorAction.Dash
                or WarriorAction.Hurt or WarriorAction.Slide or WarriorAction.EdgeGrab or WarriorAction.EdgeIdle;
        }
        private void AddFutureDanger(BossPattern step, bool followup, ref float originX, ref float cursor)
        {
            float tell, strike; Bounds bounds;
            if (step == BossPattern.DT03Bite)
            {
                bounds = Box(originX + Facing * 2.2f, FloorY + 0.9f, 2.6f, 1.8f); tell = biteTell; strike = biteStrike;
            }
            else if (step == BossPattern.DT02Sweep)
            {
                bounds = Box(originX + Facing * 2.3f, FloorY + 0.3f, 3.8f, 0.6f);
                tell = followup ? sweepFollowupTell : sweepTell; strike = sweepStrike;
            }
            else
            {
                originX = ClampBodyX(originX + Facing * slamAdvance);
                bounds = Box(originX + Facing * 3.4f, FloorY + 0.8f, 2.8f, 1.6f);
                float inner = originX + Facing * (2f - player.HurtBounds.extents.x - 0.10f);
                tell = Mathf.Max(slamTell, reactionMargin + Mathf.Max(0f, (player.transform.position.x - inner) * Facing) / Mathf.Max(1f, player.Movement.MoveSpeed));
                strike = slamStrike;
            }
            float earliestTell = tell, latestTell = tell;
            if (step == BossPattern.DT01Slam && followup)
            {
                // A later slam may adapt its tell to the player's then-current distance. Cover
                // that entire timing interval rather than trusting today's player position.
                earliestTell = slamTell;
                float inner = originX + Facing * (2f - player.HurtBounds.extents.x - 0.10f);
                float farthest = Facing > 0 ? ArenaRight - inner : inner - ArenaLeft;
                latestTell = Mathf.Max(slamTell, reactionMargin + Mathf.Max(0f, farthest) / Mathf.Max(1f, player.Movement.MoveSpeed));
            }
            routeDangers.Add(new TimedDanger { bounds = bounds, start = cursor + earliestTell, end = cursor + latestTell + strike, sweep = step == BossPattern.DT02Sweep, basic = true });
            cursor += tell + strike + comboLink;
        }
        private void ClearRockfall()
        {
            foreach (DocheolRockfall rock in rocks) rock.Dispose();
            rocks.Clear(); rockfallEnd = 0f; RockfallEscapeCorridor = default; escapeRoute.Clear();
        }
        private void BeginTransition()
        {
            ClearRockfall(); HideMarkers(); ClearBounds(); ClearCombo(); TargetLocked = false;
            transform.position = new Vector3(ClampBodyX(transform.position.x), FloorY, transform.position.z);
            Pattern = BossPattern.None; SetState(BossState.Transition, 2.4f); health.DamageEnabled = false;
            if (player != null && player.Health != null) player.Health.DamageEnabled = false;
            if (hurtCollider != null) hurtCollider.enabled = false;
            Cue("phase", transform.position, 2.4f);
        }
        public Vector2 GetArmorWorldPoint(int index)
        {
            Vector2 offset = index switch
            {
                0 => new Vector2(Facing * 0.8f, 2.65f), 1 => new Vector2(-Facing * 0.30f, 1.52f),
                2 => new Vector2(-Facing * 0.65f, 2.2f), _ => new Vector2(-Facing * 1.55f, 1.1f)
            };
            return (Vector2)transform.position + offset;
        }
        private void OnDamageTaken(DamageInfo damage)
        {
            LastHitPoint = damage.HitPoint; float closest = float.PositiveInfinity; LastHitRegion = 0;
            for (int i = 0; i < 4; i++)
            {
                float distance = (damage.HitPoint - GetArmorWorldPoint(i)).sqrMagnitude;
                if (distance < closest) { closest = distance; LastHitRegion = i; }
            }
            LocalHit?.Invoke(LastHitPoint, LastHitRegion, (ArmorMask & (1 << LastHitRegion)) != 0);
            // Armor is visual only and leaves one shared HP pool unchanged; order is deterministic.
            if (!threshold75 && health.CurrentHealth <= 30) { threshold75 = true; BreakArmor(0, GetArmorWorldPoint(0)); }
            if (!threshold60 && health.CurrentHealth <= 24) { threshold60 = true; BreakArmor(1, GetArmorWorldPoint(1)); }
            if (Phase == 1 && health.CurrentHealth <= 20 && !health.IsDead)
            {
                BreakArmor(2, GetArmorWorldPoint(2)); phasePending = true;
                if (State != BossState.Transition) BeginTransition();
            }
        }
        private void BreakArmor(int index, Vector2 point)
        {
            if ((ArmorMask & (1 << index)) == 0) return;
            ArmorMask &= ~(1 << index); ArmorBroken?.Invoke(index, ArmorMask, point); Cue("armor_break", point, 1f);
        }
        private void SetState(BossState next, float seconds)
        {
            State = next; timer = 0f; duration = seconds;
            AttackStage = next == BossState.Recovery ? "recover" : next.ToString().ToLowerInvariant(); AttackStageProgress = 0f;
        }
        private void SetStage(string stage, float progress) { AttackStage = stage; AttackStageProgress = Mathf.Clamp01(progress); }
        private void ClearBounds() { WarningBounds = ActiveBounds = SecondaryActiveBounds = default; }
        private void OnDied()
        {
            fightActive = false; ClearRockfall(); SetState(BossState.Dead, 0f); HideMarkers(); ClearBounds(); UpdateHurtbox(); Cue("death", transform.position, 1f);
        }
        private void UpdateHurtbox()
        {
            if (hurtRoot == null) return;
            hurtRoot.localPosition = new Vector3(Facing * 0.4f, 0.95f, 0f);
            weakPoint.transform.localPosition = new Vector3(Facing * 1.25f, -0.91f, 0f);
            bool grounded = Mathf.Abs(transform.position.y - FloorY) <= 0.25f;
            bool vulnerable = fightActive && State != BossState.Transition && State != BossState.Dead && grounded && !IsUnderground;
            hurtCollider.enabled = vulnerable; if (health != null) health.DamageEnabled = vulnerable;
            weakPoint.enabled = vulnerable && State == BossState.Recovery;
            weakPoint.color = new Color(1f, 0.81f, 0.29f, 0.25f + Mathf.Sin(Time.time * 9f) * 0.07f);
        }
        private void DrawBurrowTrace()
        {
            for (int i = 0; i < dustTrail.Length; i++)
                DrawRect(dustTrail[i], new Vector3(transform.position.x + (i - 2f) * 0.13f, FloorY + 0.07f), new Vector3(0.14f, 0.09f, 1f), new Color(0.65f, 0.5f, 0.27f, 0.65f));
        }
        private void CreatePresentation()
        {
            if (hurtRoot != null) return;
            GameObject weak = new GameObject("BodyMouthAndForepaws_Hurtbox");
            weak.transform.SetParent(transform, false);
            hurtRoot = weak.transform;
            hurtCollider = weak.AddComponent<BoxCollider2D>();
            hurtCollider.isTrigger = true;
            // Keep one receiver so an attack crossing mouth and body still deals exactly one hit.
            hurtCollider.size = new Vector2(5.4f, 1.85f);
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
            // Forepaw sweep dust stays attached to its footprint; only B emits traveling ground waves.
            float height = Pattern == BossPattern.DT03Bite ? 0.12f : Pattern == BossPattern.DT02Sweep ? 0.10f : 0.30f;
            DrawRect(attackFill, new Vector3(hit.center.x, FloorY + height * 0.5f),
                new Vector3(hit.size.x, height, 1f), new Color(0.85f, 0.65f, 0.40f, 0.60f));
            for (int i = 0; i < dustTrail.Length; i++)
            {
                float x = Mathf.Lerp(hit.min.x, hit.max.x, (i + 0.5f) / dustTrail.Length);
                DrawRect(dustTrail[i], new Vector3(x, FloorY + 0.12f), new Vector3(0.18f, 0.12f, 1f), new Color(0.60f, 0.44f, 0.25f, 0.45f));
            }
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
            if (health != null) { health.Died -= OnDied; health.DamageTaken -= OnDamageTaken; }
            ClearRockfall();
            if (markers != null) Destroy(markers);
            foreach (Material material in markerMaterials.Values) if (material != null) Destroy(material);
            markerMaterials.Clear();
        }
    }
}
