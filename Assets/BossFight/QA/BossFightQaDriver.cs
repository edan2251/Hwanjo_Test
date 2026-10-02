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
        [Serializable] public sealed class Report
        {
            public string mode, utc, unityVersion, platform, outcome;
            public int requestedFps, renderedFrames, damageEvents, completedAttacks;
            public float elapsedSeconds, averageFps;
            public bool passed, usesSyntheticDamage, usesForcedPatterns;
            public List<Check> checks = new List<Check>();
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
        private bool dashProbeSampling, dashProbePreviousStep, dashProbePreviousAction, dashProbeComplete;
        private float dashProbePreviousX, dashProbeDistance;
        private int dashProbeTicks;
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
                platform = Application.platform.ToString(), requestedFps = fps, usesSyntheticDamage = RequestedMode == "contracts", usesForcedPatterns = RequestedMode == "patterns" };
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
            foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (item.GetType().Name == "BossFightSession") item.SendMessage("StartFight", SendMessageOptions.DontRequireReceiver);
            yield return null;
            player.SetExternalInput(true);
            player.Damaged += OnDamage;
            CaptureFrame("start");
            if (report.mode == "contracts") yield return Contracts();
            else if (report.mode == "patterns") yield return Patterns();
            else if (report.mode == "defeat") yield return NaturalDefeat();
            else yield return FightBot();
        }
        private void CaptureFrame(string label)
        {
            if (Application.isBatchMode || string.IsNullOrEmpty(captureDirectory) || capturedFrames.Count >= 20 || !capturedFrames.Add(label)) return;
            Directory.CreateDirectory(captureDirectory);
            string path = Path.Combine(captureDirectory, capturedFrames.Count.ToString("D2") + "-" + label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            report.screenshots.Add(path);
        }

        private void CaptureCombatFrames()
        {
            if (boss == null) return;
            if (boss.State == BossState.Telegraph && boss.StateProgress > 0.6f) CaptureFrame(boss.Pattern + "-telegraph");
            if (boss.State == BossState.Active && boss.StateProgress > 0.75f) CaptureFrame(boss.Pattern + "-active");
            if (boss.State == BossState.Recovery && boss.StateProgress > 0.12f) CaptureFrame(boss.Pattern + "-recovery");
            if (boss.Pattern == BossPattern.DT05BodySlam && boss.State == BossState.Telegraph && boss.transform.position.y > boss.FloorY + 3f) CaptureFrame("DT05-apex");
            if (boss.Phase >= 2) CaptureFrame("phase2");
        }
        private void CaptureError(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                if (report.errors.Count < 30) report.errors.Add(condition + "\n" + stack);
        }
        private void OnDamage(DamageInfo info)
        {
            report.damageEvents++;
            report.events.Add("damage t=" + (Time.realtimeSinceStartup - startedAt).ToString("F2") + " hp=" + player.Health.CurrentHealth + " boss=" + (boss != null ? boss.Pattern + "/" + boss.State : "none"));
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
            float initialX = player.transform.position.x;
            yield return Drive(0.5f, 1);
            float travel = player.transform.position.x - initialX;
            Record("walk_speed", travel > 2.15f && travel < 2.9f, "travel over 0.5s=" + travel.ToString("F3"));
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
            initialX = player.transform.position.x;
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
        private IEnumerator Patterns()
        {
            if (boss == null) { Record("boss_available", false, "No boss controller"); yield return Finish("setup_failed"); yield break; }
            BossPattern[] patterns = { BossPattern.DT01Slam, BossPattern.DT02Sweep, BossPattern.DT03Bite, BossPattern.DT05BodySlam };
            foreach (BossPattern pattern in patterns)
            {
                boss.ResetFight(new Vector2(3.2f, 0));
                player.ResetForFight(new Vector2(0, boss.FloorY + 0.025f));
                player.SetExternalInput(true);
                player.Health.ConfigureInvulnerability(0);
                boss.SetFightActive(true);
                bool forced = boss.ForcePattern(pattern);
                float target = boss.TargetX;
                int id = boss.AttackId;
                int healthBefore = player.Health.CurrentHealth;
                int damageBefore = report.damageEvents;
                player.ResetForFight(new Vector2(boss.WarningBounds.center.x, boss.FloorY + 0.025f));
                float top = boss.transform.position.y;
                bool stable = true;
                float deadline = Time.realtimeSinceStartup + 8;
                while (boss.State != BossState.Recovery && !player.IsDead && Time.realtimeSinceStartup < deadline)
                {
                    player.SubmitInput(0, false, false, false);
                    top = Mathf.Max(top, boss.transform.position.y);
                    CaptureCombatFrames();
                    if (Mathf.Abs(target - boss.TargetX) > 0.01f || id != boss.AttackId) stable = false;
                    yield return null;
                }
                Record(pattern + "_forced_cycle", forced && boss.State == BossState.Recovery, "state=" + boss.State);
                Record(pattern + "_locked_target", stable, "target=" + target.ToString("F2") + " attackId=" + id);
                Record(pattern + "_single_damage_without_iframe", report.damageEvents - damageBefore == 1 && player.Health.CurrentHealth == healthBefore - 1, "damageEvents=" + (report.damageEvents - damageBefore) + " HP=" + player.Health.CurrentHealth);
                if (pattern == BossPattern.DT05BodySlam)
                    Record("DT05_whole_body_rises_and_lands", top - boss.FloorY > 3 && Mathf.Abs(boss.transform.position.y - boss.FloorY) < 0.05f, "peak=" + top.ToString("F2") + " landing=" + boss.transform.position.y.ToString("F2"));
                float recoveryX = boss.transform.position.x;
                int recoveryFacing = boss.Facing;
                float window = boss.RecoveryRemaining;
                player.ResetForFight(new Vector2(recoveryX - recoveryFacing * 4, boss.FloorY + 0.025f));
                yield return Drive(0.6f);
                CaptureCombatFrames();
                Record(pattern + "_recovery_holds_position_facing", boss.State == BossState.Recovery && Mathf.Abs(recoveryX - boss.transform.position.x) < 0.01f && recoveryFacing == boss.Facing, "window=" + window.ToString("F2") + " fixedFacing=" + (recoveryFacing == boss.Facing));
                Record(pattern + "_recovery_window", window >= 2.55f, "remaining at first rendered recovery frame=" + window.ToString("F2"));
            }
            foreach (BossPattern pattern in patterns)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float wallX = side < 0 ? boss.ArenaLeft + 0.7f : boss.ArenaRight - 0.7f;
                    boss.ResetFight(new Vector2(wallX - side * 3.3f, 0));
                    player.ResetForFight(new Vector2(wallX, boss.FloorY + 0.025f));
                    player.SetExternalInput(true);
                    player.Health.ConfigureInvulnerability(0.8f);
                    boss.SetFightActive(true);
                    bool forced = boss.ForcePattern(pattern);
                    int damageBefore = report.damageEvents;
                    // Setup ends here: subsequent evasion uses ordinary input, never teleport or HP reset.
                    yield return Drive(0.2f);
                    Bounds initialHurt = player.HurtBounds;
                    Bounds initialWarning = boss.WarningBounds;
                    bool startsInDanger = initialHurt.max.x > initialWarning.min.x && initialHurt.min.x < initialWarning.max.x;
                    bool jumpIssued = false;
                    float peakFeet = initialHurt.min.y;
                    float deadline = Time.realtimeSinceStartup + 8;
                    while (boss.State != BossState.Recovery && !player.IsDead && Time.realtimeSinceStartup < deadline)
                    {
                        float horizontal = 0;
                        bool jump = false;
                        if (pattern == BossPattern.DT02Sweep)
                        {
                            // Stay at the wall and jump near the moving wave's arrival, not at release.
                            jump = !jumpIssued && boss.State == BossState.Active && Mathf.Abs(player.HurtBounds.center.x - boss.ActiveBounds.center.x) < 1.4f;
                            if (jump) jumpIssued = true;
                        }
                        else
                        {
                            Bounds warning = boss.WarningBounds;
                            bool clear = side > 0 ? player.HurtBounds.max.x < warning.min.x - 0.2f : player.HurtBounds.min.x > warning.max.x + 0.2f;
                            horizontal = clear ? 0 : -side;
                        }
                        player.SubmitInput(horizontal, jump, false, false);
                        peakFeet = Mathf.Max(peakFeet, player.HurtBounds.min.y);
                        yield return null;
                    }
                    bool avoided = forced && startsInDanger && boss.State == BossState.Recovery && report.damageEvents == damageBefore;
                    if (pattern == BossPattern.DT02Sweep)
                        avoided &= jumpIssued && peakFeet - initialHurt.min.y > 0.8f && Mathf.Abs(player.transform.position.x - wallX) < 0.15f;
                    Record(pattern + "_wall_escape_" + side, avoided, "startedInDanger=" + startsInDanger + " damageEvents=" + (report.damageEvents - damageBefore) + " method=" + (pattern == BossPattern.DT02Sweep ? "stationary timed jump" : "walk inward") + " peakFeet=" + peakFeet.ToString("F2") + " finalX=" + player.transform.position.x.ToString("F2"));
                }
            }
            boss.SetFightActive(false);
            player.Health.ConfigureInvulnerability(0.8f);
            yield return Finish("patterns_complete");
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
            DamageHitbox2D swordHitbox = player.GetComponentInChildren<DamageHitbox2D>(true);
            BoxCollider2D swordCollider = swordHitbox != null ? swordHitbox.GetComponent<BoxCollider2D>() : null;
            float swordReach = swordCollider != null
                ? Mathf.Abs(swordCollider.transform.localPosition.x) + swordCollider.size.x * Mathf.Abs(swordCollider.transform.lossyScale.x) * 0.5f
                : 1.375f;
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
                if (danger)
                {
                    if (boss.Pattern == BossPattern.DT02Sweep)
                    {
                        jump = boss.State == BossState.Active && Mathf.Abs(px - boss.ActiveBounds.center.x) < 1.4f;
                        if (boss.State == BossState.Telegraph && Mathf.Abs(px - (bx + boss.Facing * 0.8f)) < 1.8f && boss.StateProgress > 0.9f) jump = true;
                        horizontal = 0;
                    }
                    else
                    {
                        bool overlap = hurt.max.x > warning.min.x - 0.35f && hurt.min.x < warning.max.x + 0.35f;
                        if (overlap)
                        {
                            float left = Mathf.Abs(px - (warning.min.x - 0.7f));
                            float right = Mathf.Abs(px - (warning.max.x + 0.7f));
                            horizontal = left < right ? -1 : 1;
                            if (warning.min.x - 0.7f < boss.ArenaLeft + 0.4f) horizontal = 1;
                            if (warning.max.x + 0.7f > boss.ArenaRight - 0.4f) horizontal = -1;
                            dash = boss.State == BossState.Telegraph && boss.StateProgress > 0.35f;
                        }
                    }
                }
                else if (boss.State == BossState.Recovery || boss.State == BossState.Idle)
                {
                    float reachableLeft = boss.ArenaLeft + hurt.extents.x + 0.05f;
                    float reachableRight = boss.ArenaRight - hurt.extents.x - 0.05f;
                    float desiredX = Mathf.Clamp(bx + boss.Facing * 3.75f, reachableLeft, reachableRight);
                    float facingTarget = bx;
                    bool canReach = true;
                    if (bossHurtCollider != null && bossHurtCollider.enabled)
                    {
                        Bounds target = bossHurtCollider.bounds;
                        facingTarget = target.center.x;
                        float attackableLeft = Mathf.Max(reachableLeft, target.min.x - swordReach + 0.12f);
                        float attackableRight = Mathf.Min(reachableRight, target.max.x + swordReach - 0.12f);
                        if (attackableLeft <= attackableRight)
                            desiredX = Mathf.Clamp(desiredX, attackableLeft, attackableRight);
                        else desiredX = Mathf.Clamp(target.center.x, reachableLeft, reachableRight);
                        float gap = Mathf.Max(target.min.x - px, px - target.max.x, 0f);
                        canReach = gap < swordReach - 0.1f;
                    }
                    float distance = Mathf.Abs(px - desiredX);
                    if (distance > 0.2f) horizontal = Mathf.Sign(desiredX - px);
                    else if (canReach) { horizontal = Mathf.Sign(facingTarget - px) * 0.05f; attack = true; }
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
            Record("target_stable_during_active", targetStable, "completed attacks=" + report.completedAttacks);
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
