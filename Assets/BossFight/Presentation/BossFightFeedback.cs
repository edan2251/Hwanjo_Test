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
        private const int ParticleLimit = 56;
        private static readonly Vector3 CameraRest = new Vector3(0f, 3.6f, -10f);
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly List<Debris> debris = new List<Debris>();
        private readonly System.Random random = new System.Random(17041);
        private Camera fightCamera;
        private AudioSource effects;
        private AudioSource atmosphere;
        private Texture2D pixelTexture;
        private Sprite pixel;
        private Material debrisMaterial;
        private float shakeRemaining;
        private float shakeDuration;
        private float shakeStrength;
        private float introShakeStrength;
        private bool hazardReadability;
        // A restrained default keeps floor tells readable; Retry preserves the chosen setting.
        private static float sessionShakeScale = 0.65f;
        // Retry reloads the scene, but must preserve the user's in-session audio choice.
        private static bool sessionMuted;
        private static float sessionFlashScale=1;
        private Vector2 directionalKick;
        public float FlashScale {get=>sessionFlashScale;set=>sessionFlashScale=float.IsNaN(value)?1:Mathf.Clamp01(value);}
        public float CurrentCameraOffset=>fightCamera!=null?Vector3.Distance(fightCamera.transform.position,CameraRest):0;
        public float ShakeTimeRemaining=>shakeRemaining;
        public int BossHitCount {get;private set;}
        public int PlayerHitCount {get;private set;}
        public Vector2 LastImpactDirection {get;private set;}
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
            public float Gravity = 8f;
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

        public float ShakeScale
        {
            get => sessionShakeScale;
            set
            {
                sessionShakeScale = float.IsNaN(value) ? 0.65f : Mathf.Clamp01(value);
                if (sessionShakeScale <= 0f && fightCamera != null)
                    fightCamera.transform.position = CameraRest;
            }
        }

        public void SetHazardReadability(bool active)
        {
            hazardReadability = active;
            if (!active) return;
            // Also shorten an impact already running when the first falling-rock tell appears.
            shakeStrength = Mathf.Min(shakeStrength, 0.05f);
            shakeRemaining = Mathf.Min(shakeRemaining, 0.10f);
        }

        // Called with normalized stage progress. Audio remains an entry cue, never a frame cue.
        public void SetIntroFeedback(string stage, float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (stage == "inhale" || stage == "intro_inhale")
                introShakeStrength = Mathf.Lerp(0.003f, 0.026f, progress * progress);
            else if (stage == "emerge" || stage == "intro_emerge")
                introShakeStrength = Mathf.Sin(progress * Mathf.PI) * 0.012f;
            else
                introShakeStrength = 0f;
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
            atmosphere.volume = 0.055f;
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
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
                debrisMaterial = new Material(spriteShader) { name = "BossFight_Debris_Unlit", mainTexture = pixelTexture };

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
            clips.Add("wood", Synthesize("DryWoodChip", 0.14f, 11));
            clips.Add("skin", Synthesize("IvorySkinImpact", 0.18f, 12));
            clips.Add("armor", Synthesize("ArmorSplit", 0.55f, 13));
            clips.Add("inhale", Synthesize("DevouringInhale", 2.75f, 14));
            clips.Add("roar", Synthesize("LowRoar", 1.30f, 15));
            clips.Add("lock", Synthesize("TargetLock", 0.16f, 16));
            clips.Add("travel", Synthesize("BuriedTravel", 0.70f, 17));
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
                case "start":
                    atmosphere.UnPause();
                    if (!atmosphere.isPlaying) atmosphere.Play();
                    Play("phase", position, 0.30f);
                    break;
                case "intro_emerge":
                    Play("rumble", position, 0.80f);
                    Shake(0.045f, 0.45f);
                    break;
                case "intro_inhale": Play("inhale", position, 0.63f); break;
                case "intro_silence":
                    effects.Stop();
                    atmosphere.Pause();
                    introShakeStrength = shakeRemaining = shakeStrength = 0f;
                    break;
                case "intro_roar":
                    Play("roar", position, 0.78f);
                    Shake(0.15f, 0.20f);
                    break;
                case "breathe":
                case "groundbreathe":
                    Play("inhale", position, 0.42f);
                    Play("wood", position, 0.26f);
                    break;
                case "roar":
                    Play("roar", position, 0.78f);
                    Shake(0.15f, 0.20f);
                    break;
                case "departure":Play("impact",position,.62f);Shake(.065f,.11f);Burst(position,6,new Color(.48f,.42f,.29f),.65f);break;
                case "burrow_warning":Play("rumble",position,.35f);break;
                case "flight": Play("sweep", position, 0.58f); break;
                case "wave_delay": Play("lock", position, 0.42f); break;
                case "wave":
                    Play("impact", position, 0.60f);
                    Play("sweep", position, 0.46f);
                    Shake(0.06f * amount, 0.12f);
                    break;
                case "rockfall_warning": Play("lock", position, 0.34f); break;
                case "link": Play("wood", position, 0.30f); break;
                case "rockfall_impact":
                    Play("impact", position, 0.48f);
                    Shake(0.028f, 0.085f);
                    break;
                case "armor_break": ArmorBreak(position, Mathf.RoundToInt(strength)); break;
                case "compress": Play("wood", position, 0.55f); Play("rumble", position, 0.27f); break;
                case "jump": Play("sweep", position, 0.64f); break;
                case "lock": Play("lock", position, 0.68f); break;
                case "submerge": Play("rumble", position, 0.68f); break;
                case "travel": Play("travel", position, 0.48f); break;
                case "erupt":
                    Play("impact", position, 0.80f);
                    Shake(0.13f, 0.18f);
                    Burst(position, 12, new Color(0.48f, 0.42f, 0.29f), 0.85f);
                    break;
                case "slash": Play("sweep", position, 0.24f); break;
                case "telegraph": Play("warning", position, 0.56f); break;
                case "rise":
                    Play("rumble", position, 0.65f);
                    Shake(0.04f * amount, 0.16f);
                    Burst(position, 7, new Color(0.40f, 0.34f, 0.22f), 0.6f);
                    break;
                case "release": Play("rumble", position, 0.7f); break;
                case "impact":
                    Play("impact", position, 0.64f);
                    // Slam is heavier than bite but shorter; the shared rockfall cap still applies.
                    Shake(0.18f * amount, 0.13f);
                    Burst(position, 8, new Color(0.55f, 0.49f, 0.34f), amount * 0.7f);
                    break;
                case "land":
                    Play("impact", position, 0.92f);
                    Shake(0.16f * amount, 0.18f);
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
                case "whole_bite":
                    Play("bite",position,.62f*amount);Play("impact",position,.36f*amount);Shake(.080f*amount,.11f);Burst(position,4,new Color(.54f,.44f,.28f),.45f*amount);break;
                case "whole_shift":Play("wood",position,.35f);break;
                case "phase_roar":Play("roar",position,.80f);break;
                case "root_block":
                    // Small dry deflection: no damage confirmation, hit stop or camera shake.
                    Play("wood",position,.23f);Burst(position,2,new Color(.70f,.73f,.66f),.16f);break;
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

        public void PlayerHit(Vector2 position)=>PlayerHit(position,Vector2.zero);
        public void PlayerHit(Vector2 position,Vector2 direction)
        {
            if (!configured) return;
            PlayerHitCount++;LastImpactDirection=direction.normalized;
            Play("hurt", position, 0.90f);
            Shake(.09f,.16f);directionalKick=LastImpactDirection*.045f;
            Burst(position, 7, new Color(0.87f, 0.43f, 0.31f), 0.65f);
        }

        public void BossHit(Vector2 position) => BossHit(position, true);

        public void BossHit(Vector2 position, bool armored)
        {
            if (!configured) return;
            BossHitCount++;
            Shake(.035f,.085f);directionalKick=new Vector2(position.x<0?1:-1,0)*.015f;
            Play(armored ? "wood" : "skin", position, armored ? 0.66f : 0.60f);
            Burst(position, armored ? 5 : 3,
                armored ? new Color(0.60f, 0.43f, 0.25f) : new Color(0.84f, 0.78f, 0.62f),
                armored ? 0.46f : 0.30f);
        }

        public void ArmorBreak(Vector2 position, int brokenCount)
        {
            if (!configured) return;
            Play("armor", position, 0.80f);
            Shake(Mathf.Lerp(0.11f, 0.15f, Mathf.Clamp01(brokenCount / 4f)), 0.16f);
            Burst(position, 12, new Color(0.52f, 0.34f, 0.18f), 0.85f);
        }

        public void Victory()
        {
            if (!configured) return;
            atmosphere.Stop();
            Play("victory", Vector2.zero, 0.68f);
        }

        public void ResetEffects()
        {
            shakeRemaining = shakeDuration = shakeStrength = introShakeStrength = 0f;directionalKick=Vector2.zero;
            hazardReadability = false;
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
            float remainingEnvelope = shakeDuration > 0f ? shakeRemaining / shakeDuration : 0f;
            float currentStrength = shakeStrength * remainingEnvelope * remainingEnvelope;
            duration=Mathf.Min(duration,.22f);
            float limit = hazardReadability ? 0.05f : 0.18f;
            // Compare against the decayed impulse so repeated small hits cannot revive a heavy one.
            shakeStrength = Mathf.Max(currentStrength, Mathf.Min(limit, strength));
            shakeRemaining = Mathf.Max(shakeRemaining, duration);
            if (hazardReadability) shakeRemaining = Mathf.Min(shakeRemaining, 0.10f);
            shakeDuration = shakeRemaining;
        }

        private void LateUpdate()
        {
            if (fightCamera == null) return;
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.unscaledDeltaTime * (hazardReadability ? 1.4f : 1f));
            float envelope = shakeDuration > 0f ? shakeRemaining / shakeDuration : 0f;
            float phase = Time.unscaledTime;
            Vector2 offset = new Vector2(Mathf.Sin(phase * 71f), Mathf.Sin(phase * 93f + 1f));
            float strength = shakeStrength * envelope * envelope + introShakeStrength;
            float limit = hazardReadability ? 0.05f : 0.18f;
            offset = Vector2.ClampMagnitude(offset * strength+directionalKick*envelope*envelope, limit) * ShakeScale;
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
                if (debrisMaterial != null) renderer.sharedMaterial = debrisMaterial;
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
                piece.Velocity += Vector2.down * (piece.Gravity * dt);
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

        // Single V2 synthesis pass. These clips are generated locally, not downloaded.
        // Human listening validation is still required; numerical clipping bounds are not a listening test.
        private AudioClip Synthesize(string name, float duration, int kind)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float low = 0f, medium = 0f;
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float p = t / duration;
                float noise = Range(-1f, 1f);
                low = Mathf.Lerp(low, noise, 0.014f);
                medium = Mathf.Lerp(medium, noise, 0.19f);
                float grain = medium - low;
                float signal;
                float envelope = Mathf.Min(1f, t / 0.003f) * Mathf.Pow(1f - p, 2f);
                switch (kind)
                {
                    case 0:
                        float pulse = Mathf.Repeat(t, 0.14f);
                        signal = (Wave(460f, t) * 0.20f + grain * 0.65f) * Mathf.Exp(-pulse * 38f);
                        break;
                    case 1:
                        signal = low * 2.5f + Wave(57f, t) * 0.24f + Wave(83f, t) * 0.10f;
                        envelope = Mathf.Sin(p * Mathf.PI) * Mathf.Min(1f, t / 0.025f);
                        break;
                    case 2:
                        phase += 2.0 * Math.PI * Mathf.Lerp(94f, 37f, p) / SampleRate;
                        signal = (float)Math.Sin(phase) * 0.54f + grain * Mathf.Exp(-t * 24f) * 0.60f + low * 1.8f;
                        break;
                    case 3:
                        signal = grain * 0.75f;
                        envelope = Mathf.Sin(p * Mathf.PI) * Mathf.Pow(1f - p, 0.5f);
                        break;
                    case 4:
                        signal = grain * Mathf.Exp(-t * 24f) * 0.80f + Wave(185f, t) * 0.20f;
                        break;
                    case 5:
                        signal = (Wave(1430f, t) + Wave(2187f, t) * 0.45f) * Mathf.Exp(-t * 30f) * 0.28f;
                        break;
                    case 6:
                        phase += 2.0 * Math.PI * Mathf.Lerp(170f, 92f, p) / SampleRate;
                        signal = (float)Math.Sin(phase) * 0.34f + grain * 0.6f;
                        break;
                    case 7:
                        signal = Wave(92f, t) * 0.20f + Wave(137f, t) * 0.10f + low * 2f;
                        break;
                    case 8:
                        signal = low * 3f + Wave(45f, t) * 0.35f + grain * Mathf.Exp(-t * 5f) * 0.3f;
                        break;
                    case 9:
                        float note = t < 0.3f ? 261.63f : t < 0.6f ? 329.63f : 392f;
                        phase += 2.0 * Math.PI * note / SampleRate;
                        signal = ((float)Math.Sin(phase) + Wave(523.25f, t) * 0.16f) * 0.27f;
                        envelope = Mathf.Min(1f, t / 0.03f) * Mathf.Min(1f, (duration - t) / 0.5f);
                        break;
                    case 11: // Dry short wood resonances, rather than a sword-on-metal bell.
                        signal = Wave(205f, t) * Mathf.Exp(-t * 34f) * 0.35f
                            + Wave(467f, t) * Mathf.Exp(-t * 52f) * 0.21f
                            + Wave(927f, t) * Mathf.Exp(-t * 85f) * 0.12f
                            + grain * Mathf.Exp(-t * 80f) * 1.2f;
                        break;
                    case 12: // Dense, dull impact on exposed ivory skin.
                        phase += 2.0 * Math.PI * Mathf.Lerp(132f, 76f, p) / SampleRate;
                        signal = (float)Math.Sin(phase) * Mathf.Exp(-t * 24f) * 0.45f + low * 1.6f;
                        break;
                    case 13:
                        float crack = Mathf.Exp(-t * 60f);
                        if (t >= 0.027f) crack += Mathf.Exp(-(t - 0.027f) * 70f) * 0.7f;
                        if (t >= 0.073f) crack += Mathf.Exp(-(t - 0.073f) * 65f) * 0.45f;
                        signal = grain * crack * 1.2f + Wave(163f, t) * 0.22f + low * 1.7f;
                        break;
                    case 14:
                        signal = grain * (0.16f + p * 0.55f) + low * 1.5f
                            + Wave(67f, t) * (0.03f + p * 0.12f);
                        envelope = Mathf.Min(1f, t / 0.4f) * Mathf.Min(1f, (duration - t) / 0.10f);
                        break;
                    case 15:
                        phase += 2.0 * Math.PI * (62f - p * 17f + Mathf.Sin(t * 23f) * 2f) / SampleRate;
                        signal = ((float)Math.Sin(phase) * 0.40f + low * 2f + grain * 0.18f)
                            * (0.8f + 0.2f * Mathf.Sin(t * 41f));
                        envelope = Mathf.Min(1f, t / 0.08f) * Mathf.Pow(1f - p, 0.7f);
                        break;
                    case 16:
                        signal = Wave(620f, t) * Mathf.Exp(-t * 42f) * 0.23f + grain * Mathf.Exp(-t * 65f) * 0.8f;
                        break;
                    case 17:
                        signal = low * 2.5f + grain * 0.18f + Wave(49f, t) * 0.13f;
                        envelope = Mathf.Sin(p * Mathf.PI);
                        break;
                    default:
                        signal = low * 0.6f + Wave(55f, t) * (0.12f + 0.025f * Mathf.Sin(t * Mathf.PI));
                        envelope = Mathf.Min(1f, t / 0.3f) * Mathf.Min(1f, (duration - t) / 0.3f);
                        break;
                }
                samples[i] = Mathf.Clamp(signal * envelope * 0.65f, -0.80f, 0.80f);
            }
            AudioClip clip = AudioClip.Create("BossFight_" + name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float Wave(float frequency, float t) => Mathf.Sin(t * 2f * Mathf.PI * frequency);
        private void OnDisable()
        {
            if (effects != null) effects.Stop();
            if (atmosphere != null) atmosphere.Stop();
            shakeRemaining = shakeDuration = shakeStrength = introShakeStrength = 0f;directionalKick=Vector2.zero;
            hazardReadability = false;
            if (fightCamera != null) fightCamera.transform.position = CameraRest;
        }

        private void OnDestroy()
        {
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
            if (pixel != null) Destroy(pixel);
            if (pixelTexture != null) Destroy(pixelTexture);
            if (debrisMaterial != null) Destroy(debrisMaterial);
        }
    }
}
