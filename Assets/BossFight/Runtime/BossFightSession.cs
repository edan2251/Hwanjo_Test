using System;
using System.Collections;
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
        private static bool restartImmediately;
        private static int attempts = 1;
        private float elapsed, stateTime, hitStopUntil, bossFlash;
        private string lastCue = "";
        private float cueTime;
        private uint lastAttackSequence;
        private EncounterState beforePause;
        private bool ready;

        public BossFightPlayerAdapter Player => player;
        public DoCheolBossController Boss => boss;
        public EncounterState State { get; private set; } = EncounterState.Introduction;
        public float Elapsed => elapsed;
        public float StateTime => stateTime;
        public int Attempts => attempts;
        public bool Muted => feedback != null && feedback.Muted;
        public string LastCue => lastCue;
        public float CueAge => Time.unscaledTime - cueTime;
        public bool IsQaContracts { get; private set; }
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
                if (args[i] == "-bossqa" && i + 1 < args.Length) IsQaContracts = args[i + 1] == "contracts";
                if (args[i] == "-bossqa-fps" && i + 1 < args.Length && int.TryParse(args[i + 1], out int fps))
                    Application.targetFrameRate = Mathf.Clamp(fps, 15, 240);
            }
        }

        private void Start()
        {
            if (player == null || boss == null) { Debug.LogError("Boss encounter references are missing."); return; }
            player.Configure(8, .8f);
            Health2D bossHealth = boss.GetComponent<Health2D>();
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
            feedback?.Configure(arenaCamera);
            ready = true;
            string[] args = Environment.GetCommandLineArgs();
            bool qa = Array.IndexOf(args, "-bossqa") >= 0;
            if (restartImmediately || qa) { restartImmediately = false; StartFight(); }
        }

        public void StartFight()
        {
            if (!ready || State == EncounterState.Fighting) return;
            Time.timeScale = 1f;
            hitStopUntil = 0f;
            elapsed = 0f;
            player.ResetForFight(new Vector2(-5.8f, .025f), 1);
            boss.ResetFight(new Vector2(3.2f, 0f));
            boss.GetComponent<Health2D>().DamageEnabled = true;
            boss.SetFightActive(!IsQaContracts);
            SetState(EncounterState.Fighting);
            feedback?.PlayCue("start", Vector2.zero, 1f);
        }

        private void Update()
        {
            if (!ready) return;
            stateTime += Time.unscaledDeltaTime;
            if (State == EncounterState.Fighting) elapsed += Time.deltaTime;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.mKey.wasPressedThisFrame) ToggleMute();
                if (keyboard.f11Key.wasPressedThisFrame)
                    Screen.fullScreenMode = Screen.fullScreen ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                if (keyboard.escapeKey.wasPressedThisFrame) TogglePause();
                if (keyboard.enterKey.wasPressedThisFrame && State == EncounterState.Introduction) StartFight();
                if (keyboard.rKey.wasPressedThisFrame && State != EncounterState.Introduction) Retry();
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
                feedback?.PlayCue("slash", player.transform.position, .55f);
            }

            if (bossVisual != null)
            {
                if (State == EncounterState.Victory) bossVisual.SetDead(Mathf.Clamp01(stateTime / 1.8f));
                else
                {
                    string pattern = PatternCode(boss.Pattern);
                    string stage = boss.State.ToString().ToLowerInvariant();
                    bossVisual.SetPose(pattern, stage, boss.StateProgress, boss.Facing);
                }
                bossFlash = Mathf.MoveTowards(bossFlash, 0f, Time.unscaledDeltaTime * 6f);
                bossVisual.SetFlash(bossFlash);
            }
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
                _ => "MV01"
            };
        }

        private void OnBossCue(string cue, Vector2 position, float strength)
        {
            lastCue = cue; cueTime = Time.unscaledTime;
            feedback?.PlayCue(cue, position, strength);
        }
        private void OnPlayerDamaged(DamageInfo damage)
        {
            feedback?.PlayerHit(damage.HitPoint);
            HitStop(.055f);
        }
        private void OnBossDamaged(DamageInfo damage)
        {
            bossFlash = 1f;
            feedback?.BossHit(damage.HitPoint);
            HitStop(.032f);
        }
        private void HitStop(float duration)
        {
            if (State != EncounterState.Fighting) return;
            hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + duration);
            Time.timeScale = .08f;
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
            boss.GetComponent<Health2D>().DamageEnabled = false;
            player.SetFightActive(false);
            SetState(result);
            Debug.Log($"[Docheol] {result}; seconds={elapsed:F2}; playerHp={player.Health.CurrentHealth}; attempt={attempts}");
        }
        private void SetState(EncounterState state)
        {
            State = state; stateTime = 0f; StateChanged?.Invoke(state);
        }
        public void TogglePause()
        {
            if (State == EncounterState.Fighting)
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
        public void Retry()
        {
            if (!ready) return;
            Time.timeScale = 1f;
            restartImmediately = true;
            attempts++;
            feedback?.ResetEffects();
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
                Health2D health = boss.GetComponent<Health2D>();
                if (health != null) { health.DamageTaken -= OnBossDamaged; health.Died -= OnBossDied; }
            }
        }
    }
}
