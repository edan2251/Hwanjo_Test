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
        [Serializable] public sealed class Report
        {
            public string mode, utc, unityVersion, platform, outcome;
            public string specVersion = "armor-burrow-v2";
            public string inputStrategy;
            public int requestedFps, renderedFrames, damageEvents, completedAttacks;
            public float elapsedSeconds, averageFps;
            public bool passed, usesSyntheticDamage, usesForcedPatterns, usesTestRepositioning, usesModifiedInvulnerability;
            public List<Check> checks = new List<Check>();
            public List<AttackEvidence> attacks = new List<AttackEvidence>();
            public List<PunishEvidence> punishWindows = new List<PunishEvidence>();
            public List<string> events = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> screenshots = new List<string>();
        }
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
        private bool phaseOneGrounded = true, phaseTwoUnarmored = true, sawPhaseTwo;
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
            Time.timeScale = 1;
            report = new Report { mode = RequestedMode, utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(), requestedFps = fps, inputStrategy = RequestedMode == "victory" || RequestedMode == "survival" ? "reactive_walk_jump_nearest_body_v2" : RequestedMode,
                usesSyntheticDamage = RequestedMode == "contracts" || RequestedMode == "phases" || RequestedMode == "patterns" || RequestedMode == "punish" || RequestedMode == "punishsafe",
                usesForcedPatterns = RequestedMode == "patterns" || RequestedMode == "punish" || RequestedMode == "punishsafe", usesTestRepositioning = RequestedMode == "patterns" || RequestedMode == "punish" || RequestedMode == "punishsafe",
                usesModifiedInvulnerability = RequestedMode == "patterns" };
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
            boss = FindFirstObjectByType<DoCheolBossController>();
            if (report.mode != "intro")
                foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (item.GetType().Name == "BossFightSession") item.SendMessage("StartFight", SendMessageOptions.DontRequireReceiver);
            yield return null;
            player.SetExternalInput(true);
            player.Damaged += OnDamage;
            CaptureFrame("start");
            if (!Application.isBatchMode) yield return new WaitForEndOfFrame();
            yield return null;
            if (report.mode == "contracts") yield return Contracts();
            else if (report.mode == "patterns") yield return Patterns();
            else if (report.mode == "phases") yield return Phases();
            else if (report.mode == "punish" || report.mode == "punishsafe") yield return Punish();
            else if (report.mode == "defeat") yield return NaturalDefeat();
            else if (report.mode == "mash") yield return StationaryMash();
            else if (report.mode == "intro") yield return IntroFlow();
            else yield return FightBot();
        }
        private void CaptureFrame(string label)
        {
            if (Application.isBatchMode || string.IsNullOrEmpty(captureDirectory) || capturedFrames.Count >= 40 || lastCaptureFrame == Time.frameCount || !capturedFrames.Add(label)) return;
            lastCaptureFrame = Time.frameCount;
            Directory.CreateDirectory(captureDirectory);
            string path = Path.Combine(captureDirectory, capturedFrames.Count.ToString("D2") + "-" + label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            report.screenshots.Add(path);
        }

        private void CaptureCombatFrames()
        {
            if (boss == null) return;
            if (boss.State == BossState.Transition && boss.TransitionProgress >= .45f && boss.TransitionProgress <= .65f) CaptureFrame("phase-transition-mid");
            string key = "P" + boss.Phase + "-" + boss.Pattern;
            if (boss.State == BossState.Telegraph && boss.TargetLocked && boss.StateProgress > .6f) CaptureFrame(key + "-locked-tell");
            if (boss.State == BossState.Active && boss.StateProgress > .75f) CaptureFrame(key + "-active");
            if (boss.Pattern == BossPattern.DT05BodySlam && boss.State == BossState.Telegraph && boss.transform.position.y > boss.FloorY + 3f) CaptureFrame("P2-DT05-apex");
            if (boss.Pattern == BossPattern.DT06Burrow && boss.IsUnderground && boss.AttackStage == "travel") CaptureFrame("P2-DT06-underground-trace");
        }
        private void CaptureError(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                if (report.errors.Count < 30) report.errors.Add(condition + "\n" + stack);
        }
        private void OnDamage(DamageInfo info)
        {
            report.damageEvents++;
            if (boss != null && info.Source == boss.gameObject && boss.AttackId > 0) GetAttackEvidence().damageEvents++;
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
            ObserveBossPhysics();
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
            if (boss.Phase == 1 && attacking)
                phaseOneGrounded &= boss.transform.position.y <= boss.FloorY + .1f && boss.Pattern != BossPattern.DT05BodySlam && boss.Pattern != BossPattern.DT06Burrow;
            if (!attacking || boss.AttackId <= 0) return;
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
            if (boss.State == BossState.Active && boss.ActiveBounds.size.x > .001f)
            {
                sample.activePhysicsSteps++;
                sample.warningCoversActiveX &= sample.sawTelegraph && sample.sawLock && boss.ActiveBounds.min.x >= sample.warningLeft - .025f && boss.ActiveBounds.max.x <= sample.warningRight + .025f;
            }
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
                    boss = FindFirstObjectByType<DoCheolBossController>();
                    player.Damaged += OnDamage;
                    yield return new WaitForSecondsRealtime(0.35f);
                    BossFightSession newSession = FindSession() as BossFightSession;
                    Record("retry_preserves_mute", mutedBeforeRetry && newSession != null && newSession.Muted,
                        "beforeRetry=" + mutedBeforeRetry + " afterRetry=" + (newSession != null && newSession.Muted));
                    if (newSession != null && newSession.Muted != originallyMuted) newSession.ToggleMute();
                    Record("retry_restores_health", !player.IsDead && player.Health.CurrentHealth == player.Health.MaxHealth, "HP=" + player.Health.CurrentHealth);
                    Record("retry_restores_velocity", Mathf.Abs(player.Body.linearVelocity.x) < 0.01f, "velocity=" + player.Body.linearVelocity);
                    Record("retry_restores_timescale", Mathf.Approximately(Time.timeScale, 1), "timeScale=" + Time.timeScale);
                    Record("retry_clears_attack_state", boss != null && boss.State == BossState.Dormant && boss.Pattern == BossPattern.None, "boss=" + (boss != null ? boss.State + "/" + boss.Pattern : "missing"));
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

        private IEnumerator Phases()
        {
            if (boss == null) { Record("phase_setup", false, "Boss missing"); yield return Finish("setup_failed"); yield break; }
            Record("initial_four_armor", boss.Phase == 1 && boss.ArmorMask == 15 && boss.ArmorCount == 4, "mask=" + boss.ArmorMask + " count=" + boss.ArmorCount);
            Record("p1_rejects_body_slam_and_burrow", !boss.ForcePattern(BossPattern.DT05BodySlam) && !boss.ForcePattern(BossPattern.DT06Burrow), "phase=" + boss.Phase);
            CaptureFrame("armor-4-full-controlled");
            yield return new WaitForEndOfFrame();
            yield return null;
            // Deliberately synthetic health thresholds; no claim of an unassisted playthrough.
            boss.Health.TakeDamage(new DamageInfo(boss.Health.CurrentHealth - 30, boss.GetArmorWorldPoint(1), Vector2.zero, gameObject));
            yield return Drive(.18f);
            Record("armor_break_at_75_percent", boss.Health.CurrentHealth == 30 && boss.ArmorCount == 3 && boss.Phase == 1 && (boss.ArmorMask & (1 << 1)) == 0 && boss.LastHitRegion == 1, "HP=" + boss.Health.CurrentHealth + " mask=" + boss.ArmorMask);
            CaptureFrame("armor-3-hp75-controlled");
            yield return new WaitForEndOfFrame();
            yield return null;
            boss.Health.TakeDamage(new DamageInfo(6, boss.GetArmorWorldPoint(3), Vector2.zero, gameObject));
            yield return Drive(.18f);
            Record("armor_break_at_60_percent", boss.Health.CurrentHealth == 24 && boss.ArmorCount == 2 && boss.Phase == 1 && (boss.ArmorMask & (1 << 3)) == 0 && boss.LastHitRegion == 3, "HP=" + boss.Health.CurrentHealth + " mask=" + boss.ArmorMask);
            CaptureFrame("armor-2-hp60-controlled");
            yield return new WaitForEndOfFrame();
            yield return null;
            boss.Health.TakeDamage(new DamageInfo(4, boss.GetArmorWorldPoint(0), Vector2.zero, gameObject));
            bool immediateProtection = boss.State == BossState.Transition && !boss.Health.DamageEnabled && !player.Health.DamageEnabled;
            int playerHp = player.Health.CurrentHealth, bossHp = boss.Health.CurrentHealth;
            float transitionStart = Time.time;
            bool transitionSafe = true, sawArmorZero = false;
            int syntheticId = 910000;
            float deadline = Time.realtimeSinceStartup + 6f;
            while (boss.State == BossState.Transition && Time.realtimeSinceStartup < deadline)
            {
                boss.Health.TakeDamage(new DamageInfo(1, boss.transform.position, Vector2.zero, gameObject));
                player.ReceiveAttack(++syntheticId, 1, Vector2.zero, gameObject);
                transitionSafe &= boss.Health.CurrentHealth == bossHp && player.Health.CurrentHealth == playerHp && boss.ActiveBounds.size.x < .001f;
                if (boss.TransitionProgress >= .45f) sawArmorZero |= boss.ArmorCount == 0;
                player.SubmitInput(0, false, false, false);
                CaptureFrame("armor-transition");
                yield return null;
            }
            Record("transition_immediately_protects_both", immediateProtection, "state after 50% threshold protected=" + immediateProtection);
            Record("transition_rejects_all_damage", transitionSafe && bossHp == 20 && player.Health.CurrentHealth == playerHp && boss.Health.CurrentHealth == bossHp, "bossHP=" + boss.Health.CurrentHealth + " playerHP=" + player.Health.CurrentHealth);
            Record("phase2_has_zero_armor", sawArmorZero && boss.Phase == 2 && boss.ArmorCount == 0 && phaseTwoUnarmored, "phase=" + boss.Phase + " mask=" + boss.ArmorMask);
            Record("transition_returns_to_first_tell", boss.State == BossState.Telegraph && player.Health.DamageEnabled && boss.TargetLocked, "state=" + boss.State + " pattern=" + boss.Pattern + " transitionScaledSeconds=" + (Time.time - transitionStart).ToString("F3"));
            yield return null;
            CaptureFrame("armor-0-phase2-controlled");
            yield return new WaitForEndOfFrame();
            yield return null;
            yield return Finish("phases_complete");
        }

        private IEnumerator Patterns()
        {
            if (boss == null) { Record("boss_available", false, "No boss controller"); yield return Finish("setup_failed"); yield break; }
            for (int phase = 1; phase <= 2; phase++)
            {
                BossPattern[] patterns = phase == 1
                    ? new[] { BossPattern.DT01Slam, BossPattern.DT02Sweep, BossPattern.DT03Bite }
                    : new[] { BossPattern.DT01Slam, BossPattern.DT02Sweep, BossPattern.DT03Bite, BossPattern.DT05BodySlam, BossPattern.DT06Burrow };
                foreach (BossPattern pattern in patterns)
                {
                    yield return PrepareControlledCase(phase, 3.2f, 0);
                    player.Health.ConfigureInvulnerability(0);
                    bool forced = boss.Phase == phase && boss.ForcePattern(pattern);
                    float deadline = Time.realtimeSinceStartup + 6f;
                    while (!boss.TargetLocked && boss.State == BossState.Telegraph && Time.realtimeSinceStartup < deadline) { CaptureCombatFrames(); yield return null; }
                    int id = boss.AttackId;
                    int healthBefore = player.Health.CurrentHealth;
                    int damageBefore = report.damageEvents;
                    player.ResetForFight(new Vector2(boss.WarningBounds.center.x, boss.FloorY + .025f));
                    deadline = Time.realtimeSinceStartup + 6f;
                    while (boss.State != BossState.Recovery && !player.IsDead && Time.realtimeSinceStartup < deadline)
                    {
                        player.SubmitInput(0, false, false, false);
                        CaptureCombatFrames();
                        yield return null;
                    }
                    string key = "P" + phase + "_" + pattern;
                    AttackEvidence sample = attackEvidence.TryGetValue(id, out AttackEvidence observed) ? observed : null;
                    Record(key + "_cycle", forced && boss.State == BossState.Recovery, "phase=" + boss.Phase + " state=" + boss.State);
                    Record(key + "_lock_and_warning", sample != null && sample.sawLock && sample.targetStable && sample.warningCoversActiveX && sample.activePhysicsSteps > 0,
                        "attackId=" + id + " activeSteps=" + (sample != null ? sample.activePhysicsSteps : 0) + " stable=" + (sample != null && sample.targetStable) + " warningCoversActiveX=" + (sample != null && sample.warningCoversActiveX));
                    Record(key + "_single_damage_without_iframe", report.damageEvents - damageBefore == 1 && player.Health.CurrentHealth == healthBefore - 1 && sample != null && sample.damageEvents == 1,
                        "damageEvents=" + (report.damageEvents - damageBefore) + " HP=" + player.Health.CurrentHealth);
                    if (pattern == BossPattern.DT05BodySlam)
                        Record("DT05_whole_body_rises_and_lands", sample != null && sample.maximumBodyY - boss.FloorY > 3 && Mathf.Abs(boss.transform.position.y - boss.FloorY) < .05f,
                            "peak=" + (sample != null ? sample.maximumBodyY : 0).ToString("F2") + " landing=" + boss.transform.position.y.ToString("F2"));
                    float recoveryX = boss.transform.position.x;
                    int recoveryFacing = boss.Facing;
                    float window = boss.RecoveryRemaining;
                    float expected = pattern == BossPattern.DT05BodySlam || pattern == BossPattern.DT06Burrow ? 1.25f : pattern == BossPattern.DT03Bite ? .95f : .85f;
                    player.ResetForFight(new Vector2(Mathf.Clamp(recoveryX - recoveryFacing * 4, boss.ArenaLeft + .5f, boss.ArenaRight - .5f), boss.FloorY + .025f));
                    yield return Drive(.2f);
                    Record(key + "_recovery_holds", boss.State == BossState.Recovery && Mathf.Abs(recoveryX - boss.transform.position.x) < .01f && recoveryFacing == boss.Facing,
                        "position/facing held for .2s, initial remaining=" + window.ToString("F3"));
                    Record(key + "_v2_recovery_window", window >= expected - .08f && window <= expected + .02f, "expected=" + expected.ToString("F2") + " measuredRemaining=" + window.ToString("F3"));
                }
                foreach (BossPattern pattern in patterns)
                    foreach (int side in new[] { -1, 1 })
                        yield return WallEscape(phase, pattern, side, pattern == BossPattern.DT06Burrow ? 2f : .7f);
            }
            foreach (int side in new[] { -1, 1 }) yield return WallEscape(2, BossPattern.DT06Burrow, side, .7f, false);
            Record("p1_never_leaps", phaseOneGrounded, "all P1 attack physics samples grounded and no DT05/DT06");
            Record("p2_never_has_armor", sawPhaseTwo && phaseTwoUnarmored, "all sampled P2 armor counts zero");
            boss.SetFightActive(false);
            player.Health.ConfigureInvulnerability(.8f);
            yield return Finish("patterns_v2_complete");
        }

        private IEnumerator WallEscape(int phase, BossPattern pattern, int side, float inset, bool requireDanger = true)
        {
            float wallX = side < 0 ? boss.ArenaLeft + inset : boss.ArenaRight - inset;
            yield return PrepareControlledCase(phase, wallX - side * 3.3f, wallX);
            bool forced = boss.Phase == phase && boss.ForcePattern(pattern);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!boss.TargetLocked && boss.State == BossState.Telegraph && Time.realtimeSinceStartup < deadline) { player.SubmitInput(0, false, false, false); yield return null; }
            int id = boss.AttackId;
            int damageBefore = report.damageEvents;
            yield return Drive(.2f); // All motion after target lock is ordinary input; never teleport/reset here.
            Bounds initialHurt = player.HurtBounds;
            Bounds warning = boss.WarningBounds;
            bool startsInDanger = initialHurt.max.x > warning.min.x && initialHurt.min.x < warning.max.x;
            bool jumpIssued = false;
            float peakFeet = initialHurt.min.y;
            float walkingDistance = 0;
            float previousX = player.Body.position.x;
            deadline = Time.realtimeSinceStartup + 6f;
            while (boss.State != BossState.Recovery && !player.IsDead && Time.realtimeSinceStartup < deadline)
            {
                float horizontal = 0;
                bool jump = false;
                if (pattern == BossPattern.DT02Sweep)
                {
                    jump = !jumpIssued && ShouldJumpSweep();
                    if (jump) jumpIssued = true;
                }
                else
                {
                    Bounds hurt = player.HurtBounds;
                    bool overlaps = hurt.max.x > warning.min.x - .08f && hurt.min.x < warning.max.x + .08f;
                    if (overlaps)
                    {
                        float left = warning.min.x - hurt.extents.x - .13f;
                        float right = warning.max.x + hurt.extents.x + .13f;
                        bool leftValid = left >= boss.ArenaLeft + hurt.extents.x + .03f;
                        bool rightValid = right <= boss.ArenaRight - hurt.extents.x - .03f;
                        float px = hurt.center.x;
                        float exit = leftValid && (!rightValid || Mathf.Abs(px - left) < Mathf.Abs(px - right)) ? left : right;
                        horizontal = Mathf.Sign(exit - px);
                    }
                }
                player.SubmitInput(horizontal, jump, false, false);
                walkingDistance += Mathf.Abs(player.Body.position.x - previousX);
                previousX = player.Body.position.x;
                peakFeet = Mathf.Max(peakFeet, player.HurtBounds.min.y);
                yield return null;
            }
            AttackEvidence sample = attackEvidence.TryGetValue(id, out AttackEvidence observed) ? observed : null;
            bool avoided = forced && (startsInDanger == requireDanger) && boss.State == BossState.Recovery && report.damageEvents == damageBefore && sample != null && sample.targetStable;
            if (pattern == BossPattern.DT02Sweep) avoided &= jumpIssued && peakFeet - initialHurt.min.y > .8f && Mathf.Abs(player.transform.position.x - wallX) < .15f;
            else avoided &= (requireDanger ? walkingDistance > .2f : walkingDistance < .15f);
            Record("P" + phase + "_" + pattern + (requireDanger ? "_wall_escape_" : "_extreme_wall_safe_") + side, avoided,
                "startedInDanger=" + startsInDanger + " damageEvents=" + (report.damageEvents - damageBefore) + " method=" + (pattern == BossPattern.DT02Sweep ? "timed jump" : "walk to reachable edge") + " targetStable=" + (sample != null && sample.targetStable) + " walked=" + walkingDistance.ToString("F2") + " tell=" + boss.LockedTellDuration.ToString("F2") + " startX=" + wallX.ToString("F2") + " finalX=" + player.transform.position.x.ToString("F2"));
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

        private bool ShouldJumpSweep()
        {
            if (!player.Movement.IsGrounded || boss.Pattern != BossPattern.DT02Sweep) return false;
            Bounds hurt = player.HurtBounds;
            Bounds warning = boss.WarningBounds;
            if (hurt.max.x <= warning.min.x || hurt.min.x >= warning.max.x) return false;
            float from = boss.transform.position.x + boss.Facing * .8f;
            float speed = Mathf.Abs(boss.TargetX - from) / .66f;
            float waveX = boss.State == BossState.Active ? Mathf.Lerp(from, boss.TargetX, boss.StateProgress) : from;
            float ahead = boss.Facing * (hurt.center.x - waveX);
            float halfWidth = hurt.extents.x + .45f;
            if (ahead < -halfWidth) return false; // The wave has already passed this position.
            float eta = Mathf.Max(0, ahead - halfWidth) / Mathf.Max(.01f, speed);
            if (boss.State == BossState.Telegraph) eta += (boss.Phase == 1 ? 1f : .9f) * (1f - boss.StateProgress);
            // .16 seconds gives Update/Fixed input latency plus time to lift feet above .42u.
            // Predict from the visible wave path even on the first active frame (bounds may be empty).
            return eta <= .16f;
        }

        private IEnumerator Punish()
        {
            if (boss == null) { Record("punish_setup", false, "Boss missing"); yield return Finish("setup_failed"); yield break; }
            foreach (BossPattern pattern in new[] { BossPattern.DT01Slam, BossPattern.DT02Sweep, BossPattern.DT03Bite })
                yield return MeasurePunishWindow(1, pattern);
            foreach (BossPattern pattern in new[] { BossPattern.DT05BodySlam, BossPattern.DT06Burrow })
                yield return MeasurePunishWindow(2, pattern);
            if (report.mode == "punish")
                foreach (string region in new[] { "front", "body", "rear" }) yield return ProbeBodyAttack(region);
            boss.SetFightActive(false);
            yield return Finish(report.mode == "punishsafe" ? "safe_punish_measured" : "punish_measured");
        }

        private IEnumerator MeasurePunishWindow(int phase, BossPattern pattern)
        {
            // Controlled setup ends after ForcePattern. Until recovery ends, only SubmitInput
            // changes play: no health, position, invulnerability, pattern or timer edits.
            yield return PrepareControlledCase(phase, 2.8f, -.4f);
            bool forced = boss.Phase == phase && boss.ForcePattern(pattern);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!boss.TargetLocked && boss.State == BossState.Telegraph && Time.realtimeSinceStartup < deadline)
            {
                player.SubmitInput(0, false, false, false);
                CaptureCombatFrames();
                yield return null;
            }
            Bounds warning = boss.WarningBounds;
            PunishEvidence sample = new PunishEvidence { testCase = evidenceCase, phase = phase, pattern = pattern.ToString(), attackId = boss.AttackId,
                lockPlayerX = player.Body.position.x, startedInDanger = player.HurtBounds.max.x > warning.min.x && player.HurtBounds.min.x < warning.max.x,
                limitedHits = report.mode == "punishsafe", intendedHits = phase == 1 ? 1 : 2 };
            if (sample.limitedHits) sample.approachMethod = "walk return; stop at target hits; react to next natural tell";
            report.punishWindows.Add(sample);
            int damageBefore = report.damageEvents;
            yield return Drive(.2f);
            bool jumpIssued = false;
            float previousX = player.Body.position.x;
            deadline = Time.realtimeSinceStartup + 8f;
            while (boss.State != BossState.Recovery && !player.IsDead && Time.realtimeSinceStartup < deadline)
            {
                bool jump = pattern == BossPattern.DT02Sweep && !jumpIssued && ShouldJumpSweep();
                if (jump) jumpIssued = true;
                float horizontal = pattern == BossPattern.DT02Sweep ? 0 : WalkOutOfWarning(warning);
                player.SubmitInput(horizontal, jump, false, false);
                sample.outwardDistance += Mathf.Abs(player.Body.position.x - previousX);
                previousX = player.Body.position.x;
                CaptureCombatFrames();
                yield return null;
            }
            sample.evaded = report.damageEvents == damageBefore && !player.IsDead;
            sample.observedRecovery = boss.State == BossState.Recovery && boss.AttackId == sample.attackId;
            sample.recoveryPlayerX = player.Body.position.x;
            uint sequenceBefore = player.Movement.AttackSequence;
            Collider2D shape = BossHurtShape();
            float recoveryStart = Time.time;
            if (sample.observedRecovery)
            {
                float nominal = pattern == BossPattern.DT05BodySlam || pattern == BossPattern.DT06Burrow ? 1.25f : pattern == BossPattern.DT03Bite ? .95f : .85f;
                recoveryStart -= nominal - boss.RecoveryRemaining;
                sample.recoveryDuration = nominal;
                if (shape != null) sample.recoveryInitialGap = Mathf.Max(shape.bounds.min.x - player.Body.position.x, player.Body.position.x - shape.bounds.max.x, 0f);
            }
            Action<DamageInfo> observeHit = info =>
            {
                if (boss.State != BossState.Recovery || boss.AttackId != sample.attackId) return;
                sample.recoveryHits++;
                sample.recoveryDamage += info.Amount;
                if (sample.firstHitLatency < 0) sample.firstHitLatency = Time.time - recoveryStart;
            };
            boss.Health.DamageTaken += observeHit;
            previousX = player.Body.position.x;
            float reach = SwordReach();
            deadline = Time.realtimeSinceStartup + 6f;
            while (boss.State == BossState.Recovery && boss.AttackId == sample.attackId && !player.IsDead && Time.realtimeSinceStartup < deadline)
            {
                float horizontal = 0;
                bool attack = false;
                if (shape != null && shape.enabled)
                {
                    Bounds target = shape.bounds;
                    float px = player.Body.position.x;
                    float gap = Mathf.Max(target.min.x - px, px - target.max.x, 0f);
                    float face = px < target.center.x ? 1 : -1;
                    if (gap > reach - .18f) horizontal = face;
                    else { horizontal = face * .05f; attack = true; }
                }
                if (sample.limitedHits && sample.recoveryHits >= sample.intendedHits) { horizontal = 0; attack = false; }
                player.SubmitInput(horizontal, false, attack, false);
                sample.returnDistance += Mathf.Abs(player.Body.position.x - previousX);
                previousX = player.Body.position.x;
                yield return null;
            }
            boss.Health.DamageTaken -= observeHit;
            player.SubmitInput(0, false, false, false);
            sample.completedRecovery = sample.observedRecovery && boss.State != BossState.Recovery && !player.IsDead;
            sample.acceptedPlayerAttacks = (int)(player.Movement.AttackSequence - sequenceBefore);
            sample.incomingHits = report.damageEvents - damageBefore;
            sample.finalPlayerX = player.Body.position.x;
            // Completion/avoidance are functional assertions; hit counts are tuning evidence only.
            Record("P" + phase + "_" + pattern + "_punish_observed", forced && sample.startedInDanger && sample.observedRecovery && sample.completedRecovery && shape != null,
                "evaded=" + sample.evaded + " recoveryHits=" + sample.recoveryHits + " damage=" + sample.recoveryDamage + " firstHitLatency=" + sample.firstHitLatency.ToString("F3") + "s initialGap=" + sample.recoveryInitialGap.ToString("F3") + " returnDistance=" + sample.returnDistance.ToString("F3"));
            Record("P" + phase + "_" + pattern + "_punish_evaded", sample.evaded && sample.incomingHits == 0, "ordinary input incomingHits=" + sample.incomingHits);
            CaptureFrame("punish-P" + phase + "-" + pattern + "-end");
            if (sample.limitedHits)
            {
                Record("P" + phase + "_" + pattern + "_safe_hit_target", sample.recoveryHits == sample.intendedHits && sample.acceptedPlayerAttacks == sample.intendedHits, "acceptedHits=" + sample.recoveryHits + " acceptedAttacks=" + sample.acceptedPlayerAttacks + " intended=" + sample.intendedHits);
                yield return ObserveNaturalFollowup(sample);
            }
        }

        private IEnumerator ObserveNaturalFollowup(PunishEvidence sample)
        {
            // The preceding forced attack has ended. Let the production AI select and execute
            // its next attack. No ForcePattern, reset, health edits, or boss enable/disable here.
            int damageBefore = report.damageEvents;
            float deadline = Time.realtimeSinceStartup + 20f;
            float lockedAt = -1f;
            while (!player.IsDead && Time.realtimeSinceStartup < deadline)
            {
                float horizontal = 0;
                bool jump = false;
                if (boss.AttackId != sample.attackId && boss.AttackId > 0)
                {
                    if (sample.followupAttackId == 0)
                    {
                        sample.followupAttackId = boss.AttackId;
                        sample.followupPattern = boss.Pattern.ToString();
                    }
                    if (boss.AttackId != sample.followupAttackId) break;
                    sample.followupTelegraphObserved |= boss.State == BossState.Telegraph;
                    sample.followupActiveObserved |= boss.State == BossState.Active;
                    if (boss.State == BossState.Recovery)
                    {
                        sample.followupCompleted = sample.followupActiveObserved;
                        break;
                    }
                    if (boss.TargetLocked && (boss.State == BossState.Telegraph || boss.State == BossState.Active))
                    {
                        if (lockedAt < 0) lockedAt = Time.time;
                        if (Time.time - lockedAt >= .2f)
                        {
                            if (boss.Pattern == BossPattern.DT02Sweep) jump = ShouldJumpSweep();
                            else horizontal = WalkOutOfWarning(boss.WarningBounds);
                        }
                    }
                }
                player.SubmitInput(horizontal, jump, false, false);
                CaptureCombatFrames();
                yield return null;
            }
            player.SubmitInput(0, false, false, false);
            sample.followupIncomingHits = report.damageEvents - damageBefore;
            sample.followupEvaded = sample.followupCompleted && sample.followupIncomingHits == 0 && !player.IsDead;
            Record("P" + sample.phase + "_" + sample.pattern + "_safe_natural_followup",
                sample.followupTelegraphObserved && sample.followupActiveObserved && sample.followupEvaded,
                "natural=" + sample.followupPattern + " attackId=" + sample.followupAttackId + " telegraph=" + sample.followupTelegraphObserved + " active=" + sample.followupActiveObserved + " completed=" + sample.followupCompleted + " incomingHits=" + sample.followupIncomingHits + " reactionDelay=.2s no attack input after target");
            CaptureFrame("safe-P" + sample.phase + "-" + sample.pattern + "-after-" + sample.followupPattern);
        }

        private IEnumerator ProbeBodyAttack(string region)
        {
            // Separate controlled placement probe, not part of the escape/return measurement.
            yield return PrepareControlledCase(1, 2.8f, -4.5f);
            bool forced = boss.ForcePattern(BossPattern.DT01Slam);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (boss.State != BossState.Recovery && Time.realtimeSinceStartup < deadline) { player.SubmitInput(0, false, false, false); yield return null; }
            Collider2D shape = BossHurtShape();
            Hurtbox2D[] receivers = boss.GetComponentsInChildren<Hurtbox2D>(true);
            if (shape == null) { Record("body_" + region + "_single_attack", false, "Hurt shape missing"); yield break; }
            Bounds target = shape.bounds;
            float x = region == "body" ? target.center.x : region == "front" ? (boss.Facing < 0 ? target.min.x - .65f : target.max.x + .65f) : (boss.Facing < 0 ? target.max.x + .65f : target.min.x - .65f);
            int facing = x < target.center.x ? 1 : -1;
            player.ResetForFight(new Vector2(x, boss.FloorY + .025f), facing);
            yield return Drive(.06f); // Settle grounded state before the single normal attack.
            player.SetExternalInput(true);
            int hp = boss.Health.CurrentHealth;
            uint sequence = player.Movement.AttackSequence;
            int acceptedHits = 0;
            Action<DamageInfo> observeHit = info => acceptedHits++;
            boss.Health.DamageTaken += observeHit;
            yield return Drive(.4f, 0, false, false, true);
            boss.Health.DamageTaken -= observeHit;
            int acceptedAttacks = (int)(player.Movement.AttackSequence - sequence);
            Record("body_" + region + "_single_attack", forced && receivers.Length == 1 && acceptedAttacks == 1 && acceptedHits == 1 && boss.Health.CurrentHealth == hp - 1,
                "receivers=" + receivers.Length + " acceptedAttacks=" + acceptedAttacks + " acceptedHits=" + acceptedHits + " HPdelta=" + (hp - boss.Health.CurrentHealth) + " playerX=" + x.ToString("F3"));
            CaptureFrame("body-" + region + "-single-attack");
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
            boss = FindFirstObjectByType<DoCheolBossController>();
            encounter = FindSession() as BossFightSession;
            player.Damaged += OnDamage;
            player.SetExternalInput(true);
            yield return Drive(0.25f);
            Record(prefix + "_retry_health", !player.IsDead && player.Health.CurrentHealth == 8 && boss != null && boss.Health.CurrentHealth == 40,
                "playerHP=" + player.Health.CurrentHealth + " bossHP=" + (boss != null ? boss.Health.CurrentHealth : -1));
            Record(prefix + "_retry_state", encounter != null && encounter.State == EncounterState.Fighting && Mathf.Approximately(Time.timeScale, 1) && boss != null && boss.Pattern == BossPattern.None && boss.AttackId == 0 && boss.State == BossState.Idle && player.FightActive && boss.FightActive,
                "session=" + (encounter != null ? encounter.State.ToString() : "missing") + " timescale=" + Time.timeScale + " boss=" + (boss != null ? boss.State + "/" + boss.Pattern + " id=" + boss.AttackId : "missing"));
        }
        private IEnumerator FightBot()
        {
            if (boss == null) { Record("boss_available", false, "No boss controller"); yield return Finish("setup_failed"); yield break; }
            player.SetFightActive(true);
            boss.SetFightActive(true);
            Hurtbox2D bossHurtbox = boss.GetComponentInChildren<Hurtbox2D>();
            Collider2D bossHurtCollider = bossHurtbox != null ? bossHurtbox.GetComponent<Collider2D>() : null;
            float swordReach = SwordReach();
            int lastAttack = -1;
            float lockedTarget = 0;
            int observedActive = -1;
            bool targetStable = true;
            float timeout = report.mode == "survival" ? 75 : 180;
            float endAt = Time.time + timeout;
            while (!player.IsDead && !boss.Health.IsDead && Time.time < endAt)
            {
                Bounds hurt = player.HurtBounds;
                Bounds warning = boss.WarningBounds;
                float px = hurt.center.x;
                float bx = boss.transform.position.x;
                float horizontal = 0;
                bool jump = false, attack = false, dash = false;
                bool danger = boss.State == BossState.Telegraph || boss.State == BossState.Active;
                CaptureCombatFrames();
                if (danger && boss.TargetLocked)
                {
                    if (boss.Pattern == BossPattern.DT02Sweep)
                    {
                        jump = ShouldJumpSweep();
                        horizontal = 0;
                    }
                    else horizontal = WalkOutOfWarning(warning);
                }
                else if (boss.State == BossState.Recovery || boss.State == BossState.Idle)
                {
                    // Use the nearest actual body hit range instead of returning to a fixed
                    // front-side point; walking past valid range wastes the shortened recovery.
                    if (bossHurtCollider != null && bossHurtCollider.enabled)
                    {
                        Bounds target = bossHurtCollider.bounds;
                        float gap = Mathf.Max(target.min.x - px, px - target.max.x, 0f);
                        float face = px < target.center.x ? 1 : -1;
                        if (gap > swordReach - .18f) horizontal = face;
                        else { horizontal = face * .05f; attack = true; }
                    }
                }
                player.SubmitInput(horizontal, jump, attack, dash);
                if (boss.State == BossState.Active)
                {
                    if (observedActive != boss.AttackId) { observedActive = boss.AttackId; lockedTarget = boss.TargetX; }
                    else if (Mathf.Abs(lockedTarget - boss.TargetX) > 0.01f) targetStable = false;
                }
                if (boss.State == BossState.Recovery && lastAttack != boss.AttackId)
                {
                    lastAttack = boss.AttackId;
                    report.completedAttacks++;
                    report.events.Add("recovery attack=" + lastAttack + " pattern=" + boss.Pattern + " hp=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth);
                }
                yield return null;
            }
            player.SubmitInput(0, false, false, false);
            Record("target_stable_during_active", targetStable && report.attacks.TrueForAll(a => a.targetStable), "completed attacks=" + report.completedAttacks);
            Record("p1_grounded_patterns_only", phaseOneGrounded, "P1 physical samples contain no leap/body slam/burrow");
            Record("p2_zero_armor_when_observed", phaseTwoUnarmored && (report.mode != "victory" || sawPhaseTwo), "sawPhaseTwo=" + sawPhaseTwo);
            Record("natural_warning_and_single_hit", report.attacks.TrueForAll(a => a.damageEvents <= 1 && a.warningCoversActiveX), "per-attack data in attacks array");
            Record("observed_attack_loop", report.completedAttacks >= 3, "completed attacks=" + report.completedAttacks);
            bool victory = boss.Health.IsDead;
            if (report.mode == "victory") Record("bot_victory_without_cheats", victory && !player.IsDead, "playerHP=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth);
            else Record("bot_survives_without_cheats", !player.IsDead, "playerHP=" + player.Health.CurrentHealth + " bossHP=" + boss.Health.CurrentHealth);
            if (report.mode == "victory" && victory && !player.IsDead)
            {
                yield return new WaitForSecondsRealtime(0.95f);
                CaptureFrame("victory");
                yield return null;
                yield return VerifyNaturalRetry("victory");
                yield return Finish("victory_retried");
            }
            else yield return Finish(victory ? "victory" : player.IsDead ? "defeat" : "timeout");
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
            Record("no_runtime_errors", report.errors.Count == 0, "errors=" + report.errors.Count);
            report.passed = report.checks.TrueForAll(c => c.passed);
            string parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
            Debug.Log("BOSS_QA_REPORT " + outputPath + " passed=" + report.passed);
            if (!Application.isBatchMode) ScreenCapture.CaptureScreenshot(Path.ChangeExtension(outputPath, ".png"));
            yield return new WaitForSecondsRealtime(0.4f);
            Application.Quit(report.passed ? 0 : 2);
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= CaptureError;
            if (player != null) player.Damaged -= OnDamage;
        }
    }
}
