using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;

namespace FourGuardians.BossFight
{
    // Opt-in executable QA. No health/damage cheats are used by survival/victory.
    [DefaultExecutionOrder(1000)]
    public sealed class BossFightQaDriver : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name; public bool passed; public string evidence; }
        [Serializable] public sealed class AttackEvidence
        {
            public int testCase, id, phase, armorAtStart, damageEvents, activePhysicsSteps;
            public string pattern;
            public bool sawTelegraph, sawLock, targetStable = true, warningCoversActiveX = true;
            public float lockedTargetX, warningLeft, warningRight, maximumBodyY;
            public bool completed, sawWaveWarning, waveWarningStable = true, wavePrimaryCovered = true, waveSecondaryCovered = true;
            public int landingPhysicsSteps, waveWarningPhysicsSteps, wavePhysicsSteps, wavePrimaryPhysicsSteps, waveSecondaryPhysicsSteps;
            public float waveWarningLeft, waveWarningRight, waveActiveMinX, waveActiveMaxX;
            public float waveWarningFirstPhysicsTime = -1, waveFirstActivePhysicsTime = -1;
        }
        [Serializable] public sealed class PunishEvidence
        {
            public int testCase, phase, attackId, incomingHits, recoveryHits, recoveryDamage, acceptedPlayerAttacks;
            public int intendedHits, followupAttackId, followupIncomingHits;
            public string followupPattern;
            public bool limitedHits, followupTelegraphObserved, followupActiveObserved, followupCompleted, followupEvaded;
            public string pattern, approachMethod = "walk after active ends";
            public bool startedInDanger, evaded, observedRecovery, completedRecovery;
            public float lockPlayerX, recoveryPlayerX, finalPlayerX, outwardDistance, returnDistance;
            public float recoveryDuration, recoveryInitialGap, firstHitLatency = -1f;
        }
        [Serializable] public sealed class ArmorCaptureEvidence
        {
            public int mask, visiblePlates, frame;
            public string label, screenshot;
            public Vector3 position, cameraPosition;
            public float orthographicSize;
            public bool anatomyUnchanged;
        }
        [Serializable] public sealed class Report
        {
            public string mode, utc, unityVersion, platform, outcome;
            public string specVersion = "combo-rockfall-v3";
            public string inputStrategy;
            public int requestedFps, renderedFrames, damageEvents, completedAttacks;
            public float elapsedSeconds, averageFps;
            public bool passed, usesSyntheticDamage, usesForcedPatterns, usesTestRepositioning, usesModifiedInvulnerability;
            public List<AcceptedDamageEvidence> acceptedDamage = new List<AcceptedDamageEvidence>();
            public List<Check> checks = new List<Check>();
            public List<AttackEvidence> attacks = new List<AttackEvidence>();
            public List<PunishEvidence> punishWindows = new List<PunishEvidence>();
            public List<string> events = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
            public bool usesPresentationOverrides;
            public List<ArmorCaptureEvidence> armorCaptures = new List<ArmorCaptureEvidence>();
            public List<V3Case> v3Cases = new List<V3Case>();
            public List<HazardEvidence> hazards = new List<HazardEvidence>();
        }
        private string presentationCaptureLabel;
        private Report report;
        private BossFightPlayerAdapter player;
        private DoCheolBossController boss;
        private string outputPath;
        private string captureDirectory;
        private float startedAt;
        private bool finishing;
        private readonly HashSet<string> capturedFrames = new HashSet<string>();
        private int firstFrame;
        private int lastCaptureFrame = -1;
        private bool dashProbeSampling, dashProbePreviousStep, dashProbePreviousAction, dashProbeComplete;
        private float dashProbePreviousX, dashProbeDistance;
        private int dashProbeTicks;
        private bool walkProbeSampling, walkProbeAccepted, walkProbePreviousStep, walkProbeComplete, walkProbeInterrupted;
        private float walkProbePreviousX, walkProbeDistance, walkRequestedAt, walkAcceptedAt;
        private int walkProbeTicks, walkProbeTargetTicks;
        private readonly Dictionary<int, AttackEvidence> attackEvidence = new Dictionary<int, AttackEvidence>();
        private bool phaseTwoUnarmored = true, sawPhaseTwo;
        private int evidenceCase;
        public static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-bossqa") >= 0;
        public static string RequestedMode => Argument("-bossqa", "contracts");
        private static string Argument(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : fallback;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Requested || FindFirstObjectByType<BossFightQaDriver>() != null) return;
            DontDestroyOnLoad(new GameObject("BossFight Automated QA").AddComponent<BossFightQaDriver>().gameObject);
        }
        private IEnumerator Start()
        {
            int fps;
            if (!int.TryParse(Argument("-bossqa-fps", "60"), out fps)) fps = 60;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
            Application.runInBackground = true;
            if (RequestedMode != "observe") Time.timeScale = 1;
            bool controlled = RequestedMode == "v3matrix" || RequestedMode == "v3phases";
            report = new Report { mode = RequestedMode, utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), requestedFps = fps, inputStrategy = "V3 ordinary input; .2s recognition; actual Rigidbody2D physics samples",
                usesSyntheticDamage = controlled || RequestedMode == "contracts" || RequestedMode == "v3contracts",
                usesForcedPatterns = RequestedMode == "v3matrix", usesTestRepositioning = controlled || RequestedMode == "contracts" || RequestedMode == "v3contracts",
                usesModifiedInvulnerability = false, usesPresentationOverrides = RequestedMode == "armorvisual" };
            outputPath = Argument("-bossqa-output", Path.Combine(Application.persistentDataPath, "bossqa-" + report.mode + "-" + fps + ".json"));
            captureDirectory = Argument("-bossqa-capture", null);
            Application.logMessageReceived += CaptureError;
            startedAt = Time.realtimeSinceStartup;
            firstFrame = Time.frameCount;
            while (player == null && Time.realtimeSinceStartup - startedAt < 45)
            {
                player = FindFirstObjectByType<BossFightPlayerAdapter>();
                yield return null;
            }
            if (player == null) { Record("player_available", false, "No player adapter after 45 seconds"); yield return Finish("setup_failed"); yield break; }
            boss = FindFirstObjectByType<DoCheolBossController>(); SyncBossDamageSubscription();
            if (report.mode != "intro" && report.mode != "observe")
                foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (item.GetType().Name == "BossFightSession") item.SendMessage("StartFight", SendMessageOptions.DontRequireReceiver);
            yield return null;
            if (report.mode != "observe") player.SetExternalInput(true);
            player.Damaged += OnDamage;
            CaptureFrame("start");
            if (!Application.isBatchMode) yield return new WaitForEndOfFrame();
            yield return null;
            if (report.mode == "observe") yield return ObserveManualInput();
            else if (report.mode == "contracts" || report.mode == "v3contracts") yield return Contracts();
            else if (report.mode == "v3matrix") yield return V3Matrix();
            else if (report.mode == "armorvisual") yield return ArmorVisuals();
            else if (report.mode == "v3phases") yield return V3Phases();
            else if (report.mode == "defeat") yield return NaturalDefeat();
            else if (report.mode == "mash") yield return StationaryMash();
            else if (report.mode == "intro") yield return IntroFlow();
            else if (report.mode == "victory" || report.mode == "survival" || report.mode == "v3ai") yield return V3Natural();
            else { Record("supported_v3_mode", false, "V2 phases/patterns/punish modes retired; use v3phases/v3matrix. Unknown mode=" + report.mode); yield return Finish("unsupported_mode"); }
        }
        private void CaptureFrame(string label)
        {
            if (Application.isBatchMode || string.IsNullOrEmpty(captureDirectory) || capturedFrames.Count >= 40 || lastCaptureFrame == Time.frameCount || !capturedFrames.Add(label)) return;
            lastCaptureFrame = Time.frameCount;
            Directory.CreateDirectory(captureDirectory);
            string path = Path.Combine(captureDirectory, capturedFrames.Count.ToString("D2") + "-" + label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            report.screenshots.Add(path);
            report.events.Add("capture t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " frame=" + Time.frameCount + " stage=" + (boss != null ? boss.AttackStage : "none") + " path=" + path);
        }

        private void CaptureCombatFrames()
        {
            if (boss == null) return;
            if (boss.State == BossState.Transition && boss.TransitionProgress >= .45f && boss.TransitionProgress <= .65f) CaptureFrame("phase-transition-mid");
            string key = "P" + boss.Phase + "-" + boss.Pattern + "-" + boss.ComboCode + "-" + boss.AttackStage;
            if (boss.State == BossState.Telegraph && boss.TargetLocked && boss.StateProgress > .6f) CaptureFrame(key + "-locked-tell");
            if (boss.State == BossState.Active && boss.StateProgress > .75f) CaptureFrame(key + "-active");
            if (boss.Pattern == BossPattern.BBurrowLeap && boss.transform.position.y > boss.FloorY + 3f) CaptureFrame("P" + boss.Phase + "-B-apex");
            if (boss.Pattern == BossPattern.BBurrowLeap && boss.IsUnderground && boss.AttackStage == "travel") CaptureFrame("P" + boss.Phase + "-B-underground-trace");
            if (boss.RockfallActive) CaptureFrame("P" + boss.Phase + "-rockfall-" + boss.AttackStage);
            if (boss.State == BossState.Link) CaptureFrame(key + "-link");
        }
        private void CaptureError(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                if (report.errors.Count < 30) report.errors.Add(condition + "\n" + stack);
        }
        private void OnDamage(DamageInfo info)
        {
            report.damageEvents++;
            // Per-ID attribution arrives from PlayerDamagedByAttack after ReceiveAttack succeeds.
            report.events.Add("damage t=" + (Time.realtimeSinceStartup - startedAt).ToString("F2") + " hp=" + player.Health.CurrentHealth + " boss=" + (boss != null ? boss.Pattern + "/" + boss.State : "none") + " playerX=" + player.Body.position.x.ToString("F3") + " feet=" + player.HurtBounds.min.y.ToString("F3") + " action=" + player.Movement.Action + " velocity=" + player.Body.linearVelocity + " activeBounds=" + (boss != null ? boss.ActiveBounds.ToString() : "none"));
        }
        private void Record(string name, bool passed, string evidence)
        {
            report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
            Debug.Log("BOSS_QA " + (passed ? "PASS " : "FAIL ") + name + " " + evidence);
        }
        private IEnumerator Drive(float duration, float horizontal = 0, bool jump = false, bool dash = false, bool attack = false)
        {
            float until = Time.time + duration;
            bool first = true;
            while (Time.time < until)
            {
                player.SubmitInput(horizontal, first && jump, first && attack, first && dash);
                first = false;
                yield return null;
            }
            player.SubmitInput(0, false, false, false);
        }
        // Runs after the movement controller and before physics simulation. A sampled position
        // belongs to the previous physics step; only steps commanded by the accepted dash count.
        private void FixedUpdate()
        {
            SyncBossDamageSubscription();
            ObserveBossPhysics();
            ObserveV3Physics();
            ObserveWalkPhysics();
            if (!dashProbeSampling || player == null) return;
            float x = player.Body.position.x;
            if (dashProbePreviousStep)
            {
                dashProbeDistance += x - dashProbePreviousX;
                dashProbeTicks++;
            }
            bool actionDash = player.Movement.Action == WarriorAction.Dash;
            bool dashStep = actionDash || (dashProbePreviousAction && Mathf.Abs(player.Body.linearVelocity.x - player.Movement.DashSpeed) < 0.01f);
            if (dashProbeTicks > 0 && !dashStep)
            {
                dashProbeComplete = true;
                dashProbeSampling = false;
            }
            dashProbePreviousAction = actionDash;
            dashProbePreviousStep = dashStep;
            dashProbePreviousX = x;
        }

        private void ObserveWalkPhysics()
        {
            if (!walkProbeSampling || player == null) return;
            float x = player.Body.position.x;
            if (walkProbePreviousStep)
            {
                walkProbeDistance += x - walkProbePreviousX;
                walkProbeTicks++;
                if (walkProbeTicks >= walkProbeTargetTicks)
                {
                    walkProbeComplete = true;
                    walkProbeSampling = false;
                    return;
                }
            }
            bool walking = player.Movement.Action == WarriorAction.Run && player.Body.linearVelocity.x > .01f;
            if (!walkProbeAccepted)
            {
                if (!walking) return;
                walkProbeAccepted = true;
                walkAcceptedAt = Time.realtimeSinceStartup;
            }
            else walkProbeInterrupted |= !walking;
            // Count every step after acceptance, including any unexpected stop; never filter it out.
            walkProbePreviousStep = true;
            walkProbePreviousX = x;
        }

        private IEnumerator MeasureWalk()
        {
            walkProbeTicks = 0;
            walkProbeTargetTicks = Mathf.RoundToInt(.5f / Time.fixedDeltaTime);
            walkProbeDistance = 0;
            walkProbeAccepted = walkProbePreviousStep = walkProbeComplete = walkProbeInterrupted = false;
            walkProbeSampling = true;
            walkRequestedAt = Time.realtimeSinceStartup;
            float renderedStart = player.transform.position.x;
            while (walkProbeSampling && Time.realtimeSinceStartup < walkRequestedAt + 3f)
            {
                player.SubmitInput(1, false, false, false);
                yield return null;
            }
            walkProbeSampling = false;
            player.SubmitInput(0, false, false, false);
            string evidence = "accepted=" + walkProbeAccepted + " requestToAcceptanceRealtime=" +
                (walkProbeAccepted ? walkAcceptedAt-walkRequestedAt : -1f).ToString("F3") +
                "s physicsTicks=" + walkProbeTicks + " physicsDuration=" + (walkProbeTicks*Time.fixedDeltaTime).ToString("F3") +
                "s Body.position travel=" + walkProbeDistance.ToString("F3") + " renderedTravel=" +
                (player.transform.position.x-renderedStart).ToString("F3") + " interrupted=" + walkProbeInterrupted;
            Record("walk_speed", walkProbeAccepted && walkProbeComplete && !walkProbeInterrupted &&
                walkProbeDistance > 2.15f && walkProbeDistance < 2.9f, evidence);
        }
        private AttackEvidence GetAttackEvidence()
        {
            if (!attackEvidence.TryGetValue(boss.AttackId, out AttackEvidence sample))
            {
                sample = new AttackEvidence { testCase = evidenceCase, id = boss.AttackId, phase = boss.Phase, pattern = boss.Pattern.ToString(), armorAtStart = boss.ArmorCount, maximumBodyY = boss.transform.position.y };
                attackEvidence.Add(sample.id, sample);
                report.attacks.Add(sample);
            }
            return sample;
        }

        private void ObserveBossPhysics()
        {
            if (report == null || boss == null) return;
            if (boss.Phase == 2)
            {
                sawPhaseTwo = true;
                phaseTwoUnarmored &= boss.ArmorCount == 0;
            }
            bool attacking = boss.State == BossState.Telegraph || boss.State == BossState.Active;
            if (boss.AttackId <= 0) return;
            if (!attacking)
            {
                if (attackEvidence.TryGetValue(boss.AttackId, out AttackEvidence ended))
                    ended.completed |= boss.State == BossState.Recovery || boss.State == BossState.Link;
                return;
            }
            AttackEvidence sample = GetAttackEvidence();
            sample.maximumBodyY = Mathf.Max(sample.maximumBodyY, boss.transform.position.y);
            sample.sawTelegraph |= boss.State == BossState.Telegraph;
            if (boss.TargetLocked)
            {
                if (!sample.sawLock)
                {
                    sample.sawLock = true;
                    sample.lockedTargetX = boss.TargetX;
                    sample.warningLeft = boss.WarningBounds.min.x;
                    sample.warningRight = boss.WarningBounds.max.x;
                }
                else sample.targetStable &= Mathf.Abs(sample.lockedTargetX - boss.TargetX) < .01f;
            }
            if (boss.Pattern == BossPattern.BBurrowLeap && boss.AttackStage == "wave_delay")
            {
                if (!sample.sawWaveWarning)
                {
                    sample.sawWaveWarning = true;
                    sample.waveWarningLeft = boss.WarningBounds.min.x;
                    sample.waveWarningRight = boss.WarningBounds.max.x;
                    sample.waveWarningFirstPhysicsTime = Time.fixedTime;
                }
                sample.waveWarningPhysicsSteps++;
                sample.waveWarningStable &= Mathf.Abs(sample.waveWarningLeft - boss.WarningBounds.min.x) < .01f
                    && Mathf.Abs(sample.waveWarningRight - boss.WarningBounds.max.x) < .01f;
            }
            if (boss.State != BossState.Active) return;
            bool primary = boss.ActiveBounds.size.x > .001f;
            bool secondary = boss.SecondaryActiveBounds.size.x > .001f;
            if (!primary && !secondary) return;
            sample.activePhysicsSteps++;
            bool wave = boss.Pattern == BossPattern.BBurrowLeap && boss.AttackStage == "wave";
            if (wave)
            {
                sample.wavePhysicsSteps++;
                if (sample.waveFirstActivePhysicsTime < 0) sample.waveFirstActivePhysicsTime = Time.fixedTime;
                if (primary) sample.wavePrimaryPhysicsSteps++;
                if (secondary) sample.waveSecondaryPhysicsSteps++;
                bool priorWarning = sample.sawWaveWarning && sample.waveWarningPhysicsSteps > 0
                    && sample.waveWarningFirstPhysicsTime < Time.fixedTime;
                sample.wavePrimaryCovered &= !primary || (priorWarning && ContainsWarningX(sample.waveWarningLeft, sample.waveWarningRight, boss.ActiveBounds));
                sample.waveSecondaryCovered &= !secondary || (priorWarning && ContainsWarningX(sample.waveWarningLeft, sample.waveWarningRight, boss.SecondaryActiveBounds));
                sample.warningCoversActiveX &= sample.wavePrimaryCovered && sample.waveSecondaryCovered;
                float min = Mathf.Min(primary ? boss.ActiveBounds.min.x : float.PositiveInfinity, secondary ? boss.SecondaryActiveBounds.min.x : float.PositiveInfinity);
                float max = Mathf.Max(primary ? boss.ActiveBounds.max.x : float.NegativeInfinity, secondary ? boss.SecondaryActiveBounds.max.x : float.NegativeInfinity);
                sample.waveActiveMinX = sample.wavePhysicsSteps == 1 ? min : Mathf.Min(sample.waveActiveMinX, min);
                sample.waveActiveMaxX = sample.wavePhysicsSteps == 1 ? max : Mathf.Max(sample.waveActiveMaxX, max);
            }
            else
            {
                if (boss.Pattern == BossPattern.BBurrowLeap) sample.landingPhysicsSteps++;
                sample.warningCoversActiveX &= sample.sawTelegraph && sample.sawLock
                    && (!primary || ContainsWarningX(sample.warningLeft, sample.warningRight, boss.ActiveBounds))
                    && (!secondary || ContainsWarningX(sample.warningLeft, sample.warningRight, boss.SecondaryActiveBounds));
            }
        }

        private static bool ContainsWarningX(float left, float right, Bounds active)
            => active.min.x >= left - .025f && active.max.x <= right + .025f;

        private void RecordAttackPhysicsAssertions()
        {
            // Presentation-only and passive observer modes must not imply combat certification.
            if (report.mode != "v3matrix" && report.mode != "v3ai" && report.mode != "victory" && report.mode != "survival") return;
            int completed = 0, active = 0;
            foreach (AttackEvidence sample in report.attacks)
            {
                bool damaging = sample.pattern == "DT01Slam" || sample.pattern == "DT02Sweep" || sample.pattern == "DT03Bite" || sample.pattern == "BBurrowLeap";
                if (!damaging) continue;
                if (sample.completed) completed++;
                if (sample.activePhysicsSteps > 0) active++;
                string label = "v3_physics_case" + sample.testCase + "_id" + sample.id + "_" + sample.pattern;
                if (sample.completed)
                    Record(label + "_active_observed", sample.activePhysicsSteps > 0, "completed attack activePhysicsSteps=" + sample.activePhysicsSteps);
                if (sample.activePhysicsSteps > 0)
                {
                    Record(label + "_locked_target", sample.sawTelegraph && sample.sawLock && sample.targetStable,
                        "tell=" + sample.sawTelegraph + " lock=" + sample.sawLock + " targetStable=" + sample.targetStable + " target=" + sample.lockedTargetX);
                    Record(label + "_warning_covers_damage", sample.warningCoversActiveX,
                        "initialWarning=[" + sample.warningLeft + "," + sample.warningRight + "] waveWarning=[" + sample.waveWarningLeft + "," + sample.waveWarningRight + "] waveActiveUnion=[" + sample.waveActiveMinX + "," + sample.waveActiveMaxX + "] primaryCovered=" + sample.wavePrimaryCovered + " secondaryCovered=" + sample.waveSecondaryCovered + "; raw physical extents, no arena clipping");
                }
                if (sample.completed && sample.pattern == "BBurrowLeap")
                {
                    bool phaseWave = sample.phase == 1 ? sample.wavePhysicsSteps == 0
                        : sample.sawWaveWarning && sample.waveWarningStable && sample.waveWarningPhysicsSteps > 0
                        && sample.wavePrimaryPhysicsSteps > 0 && sample.waveSecondaryPhysicsSteps > 0
                        && sample.waveFirstActivePhysicsTime > sample.waveWarningFirstPhysicsTime;
                    Record(label + "_landing_and_wave_observed", sample.landingPhysicsSteps > 0 && phaseWave,
                        "phase=" + sample.phase + " landingSteps=" + sample.landingPhysicsSteps + " warningSteps=" + sample.waveWarningPhysicsSteps + " primaryWaveSteps=" + sample.wavePrimaryPhysicsSteps + " secondaryWaveSteps=" + sample.waveSecondaryPhysicsSteps + " warningLead=" + (sample.waveFirstActivePhysicsTime-sample.waveWarningFirstPhysicsTime).ToString("F3"));
                }
            }
            Record("v3_physics_nonempty_observation", completed > 0 && active > 0, "completed=" + completed + " sampledDamageAttacks=" + active + "; unfinished final telegraphs remain raw evidence, not completed attacks");
        }

        private IEnumerator MeasureDash()
        {
            float requestedAt = Time.realtimeSinceStartup;
            player.SubmitInput(1, false, false, true);
            float deadline = requestedAt + 2f;
            while (player.Movement.Action != WarriorAction.Dash && Time.realtimeSinceStartup < deadline) yield return null;
            bool accepted = player.Movement.Action == WarriorAction.Dash;
            float latency = Time.realtimeSinceStartup - requestedAt;
            dashProbeTicks = 0;
            dashProbeDistance = 0;
            dashProbeComplete = false;
            dashProbePreviousStep = false;
            dashProbePreviousAction = accepted;
            dashProbePreviousX = player.Body.position.x;
            dashProbeSampling = accepted;
            player.SubmitInput(0, false, false, false);
            deadline = Time.realtimeSinceStartup + 2f;
            while (dashProbeSampling && Time.realtimeSinceStartup < deadline) yield return null;
            dashProbeSampling = false;
            float physicsSeconds = dashProbeTicks * Time.fixedDeltaTime;
            string evidence = "accepted=" + accepted + " requestToAcceptanceRealtime=" + latency.ToString("F3") + "s physicsTicks=" + dashProbeTicks + " physicsDuration=" + physicsSeconds.ToString("F3") + "s Body.position travel=" + dashProbeDistance.ToString("F3");
            report.events.Add("dash_measurement " + evidence);
            Record("dash_travel", accepted && dashProbeComplete && dashProbeDistance > 2.5f && dashProbeDistance < 4.1f, evidence);
            Record("dash_physics_duration", accepted && dashProbeComplete && Mathf.Abs(physicsSeconds - player.Movement.DashDuration) <= Time.fixedDeltaTime + 0.001f, evidence);
        }
        private IEnumerator Contracts()
        {
            if (boss != null) boss.SetFightActive(false);
            Vector2 spawn = player.transform.position;
            player.ResetForFight(spawn, 1);
            player.SetFightActive(true);
            player.SetExternalInput(true);
            yield return Drive(0.6f);
            yield return MeasureWalk();
            yield return Drive(0.5f, -1);
            float feet = player.HurtBounds.min.y;
            float peak = feet;
            float jumpStart = Time.time;
            player.SubmitInput(0, true, false, false);
            yield return null;
            while (Time.time - jumpStart < 0.9f)
            {
                player.SubmitInput(0, false, false, false);
                peak = Mathf.Max(peak, player.HurtBounds.min.y);
                yield return null;
            }
            float jumpHeight = peak - feet;
            Record("jump_height", jumpHeight > 0.95f && jumpHeight < 1.65f, "feet apex delta=" + jumpHeight.ToString("F3"));
            Record("jump_lands", Mathf.Abs(player.HurtBounds.min.y - feet) < 0.15f, "landing feet delta=" + (player.HurtBounds.min.y - feet).ToString("F3"));
            yield return MeasureDash();
            yield return Drive(1);
            MonoBehaviour pauseSession = FindSession();
            if (pauseSession != null)
            {
                uint sequenceBefore = player.Movement.AttackSequence;
                var actionBefore = player.Movement.Action;
                Vector2 beforePause = player.transform.position;
                pauseSession.SendMessage("TogglePause", SendMessageOptions.RequireReceiver);
                float pauseUntil = Time.realtimeSinceStartup + 0.18f;
                while (Time.realtimeSinceStartup < pauseUntil)
                {
                    player.SubmitInput(1, true, true, true);
                    yield return null;
                }
                Record("pause_rejects_gameplay_input", Time.timeScale == 0 && player.Movement.AttackSequence == sequenceBefore && player.Movement.Action == actionBefore, "action=" + player.Movement.Action + " sequence=" + player.Movement.AttackSequence);
                pauseSession.SendMessage("TogglePause", SendMessageOptions.RequireReceiver);
                yield return Drive(0.15f);
                Record("resume_has_no_queued_action", Mathf.Approximately(Time.timeScale, 1) && player.Movement.AttackSequence == sequenceBefore && Vector2.Distance(beforePause, player.transform.position) < 0.1f, "position delta=" + Vector2.Distance(beforePause, player.transform.position).ToString("F3"));
            }
            yield return Drive(0.06f, 1, false, true);
            bool wasDashing = player.Movement.Action == WarriorAction.Dash;
            float dashVelocity = player.Body.linearVelocity.x;
            bool dashHitAccepted = player.ReceiveAttack(810000, 1, new Vector2(-4f, 2.7f), gameObject);
            float hitVelocity = player.Body.linearVelocity.x;
            Record("dash_hit_overrides_momentum", wasDashing && dashVelocity > 0f && dashHitAccepted && Mathf.Abs(hitVelocity + 4f) < 0.01f,
                "wasDashing=" + wasDashing + " beforeX=" + dashVelocity.ToString("F2") + " accepted=" + dashHitAccepted + " afterX=" + hitVelocity.ToString("F2"));
            yield return Drive(0.95f);
            int hp = player.Health.CurrentHealth;
            bool first = player.ReceiveAttack(810001, 1, Vector2.zero, gameObject);
            yield return Drive(0.95f);
            bool duplicate = player.ReceiveAttack(810001, 1, Vector2.zero, gameObject);
            Record("attack_id_dedup_after_iframe", first && !duplicate && player.Health.CurrentHealth == hp - 1, "first=" + first + " duplicate=" + duplicate + " HP=" + player.Health.CurrentHealth);
            bool second = player.ReceiveAttack(810002, 1, Vector2.zero, gameObject);
            bool invulnerableHit = player.ReceiveAttack(810003, 1, Vector2.zero, gameObject);
            Record("damage_iframe", second && !invulnerableHit && player.Health.CurrentHealth == hp - 2, "newAttack=" + second + " immediateNext=" + invulnerableHit);
            yield return Drive(0.95f);
            player.ReceiveAttack(810004, 999, Vector2.zero, gameObject);
            Record("lethal_damage", player.IsDead && player.Health.CurrentHealth == 0, "IsDead=" + player.IsDead + " HP=" + player.Health.CurrentHealth);
            float initialX = player.transform.position.x;
            yield return Drive(0.3f, 1, true, true, true);
            Record("death_input_blocked", Mathf.Abs(player.transform.position.x - initialX) < 0.15f, "dead movement=" + (player.transform.position.x - initialX).ToString("F3"));
            if (!string.IsNullOrEmpty(captureDirectory))
            {
                yield return new WaitForSecondsRealtime(0.65f);
                CaptureFrame("defeat");
                yield return null;
            }
            player.ResetForFight(spawn, 1);
            player.SetFightActive(true);
            player.SetExternalInput(true);
            Record("reset_health_liveness", !player.IsDead && player.Health.CurrentHealth == player.Health.MaxHealth, "HP=" + player.Health.CurrentHealth + " dead=" + player.IsDead);
            bool fresh = player.ReceiveAttack(810001, 1, Vector2.zero, gameObject);
            Record("reset_clears_attack_ledger", fresh, "same prior attack ID accepted=" + fresh);
            Record("reset_timescale", Mathf.Approximately(Time.timeScale, 1), "timeScale=" + Time.timeScale);
            MonoBehaviour session = FindSession();
            if (session == null) Record("retry_session_available", false, "BossFightSession missing");
            else
            {
                BossFightSession oldSession = session as BossFightSession;
                bool originallyMuted = oldSession != null && oldSession.Muted;
                if (oldSession != null && !oldSession.Muted) oldSession.ToggleMute();
                bool mutedBeforeRetry = oldSession != null && oldSession.Muted;
                int oldInstance = player.GetInstanceID();
                player.Damaged -= OnDamage;
                session.SendMessage("Retry", SendMessageOptions.RequireReceiver);
                float retryDeadline = Time.realtimeSinceStartup + 15;
                BossFightPlayerAdapter nextPlayer = null;
                while (Time.realtimeSinceStartup < retryDeadline)
                {
                    nextPlayer = FindFirstObjectByType<BossFightPlayerAdapter>();
                    if (nextPlayer != null && nextPlayer.GetInstanceID() != oldInstance) break;
                    yield return null;
                }
                bool reloaded = nextPlayer != null && nextPlayer.GetInstanceID() != oldInstance;
                Record("retry_reloads_session", reloaded, "new player instance=" + reloaded);
                if (reloaded)
                {
                    player = nextPlayer;
                    boss = FindFirstObjectByType<DoCheolBossController>(); SyncBossDamageSubscription();
                    player.Damaged += OnDamage;
                    yield return new WaitForSecondsRealtime(0.35f);
                    BossFightSession newSession = FindSession() as BossFightSession;
                    Record("retry_preserves_mute", mutedBeforeRetry && newSession != null && newSession.Muted,
                        "beforeRetry=" + mutedBeforeRetry + " afterRetry=" + (newSession != null && newSession.Muted));
                    if (newSession != null && newSession.Muted != originallyMuted) newSession.ToggleMute();
                    Record("retry_restores_health", !player.IsDead && player.Health.CurrentHealth == player.Health.MaxHealth, "HP=" + player.Health.CurrentHealth);
                    Record("retry_restores_velocity", Mathf.Abs(player.Body.linearVelocity.x) < 0.01f, "velocity=" + player.Body.linearVelocity);
                    Record("retry_restores_timescale", Mathf.Approximately(Time.timeScale, 1), "timeScale=" + Time.timeScale);
                    Record("retry_clears_attack_state", boss != null && (boss.State == BossState.Idle || boss.State == BossState.Dormant) && boss.Pattern == BossPattern.None, "boss=" + (boss != null ? boss.State + "/" + boss.Pattern : "missing"));
                }
            }
            yield return Finish("contracts_complete");
        }
        private static MonoBehaviour FindSession()
        {
            foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (item.GetType().Name == "BossFightSession") return item;
            return null;
        }
        private IEnumerator PrepareControlledCase(int phase, float bossX, float playerX)
        {
            // Let a previous case's capture render before controlled setup mutates the scene.
            if (!Application.isBatchMode) yield return new WaitForEndOfFrame();
            yield return null;
            attackEvidence.Clear();
            evidenceCase++;
            boss.ResetFight(new Vector2(bossX, boss.FloorY));
            player.ResetForFight(new Vector2(playerX, boss.FloorY + .025f));
            player.SetExternalInput(true);
            player.Health.ConfigureInvulnerability(.8f);
            boss.SetFightActive(true);
            if (phase == 2)
            {
                boss.Health.TakeDamage(new DamageInfo(boss.Health.CurrentHealth - boss.Health.MaxHealth / 2, boss.GetArmorWorldPoint(0), Vector2.zero, gameObject));
                float deadline = Time.realtimeSinceStartup + 6f;
                while (boss.Phase != 2 && Time.realtimeSinceStartup < deadline) { player.SubmitInput(0, false, false, false); yield return null; }
            }
        }

        private float SwordReach()
        {
            DamageHitbox2D sword = player.GetComponentInChildren<DamageHitbox2D>(true);
            BoxCollider2D shape = sword != null ? sword.GetComponent<BoxCollider2D>() : null;
            // PlayerCombat2D serialized attackOffset is .8 in the preserved player prefab.
            // The collider transform is positioned only on its first attack, so it is not a configuration source.
            return .8f + (shape != null ? shape.size.x * Mathf.Abs(shape.transform.lossyScale.x) * .5f : .575f);
        }

        private Collider2D BossHurtShape()
        {
            Hurtbox2D receiver = boss.GetComponentInChildren<Hurtbox2D>(true);
            return receiver != null ? receiver.GetComponent<Collider2D>() : null;
        }

        private float WalkOutOfWarning(Bounds warning)
        {
            Bounds hurt = player.HurtBounds;
            if (hurt.max.x <= warning.min.x - .08f || hurt.min.x >= warning.max.x + .08f) return 0;
            float left = warning.min.x - hurt.extents.x - .13f;
            float right = warning.max.x + hurt.extents.x + .13f;
            bool leftValid = left >= boss.ArenaLeft + hurt.extents.x + .03f;
            bool rightValid = right <= boss.ArenaRight - hurt.extents.x - .03f;
            float px = hurt.center.x;
            float exit = leftValid && (!rightValid || Mathf.Abs(px - left) < Mathf.Abs(px - right)) ? left : right;
            return Mathf.Sign(exit - px);
        }

        private IEnumerator IntroFlow()
        {
            BossFightSession encounter = FindSession() as BossFightSession;
            if (encounter == null) { Record("intro_session", false, "Session missing"); yield return Finish("setup_failed"); yield break; }
            float observeSeconds;
            if (!float.TryParse(Argument("-bossqa-intro-wait", "0.6"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out observeSeconds)) observeSeconds = .6f;
            observeSeconds = Mathf.Clamp(observeSeconds, .2f, 9f);
            bool sawIntroduction = encounter.State == EncounterState.Introduction;
            bool introSafe = true;
            float deadline = Time.realtimeSinceStartup + observeSeconds;
            int initialPlayerHp = player.Health.CurrentHealth;
            int initialBossHp = boss.Health.CurrentHealth;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (encounter.State == EncounterState.Introduction)
                {
                    sawIntroduction = true;
                    introSafe &= !player.FightActive && !boss.FightActive && !player.Health.DamageEnabled && !boss.Health.DamageEnabled;
                    CaptureFrame("intro-" + encounter.IntroStage);
                    if (encounter.IntroStageProgress >= .45f && encounter.IntroStageProgress <= .65f) CaptureFrame("intro-" + encounter.IntroStage + "-mid");
                }
                player.SubmitInput(0, false, false, false);
                yield return null;
            }
            Record("intro_exists_and_protects_combat", sawIntroduction && introSafe && player.Health.CurrentHealth == initialPlayerHp && boss.Health.CurrentHealth == initialBossHp,
                "observed=" + sawIntroduction + " protected=" + introSafe + " stage=" + encounter.IntroStage + " progress=" + encounter.IntroProgress.ToString("F3"));
            bool earlySkip = observeSeconds < 7f;
            if (earlySkip)
                Record("intro_not_finished_before_skip", encounter.State == EncounterState.Introduction && !encounter.IntroComplete, "observeSeconds=" + observeSeconds.ToString("F2"));
            else
                Record("intro_automatically_completes", encounter.IntroComplete && encounter.State == EncounterState.Fighting, "observeSeconds=" + observeSeconds.ToString("F2"));
            // This is the exact StartFight entry point used by Enter and the on-screen button,
            // not a claim of synthetic keyboard or manual keyboard coverage.
            encounter.StartFight();
            yield return null;
            Record("intro_skip_entrypoint_starts_fight", encounter.IntroComplete && encounter.State == EncounterState.Fighting && player.FightActive && boss.FightActive,
                "via=StartFight shared Enter/button entrypoint state=" + encounter.State);
            yield return VerifyNaturalRetry("intro");
            BossFightSession restarted = FindSession() as BossFightSession;
            Record("retry_skips_intro", restarted != null && restarted.IntroComplete && restarted.State == EncounterState.Fighting,
                "introComplete=" + (restarted != null && restarted.IntroComplete));
            yield return Finish(earlySkip ? "intro_skipped_retried" : "intro_completed_retried");
        }
        private IEnumerator StationaryMash()
        {
            if (boss == null) { Record("mash_setup", false, "Boss missing"); yield return Finish("setup_failed"); yield break; }
            int initialBossHp = boss.Health.CurrentHealth;
            uint initialAttacks = player.Movement.AttackSequence;
            float deadline = Time.realtimeSinceStartup + 120f;
            while (!player.IsDead && !boss.Health.IsDead && Time.realtimeSinceStartup < deadline)
            {
                // Deliberately no walking, jump, dash, forced damage, repositioning or pattern selection.
                player.SubmitInput(0, false, true, false);
                CaptureCombatFrames();
                yield return null;
            }
            player.SubmitInput(0, false, false, false);
            int damageDealt = initialBossHp - boss.Health.CurrentHealth;
            uint attacks = player.Movement.AttackSequence - initialAttacks;
            Record("mash_engaged_boss", damageDealt >= 3 && attacks >= 6, "normal attack attempts=" + attacks + " boss damage=" + damageDealt);
            Record("stationary_mash_does_not_win", player.IsDead && !boss.Health.IsDead && report.damageEvents >= 3,
                "playerHP=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth + " incoming hits=" + report.damageEvents);
            if (player.IsDead || boss.Health.IsDead) yield return new WaitForSecondsRealtime(.95f);
            yield return Finish(player.IsDead ? "stationary_mash_defeated" : boss.Health.IsDead ? "stationary_mash_won" : "stationary_mash_timeout");
        }
        private IEnumerator NaturalDefeat()
        {
            BossFightSession encounter = FindSession() as BossFightSession;
            if (boss == null || encounter == null)
            {
                Record("defeat_setup", false, "Boss or session missing");
                yield return Finish("setup_failed");
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 120f;
            while (!player.IsDead && Time.realtimeSinceStartup < deadline)
            {
                player.SubmitInput(0, false, false, false);
                CaptureCombatFrames();
                yield return null;
            }
            bool naturallyDefeated = player.IsDead && player.Health.CurrentHealth == 0 && encounter.State == EncounterState.Defeat;
            Record("natural_defeat_without_cheats", naturallyDefeated && !boss.FightActive && !player.FightActive,
                "state=" + encounter.State + " playerHP=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth + " naturalDamageEvents=" + report.damageEvents);
            if (!naturallyDefeated) { yield return Finish("defeat_timeout"); yield break; }
            float deathX = player.Body.position.x;
            yield return Drive(0.3f, 1, true, true, true);
            Record("natural_defeat_blocks_input", Mathf.Abs(player.Body.position.x - deathX) < 0.1f && !player.Combat.IsHitboxActive,
                "deadXDelta=" + (player.Body.position.x - deathX).ToString("F3") + " hitboxActive=" + player.Combat.IsHitboxActive);
            yield return new WaitForSecondsRealtime(0.75f);
            CaptureFrame("natural-defeat");
            yield return null;
            yield return VerifyNaturalRetry("natural_defeat");
            yield return Finish("natural_defeat_retried");
        }

        private IEnumerator VerifyNaturalRetry(string prefix)
        {
            BossFightSession encounter = FindSession() as BossFightSession;
            if (encounter == null)
            {
                Record(prefix + "_retry_session", false, "BossFightSession missing");
                yield break;
            }
            int oldInstance = player.GetInstanceID();
            player.Damaged -= OnDamage;
            encounter.Retry();
            BossFightPlayerAdapter replacement = null;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                replacement = FindFirstObjectByType<BossFightPlayerAdapter>();
                if (replacement != null && replacement.GetInstanceID() != oldInstance) break;
                yield return null;
            }
            bool reloaded = replacement != null && replacement.GetInstanceID() != oldInstance;
            Record(prefix + "_retry_new_instance", reloaded, "scene reload produced a new player=" + reloaded);
            if (!reloaded) yield break;
            player = replacement;
            boss = FindFirstObjectByType<DoCheolBossController>(); SyncBossDamageSubscription();
            encounter = FindSession() as BossFightSession;
            player.Damaged += OnDamage;
            player.SetExternalInput(true);
            yield return Drive(0.25f);
            Record(prefix + "_retry_clears_v3_hazards", boss != null && !boss.RockfallActive && boss.Rockfalls.Count == 0 && boss.ComboIndex == 0, "hazards=" + (boss != null ? boss.Rockfalls.Count : -1));
            Record(prefix + "_retry_health", !player.IsDead && player.Health.CurrentHealth == 8 && boss != null && boss.Health.CurrentHealth == 40,
                "playerHP=" + player.Health.CurrentHealth + " bossHP=" + (boss != null ? boss.Health.CurrentHealth : -1));
            Record(prefix + "_retry_state", encounter != null && encounter.State == EncounterState.Fighting && Mathf.Approximately(Time.timeScale, 1) && boss != null && boss.Pattern == BossPattern.None && boss.AttackId == 0 && boss.State == BossState.Idle && player.FightActive && boss.FightActive,
                "session=" + (encounter != null ? encounter.State.ToString() : "missing") + " timescale=" + Time.timeScale + " boss=" + (boss != null ? boss.State + "/" + boss.Pattern + " id=" + boss.AttackId : "missing"));
        }
        [Serializable] public sealed class V3Case
        {
            public string name, requested, strategy, initialDisclosure;
            public int phase, incomingHits, planIncomingHits, acceptedAttacks, recoveryHits, followupHits, physicsSteps, jumps, dashes, rockfallBossHits, comboRetaliationHits;
            public int acceptedJumps;
            public bool cancelAttackAccepted, cancelDashRequested, cancelDashAccepted;
            public float cancelDelay = -1;
            public bool forced, complete, sawLink, fakeRecovery, directionChanged, sawWave, sawRise, sawLanding, sawRockfall, basicDuringRockfall, burrowDuringRockfall, rockfallDamageable = true;
            public float physicsTravel, peakFeet, initialX, finalX, inwardTravelAfterJump, reactionDelay = .2f;
            public List<string> timeline = new List<string>();
        }
        [Serializable] public sealed class HazardEvidence
        {
            public int id, physicsSteps, activeSteps, damageEvents;
            public string size;
            public float lockedX, warningWidth, warningLeft, warningRight;
            public bool positionStable = true, warnedBeforeImpact = true;
        }
        private V3Case currentV3;
        private float v3PreviousPhysicsX;
        private readonly Dictionary<int, HazardEvidence> hazardEvidence = new Dictionary<int, HazardEvidence>();
        private int reactionAttack = -1;
        private string reactionStage = "";
        private float reactionAt;
        private bool jumpRequested, reactionWasLocked, dashCancelProbe, jumpRequestPending;
        private float jumpFirstRequestAt = -1;
        private float cancelAttackAcceptedAt = -1;
        private uint cancelSequenceBefore;
        private readonly Dictionary<int, float> hazardFirstSeen = new Dictionary<int, float>();

        private void ObserveV3Physics()
        {
            if (report == null || boss == null || player == null) return;
            if (currentV3 != null)
            {
                currentV3.physicsSteps++;
                currentV3.physicsTravel += Mathf.Abs(player.Body.position.x - v3PreviousPhysicsX);
                currentV3.peakFeet = Mathf.Max(currentV3.peakFeet, player.HurtBounds.min.y - boss.FloorY);
                v3PreviousPhysicsX = player.Body.position.x;
            }
            foreach (DocheolRockfall rock in boss.Rockfalls)
            {
                if (!hazardEvidence.TryGetValue(rock.AttackId, out HazardEvidence sample))
                {
                    sample = new HazardEvidence { id = rock.AttackId, lockedX = rock.LockedX, warningWidth = rock.WarningBounds.size.x, warningLeft = rock.WarningBounds.min.x, warningRight = rock.WarningBounds.max.x, size = rock.Size.ToString() };
                    sample.damageEvents = acceptedDamageCounts.TryGetValue(rock.AttackId, out int acceptedHits) ? acceptedHits : 0;
                    hazardEvidence.Add(rock.AttackId, sample);
                    hazardFirstSeen[rock.AttackId] = Time.time;
                    report.hazards.Add(sample);
                }
                sample.physicsSteps++;
                sample.positionStable &= Mathf.Abs(sample.lockedX - rock.LockedX) < .001f;
                if (rock.ActiveBounds.size.x > .001f)
                {
                    sample.activeSteps++;
                    sample.warnedBeforeImpact &= sample.physicsSteps > 1 && rock.ActiveBounds.min.x >= sample.warningLeft - .025f && rock.ActiveBounds.max.x <= sample.warningRight + .025f;
                }
            }
        }

        private uint observedBotAttackSequence;
        private float observedBotAttackAt = -1, nextRockInputTrace;
        private bool botAttackClockInitialized;

        private float ObservedAttackLockRemaining()
        {
            if (!botAttackClockInitialized || player.Movement.AttackSequence != observedBotAttackSequence)
            {
                observedBotAttackSequence = player.Movement.AttackSequence;
                observedBotAttackAt = Time.time;
                botAttackClockInitialized = true;
            }
            WarriorAction action = player.Movement.Action;
            bool attackLocked = action == WarriorAction.BasicAttack || action == WarriorAction.Attack1 || action == WarriorAction.Attack2;
            // The preserved ordinary attack lasts .45 scaled seconds. Observation may occur a
            // rendered frame late, so this overestimates rather than removes the remaining lock.
            return attackLocked ? Mathf.Max(0, .45f - (Time.time - observedBotAttackAt)) : 0;
        }

        private bool RecognizedRockRouteClear(float from, float to, float waitBeforeWalking)
        {
            float speed = Mathf.Max(.01f, player.Movement.MoveSpeed);
            float radius = player.HurtBounds.extents.x + .08f;
            foreach (DocheolRockfall rock in boss.Rockfalls)
            {
                if (!hazardFirstSeen.TryGetValue(rock.AttackId, out float firstSeen) || Time.time-firstSeen < .2f || rock.Stage == "dust" || rock.Stage == "done") continue;
                // A previously seen, fixed warning gives its own visible size/timing. Derive
                // impact time from that observation; do not query the boss's route planner,
                // future combo, spawn schedule or escape-corridor API.
                float visibleAge = Time.time-firstSeen;
                float impactBegins = Mathf.Max(0, rock.WarningDuration + .25f - visibleAge - .04f);
                float impactEnds = rock.WarningDuration + .25f + .14f - visibleAge + .04f;
                if (impactEnds < 0) continue;
                // Check every ordinary physics interval throughout the currently visible
                // impact interval. The endpoint is held after arrival, not teleported to.
                for (float t = impactBegins; t <= impactEnds + .001f; t += Time.fixedDeltaTime)
                {
                    float travel = speed * Mathf.Max(0, t-waitBeforeWalking);
                    float x = Mathf.MoveTowards(from, to, travel);
                    if (x + radius > rock.WarningBounds.min.x && x - radius < rock.WarningBounds.max.x) return false;
                }
            }
            return true;
        }

        private void V3Input(bool retaliate, bool reckless = false)
        {
            float attackLockRemaining = ObservedAttackLockRemaining();
            bool changed = reactionAttack != boss.AttackId || reactionStage != boss.AttackStage;
            if (reactionAttack != boss.AttackId)
            {
                reactionAttack = boss.AttackId;
                reactionAt = Time.time;
                jumpRequested = false;
                reactionWasLocked = false;
                jumpRequestPending = false; jumpFirstRequestAt = -1;
            }
            if (boss.TargetLocked && !reactionWasLocked) { reactionAt = Time.time; reactionWasLocked = true; }
            // The separate visible delayed wave earns its own recognition interval.
            if (changed && boss.AttackStage == "wave_delay") { reactionAt = Time.time; jumpRequested = false; jumpRequestPending = false; jumpFirstRequestAt = -1; }
            reactionStage = boss.AttackStage;
            if (jumpRequestPending && !jumpRequested && player.Movement.Action == WarriorAction.JumpUp && !player.Movement.IsGrounded)
            {
                jumpRequested = true;
                if (currentV3 != null) currentV3.acceptedJumps++;
                report.events.Add("jump_accepted t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " scaled=" + Time.time.ToString("F3") + " id=" + boss.AttackId + " inputToObservedAcceptance=" + (Time.time-jumpFirstRequestAt).ToString("F3") + " feet=" + player.HurtBounds.min.y.ToString("F3"));
            }
            float horizontal = 0;
            bool jump = false, attack = false, dash = false;
            bool danger = boss.State == BossState.Telegraph || boss.State == BossState.Active;
            bool ready = Time.time - reactionAt >= .2f;
            if (danger && ready && boss.TargetLocked)
            {
                if (boss.Pattern == BossPattern.DT02Sweep)
                {
                    // A front paw sweep is a local fast hit; this deliberately does not reuse V2 travelling-wave ETA.
                    if (!jumpRequested && player.Movement.IsGrounded && boss.State == BossState.Telegraph)
                        jump = true;
                }
                else if (boss.Pattern == BossPattern.BBurrowLeap && (boss.AttackStage == "wave_delay" || boss.AttackStage == "wave"))
                {
                    if (!jumpRequested && player.Movement.IsGrounded) jump = true;
                }
                else horizontal = WalkOutOfWarning(boss.WarningBounds);
            }
            if (reckless && (!dashCancelProbe || currentV3 == null || !currentV3.cancelDashRequested) && (boss.State == BossState.Link || (boss.ComboIndex > 0 && boss.State == BossState.Telegraph)))
            {
                horizontal = Mathf.Sign(boss.transform.position.x - player.Body.position.x);
                attack = true;
            }
            else if (retaliate && (boss.State == BossState.Recovery || boss.State == BossState.Idle || boss.Pattern == BossPattern.ARockfall))
            {
                Collider2D shape = BossHurtShape();
                if (shape != null && shape.enabled)
                {
                    float px = player.Body.position.x;
                    float face = px < shape.bounds.center.x ? 1 : -1;
                    float gap = Mathf.Max(shape.bounds.min.x - px, px - shape.bounds.max.x, 0f);
                    horizontal = gap > SwordReach() - .18f ? face : face * .05f;
                    attack = gap <= SwordReach() - .18f;
                }
            }
            // Select a reachable safe point against the union of CURRENT visible rock warnings
            // plus the boss warning, then reach it through ordinary movement physics.
            if (boss.RockfallActive)
            {
                float px = player.Body.position.x;
                float best = px, bestCost = float.PositiveInfinity;
                for (float x = boss.ArenaLeft + .65f; x <= boss.ArenaRight - .65f; x += .2f)
                {
                    bool safe = true;
                    foreach (DocheolRockfall rock in boss.Rockfalls)
                        if (hazardFirstSeen.TryGetValue(rock.AttackId, out float seenAt) && Time.time - seenAt >= .2f && rock.Stage != "dust" && rock.Stage != "done" && x > rock.WarningBounds.min.x - .52f && x < rock.WarningBounds.max.x + .52f) { safe = false; break; }
                    if (danger && ready && boss.TargetLocked && boss.Pattern != BossPattern.DT02Sweep && boss.WarningBounds.size.x > .01f && x > boss.WarningBounds.min.x - .52f && x < boss.WarningBounds.max.x + .52f) safe = false;
                    if (safe && RecognizedRockRouteClear(px, x, attackLockRemaining) && Mathf.Abs(x - px) < bestCost) { best = x; bestCost = Mathf.Abs(x - px); }
                }
                if (float.IsInfinity(bestCost)) { horizontal = 0; attack = false; }
                else if (bestCost > .12f) { horizontal = Mathf.Sign(best-px); attack = false; }
                else horizontal = 0; // Hold the validated endpoint; do not leak recovery approach input into a rock.
                // Do not start a new ordinary attack if its .45s movement commitment would
                // invalidate the route to the same visible safe endpoint. This is input
                // selection only; actual collisions and accepted attacks remain authoritative.
                if (attack && !RecognizedRockRouteClear(px, best, .45f + Time.fixedDeltaTime)) attack = false;
                if (attack)
                {
                    // Grounded, stationary basic attacks set facing before RequestAttack and
                    // hold vx=0. Air attacks preserve current vx and do not match this hold plan.
                    if (!player.Movement.IsGrounded || Mathf.Abs(player.Body.linearVelocity.x) >= .1f) attack = false;
                    else
                    {
                        Collider2D attackTarget = BossHurtShape();
                        horizontal = attackTarget != null && px < attackTarget.bounds.center.x ? .05f : -.05f;
                    }
                }
                if (Time.time >= nextRockInputTrace)
                {
                    report.events.Add("rock_input t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " scaled=" + Time.time.ToString("F3") + " px=" + px.ToString("F3") + " action=" + player.Movement.Action + " observedLock=" + attackLockRemaining.ToString("F3") + " goal=" + best.ToString("F3") + " routeAvailable=" + !float.IsInfinity(bestCost) + " move=" + horizontal + " jump=" + jump + " attack=" + attack);
                    nextRockInputTrace = Time.time + .1f;
                }
            }
            if (jump)
            {
                if (!jumpRequestPending)
                {
                    jumpRequestPending = true; jumpFirstRequestAt = Time.time;
                    report.events.Add("jump_requested t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " scaled=" + Time.time.ToString("F3") + " id=" + boss.AttackId + " pattern=" + boss.Pattern + " stage=" + boss.AttackStage + " stateProgress=" + boss.StateProgress.ToString("F3") + " recognizedFor=" + (Time.time-reactionAt).ToString("F3") + " playerAction=" + player.Movement.Action);
                }
                if (currentV3 != null) currentV3.jumps++;
                // A locked player can discard a jump request. Retry ordinary button requests
                // while grounded until an actual JumpUp action is observed; never claim the
                // first request itself was a successful jump or advance the player state.
            }
            if (dashCancelProbe && currentV3 != null)
            {
                if (player.Movement.AttackSequence > cancelSequenceBefore && cancelAttackAcceptedAt < 0)
                {
                    cancelAttackAcceptedAt = Time.time;
                    currentV3.cancelAttackAccepted = true;
                }
                if (cancelAttackAcceptedAt >= 0 && !currentV3.cancelDashRequested && Time.time - cancelAttackAcceptedAt >= .12f)
                {
                    horizontal = Mathf.Sign(player.Body.position.x - boss.transform.position.x);
                    dash = true; attack = false;
                    currentV3.cancelDashRequested = true;
                    currentV3.cancelDelay = Time.time - cancelAttackAcceptedAt;
                    currentV3.dashes++;
                }
                if (currentV3.cancelDashRequested)
                    currentV3.cancelDashAccepted |= player.Movement.Action == WarriorAction.Dash;
            }
            player.SubmitInput(horizontal, jump, attack, dash);
        }

        private static SpriteRenderer VisualPart(DocheolVisual visual, string name)
        {
            foreach (SpriteRenderer part in visual.GetComponentsInChildren<SpriteRenderer>())
                if (part.name == name) return part;
            return null;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(presentationCaptureLabel)) return;
            GUI.Box(new Rect(12, 12, Screen.width - 24, 54),
                "QA PRESENTATION ONLY | " + presentationCaptureLabel + "\nFixed pose/camera; visual armor override, no combat or HP measurement.");
        }

        private IEnumerator ArmorVisuals()
        {
            report.inputStrategy = "presentation-only snapshot; visual overrides, no combat/HP progression measurement";
            DocheolVisual visual = FindFirstObjectByType<DocheolVisual>();
            BossFightSession session = FindSession() as BossFightSession;
            BossFightHud hud = FindFirstObjectByType<BossFightHud>();
            if (visual == null || !visual.HasV2Artwork || boss == null || session == null)
            {
                Record("armor_visual_setup", false, "existing articulated artwork, boss and session required");
                yield return Finish("setup_failed"); yield break;
            }
            bool sessionEnabled = session.enabled, hudEnabled = hud != null && hud.enabled;
            bool bossActive = boss.FightActive, playerActive = player.FightActive;
            float previousScale = Time.timeScale;
            Vector3 initialPosition = visual.transform.position;
            session.enabled = false;
            if (hud != null) hud.enabled = false;
            boss.SetFightActive(false); player.SetFightActive(false);
            player.SubmitInput(0, false, false, false);
            Time.timeScale = 0f;
            try
            {
                yield return null; // Freeze scaled pose time before fixing the comparison.
                visual.SetCombatState(1, 15, "idle", 0, 0, false, false);
                visual.SetPose("", "idle", 0, -1);
                visual.SetFlash(0);
                SpriteRenderer forehead = VisualPart(visual, "ForeheadArmor");
                SpriteRenderer dorsal = VisualPart(visual, "DorsalArmor");
                SpriteRenderer shoulder = VisualPart(visual, "ShoulderArmor");
                Texture2D core = VisualPart(visual, "BodyFront").sprite.texture;
                Texture2D supports = VisualPart(visual, "Roots").sprite.texture;
                Texture2D armor = forehead.sprite.texture;
                visual.OnLocalHit(forehead.bounds.center, 0, true);
                visual.OnLocalHit(dorsal.bounds.center, 3, true);
                visual.SetPose("", "idle", 0, -1);
                Record("local_hits_same_frame", forehead.color.g < .95f && dorsal.color.g < .95f && shoulder.color == Color.white,
                    "foreheadG=" + forehead.color.g + " dorsalG=" + dorsal.color.g + " untouchedShoulder=" + shoulder.color);
                Color foreheadColor = forehead.color, dorsalColor = dorsal.color;
                visual.transform.position += Vector3.right * 2;
                visual.SetPose("", "idle", 0, -1);
                Record("local_hits_follow_rig", forehead.color == foreheadColor && dorsal.color == dorsalColor,
                    "two independent marks keep their colors after a two-unit visual translation");
                visual.transform.position = initialPosition;
                visual.SetPose("", "idle", 0, -1);
                presentationCaptureLabel = "two independent armored hit flashes";
                yield return CaptureVisualFrame("local-hits-two-regions");

                // Exercise the real public reconfiguration path with queued debris and a death tint.
                visual.SetDead(1); visual.SetFlash(1);
                visual.OnArmorBroken(1, 13, shoulder.bounds.center);
                visual.ConfigureV2(core, supports, armor);
                int liveRigs = 0;
                foreach (Transform child in visual.transform)
                    if (child.name == "Docheol_ArticulatedRig" && child.gameObject.activeSelf) liveRigs++;
                Record("reconfigure_hides_old_rig_immediately", liveRigs == 1, "active rigs before deferred destruction=" + liveRigs);
                visual.SetCombatState(1, 15, "idle", 0, 0, false, false);
                visual.SetPose("", "idle", 0, -1);
                yield return null;
                bool cleanColors = true;
                foreach (SpriteRenderer part in visual.GetComponentsInChildren<SpriteRenderer>())
                    if (part.name != "FloorAnchoredShadow") cleanColors &= part.color == Color.white;
                int debris = 0;
                foreach (SpriteRenderer part in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    if (part.name.StartsWith("DetachedArmor_")) debris++;
                Record("reconfigure_clears_hit_death_and_queued_debris", cleanColors && debris == 0,
                    "all rig colors white=" + cleanColors + " detached plates=" + debris);

                string[] armorNames = { "ForeheadArmor", "ShoulderArmor", "CheekArmor", "DorsalArmor" };
                string[] anatomyNames = { "BodyFront", "BodyRear", "NearHoof", "FarHoof", "NearHorn", "FarHorn", "TwoTrees" };
                SpriteRenderer[] anatomy = new SpriteRenderer[anatomyNames.Length];
                Matrix4x4[] matrices = new Matrix4x4[anatomyNames.Length];
                for (int i = 0; i < anatomy.Length; i++)
                {
                    anatomy[i] = VisualPart(visual, anatomyNames[i]);
                    matrices[i] = anatomy[i].transform.localToWorldMatrix;
                }
                int[] masks = { 15, 14, 12, 0 }; // V3 fixed armor order; rerun snapshots, do not reuse V2 results.
                int[] counts = { 4, 3, 2, 0 };
                Camera camera = Camera.main;
                Vector3 cameraPosition = camera.transform.position;
                float cameraSize = camera.orthographicSize;
                for (int step = 0; step < masks.Length; step++)
                {
                    visual.SetCombatState(step == 3 ? 2 : 1, masks[step], "idle", 0, 0, false, false);
                    visual.SetPose("", "idle", 0, -1);
                    int visible = 0, actualMask = 0;
                    for (int i = 0; i < armorNames.Length; i++)
                        if (VisualPart(visual, armorNames[i]).enabled) { visible++; actualMask |= 1 << i; }
                    bool sameAnatomy = true;
                    for (int i = 0; i < anatomy.Length; i++)
                        sameAnatomy &= anatomy[i] == VisualPart(visual, anatomyNames[i]) && anatomy[i].enabled && matrices[i] == anatomy[i].transform.localToWorldMatrix;
                    Record("armor_comparison_" + counts[step], visible == counts[step] && actualMask == masks[step] && sameAnatomy
                        && visual.transform.position == initialPosition && camera.transform.position == cameraPosition && camera.orthographicSize == cameraSize,
                        "mask=" + actualMask + " visible=" + visible + " same anatomy/pose=" + sameAnatomy);
                    string label = "armor-compare-" + counts[step] + "-mask" + masks[step];
                    presentationCaptureLabel = counts[step] + " attached armor plates (mask " + masks[step] + ")";
                    int before = report.screenshots.Count;
                    yield return CaptureVisualFrame(label);
                    report.armorCaptures.Add(new ArmorCaptureEvidence { label = label, mask = masks[step], visiblePlates = visible,
                        frame = lastCaptureFrame, screenshot = report.screenshots.Count > before ? report.screenshots[report.screenshots.Count - 1] : "",
                        position = initialPosition, cameraPosition = cameraPosition, orthographicSize = cameraSize, anatomyUnchanged = sameAnatomy });
                }
                SpriteRenderer skin = VisualPart(visual, "BodyFront");
                visual.OnLocalHit(skin.bounds.center, 0, false);
                Record("unarmored_hit_material", skin.color.g < .95f && !VisualPart(visual, "ForeheadArmor").enabled,
                    "skinG=" + skin.color.g + " forehead plate remains hidden");
                presentationCaptureLabel = "unarmored local hit";
                yield return CaptureVisualFrame("local-hit-unarmored");
                // Resume only the visual clock, while session/controller/input remain disabled.
                Time.timeScale = 1;
                yield return new WaitForSeconds(.20f);
                Time.timeScale = 0;
                Record("local_hit_fades", skin.color == Color.white, "skin after .20 scaled seconds=" + skin.color);
                presentationCaptureLabel = "unarmored flash cleared";
                yield return CaptureVisualFrame("local-hit-cleared");
                Record("armor_comparison_capture_requests", !Application.isBatchMode && report.armorCaptures.Count == 4
                    && report.armorCaptures.TrueForAll(c => !string.IsNullOrEmpty(c.screenshot)),
                    "four native screenshot requests; inspect output files separately");
            }
            finally
            {
                visual.transform.position = initialPosition;
                visual.SetCombatState(boss.Phase, boss.ArmorMask, boss.AttackStage, boss.AttackStageProgress, boss.TransitionProgress, boss.IsUnderground, boss.TargetLocked);
                presentationCaptureLabel = null;
                Time.timeScale = previousScale;
                boss.SetFightActive(bossActive); player.SetFightActive(playerActive);
                session.enabled = sessionEnabled;
                if (hud != null) hud.enabled = hudEnabled;
            }
            yield return Finish("armor_visual_complete");
        }

        private IEnumerator CaptureVisualFrame(string label)
        {
            yield return null;
            CaptureFrame(label);
            if (!Application.isBatchMode) yield return new WaitForEndOfFrame();
            yield return null;
        }

        private IEnumerator V3Phases()
        {
            yield return PrepareControlledCase(1, 2.8f, -5f);
            Record("v3_initial_armor", boss.ArmorMask == 15 && boss.Health.CurrentHealth == 40, "shared HP=" + boss.Health.CurrentHealth + " mask=" + boss.ArmorMask);
            for (int hitRegion = 0; hitRegion < 4; hitRegion++)
            {
                yield return PrepareControlledCase(1, 2.8f, -5f);
                boss.Health.TakeDamage(new DamageInfo(10, boss.GetArmorWorldPoint(hitRegion), Vector2.zero, gameObject));
                yield return Drive(.18f);
                Record("v3_armor30_hitregion" + hitRegion, boss.Health.CurrentHealth == 30 && boss.ArmorMask == 14, "HP=" + boss.Health.CurrentHealth + " mask=" + boss.ArmorMask + " expected fixed piece0 regardless of hit region");
                CaptureFrame("v3-armor30-region" + hitRegion);
                boss.Health.TakeDamage(new DamageInfo(6, boss.GetArmorWorldPoint(hitRegion), Vector2.zero, gameObject));
                yield return Drive(.18f);
                Record("v3_armor24_hitregion" + hitRegion, boss.Health.CurrentHealth == 24 && boss.ArmorMask == 12, "HP=" + boss.Health.CurrentHealth + " mask=" + boss.ArmorMask);
                boss.Health.TakeDamage(new DamageInfo(4, boss.GetArmorWorldPoint(hitRegion), Vector2.zero, gameObject));
                Record("v3_armor20_hitregion" + hitRegion, boss.ArmorMask == 8, "mask=" + boss.ArmorMask + " expected fixed third piece2 removed immediately");
                bool protectedImmediately = !boss.Health.DamageEnabled && !player.Health.DamageEnabled && boss.State == BossState.Transition;
                int hp = player.Health.CurrentHealth;
                bool safe = true;
                float until = Time.realtimeSinceStartup + 6;
                while (boss.State == BossState.Transition && Time.realtimeSinceStartup < until)
                {
                    boss.Health.TakeDamage(new DamageInfo(1, boss.transform.position, Vector2.zero, gameObject));
                    player.ReceiveAttack(950000 + Time.frameCount, 1, Vector2.zero, gameObject);
                    safe &= boss.Health.CurrentHealth == 20 && player.Health.CurrentHealth == hp && boss.ActiveBounds.size.x == 0 && !boss.RockfallActive;
                    CaptureCombatFrames();
                    yield return null;
                }
                Record("v3_transition_protection_region" + hitRegion, protectedImmediately && safe && boss.Phase == 2 && boss.ArmorMask == 0, "immediate=" + protectedImmediately + " safe=" + safe + " phase=" + boss.Phase + " mask=" + boss.ArmorMask);
            }
            Record("v3_phase2_unarmored_physics", phaseTwoUnarmored && sawPhaseTwo, "actual FixedUpdate samples phase2 armor count=0");
            yield return Finish("v3_phases_complete");
        }

        private IEnumerator V3Matrix()
        {
            if (boss == null) { Record("v3_boss", false, "missing"); yield return Finish("setup_failed"); yield break; }
            for (int phase = 1; phase <= 2; phase++)
            {
                if (Argument("-bossqa-phase", "all") != "all" && Argument("-bossqa-phase", "all") != phase.ToString()) continue;
                yield return PrepareControlledCase(phase, 2.8f, -.4f);
                foreach (BossPattern legacy in new[] { BossPattern.DT04Inhale, BossPattern.DT05BodySlam, BossPattern.DT06Burrow })
                    Record("v3_P" + phase + "_reject_" + legacy, !boss.ForcePattern(legacy), "legacy enum retained, ForcePattern must reject");
                if (phase == 1)
                {
                    Record("v3_P1_reject_1", !boss.ForcePattern(BossPattern.DT01Slam) && !boss.ForceCombo("1") && !boss.ForceCombo("2-1") && !boss.ForceCombo("3-2-1"), "phase gate tested for singles and complete combo plans");
                    Record("v3_earlyP1_reject_A", !boss.ForcePattern(BossPattern.ARockfall), "HP40 must not begin late-P1 A");
                }
                string[] combos = phase == 1 ? new[] { "3", "2", "3-2" } : new[] { "3", "2", "1", "3-2", "2-1", "3-2-1" };
                string requestedCase = Argument("-bossqa-case", "all");
                foreach (string code in combos)
                    if (requestedCase == "all" || requestedCase == code) yield return V3Measure(phase, code, 2.8f, -.4f, false);
                if (requestedCase == "all" || requestedCase == "3-2")
                {
                    V3Case safe = report.v3Cases[report.v3Cases.Count - 1];
                    foreach (V3Case item in report.v3Cases) if (item.phase == phase && item.requested == "3-2" && item.name.EndsWith("_safe")) safe = item;
                    yield return V3Measure(phase, "3-2", 2.8f, -.4f, true);
                    V3Case greed = report.v3Cases[report.v3Cases.Count - 1];
                    Record("v3_P" + phase + "_greed_vs_safe", safe.planIncomingHits == 0 && greed.planIncomingHits > safe.planIncomingHits,
                        "same initial fixture; forcedPlanSafeHits=" + safe.planIncomingHits + " forcedPlanGreedHits=" + greed.planIncomingHits + " safeAcceptedAttacks=" + safe.acceptedAttacks + " greedAcceptedAttacks=" + greed.acceptedAttacks + "; requires actual risk difference, not green UI suppression");
                }
                if (requestedCase == "all" || requestedCase == "3-2") yield return V3Measure(phase, "3-2", 2.8f, -.4f, true, true);
                if (requestedCase == "all" || requestedCase == "B") yield return V3Measure(phase, "B", 2.8f, -.4f, false);
                if (requestedCase == "all" || requestedCase == "B") yield return V3Measure(phase, "B", boss.ArenaLeft + 3.1f, boss.ArenaLeft + .8f, false);
                if (requestedCase == "all" || requestedCase == "B") yield return V3Measure(phase, "B", boss.ArenaRight - 3.1f, boss.ArenaRight - .8f, false);
                if (requestedCase == "all" || requestedCase == "A")
                {
                    yield return V3Measure(phase, "A", 2.8f, -.4f, false);
                    // Separate controlled gate fixture; never insert a Force call into the evasion measurement.
                    yield return PrepareControlledCase(phase, 2.8f, -.4f);
                    if (phase == 1) { boss.Health.TakeDamage(new DamageInfo(12, boss.transform.position, Vector2.zero, gameObject)); yield return Drive(.18f); }
                    bool began = boss.ForcePattern(BossPattern.ARockfall);
                    float gateUntil = Time.realtimeSinceStartup + 4;
                    while (!boss.RockfallActive && Time.realtimeSinceStartup < gateUntil) { player.SubmitInput(0, false, false, false); yield return null; }
                    bool activeBefore = boss.RockfallActive;
                    bool rejected = !boss.ForcePattern(BossPattern.BBurrowLeap);
                    Record("v3_P" + phase + "_rockfall_rejects_B", began && activeBefore && rejected, "separate initial-state gate fixture; rocks=" + activeBefore + " rejected=" + rejected);
                }
            }
            if (Argument("-bossqa-case", "all") == "all" || Argument("-bossqa-case", "all") == "A") Record("v3_hazard_physics_evidence", report.hazards.Count > 0 && report.hazards.TrueForAll(h => h.positionStable && h.warnedBeforeImpact), "hazards=" + report.hazards.Count + "; fixed warning X and advertised size checked against active physics samples");
            yield return Finish("v3_matrix_complete");
        }

        private IEnumerator V3Measure(int phase, string code, float bossX, float playerX, bool reckless, bool dashCancel = false)
        {
            yield return PrepareControlledCase(phase, bossX, playerX);
            if (code == "A" && phase == 1)
            {
                boss.Health.TakeDamage(new DamageInfo(12, boss.GetArmorWorldPoint(3), Vector2.zero, gameObject));
                yield return Drive(.18f);
            }
            V3Case sample = new V3Case { name = "P" + phase + "_" + code + "_x" + playerX.ToString("F1") + (dashCancel ? "_dashcancel" : reckless ? "_greed" : "_safe"), phase = phase, requested = code,
                strategy = reckless ? ".2s reaction; evade first hit, attack/advance during Link and followup tell" : ".2s recognition; walk outside bite/slam, jump paw/wave, walk nearest body for retaliation",
                initialDisclosure = "ResetFight/ResetForFight initial placement; synthetic P2/late-P1 HP before force; ordinary invulnerability; measurement begins at Force and ends after natural followup or A expiration", initialX = player.Body.position.x };
            if (dashCancel) sample.strategy = "evade bite; normal attack during Link; .12 scaled seconds after accepted attack, request ordinary outward dash cancel; preserve actual hit/damage result";
            botAttackClockInitialized = false;
            nextRockInputTrace = 0;
            dashCancelProbe = dashCancel;
            cancelAttackAcceptedAt = -1;
            cancelSequenceBefore = player.Movement.AttackSequence;
            report.v3Cases.Add(sample);
            currentV3 = sample;
            v3PreviousPhysicsX = player.Body.position.x;
            reactionAttack = -1;
            int damageBefore = report.damageEvents;
            uint sequenceBefore = player.Movement.AttackSequence;
            sample.forced = code == "A" ? boss.ForcePattern(BossPattern.ARockfall) : code == "B" ? boss.ForcePattern(BossPattern.BBurrowLeap) : boss.ForceCombo(code);
            string plan = boss.ComboCode;
            int facing = boss.Facing;
            bool sawFinalRecovery = false, sawFollowup = false, jumpedSweep = false, reachedInsideSlam = false;
            float lastPlayerX = player.Body.position.x;
            int finalAttack = -1, lastAttack = -1, followupDamageBefore = -1;
            string lastStage = "";
            float until = Time.realtimeSinceStartup + (code == "A" ? 18f : 16f);
            Action<DamageInfo> onHit = info => { if (boss.State == BossState.Recovery && !sawFollowup) sample.recoveryHits++; if (boss.RockfallActive) sample.rockfallBossHits++; if (boss.State == BossState.Link || (boss.ComboIndex > 0 && boss.State == BossState.Telegraph)) sample.comboRetaliationHits++; };
            boss.Health.DamageTaken += onHit;
            while (sample.forced && !player.IsDead && !boss.Health.IsDead && Time.realtimeSinceStartup < until)
            {
                if (boss.AttackId != lastAttack || boss.AttackStage != lastStage)
                {
                    sample.timeline.Add("t=" + (Time.realtimeSinceStartup - startedAt).ToString("F3") + " id=" + boss.AttackId + " combo=" + boss.ComboCode + " index=" + boss.ComboIndex + " " + boss.State + "/" + boss.AttackStage + " px=" + player.Body.position.x.ToString("F3") + " facing=" + boss.Facing);
                    lastAttack = boss.AttackId; lastStage = boss.AttackStage;
                }
                if (!sawFinalRecovery && code != "A" && code != "B")
                {
                    sample.directionChanged |= boss.Facing != facing;
                    sample.fakeRecovery |= boss.State == BossState.Recovery && boss.ComboIndex < boss.ComboLength - 1;
                    if (boss.ComboCode != plan) sample.fakeRecovery = true;
                }
                sample.sawLink |= boss.State == BossState.Link;
                sample.sawWave |= boss.Pattern == BossPattern.BBurrowLeap && boss.AttackStage == "wave";
                sample.sawRise |= boss.Pattern == BossPattern.BBurrowLeap && boss.transform.position.y > boss.FloorY + 1.5f;
                sample.sawLanding |= boss.Pattern == BossPattern.BBurrowLeap && boss.AttackStage == "land";
                sample.sawRockfall |= boss.RockfallActive;
                if (boss.RockfallActive)
                {
                    sample.basicDuringRockfall |= boss.Pattern == BossPattern.DT01Slam || boss.Pattern == BossPattern.DT02Sweep || boss.Pattern == BossPattern.DT03Bite;
                    sample.burrowDuringRockfall |= boss.Pattern == BossPattern.BBurrowLeap;
                    sample.rockfallDamageable &= boss.Health.DamageEnabled;
                }
                if (boss.Pattern == BossPattern.DT02Sweep && sample.jumps > 0 && player.Movement.Action == WarriorAction.JumpUp && !player.Movement.IsGrounded) jumpedSweep = true;
                if (jumpedSweep && boss.Pattern == BossPattern.DT01Slam)
                {
                    sample.inwardTravelAfterJump += Mathf.Max(0, (player.Body.position.x - lastPlayerX) * -boss.Facing);
                    if (boss.State == BossState.Active)
                        reachedInsideSlam |= boss.Facing < 0 ? player.HurtBounds.min.x > boss.WarningBounds.max.x : player.HurtBounds.max.x < boss.WarningBounds.min.x;
                }
                lastPlayerX = player.Body.position.x;
                if (boss.State == BossState.Recovery)
                {
                    if (!sawFinalRecovery) { sawFinalRecovery = true; finalAttack = boss.AttackId; sample.planIncomingHits = report.damageEvents - damageBefore; }
                    else if (code != "A" && sawFollowup && boss.AttackId != finalAttack) { sample.complete = true; break; }
                }
                if (sawFinalRecovery && boss.AttackId != finalAttack && !sawFollowup) { sawFollowup = true; followupDamageBefore = report.damageEvents; }
                if (code == "A" && sample.sawRockfall && !boss.RockfallActive) { sample.complete = true; break; }
                V3Input(true, reckless && !sawFinalRecovery);
                CaptureCombatFrames();
                yield return null;
            }
            boss.Health.DamageTaken -= onHit;
            player.SubmitInput(0, false, false, false);
            currentV3 = null;
            dashCancelProbe = false;
            sample.incomingHits = report.damageEvents - damageBefore;
            if (!sawFinalRecovery) sample.planIncomingHits = sample.incomingHits;
            sample.followupHits = followupDamageBefore < 0 ? 0 : report.damageEvents - followupDamageBefore;
            sample.acceptedAttacks = (int)(player.Movement.AttackSequence - sequenceBefore);
            sample.finalX = player.Body.position.x;
            Record(sample.name + "_executed", sample.forced && sample.complete && sample.physicsSteps > 0, "complete=" + sample.complete + " physicsSteps=" + sample.physicsSteps + " travel=" + sample.physicsTravel.ToString("F3") + " acceptedAttacks=" + sample.acceptedAttacks + " recoveryHits=" + sample.recoveryHits + " incomingHits=" + sample.incomingHits);
            if (dashCancel) Record(sample.name + "_cancel_input_observed", sample.cancelAttackAccepted && sample.cancelDashRequested && sample.cancelDashAccepted, "attackAccepted=" + sample.cancelAttackAccepted + " dashRequested=" + sample.cancelDashRequested + " dashAccepted=" + sample.cancelDashAccepted + " delay=" + sample.cancelDelay.ToString("F3") + " planHits=" + sample.planIncomingHits + " retaliationHits=" + sample.comboRetaliationHits + "; ordinary player cancellation is preserved; no universal retaliation-impossibility claim");
            if (!reckless) Record(sample.name + "_ordinary_input_evasion", sample.incomingHits == 0 && !player.IsDead, "reaction=.2s hits=" + sample.incomingHits + " jumps=" + sample.jumps + " actualBodyTravel=" + sample.physicsTravel.ToString("F3") + "; no state edits after force");
            if (code.Contains("-")) Record(sample.name + "_committed_combo", sample.sawLink && !sample.fakeRecovery && !sample.directionChanged, "Link=" + sample.sawLink + " fakeRecoveryOrChangedPlan=" + sample.fakeRecovery + " directionChanged=" + sample.directionChanged);
            if (code == "2-1") Record(sample.name + "_jump_then_inside", jumpedSweep && reachedInsideSlam && sample.incomingHits == 0 && sample.inwardTravelAfterJump > .1f, "reachedInsideSlam=" + reachedInsideSlam + " jumpedSweep=" + jumpedSweep + " inwardPhysicsTravel=" + sample.inwardTravelAfterJump.ToString("F3"));
            if (code == "B") Record(sample.name + "_burrow_leap_wave", sample.sawRise && sample.sawLanding && sample.sawWave == (phase == 2), "rise=" + sample.sawRise + " land=" + sample.sawLanding + " wave=" + sample.sawWave);
            if (code == "A") Record(sample.name + "_rockfall_and_basic_path", sample.sawRockfall && sample.basicDuringRockfall && !sample.burrowDuringRockfall && sample.rockfallDamageable && sample.rockfallBossHits > 0 && sample.physicsTravel > .3f, "rocks=" + sample.sawRockfall + " basics=" + sample.basicDuringRockfall + " B=" + sample.burrowDuringRockfall + " bossDamageable=" + sample.rockfallDamageable + " actualBossHitsDuringRocks=" + sample.rockfallBossHits + " physicsTravel=" + sample.physicsTravel.ToString("F3"));
            CaptureFrame(sample.name + "-complete");
        }

        private IEnumerator V3Natural()
        {
            bool ai = report.mode == "v3ai";
            float seconds;
            if (!float.TryParse(Argument("-bossqa-seconds", ai ? "120" : "180"), out seconds)) seconds = 120;
            int attackId = -1, recovered = -1;
            var choices = new Dictionary<string, int>();
            float until = Time.realtimeSinceStartup + seconds;
            reactionAttack = -1;
            while (!player.IsDead && !boss.Health.IsDead && Time.realtimeSinceStartup < until)
            {
                if (attackId != boss.AttackId && boss.AttackId > 0 && boss.ComboIndex == 0)
                {
                    attackId = boss.AttackId;
                    string key = "P" + boss.Phase + ":" + boss.ComboCode;
                    choices[key] = choices.TryGetValue(key, out int n) ? n + 1 : 1;
                    report.events.Add("AI choice t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " " + key + " id=" + attackId);
                }
                if (boss.State == BossState.Recovery && recovered != boss.AttackId) { recovered = boss.AttackId; report.completedAttacks++; }
                V3Input(true);
                CaptureCombatFrames();
                yield return null;
            }
            player.SubmitInput(0, false, false, false);
            foreach (KeyValuePair<string, int> pair in choices) report.events.Add("AI distribution " + pair.Key + "=" + pair.Value);
            Record("v3_natural_multiple_choices", choices.Count >= 3 && report.completedAttacks >= 6, "distinctPhasePlans=" + choices.Count + " completed=" + report.completedAttacks + "; empirical distribution only, finite run cannot prove randomness");
            Record("v3_natural_legal_patterns", report.attacks.TrueForAll(a => a.pattern != "DT04Inhale" && a.pattern != "DT05BodySlam" && a.pattern != "DT06Burrow" && (a.phase != 1 || a.pattern != "DT01Slam")), "every observed attack checked against V3 phase gates");
            bool victory = boss.Health.IsDead && !player.IsDead;
            if (!ai && report.mode == "victory") Record("v3_natural_victory", victory, "playerHP=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth + "; ordinary input, .2s recognition, no forced state or damage");
            if (report.mode == "survival") Record("v3_survival_alive", !player.IsDead, "playerHP=" + player.Health.CurrentHealth);
            if (ai) Record("v3_ai_observation_completed", victory || !player.IsDead, "outcome=" + (victory ? "victory" : player.IsDead ? "defeat" : "timeout") + "; bot success does not establish fun");
            if (victory) { yield return new WaitForSecondsRealtime(.95f); CaptureFrame("v3-natural-victory"); yield return null; yield return VerifyNaturalRetry("v3_victory"); }
            yield return Finish(victory ? "v3_natural_victory_retried" : player.IsDead ? "v3_natural_defeat" : "v3_natural_timeout");
        }

        private IEnumerator ObserveManualInput()
        {
            report.inputStrategy = "observation only: real keyboard remains enabled; pass covers runtime errors only, not manual playability";
            float seconds;
            if (!float.TryParse(Argument("-bossqa-seconds", "120"), out seconds)) seconds = 120;
            float until = Time.realtimeSinceStartup + Mathf.Max(1, seconds);
            int previousAttack = -1;
            string previousStage = "";
            float nextTimedCapture = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < until)
            {
                // Scene retry may destroy these observed objects. Rebind only; never submit input.
                if (player == null)
                {
                    player = FindFirstObjectByType<BossFightPlayerAdapter>();
                    if (player != null) player.Damaged += OnDamage;
                    boss = FindFirstObjectByType<DoCheolBossController>(); SyncBossDamageSubscription();
                }
                if (boss == null) boss = FindFirstObjectByType<DoCheolBossController>(); SyncBossDamageSubscription();
                if (boss != null && (boss.AttackId != previousAttack || boss.AttackStage != previousStage))
                {
                    previousAttack = boss.AttackId; previousStage = boss.AttackStage;
                    report.events.Add("observe t=" + (Time.realtimeSinceStartup-startedAt).ToString("F3") + " state=" + boss.State + " stage=" + boss.AttackStage + " combo=" + boss.ComboCode + " attackId=" + boss.AttackId + " playerHP=" + (player != null ? player.Health.CurrentHealth : -1) + " bossHP=" + boss.Health.CurrentHealth + " timeScale=" + Time.timeScale);
                }
                CaptureCombatFrames();
                if (Time.realtimeSinceStartup >= nextTimedCapture)
                {
                    CaptureFrame("manual-t" + Mathf.RoundToInt(Time.realtimeSinceStartup-startedAt));
                    nextTimedCapture += 10;
                }
                yield return null;
            }
            report.events.Add("Observe complete: no automatic StartFight, external input, submitted input, synthetic damage, forced selection, repositioning or gameplay-state changes. External keyboard timeline and visual review are separate evidence.");
            yield return Finish("manual_observation_complete");
        }
        private DoCheolBossController subscribedDamageBoss;
        [Serializable] public sealed class AcceptedDamageEvidence
        {
            public int attackId, testCase;
            public string kind;
            public float realtime;
        }
        private readonly Dictionary<int, int> acceptedDamageCounts = new Dictionary<int, int>();
        private void SyncBossDamageSubscription()
        {
            if (ReferenceEquals(subscribedDamageBoss, boss)) return;
            if (!ReferenceEquals(subscribedDamageBoss, null)) subscribedDamageBoss.PlayerDamagedByAttack -= OnAcceptedPlayerDamage;
            subscribedDamageBoss = boss;
            if (subscribedDamageBoss != null) subscribedDamageBoss.PlayerDamagedByAttack += OnAcceptedPlayerDamage;
        }
        private void OnAcceptedPlayerDamage(int attackId)
        {
            acceptedDamageCounts[attackId] = acceptedDamageCounts.TryGetValue(attackId, out int before) ? before + 1 : 1;
            string kind = "unresolved-id";
            if (attackEvidence.TryGetValue(attackId, out AttackEvidence sample))
            {
                sample.damageEvents++;
                kind = "boss:" + sample.pattern;
            }
            else if (boss != null && boss.AttackId == attackId)
            {
                GetAttackEvidence().damageEvents++;
                kind = "boss:" + boss.Pattern;
            }
            else
            {
                if (hazardEvidence.TryGetValue(attackId, out HazardEvidence rockSample))
                {
                    rockSample.damageEvents++;
                    kind = "rockfall";
                }
                else if (boss != null)
                    foreach (DocheolRockfall rock in boss.Rockfalls)
                        if (rock.AttackId == attackId) { kind = "rockfall"; break; }
            }
            // A fatal hit may clear the controller's hazard list before this event. Keep the exact
            // accepted ID even then; never guess a current pattern from a shared Source GameObject.
            report.acceptedDamage.Add(new AcceptedDamageEvidence { attackId = attackId, testCase = evidenceCase, kind = kind, realtime = Time.realtimeSinceStartup-startedAt });
        }

        private void RecordAcceptedDamageAssertions()
        {
            if (report.mode != "v3matrix" && report.mode != "v3ai" && report.mode != "victory" && report.mode != "survival" && report.mode != "defeat") return;
            bool unique = true;
            foreach (KeyValuePair<int, int> entry in acceptedDamageCounts) unique &= entry.Value <= 1;
            Record("v3_exact_attack_id_damage_attribution", report.acceptedDamage.Count == report.damageEvents,
                "accepted-ID events=" + report.acceptedDamage.Count + " player damage events=" + report.damageEvents + "; synthetic setup damages only boss, not player; zero hits is not dedup stress coverage");
            Record("v3_one_accepted_hit_per_attack_id", unique, "distinct IDs=" + acceptedDamageCounts.Count + "; includes B landing/both waves and each separate rock ID; only observed contacts are certified");
        }

        private IEnumerator Finish(string outcome)
        {
            if (finishing) yield break;
            finishing = true;
            report.outcome = outcome;
            if (!Application.isBatchMode) yield return new WaitForEndOfFrame();
            yield return null;
            if (outcome == "victory" || outcome == "defeat") yield return new WaitForSecondsRealtime(0.95f);
            CaptureFrame(outcome);
            yield return null;
            report.elapsedSeconds = Time.realtimeSinceStartup - startedAt;
            report.renderedFrames = Time.frameCount - firstFrame;
            report.averageFps = report.renderedFrames / Mathf.Max(0.01f, report.elapsedSeconds);
            RecordAttackPhysicsAssertions();
            RecordAcceptedDamageAssertions();
            Record("no_runtime_errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            report.passed = report.checks.TrueForAll(c => c.passed);
            string parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            Debug.Log("BOSS_QA_REPORT " + outputPath + " passed=" + report.passed);
            // CaptureFrame owns all screenshots: at most forty and one request per rendered frame.
            yield return new WaitForSecondsRealtime(0.4f);
            Application.Quit(report.passed ? 0 : 2);
        }
        private void OnDestroy()
        {
            if (!ReferenceEquals(subscribedDamageBoss, null)) subscribedDamageBoss.PlayerDamagedByAttack -= OnAcceptedPlayerDamage;
            Application.logMessageReceived -= CaptureError;
            if (player != null) player.Damaged -= OnDamage;
        }
    }
}
