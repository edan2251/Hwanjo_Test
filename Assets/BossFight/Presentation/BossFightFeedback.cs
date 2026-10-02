using System;
using System.Collections.Generic;
using UnityEngine;

namespace FourGuardians.BossFight
{
    /// <summary>Scene-owned, procedurally generated audio and restrained impact feedback. Never changes time scale.</summary>
    [DisallowMultipleComponent]
    public sealed class BossFightFeedback : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const int ParticleLimit = 80;
        private static readonly Vector3 CameraRest = new Vector3(0f, 3.6f, -10f);
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly List<Debris> debris = new List<Debris>();
        private readonly System.Random random = new System.Random(17041);
        private Camera fightCamera;
        private AudioSource effects;
        private AudioSource atmosphere;
        private Texture2D pixelTexture;
        private Sprite pixel;
        private float shakeRemaining;
        private float shakeDuration;
        private float shakeStrength;
        // Retry reloads the scene, but must preserve the user's in-session audio choice.
        private static bool sessionMuted;
        private bool muted;
        private bool configured;

        private sealed class Debris
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Spin;
            public Vector3 Scale;
            public Color Color;
        }

        public bool Muted
        {
            get => muted;
            set
            {
                muted = value;
                sessionMuted = value;
                if (effects != null) effects.mute = value;
                if (atmosphere != null) atmosphere.mute = value;
            }
        }

        public void Configure(Camera camera)
        {
            fightCamera = camera;
            if (configured) return;
            configured = true;
            muted = sessionMuted;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0f;
            effects.volume = 0.58f;
            effects.priority = 64;
            atmosphere = gameObject.AddComponent<AudioSource>();
            atmosphere.playOnAwake = false;
            atmosphere.spatialBlend = 0f;
            atmosphere.loop = true;
            atmosphere.volume = 0.075f;
            atmosphere.priority = 180;
            if (camera != null && FindFirstObjectByType<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();

            pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixelTexture.name = "BossFight_RuntimePixel";
            pixelTexture.filterMode = FilterMode.Point;
            pixelTexture.SetPixel(0, 0, Color.white);
            pixelTexture.Apply();
            pixel = Sprite.Create(pixelTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            pixel.name = "BossFight_RuntimeDebris";

            clips.Add("warning", Synthesize("Warning", 0.32f, 0));
            clips.Add("rumble", Synthesize("RootRumble", 0.65f, 1));
            clips.Add("impact", Synthesize("GroundImpact", 0.48f, 2));
            clips.Add("sweep", Synthesize("LowSweep", 0.30f, 3));
            clips.Add("bite", Synthesize("Bite", 0.26f, 4));
            clips.Add("sword", Synthesize("SwordConfirm", 0.16f, 5));
            clips.Add("hurt", Synthesize("PlayerHurt", 0.24f, 6));
            clips.Add("phase", Synthesize("PhaseWarning", 0.80f, 7));
            clips.Add("death", Synthesize("RootsCollapse", 1.20f, 8));
            clips.Add("victory", Synthesize("Victory", 1.80f, 9));
            clips.Add("air", Synthesize("QuietWind", 8f, 10));
            atmosphere.clip = clips["air"];
            Muted = muted;
            atmosphere.Play();
        }

        public void PlayCue(string cue, Vector2 position, float strength)
        {
            if (!configured) return;
            float amount = Mathf.Clamp(strength, 0.2f, 2f);
            switch (cue)
            {
                case "start": Play("phase", position, 0.42f); break;
                case "slash": Play("sweep", position, 0.24f); break;
                case "telegraph": Play("warning", position, 0.56f); break;
                case "rise":
                    Play("rumble", position, 0.65f);
                    Shake(0.045f * amount, 0.36f);
                    Burst(position, 7, new Color(0.40f, 0.34f, 0.22f), 0.6f);
                    break;
                case "release": Play("rumble", position, 0.7f); break;
                case "impact":
                case "land":
                    Play("impact", position, 0.85f);
                    Shake(0.14f * amount, 0.26f);
                    Burst(position, 14, new Color(0.55f, 0.49f, 0.34f), amount);
                    break;
                case "sweep":
                    Play("sweep", position, 0.72f);
                    Shake(0.035f * amount, 0.13f);
                    Burst(position, 6, new Color(0.43f, 0.47f, 0.26f), 0.55f);
                    break;
                case "bite":
                    Play("bite", position, 0.78f);
                    Shake(0.065f * amount, 0.16f);
                    break;
                case "recover": break;
                case "phase":
                    Play("phase", position, 0.70f);
                    Shake(0.085f, 0.42f);
                    Burst(position, 12, new Color(0.45f, 0.23f, 0.16f), 0.9f);
                    break;
                case "death":
                    Play("death", position, 0.85f);
                    Shake(0.11f, 0.50f);
                    Burst(position, 18, new Color(0.50f, 0.43f, 0.28f), 1.1f);
                    break;
            }
        }

        public void PlayerHit(Vector2 position)
        {
            if (!configured) return;
            Play("hurt", position, 0.90f);
            Shake(0.09f, 0.18f);
            Burst(position, 7, new Color(0.87f, 0.43f, 0.31f), 0.65f);
        }

        public void BossHit(Vector2 position)
        {
            if (!configured) return;
            Play("sword", position, 0.55f);
            Shake(0.035f, 0.09f);
            Burst(position, 6, new Color(0.93f, 0.81f, 0.53f), 0.52f);
        }

        public void Victory()
        {
            if (!configured) return;
            atmosphere.Stop();
            Play("victory", Vector2.zero, 0.68f);
        }

        public void ResetEffects()
        {
            shakeRemaining = shakeStrength = 0f;
            if (fightCamera != null) fightCamera.transform.position = CameraRest;
            for (int i = debris.Count - 1; i >= 0; i--)
                if (debris[i].Transform != null) Destroy(debris[i].Transform.gameObject);
            debris.Clear();
            if (effects != null) effects.Stop();
            if (atmosphere != null && isActiveAndEnabled)
            {
                atmosphere.Stop();
                atmosphere.Play();
            }
        }

        private void Play(string key, Vector2 position, float volume)
        {
            if (effects == null || !clips.TryGetValue(key, out AudioClip clip)) return;
            effects.panStereo = Mathf.Clamp(position.x / 18f, -0.28f, 0.28f);
            effects.PlayOneShot(clip, volume);
        }

        private void Shake(float strength, float duration)
        {
            shakeStrength = Mathf.Max(shakeStrength, Mathf.Min(0.18f, strength));
            shakeRemaining = Mathf.Max(shakeRemaining, duration);
            shakeDuration = shakeRemaining;
        }

        private void LateUpdate()
        {
            if (fightCamera == null) return;
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.unscaledDeltaTime);
            float envelope = shakeDuration > 0f ? shakeRemaining / shakeDuration : 0f;
            float phase = Time.unscaledTime;
            Vector2 offset = new Vector2(Mathf.Sin(phase * 71f), Mathf.Sin(phase * 93f + 1f));
            offset = Vector2.ClampMagnitude(offset * shakeStrength * envelope, 0.18f);
            fightCamera.transform.position = CameraRest + new Vector3(offset.x, offset.y, 0f);
            if (shakeRemaining <= 0f) shakeStrength = 0f;
        }

        private void Burst(Vector2 position, int count, Color tint, float strength)
        {
            for (int i = 0; i < count && debris.Count < ParticleLimit; i++)
            {
                GameObject piece = new GameObject("ImpactDebris", typeof(SpriteRenderer));
                piece.transform.SetParent(transform, false);
                piece.transform.position = position + new Vector2(Range(-0.28f, 0.28f), Range(0.02f, 0.12f));
                float size = Range(0.035f, 0.085f);
                piece.transform.localScale = new Vector3(size * Range(1f, 2.4f), size, 1f);
                SpriteRenderer renderer = piece.GetComponent<SpriteRenderer>();
                renderer.sprite = pixel;
                renderer.sortingOrder = 24;
                renderer.color = tint;
                debris.Add(new Debris
                {
                    Transform = piece.transform,
                    Renderer = renderer,
                    Velocity = new Vector2(Range(-3.6f, 3.6f), Range(1.6f, 4.5f)) * strength,
                    Lifetime = Range(0.23f, 0.52f),
                    Spin = Range(-300f, 300f),
                    Scale = piece.transform.localScale,
                    Color = tint
                });
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                Debris piece = debris[i];
                piece.Age += dt;
                if (piece.Transform == null || piece.Age >= piece.Lifetime)
                {
                    if (piece.Transform != null) Destroy(piece.Transform.gameObject);
                    debris.RemoveAt(i);
                    continue;
                }
                piece.Velocity += Vector2.down * (8f * dt);
                piece.Transform.position += (Vector3)(piece.Velocity * dt);
                piece.Transform.Rotate(0f, 0f, piece.Spin * dt);
                float remaining = 1f - piece.Age / piece.Lifetime;
                piece.Transform.localScale = piece.Scale * Mathf.Lerp(0.35f, 1f, remaining);
                Color color = piece.Color;
                color.a *= remaining;
                piece.Renderer.color = color;
            }
        }

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        private AudioClip Synthesize(string name, float duration, int kind)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float lowNoise = 0f;
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / duration;
                float noise = Range(-1f, 1f);
                lowNoise = Mathf.Lerp(lowNoise, noise, kind == 10 ? 0.007f : 0.035f);
                float signal;
                float envelope = Mathf.Min(1f, t / 0.008f) * Mathf.Pow(1f - progress, 2f);
                switch (kind)
                {
                    case 0: // Two easily recognized warning taps.
                        float pulse = Mathf.Repeat(t, 0.16f);
                        signal = Mathf.Sin(2f * Mathf.PI * 640f * t) * Mathf.Exp(-pulse * 35f) * 0.45f;
                        break;
                    case 1:
                        signal = lowNoise * 1.4f + Mathf.Sin(t * 2f * Mathf.PI * 56f) * 0.35f;
                        break;
                    case 2:
                        phase += 2.0 * Math.PI * Mathf.Lerp(115f, 38f, progress) / SampleRate;
                        signal = (float)Math.Sin(phase) * 0.67f + noise * Mathf.Exp(-t * 70f) * 0.25f + lowNoise;
                        break;
                    case 3:
                        signal = noise * 0.38f * Mathf.Sin(progress * Mathf.PI) + lowNoise;
                        break;
                    case 4:
                        signal = noise * Mathf.Exp(-t * 32f) * 0.52f + Mathf.Sin(t * 2f * Mathf.PI * 125f) * 0.34f;
                        break;
                    case 5:
                        signal = (Mathf.Sin(t * 2f * Mathf.PI * 1480f) + Mathf.Sin(t * 2f * Mathf.PI * 2317f) * 0.6f) * Mathf.Exp(-t * 26f) * 0.42f;
                        break;
                    case 6:
                        phase += 2.0 * Math.PI * Mathf.Lerp(235f, 105f, progress) / SampleRate;
                        signal = (float)Math.Sin(phase) * 0.38f + lowNoise * 0.75f;
                        break;
                    case 7:
                        signal = Mathf.Sin(t * 2f * Mathf.PI * 180f) * 0.27f + Mathf.Sin(t * 2f * Mathf.PI * 270f) * 0.18f + lowNoise;
                        break;
                    case 8:
                        signal = lowNoise * 1.8f + Mathf.Sin(t * 2f * Mathf.PI * 45f) * 0.46f;
                        break;
                    case 9:
                        float note = t < 0.3f ? 261.63f : t < 0.6f ? 329.63f : 392f;
                        phase += 2.0 * Math.PI * note / SampleRate;
                        signal = ((float)Math.Sin(phase) + Mathf.Sin(t * 2f * Mathf.PI * 523.25f) * 0.2f) * 0.35f;
                        envelope = Mathf.Min(1f, t / 0.03f) * Mathf.Min(1f, (duration - t) / 0.5f);
                        break;
                    default:
                        signal = lowNoise * 0.6f + Mathf.Sin(t * 2f * Mathf.PI * 55f) * (0.16f + 0.035f * Mathf.Sin(t * 2f * Mathf.PI * 0.5f));
                        envelope = Mathf.Min(1f, t / 0.3f) * Mathf.Min(1f, (duration - t) / 0.3f);
                        break;
                }
                samples[i] = Mathf.Clamp(signal * envelope * 0.65f, -0.85f, 0.85f);
            }
            AudioClip clip = AudioClip.Create("BossFight_" + name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDisable()
        {
            if (effects != null) effects.Stop();
            if (atmosphere != null) atmosphere.Stop();
            shakeRemaining = shakeStrength = 0f;
            if (fightCamera != null) fightCamera.transform.position = CameraRest;
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
            if (pixel != null) Destroy(pixel);
            if (pixelTexture != null) Destroy(pixelTexture);
        }
    }
}