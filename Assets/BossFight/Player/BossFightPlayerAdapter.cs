using System;
using System.Collections.Generic;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;
using UnityEngine;

namespace FourGuardians.BossFight
{
    /// <summary>Opt-in bridge from the unchanged warrior asset hierarchy to a boss encounter.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BasicPlayerMovement2D), typeof(Health2D), typeof(PlayerCombat2D))]
    public sealed class BossFightPlayerAdapter : MonoBehaviour
    {
        private readonly HashSet<int> resolvedAttacks = new HashSet<int>();
        private Collider2D hurtCollider;
        private bool subscribed;

        public BasicPlayerMovement2D Movement { get; private set; }
        public Health2D Health { get; private set; }
        public PlayerCombat2D Combat { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public Bounds HurtBounds => hurtCollider != null ? hurtCollider.bounds : new Bounds(transform.position, Vector3.zero);
        public bool IsDead => Health != null && Health.IsDead;
        public bool FightActive { get; private set; }
        public event Action<DamageInfo> Damaged;
        public event Action Died;

        private void Awake()
        {
            CacheComponents();
            Movement.ConfigureBossCombat();
        }

        private void CacheComponents()
        {
            Movement = GetComponent<BasicPlayerMovement2D>();
            Health = GetComponent<Health2D>();
            Combat = GetComponent<PlayerCombat2D>();
            Body = GetComponent<Rigidbody2D>();
            Hurtbox2D hurtbox = GetComponentInChildren<Hurtbox2D>();
            hurtCollider = hurtbox != null ? hurtbox.GetComponent<Collider2D>() : GetComponent<Collider2D>();
        }

        private void OnEnable()
        {
            if (Health == null) CacheComponents();
            if (subscribed) return;
            Health.DamageTaken += OnDamageTaken;
            Health.Died += OnDeath;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (subscribed && Health != null)
            {
                Health.DamageTaken -= OnDamageTaken;
                Health.Died -= OnDeath;
            }
            subscribed = false;
            SetFightActive(false);
        }

        public void Configure(int health = 8, float hitInvulnerability = 0.8f)
        {
            CacheComponents();
            Movement.ConfigureBossCombat();
            Health.Configure(health, CombatTeam.Player);
            Health.ConfigureInvulnerability(hitInvulnerability);
            Health.ResetHealth();
            resolvedAttacks.Clear();
            SetFightActive(false);
        }

        public void SetFightActive(bool active)
        {
            FightActive = active && !IsDead;
            if (Health != null) Health.DamageEnabled = FightActive;
            if (Movement != null) Movement.SetGameplayEnabled(FightActive);
            if (Combat != null)
            {
                if (!FightActive) Combat.ResetCombat();
                Combat.enabled = FightActive;
            }
        }

        public void ResetForFight(Vector2 position, int facing = 1)
        {
            resolvedAttacks.Clear();
            Health.ResetHealth();
            Movement.ResetState(position, facing);
            Combat.ResetCombat();
            SetFightActive(true);
        }

        /// <summary>Use the same attackId for every volume/stage of one attack, including a slam's landing.</summary>
        public bool ReceiveAttack(int attackId, int damage, Vector2 knockback, GameObject source)
        {
            if (!FightActive || IsDead || damage <= 0 || !resolvedAttacks.Add(attackId)) return false;
            int before = Health.CurrentHealth;
            Vector2 hitPoint = HurtBounds.center;
            Health.TakeDamage(new DamageInfo(damage, hitPoint, knockback, source));
            return Health.CurrentHealth < before;
        }

        public void SetExternalInput(bool enabled)
        {
            Movement.SetExternalInput(enabled);
        }

        /// <summary>Feeds the same movement and attack path as keyboard input; flags are one-shot requests.</summary>
        public void SubmitInput(float horizontal, bool jump, bool attack, bool dash, float vertical = 0f)
        {
            Movement.SubmitInput(horizontal, jump, attack, dash, vertical);
        }

        private void OnDamageTaken(DamageInfo damage)
        {
            Combat.ResetCombat();
            // The encounter's recoil must win over incoming dash/run momentum.
            // Health2D keeps its additive behavior for the original course scenes.
            if (Body != null) Body.linearVelocity = damage.Knockback;
            if (Health.IsDead) Movement.SetDead();
            else Movement.ReceiveHit();
            Damaged?.Invoke(damage);
        }

        private void OnDeath()
        {
            Movement.SetDead();
            SetFightActive(false);
            Died?.Invoke();
        }
    }
}