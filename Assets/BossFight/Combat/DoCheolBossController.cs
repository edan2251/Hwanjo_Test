using System;
using System.Collections.Generic;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;
using UnityEngine;
namespace FourGuardians.BossFight
{
    public enum BossState { Dormant, Idle, Telegraph, Active, Recovery, Transition, Dead, Link, Waiting, Followup, Turning, Settling, Reposition, Backdash, ActionWait }
    public enum BossPattern { None, DT01Slam, DT02Sweep, DT03Bite, DT04Inhale, DT05BodySlam, DT06Burrow, ARockfall, BBurrowLeap, WholeBodyBite }
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
        public bool ControlsLocked => State == BossState.Transition;
        public bool Turning => State == BossState.Turning;
        public int TurnFromFacing { get; private set; } = -1;
        public int TurnToFacing { get; private set; } = -1;
        public int TurnCount { get; private set; }
        public int BackdashCount { get; private set; }
        public int RepositionCount { get; private set; }
        public float BackdashCooldownRemaining => Mathf.Max(0f, nextBackdashTime - encounterTime);
        public float MouthMinimumReach => .9f;
        public float MouthMaximumReach => 3.5f;
        public float PawMinimumReach => -.65f;
        public float PawMaximumReach => 4.2f;
        public int AttackId { get; private set; }
        public int LastCompletedAttackId { get; private set; }
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
        public float EncounterSeconds => encounterTime;
        public bool RockfallActive => rockfallEnd > encounterTime || rocks.Count > 0;
        public float RockfallRemaining => Mathf.Max(0f, rockfallEnd - encounterTime);
        public IReadOnlyList<DocheolRockfall> Rockfalls => rocks.AsReadOnly();
        public int RejectedRockfallPlacements { get; private set; }
        public Bounds RockfallEscapeCorridor { get; private set; }
        // Samples are (hurtbox center X, hurtbox bottom Y, seconds from the preview origin).
        public IReadOnlyList<Vector3> RockfallEscapeRoute => escapeRoute.AsReadOnly();
        public float EscapeRouteOriginTime { get; private set; }
        public bool EscapePreviewAvailable => player != null && !PlayerMovementCommitted();
        public string BurrowExitPlan { get; private set; } = "";
        public bool BurrowPackageActive { get; private set; }
        public int SafetyWaitCount { get; private set; }
        public string SafetyWaitReason { get; private set; } = "";
        public int RockfallSeed => rockfallSeed;
        public IReadOnlyList<RockfallSize> RockfallSizeDraws => rockSizeDraws.AsReadOnly();
        public bool EntryRockfallUsed { get; private set; }
        public bool LateRockfallUsed { get; private set; }
        public bool LateRockfallPending { get; private set; }
        public int RockfallEventCount { get; private set; }
        public int WholeBodyAttackCount { get; private set; }
        public int WholeFirstSide { get; private set; }
        public int WholeSwingSide { get; private set; }
        public Bounds SecondaryWarningBounds { get; private set; }
        public float WaveDelayDuration => waveDelay;
        public float WaveTravelDuration => waveDuration;
        public float WholeRecoveryDuration => wholeRecovery;
        public float ArenaLeft = -9.5f, ArenaRight = 9.5f, FloorY;
        [Header("Basic attack timing")]
        [SerializeField] private float biteTell = 0.95f, biteStrike = 0.16f, biteRecovery = 0.30f;
        [SerializeField] private float sweepTell = 0.50f, sweepFollowupTell = 0.30f, sweepStrike = 0.16f, sweepRecovery = 0.34f;
        [SerializeField] private float biteAdvance = .28f, sweepAdvance = .22f, comboAdvanceLimit = .50f;
        public float LockedAdvanceDistance { get; private set; }
        public float ComboAdvanceDistance { get; private set; }
        [SerializeField] private float comboLink = 0.08f, reactionMargin = 0.20f;
        [Header("Grounded preparation")]
        [SerializeField] private float turnDuration = .78f, settleDuration = .14f;
        [SerializeField] private float repositionSpeed = 3f, backdashSpeed = 8.5f, backdashCooldown = 4.8f;
        private float nextBackdashTime, spacingDestination;
        private string preparedPlan;
        [Header("V5: committed approach and phase rhythm")]
        [SerializeField] private float phaseOneActionWait=1.15f, phaseTwoActionWait=.32f;
        [SerializeField] private float phaseOneApproachLimit=1.65f, phaseTwoApproachLimit=2.1f;
        private bool decisionOpen, decisionTurnSpent, decisionMoveSpent;
        public float DecisionTargetX { get; private set; }
        public int DecisionCount { get; private set; }
        public int RootBlockCount { get; private set; }
        public float ActionWaitRemaining => State==BossState.ActionWait?Mathf.Max(0,duration-timer):0;
        public float PhaseActionWait => Phase==1?phaseOneActionWait:phaseTwoActionWait;
        public float PreparationSeconds { get; private set; }
        public int MovementSegmentsThisDecision { get; private set; }
        public int TurnSegmentsThisDecision { get; private set; }
        public void RecordRootBlock(Vector2 point){RootBlockCount++;Cue("root_block",point,.25f);}
        [Header("A: grounded roar and persistent rockfall")]
        [SerializeField] private float rockfallCooldown = 18f, rockfallDuration = 5.5f, rockfallInterval = 0.58f;
        [SerializeField] private float breatheDuration = 0.55f, roarDuration = 0.60f, escapeCorridorWidth = 2.2f;
        [SerializeField] private int rockfallSeed = 20261003;
        [Header("B: underground travel and forward landing")]
        [SerializeField] private float burrowCooldown = 12f, submergeDuration = 0.48f, travelDuration = 0.65f;
        [SerializeField] private float riseDuration = 0.48f, flightDuration = 0.85f, leapHeight = 4.2f;
        [SerializeField] private float landingDuration = 0.16f, landingWidth = 4.4f, waveDelay = 0.44f, waveDuration = 0.90f, leapRecovery = 1.15f;
        [SerializeField] private float burrowFollowupBrace = 0.24f, burrowFollowupTell = 0.90f;
        [Header("Phase-two whole-body bite")]
        [SerializeField] private float wholeTell=1.80f,wholeArc=.12f,wholeContact=.12f,wholeBridge=.24f,wholeRecovery=1.90f,wholeCooldown=8.5f;
        [SerializeField] private int lateRockfallHealth=10;
        private float nextWholeTime;
        private Bounds wholeFirstBounds,wholeSecondBounds;
        [SerializeField] private float wholeFinalDelay=1.05f;
        private int allocatedWholeIndex;
        public int WholeContactIndex { get; private set; }
        public float WholeInitialTell => wholeTell;
        public float WholeFastInterval => wholeArc+wholeContact+wholeBridge;
        public float WholeFinalDelay => wholeFinalDelay;
        private SpriteRenderer wholeSecondWarning;
        private BossFightPlayerAdapter player;
        private Health2D health;
        private BoxCollider2D hurtCollider;
        private DocheolHitRegions hitRegions;
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
        private readonly List<RockfallSize> rockSizeDraws = new List<RockfallSize>();
        private System.Random rockRandom;
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
        private static readonly string[] PhaseTwoCombos = { "3", "2", "3-2" };
        public void Configure(BossFightPlayerAdapter newPlayer, Health2D newHealth)
        {
            if (health != null) { health.Died -= OnDied; health.DamageTaken -= OnDamageTaken; }
            player = newPlayer; health = newHealth;
            if (health != null)
            {
                health.Configure(40, CombatTeam.Enemy); health.ConfigureInvulnerability(0.12f);
                health.Died += OnDied; health.DamageTaken += OnDamageTaken; health.DamageEnabled = false;
            }
            CreatePresentation();
            var regions=GetComponent<DocheolHitRegions>();
            if(regions==null)regions=gameObject.AddComponent<DocheolHitRegions>();
            regions.Configure(this,health,hurtCollider,GetComponentInChildren<DocheolVisual>());
            hitRegions=regions;
            UpdateHurtbox();
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
            Facing = TurnFromFacing = TurnToFacing = -1; Phase = 1; ArmorMask = 15; threshold75 = threshold60 = phasePending = false;
            decisionOpen=decisionTurnSpent=decisionMoveSpent=false;DecisionCount=RootBlockCount=MovementSegmentsThisDecision=TurnSegmentsThisDecision=0;PreparationSeconds=0;DecisionTargetX=position.x;
            LockedAdvanceDistance=ComboAdvanceDistance=0;TurnCount = BackdashCount = RepositionCount = 0; nextBackdashTime = nextWholeTime = 0; preparedPlan = null;
            EntryRockfallUsed=LateRockfallUsed=LateRockfallPending=false;RockfallEventCount=WholeBodyAttackCount=0;
            WholeFirstSide=WholeSwingSide=-1;SecondaryWarningBounds=default;allocatedWholeIndex=0;WholeContactIndex=0;
            LastHitRegion = -1; LastHitPoint = position; encounterTime = nextBurrowTime = nextRockfallTime = 0f;
            rockSequence = completedSequences = RejectedRockfallPlacements = 0;
            rockRandom = new System.Random(rockfallSeed); rockSizeDraws.Clear();
            BurrowPackageActive = false; BurrowExitPlan = ""; SafetyWaitCount = 0; SafetyWaitReason = "";
            recentStarts.Clear(); ClearCombo(); Pattern = BossPattern.None; AttackId = LastCompletedAttackId = 0;
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
                case BossPattern.DT01Slam: return false; // Retired in both phases.
                case BossPattern.DT02Sweep: return ForceCombo("2");
                case BossPattern.DT03Bite: return ForceCombo("3");
                case BossPattern.ARockfall:
                    if (Phase!=2 || LateRockfallUsed || RockfallActive || BurrowPackageActive) return false;
                    LateRockfallUsed=true;LateRockfallPending=false;BeginSpecial(pattern); return true;
                case BossPattern.WholeBodyBite:
                    if(Phase!=2 || RockfallActive || BurrowPackageActive)return false;
                    FacePlayer();BeginWholeBodyBite();return true;
                case BossPattern.BBurrowLeap:
                    if (RockfallActive || Pattern == BossPattern.ARockfall && State != BossState.Recovery) return false;
                    BeginSpecial(pattern); return true;
                default: return false;
            }
        }
        public bool ForceCombo(string code)
        {
            if (!CanForce() || Array.IndexOf(Phase == 1 ? PhaseOneCombos : PhaseTwoCombos, code) < 0) return false;
            if (RockfallActive && (!EscapePreviewAvailable || !FindTimedEscape(default, false, 0f, code, false))) return false;
            transform.position = new Vector3(ClampBodyX(transform.position.x), FloorY, transform.position.z);
            FacePlayer(); BeginCombo(code); return true; // Controlled initial QA setup only.
        }
        /// <summary>Controlled initial B package, including its exit chosen before the first tell.</summary>
        public bool ForceBurrowPlan(string exitPlan)
        {
            if (!CanForce() || RockfallActive || (exitPlan != "rest" && Array.IndexOf(Phase == 1 ? PhaseOneCombos : PhaseTwoCombos, exitPlan) < 0)) return false;
            BeginSpecial(BossPattern.BBurrowLeap, exitPlan); return true;
        }
        public bool ConfigureRockfallSeed(int seed)
        {
            if (fightActive) return false;
            rockfallSeed = seed; rockRandom = new System.Random(seed); rockSizeDraws.Clear(); return true;
        }
        private bool CanForce() => fightActive && health != null && !health.IsDead && player != null && !player.IsDead && State != BossState.Transition;
        private void FixedUpdate()
        {
            if (!fightActive || player == null || health == null || health.IsDead) return;
            if (player.IsDead) { SetFightActive(false); return; }
            float dt = Time.fixedDeltaTime; timer += dt; encounterTime += dt;
            if (!ControlsLocked) TickRockfall(dt);
            if (!fightActive || player.IsDead || health.IsDead) return;
            // A threshold never cancels an advertised attack package or recovery.
            if (phasePending && (State == BossState.Idle || State == BossState.Waiting || State == BossState.ActionWait)) BeginTransition();
            switch (State)
            {
                case BossState.ActionWait:
                    SetStage("action_wait",StateProgress);
                    if(timer>=duration)SelectPattern();
                    break;
                case BossState.Idle:
                    SetStage("idle", StateProgress);
                    if (timer >= duration) SelectPattern();
                    break;
                case BossState.Turning:
                    SetStage("turn", StateProgress);
                    if (timer >= duration) { Facing = TurnToFacing; SetState(BossState.Settling, settleDuration); SetStage("settle", 0); }
                    break;
                case BossState.Reposition:
                case BossState.Backdash:
                    SetStage(State == BossState.Backdash ? "backdash" : "reposition", StateProgress);
                    PreparationSeconds+=dt; TickSpacing(dt);
                    if (timer >= duration) { SetState(BossState.Settling, settleDuration); SetStage("settle", 0); }
                    break;
                case BossState.Settling:
                    SetStage("settle", StateProgress);
                    if (timer >= duration) SelectPattern();
                    break;
                case BossState.Telegraph: TickTelegraph(); if (timer >= duration) BeginActive(); break;
                case BossState.Active:
                    TickAttack();
                    if (fightActive && State == BossState.Active && timer >= duration) FinishAttack();
                    break;
                case BossState.Link:
                    SetStage("link", StateProgress);
                    if (timer >= duration) { SetState(BossState.Telegraph, LockedTellDuration); SetStage("tell", 0f); }
                    break;
                case BossState.Waiting:
                    SetStage("wait", StateProgress);
                    if (timer >= duration) SelectPattern();
                    break;
                case BossState.Followup:
                    SetStage("link", StateProgress);
                    if (timer >= duration) { safetyTellFloor = burrowFollowupTell; preparedPlan = BurrowExitPlan; SelectPattern(); }
                    break;
                case BossState.Recovery:
                    SetStage("recover", StateProgress);
                    if (timer >= duration) { BurrowPackageActive = false; decisionOpen=false; Pattern=BossPattern.None;TargetLocked=false;ClearCombo();SetState(BossState.ActionWait,PhaseActionWait);SetStage("action_wait",0); }
                    break;
                case BossState.Transition:
                    TickTransition();
                    break;
            }
            UpdateHurtbox();
        }
        private bool PreparePosition()
        {
            // One observation and at most one turn + one movement per decision.
            // Settling resumes this committed decision, never a fresh pursuit loop.
            float dx=DecisionTargetX-transform.position.x;
            int desired=Mathf.Abs(dx)>.12f?(dx>0?1:-1):Facing;
            if(desired!=Facing && !decisionTurnSpent)
            {
                decisionTurnSpent=true;TurnSegmentsThisDecision++;TurnFromFacing=Facing;TurnToFacing=desired;TurnCount++;
                ClearBounds();HideMarkers();Pattern=BossPattern.None;TargetLocked=false;
                SetState(BossState.Turning,turnDuration);SetStage("turn",0);return false;
            }
            if(decisionMoveSpent)return true;
            decisionMoveSpent=true;
            float distance=Mathf.Abs(dx);
            // Use the effective paw reach, instead of the old narrow ideal-distance band.
            if(distance>=1.05f && distance<=PawMaximumReach)return true;
            float side=dx>=0?-1:1;
            float goal=ClampBodyX(DecisionTargetX+side*2.55f);
            if((goal-DecisionTargetX)*side<.4f)goal=transform.position.x;
            float available=Mathf.Abs(goal-transform.position.x);
            if(available<.12f)
            {
                if(!RockfallActive && BurrowCooldownRemaining<=0 && !BurrowPackageActive)
                {BeginSpecial(BossPattern.BBurrowLeap);return false;}
                return true;
            }
            bool retreat=distance<1.05f;
            bool dash=retreat && BackdashCooldownRemaining<=0 && (distance<.65f || UnityEngine.Random.value<.40f);
            float step=Mathf.Min(available,dash?1.65f:retreat?.95f:Phase==1?phaseOneApproachLimit:phaseTwoApproachLimit);
            spacingDestination=transform.position.x+Mathf.Sign(goal-transform.position.x)*step;
            Pattern=BossPattern.None;TargetLocked=false;ClearBounds();HideMarkers();MovementSegmentsThisDecision++;
            if(dash){nextBackdashTime=encounterTime+backdashCooldown;BackdashCount++;}else RepositionCount++;
            SetState(dash?BossState.Backdash:BossState.Reposition,step/(dash?backdashSpeed:repositionSpeed));
            SetStage(dash?"backdash":"reposition",0);return false;
        }
        private void TickSpacing(float dt)
        {
            float x=transform.position.x, direction=Mathf.Sign(spacingDestination-x);
            float next=Mathf.MoveTowards(x,spacingDestination,(State==BossState.Backdash?backdashSpeed:repositionSpeed)*dt);
            float gap=player.HurtBounds.center.x-x;
            if(gap*direction>0 && Mathf.Abs(gap)<.65f) next=x;
            transform.position=new Vector3(ClampBodyX(next),FloorY,transform.position.z);
        }
        private bool EffectiveBasic(string code)
        {
            float minimum=code[0]=='3'?MouthMinimumReach:PawMinimumReach;
            float maximum=code[0]=='3'?MouthMaximumReach:PawMaximumReach;
            float center=(player.HurtBounds.center.x-transform.position.x)*Facing;
            float radius=player.HurtBounds.extents.x;
            return center>0 && center+radius>=minimum && center-radius<=maximum;
        }
        private float ClampBodyX(float x) => Mathf.Clamp(x, ArenaLeft + 3f, ArenaRight - 3f);
        private void FacePlayer()
        {
            float dx = player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.1f) Facing = dx < 0f ? -1 : 1;
        }
        private void SelectPattern()
        {
            if (phasePending) { preparedPlan=null; BeginTransition(); return; }
            if(Phase==2 && LateRockfallPending && !RockfallActive && !BurrowPackageActive)
            {LateRockfallPending=false;LateRockfallUsed=true;BeginSpecial(BossPattern.ARockfall);return;}
            if(!decisionOpen)
            {
                decisionOpen=true;decisionTurnSpent=decisionMoveSpent=false;DecisionCount++;
                DecisionTargetX=player.HurtBounds.center.x;MovementSegmentsThisDecision=TurnSegmentsThisDecision=0;
            }
            if (!PreparePosition()) return;
            if (RockfallActive && !EscapePreviewAvailable) { WaitForSafePattern("player_action_locked"); return; }
            if (!string.IsNullOrEmpty(preparedPlan))
            {
                string plan=preparedPlan;
                if ((!RockfallActive || EffectiveBasic(plan)) && (!RockfallActive || FindTimedEscape(default,false,0,plan,false)))
                { preparedPlan=null; BeginCombo(plan); return; }
                WaitForSafePattern("prepared_followup_not_yet_valid"); return;
            }
            string[] candidates=Phase==1?PhaseOneCombos:PhaseTwoCombos;
            float[] weights=new float[candidates.Length+2]; float total=0;
            for(int i=0;i<candidates.Length;i++)
            {
                string code=candidates[i];
                float weight=code=="3-2"?(Phase==2?3.25f:.32f):(Phase==2?1.1f:1.35f);
                foreach(string prior in recentStarts)if(prior==code)weight*=.55f;
                if(!EffectiveBasic(code) || RockfallActive && !FindTimedEscape(default,false,0,code,false))weight=0;
                weights[i]=weight;total+=weight;
            }
            if(string.IsNullOrEmpty(preparedPlan) && !RockfallActive && !BurrowPackageActive && BurrowCooldownRemaining<=0 && completedSequences>=2)
                weights[candidates.Length]=.40f;
            if(string.IsNullOrEmpty(preparedPlan) && Phase==2 && !RockfallActive && !BurrowPackageActive && encounterTime>=nextWholeTime && WholeBodyCanThreatenPlayer())
                weights[candidates.Length+1]=.65f;
            total+=weights[candidates.Length]+weights[candidates.Length+1];
            if(total<.0001f)
            {
                // A crossing/running player cannot buy endless new turns or approach segments.
                // Threaten the committed front with an existing sweep, then wait normally.
                if(!RockfallActive){BeginCombo("2");return;}
                WaitForSafePattern("no_effective_verified_attack");return;
            }
            float draw=UnityEngine.Random.value*total;
            for(int i=0;i<weights.Length;i++)
            {
                if(weights[i]<=0)continue;
                draw-=weights[i];if(draw>0 && i<weights.Length-1)continue;
                if(i<candidates.Length)BeginCombo(candidates[i]);else if(i==candidates.Length)BeginSpecial(BossPattern.BBurrowLeap);else BeginWholeBodyBite();
                return;
            }
        }
        private void WaitForSafePattern(string reason)
        {
            ClearCombo(); Pattern = BossPattern.None; TargetLocked = false; ClearBounds(); HideMarkers();
            SafetyWaitReason = reason; SafetyWaitCount++;
            // Grounded and hittable, but not advertised as a final combo recovery. Never move a locked player.
            SetState(BossState.Waiting, 0.12f); SetStage("wait", 0f);
        }
        private void RepositionWhileWaiting(float dt) { /* Preparation states own movement and cannot cross the player. */ }
        private void RememberStart(string code) { recentStarts.Enqueue(code); while (recentStarts.Count > 3) recentStarts.Dequeue(); }
        private void ClearCombo() { combo = Array.Empty<BossPattern>(); ComboCode = ""; ComboIndex = 0; }
        private void BeginCombo(string code)
        {
            // Entire chain is selected before its first tell. Inputs never create or cancel followups.
            decisionOpen=false; ComboAdvanceDistance=0; ComboCode = code; ComboIndex = 0;
            string[] steps = code.Split('-'); combo = new BossPattern[steps.Length];
            for (int i = 0; i < steps.Length; i++) combo[i] = steps[i] == "3" ? BossPattern.DT03Bite : BossPattern.DT02Sweep;
            RememberStart(code); BeginBasic(combo[0]);
        }
        private void BeginBasic(BossPattern pattern)
        {
            HideMarkers(); ClearBounds(); Pattern = pattern; AttackId = ++nextAttackId;
            TargetLocked = true; landed = false; riseOrigin = transform.position; motionEndX = transform.position.x;
            float requested=pattern==BossPattern.DT03Bite?biteAdvance:sweepAdvance;
            float remaining=Mathf.Max(0,comboAdvanceLimit-ComboAdvanceDistance);
            motionEndX=ClampBodyX(riseOrigin.x+Facing*Mathf.Min(requested,remaining));
            LockedAdvanceDistance=Mathf.Abs(motionEndX-riseOrigin.x);ComboAdvanceDistance+=LockedAdvanceDistance;
            float tell;
            if (pattern == BossPattern.DT03Bite)
            {
                TargetX = motionEndX + Facing * 2.2f;
                WarningBounds = Box((riseOrigin.x+motionEndX)*.5f+Facing*2.2f, FloorY + 0.9f, 2.6f+LockedAdvanceDistance, 1.8f); tell = biteTell;
            }
            else if (pattern == BossPattern.DT02Sweep)
            {
                // Short weight transfer is committed now; the entire chain advances <=.50 units.
                TargetX = motionEndX + Facing * ((PawMinimumReach + PawMaximumReach)*.5f);
                WarningBounds = Box(TargetX, FloorY + 0.30f, PawMaximumReach-PawMinimumReach, 0.60f);
                tell = IsComboFollowup ? sweepFollowupTell : sweepTell;
            }
            else { throw new InvalidOperationException("Retired or non-basic pattern cannot begin a basic attack."); }
            tell = Mathf.Max(tell, safetyTellFloor); safetyTellFloor = 0f;
            LockedTellDuration = tell; SetState(BossState.Telegraph, tell); SetStage("tell", 0f);
            ShowWarning(); Cue("telegraph", new Vector2(TargetX, FloorY), tell); UpdateHurtbox();
        }
        private void BeginSpecial(BossPattern pattern, string burrowExit = null)
        {
            ClearCombo(); HideMarkers(); ClearBounds();
            transform.position = new Vector3(ClampBodyX(transform.position.x), FloorY, transform.position.z);
            decisionOpen=false; Pattern = pattern; AttackId = ++nextAttackId; riseOrigin = transform.position;
            TargetLocked = false; LockedTellDuration = 0f; landed = false;
            RememberStart(pattern == BossPattern.ARockfall ? "A" : "B");
            if (pattern == BossPattern.ARockfall)
            {
                TargetX = transform.position.x; nextRockfallTime = encounterTime + rockfallCooldown;
                SetState(BossState.Telegraph, breatheDuration); SetStage("breathe", 0f); Cue("breathe", transform.position, breatheDuration);
            }
            else
            {
                string[] valid = Phase == 1 ? PhaseOneCombos : PhaseTwoCombos;
                BurrowExitPlan = burrowExit ?? (UnityEngine.Random.value < 0.45f ? "rest" : valid[UnityEngine.Random.Range(0, valid.Length)]);
                BurrowPackageActive = true;
                float frontSpace=Facing>0?ArenaRight-transform.position.x:transform.position.x-ArenaLeft;
                bool wallReset=Mathf.Abs(transform.position.x)>5.4f && (frontSpace<3.4f || Mathf.Abs(player.transform.position.x-transform.position.x)<1.35f);
                TargetX=wallReset?ClampBodyX(transform.position.x*.52f):ClampBodyX(player.HurtBounds.center.x);
                emergenceX=ClampBodyX(TargetX-Facing*3f);nextBurrowTime=encounterTime+burrowCooldown;
                LockLeapLanding();
                SetState(BossState.Telegraph, submergeDuration + travelDuration + riseDuration);
                SetStage("submerge", 0f); Cue("submerge", transform.position, submergeDuration);
            }
            UpdateHurtbox();
        }
        private void TickTelegraph()
        {
            if (Pattern == BossPattern.ARockfall) { SetStage("breathe", StateProgress); return; }
            if(Pattern==BossPattern.WholeBodyBite){SetStage("whole_tell",StateProgress);AnimateWarning();return;}
            if (Pattern != BossPattern.BBurrowLeap)
            {
                SetStage("tell", StateProgress);
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
                transform.position = new Vector3(Mathf.Lerp(riseOrigin.x, emergenceX, AttackStageProgress), FloorY - 4.7f, riseOrigin.z);
                DrawBurrowTrace();
            }
            else
            {
                if (AttackStage!="rise")Cue("rise",new Vector2(emergenceX,FloorY),riseDuration);
                SetStage("rise", (timer - submergeDuration - travelDuration) / riseDuration);
                float y = Mathf.Lerp(-4.7f, leapHeight, Mathf.SmoothStep(0f, 1f, AttackStageProgress));
                transform.position = new Vector3(emergenceX, FloorY + y, riseOrigin.z);
                AnimateWarning();
            }
        }
        private void LockLeapLanding()
        {
            // The landing and facing are fixed before the very first submerge warning.
            WarningBounds=Box(TargetX,FloorY+.7f,landingWidth,1.4f);TargetLocked=true;
            float radius=Mathf.Max(.35f,player.HurtBounds.extents.x),px=player.HurtBounds.center.x;
            float left=WarningBounds.min.x-radius-.1f,right=WarningBounds.max.x+radius+.1f,escape=float.PositiveInfinity;
            if(left>=ArenaLeft+radius)escape=Mathf.Min(escape,Mathf.Max(0,px-left));
            if(right<=ArenaRight-radius)escape=Mathf.Min(escape,Mathf.Max(0,right-px));
            if(px+radius<=WarningBounds.min.x || px-radius>=WarningBounds.max.x)escape=0;
            float required=reactionMargin+escape/Mathf.Max(1,player.Movement.MoveSpeed);
            leapFlightTime=Mathf.Max(flightDuration,required-submergeDuration-travelDuration-riseDuration);
            LockedTellDuration=submergeDuration+travelDuration+riseDuration+leapFlightTime;ShowWarning();
        }
        private void BeginWholeBodyBite()
        {
            decisionOpen=false;ClearCombo();HideMarkers();ClearBounds();Pattern=BossPattern.WholeBodyBite;AttackId=++nextAttackId;
            WholeFirstSide=WholeSwingSide=-1;WholeBodyAttackCount++;allocatedWholeIndex=0;WholeContactIndex=0;
            wholeFirstBounds=WholeBounds(-1);wholeSecondBounds=WholeBounds(1);
            WarningBounds=wholeFirstBounds;SecondaryWarningBounds=wholeSecondBounds;TargetX=wholeFirstBounds.center.x;TargetLocked=true;
            nextWholeTime=encounterTime+wholeCooldown;LockedTellDuration=wholeTell;
            SetState(BossState.Telegraph,wholeTell);SetStage("whole_tell",0);ShowWarning();
            DrawRect(wholeSecondWarning,new Vector3(wholeSecondBounds.center.x,FloorY+.06f,0),new Vector3(wholeSecondBounds.size.x,.045f,1),new Color(.94f,.55f,.24f,.45f));
            Cue("telegraph",new Vector2(TargetX,FloorY),wholeTell);
        }
        private Bounds WholeBounds(int side)
        {
            // Floor projection of the moving body/mouth, including the central body strike.
            Bounds b=DocheolWholeMotion.Bounds(new Vector2(transform.position.x,FloorY),side,1);
            float left=Mathf.Max(ArenaLeft,b.min.x),right=Mathf.Min(ArenaRight,b.max.x);
            return Box((left+right)*.5f,FloorY+.65f,right-left,1.3f);
        }
        private bool WholeBodyCanThreatenPlayer()
        {
            Bounds hurt=player.HurtBounds;
            Bounds left=WholeBounds(-1),right=WholeBounds(1);
            return hurt.max.x>left.min.x && hurt.min.x<left.max.x || hurt.max.x>right.min.x && hurt.min.x<right.max.x;
        }
        private void TickWholeBodyBite()
        {
            float strike=wholeArc+wholeContact, fast= strike+wholeBridge;
            float fifthStart=3*fast+strike+wholeFinalDelay;
            int index;float elapsed;
            if(timer<fifthStart){index=Mathf.Min(3,Mathf.FloorToInt(timer/fast));elapsed=timer-index*fast;}
            else{index=4;elapsed=timer-fifthStart;}
            bool preparing=elapsed>=strike;
            int next=preparing?index+1:index;
            if(next!=allocatedWholeIndex)
            {
                allocatedWholeIndex=next;AttackId=++nextAttackId;HideMarkers();
                int nextSide=next%2==0?-1:1;
                WarningBounds=nextSide<0?wholeFirstBounds:wholeSecondBounds;SecondaryWarningBounds=nextSide<0?wholeSecondBounds:wholeFirstBounds;TargetX=WarningBounds.center.x;ShowWarning();
                Cue("whole_shift",new Vector2(TargetX,FloorY),next==4?wholeFinalDelay:wholeBridge);
            }
            WholeSwingSide=next%2==0?-1:1;WholeContactIndex=index;
            if(preparing)
            {
                float wait=index==3?wholeFinalDelay:wholeBridge;
                SetStage(index==3?"whole_final_windup":"whole_cross",(elapsed-strike)/wait);AnimateWarning();return;
            }
            if(elapsed<wholeArc)
            {
                SetStage("whole_arc",elapsed/wholeArc);AnimateWarning();
                // The last half of the real downswing can hit airborne overlap, sharing this contact ID.
                if(AttackStageProgress>=.5f)WholeDamage(AttackStageProgress);
                return;
            }
            if(AttackStage!="whole_hit")Cue("whole_bite",new Vector2(TargetX,FloorY),1);
            SetStage("whole_hit",(elapsed-wholeArc)/wholeContact);WholeDamage(1);
        }
        private void WholeDamage(float downswing)
        {
            Vector2 root=new Vector2(transform.position.x,FloorY);
            ActiveBounds=DocheolWholeMotion.Bounds(root,WholeSwingSide,downswing);
            DrawAttackEffect(WholeSwingSide<0?wholeFirstBounds:wholeSecondBounds);
            // Bounds are presentation/debug envelopes; damage is the rounded anatomical chain.
            if(DocheolWholeMotion.Intersects(root,WholeSwingSide,downswing,player.HurtBounds))
            {
                if(player.ReceiveAttack(AttackId,1,new Vector2(WholeSwingSide*4f,2.7f),gameObject))PlayerDamagedByAttack?.Invoke(AttackId);
            }
        }
        private void BeginActive()
        {
            if (Pattern == BossPattern.ARockfall)
            {
                SetState(BossState.Active, roarDuration); SetStage("roar", 0f);
                Cue("roar", transform.position, roarDuration); return;
            }
            if(Pattern==BossPattern.WholeBodyBite)
            {SetState(BossState.Active,5*(wholeArc+wholeContact)+3*wholeBridge+wholeFinalDelay);SetStage("whole_arc",0);return;}
            if (Pattern == BossPattern.BBurrowLeap)
            {
                if (!TargetLocked) LockLeapLanding();
                SetState(BossState.Active, leapFlightTime + landingDuration + waveDelay + waveDuration);
                SetStage("flight", 0f); Cue("flight", new Vector2(TargetX, FloorY), leapFlightTime); return;
            }
            SetState(BossState.Active, Pattern == BossPattern.DT02Sweep ? sweepStrike : biteStrike);
            SetStage("strike", 0f); Cue("release", new Vector2(TargetX, FloorY), 1f);
            Cue(Pattern == BossPattern.DT02Sweep ? "sweep" : "bite", new Vector2(TargetX, FloorY), 0.5f);
        }
        private void TickAttack()
        {
            ActiveBounds = SecondaryActiveBounds = default;
            if (Pattern == BossPattern.ARockfall) { SetStage("roar", StateProgress); return; }
            if(Pattern==BossPattern.WholeBodyBite){TickWholeBodyBite();return;}
            if (Pattern != BossPattern.BBurrowLeap)
            {
                SetStage("strike", StateProgress);
                transform.position=new Vector3(Mathf.Lerp(riseOrigin.x,motionEndX,Mathf.SmoothStep(0,1,Mathf.Clamp01(StateProgress/.45f))),FloorY,riseOrigin.z);
                if(Pattern==BossPattern.DT02Sweep)
                {
                    float inner=Facing<0?WarningBounds.max.x:WarningBounds.min.x,outer=Facing<0?WarningBounds.min.x:WarningBounds.max.x;
                    float hoof=Mathf.Lerp(inner+Facing*.55f,outer-Facing*.55f,Mathf.SmoothStep(0,1,StateProgress));
                    ActiveBounds=Box(hoof,FloorY+.30f,1.10f,.60f);
                }
                else ActiveBounds=Box(transform.position.x+Facing*2.2f,FloorY+.9f,2.6f,1.8f);
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
            LastCompletedAttackId = AttackId;
            transform.position = new Vector3(transform.position.x, FloorY, transform.position.z);
            HideMarkers(); ClearBounds();
            if (Pattern == BossPattern.ARockfall) { completedSequences++;StartRockfallWindow();SetState(BossState.Idle,.35f);return; }
            if(Pattern==BossPattern.WholeBodyBite)Facing=-1;
            if (Pattern == BossPattern.BBurrowLeap && BurrowExitPlan != "rest")
            {
                completedSequences++;
                SetState(BossState.Followup, burrowFollowupBrace); SetStage("link", 0f); Cue("link", transform.position, burrowFollowupBrace); return;
            }
            if (combo.Length > 0 && ComboIndex + 1 < combo.Length)
            {
                // The next locked footprint is already visible during Link. It earns reaction time
                // without adding a recovery or changing the plan after seeing an attack/dash input.
                ComboIndex++; BeginBasic(combo[ComboIndex]);
                SetState(BossState.Link, comboLink); SetStage("link", 0f); Cue("link", transform.position, comboLink); return;
            }
            completedSequences++;
            if (Pattern == BossPattern.BBurrowLeap) BurrowPackageActive = false;
            float recovery = Pattern == BossPattern.BBurrowLeap ? leapRecovery : Pattern == BossPattern.WholeBodyBite ? wholeRecovery : Pattern == BossPattern.DT02Sweep ? sweepRecovery : biteRecovery;
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
            RockfallSize size = DrawRockSize(); rockSequence++;
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
        private RockfallSize DrawRockSize()
        {
            if (rockRandom == null) rockRandom = new System.Random(rockfallSeed);
            // Local seeded generator is independent of audio/particle/global Unity randomness.
            // Base weights 45/35/20; cap all runs at two and never repeat a large rock immediately.
            float[] weights = { .45f, .35f, .20f };
            int count = rockSizeDraws.Count;
            if (count > 0)
            {
                RockfallSize last = rockSizeDraws[count - 1];
                if (last == RockfallSize.Large || (count > 1 && last == rockSizeDraws[count - 2])) weights[(int)last] = 0f;
            }
            double draw = rockRandom.NextDouble() * (weights[0] + weights[1] + weights[2]);
            RockfallSize chosen = RockfallSize.Large;
            for (int i = 0; i < 3; i++) { draw -= weights[i]; if (draw < 0) { chosen = (RockfallSize)i; break; } }
            rockSizeDraws.Add(chosen); return chosen;
        }
        public bool HasRockfallEscapeRoute() => GetRockfallEscapeCorridor(out _);
        public bool GetRockfallEscapeCorridor(out Bounds corridor) => FindEscapeCorridor(default, false, out corridor);
        private bool FindEscapeCorridor(Bounds proposed, bool includeProposed, out Bounds corridor)
        {
            blockedIntervals.Clear();
            foreach (DocheolRockfall rock in rocks) if (rock.Stage != "dust") blockedIntervals.Add(new Vector2(rock.WarningBounds.min.x, rock.WarningBounds.max.x));
            if (includeProposed) blockedIntervals.Add(new Vector2(proposed.min.x, proposed.max.x));
            if ((State == BossState.Telegraph || State == BossState.Active || State == BossState.Link) && WarningBounds.size.x > 0f && Pattern != BossPattern.ARockfall)
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
                    BossPattern step = steps[i] == "3" ? BossPattern.DT03Bite : BossPattern.DT02Sweep;
                    AddFutureDanger(step, i > 0, ref forecastX, ref cursor);
                }
            }
            else if (combo.Length > 0 && (State == BossState.Telegraph || State == BossState.Active || State == BossState.Link))
            {
                int first = ComboIndex;
                if (State == BossState.Link) { cursor = Mathf.Max(0f, duration - timer); }
                else
                {
                    float strike = Pattern == BossPattern.DT02Sweep ? sweepStrike : biteStrike;
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
            // A command submitted in rendered Update reaches movement on a later
            // fixed tick. Keep the safety proof clear of the footprint by that
            // travel distance as well; this never enlarges actual damage bounds.
            float commandTravel = speed * (2f * Time.fixedDeltaTime + 1f / 30f);
            float radius = Mathf.Max(0.35f, player.HurtBounds.extents.x) + 0.04f + commandTravel;
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
        private void AddFutureDanger(BossPattern step,bool followup,ref float originX,ref float cursor)
        {
            bool sweep=step==BossPattern.DT02Sweep;
            float initialOrigin=originX;
            originX=ClampBodyX(originX+Facing*(sweep?sweepAdvance:biteAdvance));
            Bounds bounds=sweep?Box(originX+Facing*((PawMinimumReach+PawMaximumReach)*.5f),FloorY+.3f,PawMaximumReach-PawMinimumReach,.6f)
                :Box((initialOrigin+originX)*.5f+Facing*2.2f,FloorY+.9f,2.6f+Mathf.Abs(originX-initialOrigin),1.8f);
            float tell=sweep?(followup?sweepFollowupTell:sweepTell):biteTell,strike=sweep?sweepStrike:biteStrike;
            routeDangers.Add(new TimedDanger{bounds=bounds,start=cursor+tell,end=cursor+tell+strike,sweep=sweep,basic=true});
            cursor+=tell+strike+comboLink;
        }
        private void ClearRockfall()
        {
            foreach (DocheolRockfall rock in rocks) rock.Dispose();
            rocks.Clear(); rockfallEnd = 0f; RockfallEscapeCorridor = default; escapeRoute.Clear();
        }
        private void BeginTransition()
        {
            BurrowPackageActive=false;preparedPlan=null;ClearRockfall();HideMarkers();ClearBounds();ClearCombo();TargetLocked=false;
            transform.position=new Vector3(ClampBodyX(transform.position.x),FloorY,transform.position.z);
            Pattern=BossPattern.None;SetState(BossState.Transition,1.70f);SetStage("armor_drop",0);health.DamageEnabled=false;
            if(player!=null && player.Health!=null)player.Health.DamageEnabled=false;
            if(hurtCollider!=null)hurtCollider.enabled=false;
            for(int i=0;i<4;i++)BreakArmor(i,GetArmorWorldPoint(i));
            Cue("phase",transform.position,.7f);
        }
        private void TickTransition()
        {
            if(timer<.12f)SetStage("armor_drop",timer/.12f);
            else if(timer<.80f)SetStage("groggy",(timer-.12f)/.68f);
            else
            {
                if(AttackStage!="phase_roar")Cue("phase_roar",transform.position,.90f);
                SetStage("phase_roar",(timer-.80f)/.90f);
            }
            if(timer>=duration)
            {
                Phase=2;phasePending=false;EntryRockfallUsed=true;StartRockfallWindow();
                SetState(BossState.Idle,.40f);SetStage("idle",0);
            }
        }
        private void StartRockfallWindow()
        {
            RockfallEventCount++;rockfallEnd=encounterTime+rockfallDuration;
            // Player control returns first, then a .40s grace plus each rock's >=.95s fixed warning.
            nextRockTime=encounterTime+.40f;Cue("rockfall_start",transform.position,rockfallDuration);
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
                // Deferred until the complete current attack and promised recovery have finished.
            }
            if(Phase==2 && health.CurrentHealth<=lateRockfallHealth && !health.IsDead && !LateRockfallUsed) LateRockfallPending=true;
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
        private void ClearBounds() { WarningBounds = ActiveBounds = SecondaryActiveBounds = SecondaryWarningBounds = default; }
        private void OnDied()
        {
            LateRockfallPending=false;preparedPlan=null;fightActive = false; ClearRockfall(); SetState(BossState.Dead, 0f); HideMarkers(); ClearBounds(); UpdateHurtbox(); Cue("death", transform.position, 1f);
        }
        private void UpdateHurtbox()
        {
            if (hurtRoot == null) return;
            // The partition owns the collider's painted-front pose even during a committed
            // whole-body swing/turn. Do not undo that alignment every physics tick.
            if(hitRegions==null)hurtRoot.localPosition = new Vector3(Facing * 0.4f, 0.95f, 0f);
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
            wholeSecondWarning=NewSprite("WholeSecondGroundWarning",markers.transform,8);
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
            if(wholeSecondWarning!=null)wholeSecondWarning.enabled=false;
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
