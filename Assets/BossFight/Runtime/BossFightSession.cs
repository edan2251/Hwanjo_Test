using System;
using FourGuardians.CourseContent.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FourGuardians.BossFight
{
    public enum EncounterState { Introduction, Fighting, Victory, Defeat, Paused }

    [DefaultExecutionOrder(100)]
    public sealed class BossFightSession : MonoBehaviour
    {
        [SerializeField] private BossFightPlayerAdapter player;
        [SerializeField] private DoCheolBossController boss;
        [SerializeField] private Camera arenaCamera;
        [SerializeField] private DocheolVisual bossVisual;
        [SerializeField] private BossFightFeedback feedback;
        private DocheolArenaEffects arenaEffects;
        private static bool restartImmediately;
        private static int attempts = 1;
        private float elapsed, stateTime, hitStopUntil, bossFlash, introTime;
        private const float IntroDuration = 7f;
        private string lastCue = "", lastIntroStage = "";
        private float cueTime;
        private uint lastAttackSequence;
        private EncounterState beforePause;
        private bool ready, qaIntro;
        private Health2D bossHealth;

        public BossFightPlayerAdapter Player => player;
        public DoCheolBossController Boss => boss;
        public EncounterState State { get; private set; } = EncounterState.Introduction;
        public float Elapsed => elapsed;
        public float StateTime => stateTime;
        public int Attempts => attempts;
        public bool Muted => feedback != null && feedback.Muted;
        public float CameraShake => feedback != null ? feedback.ShakeScale : 0f;
        public string LastCue => lastCue;
        public float CueAge => Time.unscaledTime - cueTime;
        public float LastRootDeflectTime { get; private set; } = -100f;
        public bool IsQaContracts { get; private set; }
        public bool IntroComplete { get; private set; }
        public float IntroProgress => Mathf.Clamp01(introTime / IntroDuration);
        public string IntroStage { get; private set; } = "buried";
        public float IntroStageProgress { get; private set; }
        public event Action<EncounterState> StateChanged;

        public void SetReferences(BossFightPlayerAdapter p, DoCheolBossController b,
            Camera c, DocheolVisual visual, BossFightFeedback effects)
        {
            player = p; boss = b; arenaCamera = c; bossVisual = visual; feedback = effects;
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-bossqa" && i + 1 < args.Length)
                {
                    IsQaContracts = args[i + 1] == "contracts" || args[i + 1] == "v3contracts";
                    qaIntro = args[i + 1] == "intro" || args[i + 1] == "v3intro" || args[i + 1] == "observe" || args[i + 1] == "v4observe" || args[i + 1] == "v5observe";
                }
                if (args[i] == "-bossqa-fps" && i + 1 < args.Length && int.TryParse(args[i + 1], out int fps))
                    Application.targetFrameRate = Mathf.Clamp(fps, 15, 240);
            }
        }

        private void Start()
        {
            if (player == null || boss == null) { Debug.LogError("Boss encounter references are missing."); return; }
            player.Configure(8, .8f);
            bossHealth = boss.GetComponent<Health2D>();
            bossHealth.Configure(40, CombatTeam.Enemy);
            bossHealth.ResetHealth();
            boss.Configure(player, bossHealth);
            boss.SetFightActive(false);
            bossHealth.DamageEnabled = false;
            player.Damaged += OnPlayerDamaged;
            player.Died += OnPlayerDied;
            bossHealth.DamageTaken += OnBossDamaged;
            bossHealth.Died += OnBossDied;
            boss.Cued += OnBossCue;
            boss.ArmorBroken += OnArmorBroken;
            boss.LocalHit += OnLocalHit;
            feedback?.Configure(arenaCamera);
            arenaEffects = GetComponent<DocheolArenaEffects>();
            if (arenaEffects == null) arenaEffects = gameObject.AddComponent<DocheolArenaEffects>();
            arenaEffects.Configure(this);
            ready = true;
            string[] args = Environment.GetCommandLineArgs();
            bool qa = Array.IndexOf(args, "-bossqa") >= 0;
            if (restartImmediately || (qa && !qaIntro)) { restartImmediately = false; StartFight(); }
            else UpdateIntroduction(0f);
        }

        // ENTER and the on-screen skip button use this same entry point.
        public void StartFight()
        {
            if (!ready || State != EncounterState.Introduction) return;
            Time.timeScale = 1f;
            hitStopUntil = 0f;
            elapsed = 0f;
            introTime = IntroDuration;
            IntroComplete = true;
            IntroStage = "done";
            IntroStageProgress = 1f;
            arenaEffects?.ResetEffects();
            bossVisual?.SetIntro("done", 1f);
            player.ResetForFight(new Vector2(-5.8f, .025f), 1);
            boss.ResetFight(new Vector2(3.2f, 0f));
            bossHealth.DamageEnabled = true;
            boss.SetFightActive(!IsQaContracts);
            SetState(EncounterState.Fighting);
            feedback?.PlayCue("start", Vector2.zero, .65f);
        }

        private void UpdateIntroduction(float delta)
        {
            introTime += delta;
            if (introTime < .5f) SetIntroStage("buried", introTime / .5f);
            else if (introTime < 1.85f) SetIntroStage("emerge", (introTime - .5f) / 1.35f);
            else if (introTime < 4.6f) SetIntroStage("inhale", (introTime - 1.85f) / 2.75f);
            else if (introTime < 5.2f) SetIntroStage("silence", (introTime - 4.6f) / .6f);
            else if (introTime < 6.5f) SetIntroStage("roar", (introTime - 5.2f) / 1.3f);
            else SetIntroStage("done", (introTime - 6.5f) / .5f);
            player.Health.DamageEnabled = false;
            bossHealth.DamageEnabled = false;
            arenaEffects?.SetIntro(IntroStage, IntroStageProgress, boss.transform.position);
            if (introTime >= IntroDuration) StartFight();
        }

        private void SetIntroStage(string stage, float progress)
        {
            IntroStage = stage;
            IntroStageProgress = Mathf.Clamp01(progress);
            if (lastIntroStage == stage) return;
            lastIntroStage = stage;
            if (stage != "buried" && stage != "done")
                feedback?.PlayCue("intro_" + stage, boss.transform.position, .8f);
            Debug.Log("[Docheol] INTRO " + stage);
        }

        private void Update()
        {
            if (!ready) return;
            stateTime += Time.unscaledDeltaTime;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.mKey.wasPressedThisFrame) ToggleMute();
                if (keyboard.f6Key.wasPressedThisFrame) CycleCameraShake();
                if (keyboard.f11Key.wasPressedThisFrame)
                    Screen.fullScreenMode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                if (keyboard.escapeKey.wasPressedThisFrame) TogglePause();
                if (keyboard.enterKey.wasPressedThisFrame && State == EncounterState.Introduction) StartFight();
                if (keyboard.rKey.wasPressedThisFrame && State != EncounterState.Introduction) Retry();
            }
            if (State == EncounterState.Introduction) UpdateIntroduction(Time.deltaTime);
            feedback?.SetHazardReadability(State == EncounterState.Fighting && boss.RockfallActive);
            feedback?.SetIntroFeedback(State == EncounterState.Introduction ? IntroStage : "", IntroStageProgress);
            if (State == EncounterState.Fighting)
            {
                elapsed += Time.deltaTime;
                bool canControl=!player.IsDead && !boss.ControlsLocked;
                if(player.FightActive!=canControl)player.SetFightActive(canControl);
                player.Health.DamageEnabled=canControl;
            }
            if (State != EncounterState.Paused && hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
            {
                hitStopUntil = 0f;
                Time.timeScale = 1f;
            }
            if (arenaCamera != null)
                arenaCamera.orthographicSize = Mathf.Max(5.8f, 10.6f / Mathf.Max(.5f, arenaCamera.aspect));

            if (State == EncounterState.Fighting && player.Movement.AttackSequence != lastAttackSequence)
            {
                lastAttackSequence = player.Movement.AttackSequence;
                feedback?.PlayCue("slash", player.transform.position, .36f);
            }

            if (bossVisual != null)
            {
                bossVisual.SetWholeBodyState(boss.WholeFirstSide,boss.WholeSwingSide);
                bossVisual.SetTurnState(boss.Turning,boss.StateProgress,boss.TurnFromFacing,boss.TurnToFacing);
                bossVisual.SetCombatState(boss.Phase, boss.ArmorMask, boss.AttackStage, boss.AttackStageProgress,
                    boss.TransitionProgress, boss.IsUnderground, boss.TargetLocked);
                bossVisual.SetAttackGeometry(boss.WarningBounds, boss.ActiveBounds, boss.FloorY);
                if (State == EncounterState.Victory) bossVisual.SetDead(Mathf.Clamp01(stateTime / 1.8f));
                else
                {
                    bossVisual.SetPose(PatternCode(boss.Pattern), boss.State.ToString().ToLowerInvariant(),
                        boss.StateProgress, boss.Facing);
                    if (State == EncounterState.Introduction ||
                        (State == EncounterState.Paused && beforePause == EncounterState.Introduction))
                        bossVisual.SetIntro(IntroStage, IntroStageProgress);
                }
                bossFlash = Mathf.MoveTowards(bossFlash, 0f, Time.unscaledDeltaTime * 9f);
                bossVisual.SetFlash(bossFlash);
            }
            if (State == EncounterState.Fighting && boss.Pattern == BossPattern.BBurrowLeap)
                arenaEffects?.SetBurrow(boss.AttackStage, boss.transform.position, boss.AttackStageProgress);
            else arenaEffects?.SetBurrow("none", boss.transform.position, 0f);
            if (State == EncounterState.Fighting && player.transform.position.y < -4f && !player.Health.IsInvincible)
                player.ReceiveAttack(int.MinValue, 8, Vector2.zero, gameObject);
        }

        public static string PatternCode(BossPattern pattern)
        {
            return pattern switch
            {
                BossPattern.DT01Slam => "DT01",
                BossPattern.DT02Sweep => "DT02",
                BossPattern.DT03Bite => "DT03",
                BossPattern.DT04Inhale => "DT04",
                BossPattern.DT05BodySlam => "DT05",
                BossPattern.DT06Burrow => "DT06",
                BossPattern.ARockfall => "ARockfall",
                BossPattern.BBurrowLeap => "BBurrowLeap",
                BossPattern.WholeBodyBite => "WholeBodyBite",
                _ => "MV01"
            };
        }

        private void OnBossCue(string cue, Vector2 position, float strength)
        {
            lastCue = cue; cueTime = Time.unscaledTime;
            if(cue=="root_block"){LastRootDeflectTime=Time.unscaledTime;bossVisual?.OnRootBlocked();}
            if (cue != "armor_break") feedback?.PlayCue(cue, position, strength);
        }
        private void OnArmorBroken(int index, int mask, Vector2 point)
        {
            bossVisual?.OnArmorBroken(index, mask, point);
            feedback?.ArmorBreak(point, 1);
            HitStop(.045f);
        }
        private void OnLocalHit(Vector2 point, int region, bool hasArmor)
        {
            bossVisual?.OnLocalHit(point, region, hasArmor);
            feedback?.BossHit(point, hasArmor);
            HitStop(hasArmor ? .018f : .025f);
        }
        private void OnPlayerDamaged(DamageInfo damage)
        {
            feedback?.PlayerHit(damage.HitPoint);
            HitStop(.050f);
        }
        private void OnBossDamaged(DamageInfo damage)
        {
            bossFlash = .45f;
        }
        private void HitStop(float duration)
        {
            if (State != EncounterState.Fighting || boss.State == BossState.Transition) return;
            hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + duration);
            Time.timeScale = .12f;
        }
        private void OnPlayerDied()
        {
            if (State != EncounterState.Fighting) return;
            EndFight(EncounterState.Defeat);
        }
        private void OnBossDied()
        {
            if (State != EncounterState.Fighting) return;
            EndFight(EncounterState.Victory);
            feedback?.Victory();
        }
        private void EndFight(EncounterState result)
        {
            hitStopUntil = 0f;
            Time.timeScale = 1f;
            boss.SetFightActive(false);
            bossHealth.DamageEnabled = false;
            player.SetFightActive(false);
            arenaEffects?.ResetEffects();
            SetState(result);
            Debug.Log($"[Docheol] {result}; seconds={elapsed:F2}; playerHp={player.Health.CurrentHealth}; attempt={attempts}");
        }
        private void SetState(EncounterState state)
        {
            State = state; stateTime = 0f; StateChanged?.Invoke(state);
        }
        public void TogglePause()
        {
            if (State == EncounterState.Fighting || State == EncounterState.Introduction)
            {
                beforePause = State;
                SetState(EncounterState.Paused);
                hitStopUntil = 0f;
                Time.timeScale = 0f;
            }
            else if (State == EncounterState.Paused)
            {
                Time.timeScale = 1f;
                SetState(beforePause);
            }
        }
        public void ToggleMute() { if (feedback != null) feedback.Muted = !feedback.Muted; }
        public void CycleCameraShake()
        {
            if (feedback == null) return;
            float value = feedback.ShakeScale;
            feedback.ShakeScale = value < .01f ? .35f : value < .5f ? .65f : value < .9f ? 1f : 0f;
        }
        public void Retry()
        {
            if (!ready) return;
            Time.timeScale = 1f;
            restartImmediately = true;
            attempts++;
            feedback?.ResetEffects();
            arenaEffects?.ResetEffects();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        public void Quit()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        private void OnDestroy()
        {
            Time.timeScale = 1f;
            if (player != null) { player.Damaged -= OnPlayerDamaged; player.Died -= OnPlayerDied; }
            if (boss != null)
            {
                boss.Cued -= OnBossCue;
                boss.ArmorBroken -= OnArmorBroken;
                boss.LocalHit -= OnLocalHit;
                if (bossHealth != null) { bossHealth.DamageTaken -= OnBossDamaged; bossHealth.Died -= OnBossDied; }
            }
        }
    }
}
