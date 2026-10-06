#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace FourGuardians.BossFight.Editor
{
    /// <summary>
    /// Agent-directed observation/input/advance controls. No AI decisions, health changes,
    /// teleports or forced boss patterns. EditorApplication drives the runner so no helper
    /// component or scene modification is needed and nothing is included in the player.
    /// </summary>
    [InitializeOnLoad]
    public static class BossFightPlaytest
    {
        private const string MenuRoot = "Tools/Boss Fight/Playtest/";
        private const string StartPendingKey = "BossFight.AgentPlaytest.StartPending";
        private static bool waitingForStart;
        private static bool advancing;
        private static double startDeadline;
        private static double simulationStart;
        private static float duration;
        private static float horizontal;
        private static bool jump, attack, dash, firstInput;
        private static string command;
        private static BossFightSession session;

        static BossFightPlaytest()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(StartPendingKey, false))
                {
                    waitingForStart = true;
                    startDeadline = EditorApplication.timeSinceStartup + 20;
                    EditorApplication.update -= Tick;
                    EditorApplication.update += Tick;
                }
                else if (change == PlayModeStateChange.EnteredEditMode) CancelRunner();
            };
        }
        [MenuItem(MenuRoot + "Start Paused")]
        public static void StartPaused()
        {
            CancelRunner();
            waitingForStart = true;
            SessionState.SetBool(StartPendingKey, true);
            startDeadline = EditorApplication.timeSinceStartup + 20;
            EditorApplication.update += Tick;
            EditorApplication.isPaused = false;
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        [MenuItem(MenuRoot + "Wait")]
        public static void Wait() => Advance("Wait", .5f, 0);
        [MenuItem(MenuRoot + "Move Left")]
        public static void MoveLeft() => Advance("Move Left", .5f, -1);
        [MenuItem(MenuRoot + "Move Right")]
        public static void MoveRight() => Advance("Move Right", .5f, 1);
        [MenuItem(MenuRoot + "Jump Left")]
        public static void JumpLeft() => Advance("Jump Left", .5f, -1, true);
        [MenuItem(MenuRoot + "Jump Right")]
        public static void JumpRight() => Advance("Jump Right", .5f, 1, true);
        [MenuItem(MenuRoot + "Jump Still")]
        public static void JumpStill() => Advance("Jump Still", .5f, 0, true);
        [MenuItem(MenuRoot + "Attack Left")]
        public static void AttackLeft() => Advance("Attack Left", .3f, -0.05f, false, true);
        [MenuItem(MenuRoot + "Attack Right")]
        public static void AttackRight() => Advance("Attack Right", .3f, 0.05f, false, true);
        [MenuItem(MenuRoot + "Dash Left")]
        public static void DashLeft() => Advance("Dash Left", .3f, -1, false, false, true);
        [MenuItem(MenuRoot + "Dash Right")]
        public static void DashRight() => Advance("Dash Right", .3f, 1, false, false, true);

        private static void Advance(string label, float seconds, float move, bool requestJump = false,
            bool requestAttack = false, bool requestDash = false)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                Debug.LogWarning("[BossPlaytest] Use Start Paused after compilation finishes.");
                return;
            }
            if (advancing || waitingForStart)
            {
                Debug.LogWarning("[BossPlaytest] A step is already pending; wait for its pause result.");
                return;
            }
            session = UnityEngine.Object.FindFirstObjectByType<BossFightSession>();
            if (session == null || session.Player == null || session.Boss == null || session.State != EncounterState.Fighting || Time.timeScale <= 0)
            {
                Debug.LogWarning("[BossPlaytest] The encounter must be fighting. These tools do not reset a finished fight or bypass the game's pause menu.");
                return;
            }
            session.Player.SetExternalInput(true);
            command = label;
            duration = seconds;
            horizontal = move;
            jump = requestJump;
            attack = requestAttack;
            dash = requestDash;
            firstInput = true;
            simulationStart = Time.timeAsDouble;
            advancing = true;
            EditorApplication.update += Tick;
            Submit();
            EditorApplication.isPaused = false;
        }

        private static void Tick()
        {
            if (waitingForStart)
            {
                if (EditorApplication.timeSinceStartup > startDeadline)
                {
                    CancelRunner();
                    if (EditorApplication.isPlaying) EditorApplication.isPaused = true;
                    Debug.LogError("[BossPlaytest] Start timed out; open DocheolArena and try again.");
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                session = UnityEngine.Object.FindFirstObjectByType<BossFightSession>();
                if (session == null || session.Player == null || session.Player.Health == null || session.Boss == null || session.Boss.Health == null) return;
                if (session.State == EncounterState.Introduction) session.StartFight();
                if (session.State != EncounterState.Fighting)
                {
                    CancelRunner();
                    EditorApplication.isPaused = true;
                    Debug.LogWarning("[BossPlaytest] Fight has ended. Use the game's retry before Start Paused.");
                    return;
                }
                session.Player.SetExternalInput(true);
                waitingForStart = false;
                SessionState.SetBool(StartPendingKey, false);
                EditorApplication.update -= Tick;
                EditorApplication.isPaused = true;
                LogState("Start Paused", 0);
                return;
            }
            if (!advancing) { EditorApplication.update -= Tick; return; }
            if (!EditorApplication.isPlaying || session == null)
            {
                CancelRunner();
                return;
            }
            double elapsed = Time.timeAsDouble - simulationStart;
            if (session.State != EncounterState.Fighting || elapsed >= duration)
            {
                session.Player.SubmitInput(0, false, false, false);
                advancing = false;
                EditorApplication.update -= Tick;
                EditorApplication.isPaused = true;
                LogState(command, elapsed);
                return;
            }
            Submit();
        }

        private static void Submit()
        {
            if (session == null || session.Player == null) return;
            session.Player.SubmitInput(horizontal, firstInput && jump, firstInput && attack, firstInput && dash);
            firstInput = false;
        }

        private static void LogState(string label, double elapsed)
        {
            if (session == null || session.Player == null || session.Boss == null) return;
            Debug.Log($"[BossPlaytest] {label}; advanced={elapsed:F3}s; state={session.State}; player={session.Player.transform.position}; playerHP={session.Player.Health.CurrentHealth}; bossHP={session.Boss.Health.CurrentHealth}; pattern={session.Boss.Pattern}; stage={session.Boss.State}; progress={session.Boss.StateProgress:F3}. Agent-directed input, not keyboard-manual play.");
        }

        private static void CancelRunner()
        {
            EditorApplication.update -= Tick;
            waitingForStart = advancing = false;
            SessionState.SetBool(StartPendingKey, false);
            session = null;
        }
    }
}
#endif
