using System.Collections.Generic;
using UnityEngine;

namespace FourGuardians.BossFight
{
    /// <summary>Articulated atlas rig. Combat and world displacement belong to the parent controller.</summary>
    public sealed class DocheolVisual : MonoBehaviour
    {
        [SerializeField] private Texture2D atlas;
        [SerializeField] private Texture2D supportsAtlas;
        [SerializeField] private Texture2D armorAtlas;
        [SerializeField] private Rect[] v2Regions = new Rect[13];
        public bool HasV2Artwork => supportsAtlas != null && armorAtlas != null && v2Regions != null && v2Regions.Length == 13 && v2Regions[0].width > 0f;
        private readonly Part[] armorParts = new Part[4];
        private Part hornNear, hornFar, dorsalTrees;
        private SpriteMask floorMask;
        private readonly Dictionary<SpriteRenderer, int> regionByRenderer = new Dictionary<SpriteRenderer, int>();
        private readonly Dictionary<SpriteRenderer, int> armorByRenderer = new Dictionary<SpriteRenderer, int>();
        private readonly List<PlateDebris> detachedPlates = new List<PlateDebris>();
        private readonly float[] localHitFlash = new float[4];
        private readonly bool[] localHitArmor = new bool[4];
        private float lastRootX, horizontalSpeed;
        private readonly Vector2[] localHitPoints = new Vector2[4];
        private string lastPosePattern = "", lastPoseStage = "idle";
        private float lastPoseProgress, lastFacing = -1f;
        private bool introApplied;
        private bool turning;
        private float turnProgress;
        private int turnFrom=-1,turnTo=-1,wholeSide=-1,wholeFirst=-1;
        public void SetWholeBodyState(int first,int side){wholeFirst=first;wholeSide=side;}
        public void SetTurnState(bool active,float progress,int from,int to)
        {turning=active;turnProgress=Mathf.Clamp01(progress);turnFrom=from;turnTo=to;}
        private bool mainVisible = true;
        private float rootDeflectUntil;
        public void OnRootBlocked(){rootDeflectUntil=Time.time+.10f;}
        public bool TryGetDamageArtBounds(out Bounds solid,out Bounds fan)
        {
            solid=fan=default;if(body==null || roots==null || body.graphics.Count==0 || roots.graphics.Count==0)return false;
            solid=body.graphics[0].bounds;foreach(var sr in body.graphics)solid.Encapsulate(sr.bounds);
            // Preserve painted body/forepaws/jaw at their overlap with the root fan.
            foreach(var part in new[]{nearPaw,farPaw,jaw,mouth})if(part!=null)foreach(var sr in part.graphics)solid.Encapsulate(sr.bounds);
            // Attached body armor remains a valid target even where it overhangs the ivory body.
            foreach(var part in armorParts)if(part!=null)foreach(var sr in part.graphics)if(sr.enabled)solid.Encapsulate(sr.bounds);
            fan=roots.graphics[0].bounds;foreach(var sr in roots.graphics)fan.Encapsulate(sr.bounds);return true;
        }
        private sealed class PlateDebris
        {
            public Transform transform;
            public SpriteRenderer renderer;
            public Vector2 velocity;
            public float age, spin;
        }
        public float VisualHeight => 4.3f;
        private Transform rig;
        private Part body, nearPaw, farPaw, roots, jaw, mouth, airborneRoot;
        private Bounds attackWarning, attackActive;
        private bool hasAttackGeometry, airborneRootVisible;
        public Vector2 MouthWorldPosition => mouth != null ? (Vector2)mouth.joint.position : (Vector2)transform.position;
        public Vector2 NearHoofWorldPosition => HoofPoint(nearPaw);
        public Vector2 FarHoofWorldPosition => HoofPoint(farPaw);

        // Bounds remain combat-owned. These inputs only align painted limbs with their tells.
        public void SetAttackGeometry(Bounds warning, Bounds active, float floorY)
        {
            attackActive = active; groundY = floorY;
            if (warning.size.x > 0f) { attackWarning = warning; hasAttackGeometry = true; }
            else if (presentationAttackStage != "recover") hasAttackGeometry = false;
        }
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        private readonly List<Part> parts = new List<Part>();
        private readonly Dictionary<Texture2D, Material> textureMaterials = new Dictionary<Texture2D, Material>();
        private SpriteRenderer groundShadow;
        private readonly List<Texture2D> runtimeTextures = new List<Texture2D>();
        [SerializeField] private Shader whiteFlashShader;
        public float WholeFlashAmount=>flash;
        public bool WholeWhiteFlashEnabled=>whiteFlashShader!=null;
        public void ConfigureWhiteFlashShader(Shader shader){whiteFlashShader=shader;}
        private MaterialPropertyBlock flashBinding;
        private float flash, death, groundY;
        private bool built;
        // Authoritative presentation inputs; visuals are integrated after reference review.
        private int presentationPhase = 1, presentationArmorMask = 15;
        private string presentationAttackStage = "idle", presentationIntroStage = "hidden";
        private float presentationAttackProgress, presentationTransitionProgress, presentationIntroProgress;
        private bool presentationUnderground, presentationTargetLocked;
        private readonly Queue<ArmorBreakVisualEvent> pendingArmorBreaks = new Queue<ArmorBreakVisualEvent>(4);
        private struct ArmorBreakVisualEvent { public int index, remainingMask; public Vector2 point; }
        public void SetCombatState(int phase, int armorMask, string attackStage, float attackProgress,
            float transitionProgress, bool underground, bool locked)
        {
            presentationPhase = Mathf.Clamp(phase, 1, 2);
            presentationArmorMask = armorMask & 15;
            presentationAttackStage = string.IsNullOrEmpty(attackStage) ? "idle" : attackStage.ToLowerInvariant();
            presentationAttackProgress = Mathf.Clamp01(attackProgress);
            presentationTransitionProgress = Mathf.Clamp01(transitionProgress);
            presentationUnderground = underground;
            presentationTargetLocked = locked;
        }
        public void SetIntro(string stage, float progress)
        {
            presentationIntroStage = string.IsNullOrEmpty(stage) ? "hidden" : stage.ToLowerInvariant();
            presentationIntroProgress = Mathf.Clamp01(progress);
            if (built) ApplyIntroPose();
        }
        public void OnArmorBroken(int index, int mask, Vector2 point)
        {
            if (index < 0 || index > 3) return;
            presentationArmorMask = mask & 15;
            if (pendingArmorBreaks.Count >= 16) pendingArmorBreaks.Dequeue();
            pendingArmorBreaks.Enqueue(new ArmorBreakVisualEvent { index=index, remainingMask=mask & 15, point=point });
        }
        public void OnLocalHit(Vector2 point, int region, bool hasArmor)
        {
            region = Mathf.Clamp(region, 0, 3);
            // Keep separate hits in one frame, and let each mark follow the moving rig.
            localHitPoints[region] = transform.InverseTransformPoint(point);
            localHitFlash[region] = 1f;
            localHitArmor[region] = hasArmor;
            ApplyColors();
        }


        private sealed class Part
        {
            public Transform joint;
            public Vector3 restPosition;
            public readonly List<SpriteRenderer> graphics = new List<SpriteRenderer>();
        }

        public void Configure(Texture2D texture)
        {
            atlas = texture;
            // Scene serialization retains only the texture, never transient Sprite.Create results.
            if (Application.isPlaying) { ClearRig(); BuildRig(); }
        }
        public void ConfigureV2(Texture2D core, Texture2D supports, Texture2D armor)
        {
            atlas = core; supportsAtlas = supports; armorAtlas = armor;
            if (Application.isPlaying) { ClearRig(); BuildRig(); }
        }

        // Regions are actual pixel rectangles measured from the atlas top-left, in the agreed 13-part order.
        public void ConfigureV2Regions(Rect[] regions)
        {
            if (regions == null || regions.Length != 13) throw new System.ArgumentException("Docheol V2 requires exactly 13 atlas regions.");
            v2Regions = (Rect[])regions.Clone();
            if (Application.isPlaying) { ClearRig(); BuildRig(); }
        }
        private void Awake() { BuildRig(); }

        private void BuildRig()
        {
            if (built || atlas == null) return;
            built = true;
            groundY = transform.parent != null ? transform.parent.position.y : transform.position.y;
            rig = new GameObject("Docheol_ArticulatedRig").transform;
            rig.SetParent(transform, false);
            lastRootX = transform.position.x;
            if (HasV2Artwork)
            {
                BuildV2Rig(); BuildGroundShadow(); BuildFloorMask();
                SetPose("", "idle", 0f, -1f); return;
            }

            roots = MakePart("RootFan_NoHindLegs", new Vector2(1.25f, 2.385f));
            AddVisual(roots, "Roots", new Rect(0,585,695,430), new Vector2(.40f,.90f), Vector2.zero, new Vector2(3.70f,2.65f), 10);
            farPaw = MakePart("FarForepaw", new Vector2(-2.05f,1.41f));
            AddVisual(farPaw, "FarPaw", new Rect(1160,110,355,450), new Vector2(.5f,.85f), Vector2.zero, new Vector2(1.50f,1.64f), 11);
            mouth = MakePart("MouthCavity", new Vector2(-1.20f,1.50f));
            AddVisual(mouth, "Throat", new Rect(1170,585,355,420), new Vector2(.5f,.5f), Vector2.zero, new Vector2(1.94f,2.20f), 12);
            body = MakePart("HornedUpperBody", new Vector2(-.06f,.52f));
            // Exclude the baked lower jaw. Two contiguous crops retain the horns and right shoulder.
            AddVisual(body, "Crown", new Rect(0,0,715,350), new Vector2(.55f,0f), new Vector2(0f,1.078f), new Vector2(4.66f,2.315f), 13);
            AddVisual(body, "Shoulder", new Rect(400,350,315,220), Vector2.zero, new Vector2(.044f,-.377f), new Vector2(2.053f,1.455f), 13);
            jaw = MakePart("LowerJaw", new Vector2(-.38f,1.60f));
            AddVisual(jaw, "Jaw", new Rect(695,625,410,345), new Vector2(.90f,.80f), Vector2.zero, new Vector2(2.05f,1.75f), 14);
            nearPaw = MakePart("NearForepaw", new Vector2(.18f,1.43f));
            AddVisual(nearPaw, "NearPaw", new Rect(740,100,370,460), new Vector2(.5f,.85f), Vector2.zero, new Vector2(1.70f,1.65f), 15);
            BuildGroundShadow();
            SetPose("", "idle", 0f, -1f);
        }

        private Part MakePart(string label, Vector2 position)
        {
            GameObject obj = new GameObject(label);
            obj.transform.SetParent(rig, false);
            Part part = new Part { joint = obj.transform, restPosition = new Vector3(position.x,position.y,0f) };
            parts.Add(part);
            ResetPart(part);
            return part;
        }

        private void AddVisual(Part part, string label, Rect source, Vector2 pivot, Vector2 position, Vector2 size, int order)
        {
            float sx = atlas.width / 1536f, sy = atlas.height / 1024f;
            Rect pixels = new Rect(source.x*sx, atlas.height-source.yMax*sy, source.width*sx, source.height*sy);
            Texture2D spriteTexture = atlas;
            if (label == "Crown" && atlas.isReadable)
            {
                spriteTexture = FeatherMouthJoin(pixels);
                pixels = new Rect(0,0,spriteTexture.width,spriteTexture.height);
            }
            Sprite sprite = Sprite.Create(spriteTexture, pixels, pivot, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "Docheol_" + label;
            sprites.Add(sprite);
            GameObject obj = new GameObject(label);
            obj.transform.SetParent(part.joint, false);
            obj.transform.localPosition = new Vector3(position.x,position.y,0f);
            obj.transform.localScale = new Vector3(size.x/sprite.bounds.size.x,size.y/sprite.bounds.size.y,1f);
            SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            BindSpriteTexture(renderer, spriteTexture);
            renderers.Add(renderer);
            part.graphics.Add(renderer);
            regionByRenderer[renderer] = 0;
        }

        // Each generated texture owns a material and an explicit per-renderer binding.
        // A shared custom sprite material across atlas/crown/shadow can inherit the last bound texture.
        private void BindSpriteTexture(SpriteRenderer renderer, Texture2D texture)
        {
            if (!textureMaterials.TryGetValue(texture, out Material material))
            {
                Shader shader=whiteFlashShader!=null?whiteFlashShader:Shader.Find("Sprites/Default");
                if(shader==null)return;
                material=new Material(shader) { name="Docheol_"+texture.name+"_Material" };
                material.mainTexture=texture;
                material.SetTexture("_MainTex",texture);
                material.enableInstancing=false;
                textureMaterials.Add(texture,material);
            }
            renderer.sharedMaterial=material;
            MaterialPropertyBlock binding=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(binding);
            binding.SetTexture("_MainTex",texture);
            renderer.SetPropertyBlock(binding);
        }

        public void SetPose(string pattern, string stage, float normalized, float facing)
        {
            if (!built) BuildRig();
            if (!built) return;
            lastPosePattern = pattern; lastPoseStage = stage; lastPoseProgress = normalized; lastFacing = facing;
            horizontalSpeed = Time.deltaTime > 0f ? (transform.position.x-lastRootX)/Time.deltaTime : 0f;
            lastRootX = transform.position.x;
            introApplied = false;
            rig.localPosition = HasV2Artwork ? new Vector3(facing*1.2f,0,0) : Vector3.zero;
            rig.localRotation = Quaternion.identity;
            float p = Mathf.Clamp01(normalized);
            string state = (stage ?? "idle").ToLowerInvariant();
            string move = (pattern ?? "").ToUpperInvariant();
            rig.localScale = new Vector3(facing > 0f ? -1f : 1f,1f,1f);
            foreach (Part part in parts) ResetPart(part);
            float breath = Mathf.Sin(Time.time*1.8f);
            Shift(body,0f,breath*.026f);
            Scale(body,1f,1f+breath*.007f);
            Rotate(jaw,breath*.65f);
            Scale(mouth,1f,1f+breath*.018f);
            Rotate(roots,Mathf.Sin(Time.time*1.1f)*.55f);
            airborneRootVisible = false;
            ApplyAttackPose(move, state, p);
            if (move.StartsWith("MV01"))
            {
                float crawl = Time.time*5f;
                Shift(nearPaw,Mathf.Sin(crawl)*.17f,Mathf.Max(0f,Mathf.Cos(crawl))*.15f);
                Shift(farPaw,-Mathf.Sin(crawl)*.15f,Mathf.Max(0f,-Mathf.Cos(crawl))*.13f);
                Scale(roots,1f+Mathf.Sin(crawl-.7f)*.035f,1f);
                Shift(body,0f,Mathf.Abs(Mathf.Sin(crawl))*.035f);
            }
            if (state == "transition")
            {
                float roar = Mathf.Sin(Mathf.PI*p);
                OpenMouth(roar*.8f);
                Shift(nearPaw,-.18f*roar,.15f*roar);
                Rotate(farPaw,-9f*roar);
                Scale(roots,1f+.12f*roar,1f);
            }
            if (state == "dead") death = Mathf.Max(death,p);
            if (HasV2Artwork) ApplyV2CombatPose(move, state);
            if(turning && HasV2Artwork) ApplyTurnPose();
            else if(HasV2Artwork && (move=="WHOLEBODYBITE" || presentationAttackStage.StartsWith("quick_slam") || presentationAttackStage=="quick_tell" || presentationAttackStage=="burrow_five_tell"))ApplyWholeBodyPose(state);
            else AlignAttackLimbs(move, state);
            if(roots!=null && Time.time<rootDeflectUntil)Shift(roots,Mathf.Sin((rootDeflectUntil-Time.time)*100f)*.025f,0);
            ApplyDeath();
            ApplyVisibility();
            ApplyColors();
            UpdateGroundShadow();
        }

        private void ApplyTurnPose()
        {
            // Fixed ground root pivot. Retract the whole attached front through the center,
            // then extend it on the other side; no instantaneous world-scale mirror.
            rig.localScale=Vector3.one;rig.localPosition=Vector3.zero;
            float sign=-turnFrom*Mathf.Cos(Mathf.PI*turnProgress);
            ResetPart(body);ResetPart(roots);
            roots.joint.localPosition=new Vector3(0,roots.restPosition.y,0);
            roots.joint.localScale=new Vector3(sign,1,1);
            body.joint.localPosition=new Vector3((body.restPosition.x-1.2f)*sign,body.restPosition.y+Mathf.Sin(Mathf.PI*turnProgress)*.28f,0);
            body.joint.localScale=new Vector3(sign,1,1);
            Rotate(body,Mathf.Sin(Mathf.PI*turnProgress)*turnFrom*16f);
            OpenMouth(.12f);
        }

        private void ApplyWholeBodyPose(string state)
        {
            string step=presentationAttackStage;float p=presentationAttackProgress;
            if(step=="quick_tell" || step=="burrow_five_tell")step="whole_tell";
            else if(step=="quick_slam_arc")step="whole_arc";
            else if(step=="quick_slam_hit")step="whole_hit";
            float sign=-wholeSide,angle=wholeSide*30f,lift=0;
            if(step=="whole_tell")
            {
                angle=wholeFirst*30f*Ease(p);
                if(wholeFirst<0 && lastFacing>0)
                {
                    // A world-left first strike still earns a visible committed preparation
                    // when the previous pose faces right. No instant full-body mirror.
                    float retract=Mathf.Clamp01(p/.32f);
                    sign=-lastFacing*Mathf.Cos(Mathf.PI*retract);lift=Mathf.Sin(Mathf.PI*retract)*.20f;
                }
            }
            else if(step=="whole_arc")angle=DocheolWholeMotion.Angle(wholeSide,p);
            else if(step=="whole_hit")angle=-wholeSide*30f;
            else if(step=="whole_cross" || step=="whole_final_windup")
            {
                // Retract/turn early, then hold the reared left-side preparation visibly.
                // Spending the entire delayed beat edge-on would hide the creature's intent.
                float cross=step=="whole_final_windup"?Mathf.Clamp01(p/.30f):p;
                sign=wholeSide*Mathf.Cos(Mathf.PI*cross);angle=wholeSide*30f;
                lift=Mathf.Sin(Mathf.PI*cross)*.25f+(step=="whole_final_windup"?Mathf.Sin(Mathf.PI*p)*.15f:0);
            }
            else if(state=="recovery"){sign=-lastFacing;angle=-lastFacing*30f*(1-Ease(Mathf.Clamp01(p/.55f)));}
            rig.localPosition=Vector3.zero;rig.localScale=Vector3.one;
            ResetPart(body);ResetPart(roots);roots.joint.localPosition=new Vector3(0,roots.restPosition.y,0);roots.joint.localScale=new Vector3(sign,1,1);
            Vector3 offset=new Vector3((body.restPosition.x-1.2f)*sign,body.restPosition.y,0);
            body.joint.localPosition=Quaternion.Euler(0,0,angle)*offset+Vector3.up*lift;
            body.joint.localScale=new Vector3(sign,1,1);Rotate(body,angle);OpenMouth(state=="recovery"?.35f:.80f);
        }

        private void ApplyAttackPose(string move, string state, float fallbackProgress)
        {
            string step = presentationAttackStage;
            float p = presentationAttackProgress;
            if (step == "idle")
            {
                step = state == "telegraph" ? "tell" : state == "active" ? "strike" : state == "recovery" ? "recover" : state;
                p = fallbackProgress;
            }
            bool tell = step == "tell", strike = step == "strike", recover = step == "recover";
            float tension = tell ? Ease(p) : strike ? 1f : recover ? 1f-Ease(p) : 0f;
            float impact = strike ? Ease(Mathf.Clamp01(p/.28f)) : recover ? 1f : 0f;
            if (step == "link" || state == "link")
            {
                // Braced handoff stays distinct from a full recovery's relaxation.
                Shift(body,-.12f,-.045f); Rotate(body,-1.2f);
                Shift(nearPaw,-.22f,.08f); Rotate(nearPaw,-9f);
                Shift(farPaw,-.12f,.035f); Scale(roots,1.04f,.97f);
                OpenMouth(.22f); return;
            }
            if (move.StartsWith("DT01"))
            {
                float lift = tension*(1f-impact);
                Shift(nearPaw,-.90f*lift-2.40f*impact*tension,1.65f*lift+.08f*impact*tension);
                Rotate(nearPaw,-48f*lift+7f*impact*tension);
                Shift(body,-.12f*impact*tension,-.10f*impact*tension);
                Rotate(farPaw,6f*tension); Scale(roots,1f+.07f*impact*tension,1f);
            }
            else if (move.StartsWith("DT02"))
            {
                float sweep = strike ? Ease(p) : recover ? 1f : 0f;
                // A low horizontal foreleg swing, with no independent wave.
                Shift(nearPaw,(-.6f-2.6f*sweep)*tension,-.04f*tension);
                Rotate(nearPaw,(-5f+10f*sweep)*tension);
                Shift(farPaw,-.18f*sweep*tension,0f);
                Shift(body,-.12f*tension,-.045f*tension);
                Rotate(body,1.5f*tension); Scale(roots,1f+.06f*tension,1f-.04f*tension);
            }
            else if (move.StartsWith("DT03"))
            {
                // Horizontal recoil -> accelerated front extension -> jaw snap -> withdrawal.
                // The head advances through its locked mouth volume, without a vertical slam.
                float ready=tell?Ease(p):0;
                float snap=strike?Ease(Mathf.Clamp01(p/.32f)):recover?1-Ease(p):0;
                Shift(body,.14f*ready-.46f*snap,-.018f*snap);Rotate(body,1.6f*snap);
                Scale(body,1+.045f*snap,1-.015f*snap);
                Shift(jaw,-.08f*ready-.12f*snap,.31f*snap);Rotate(jaw,13f*ready-11f*snap);
                Scale(jaw,1,1-.14f*snap);Scale(mouth,1+.10f*ready,1+.12f*ready-.58f*snap);
                Rotate(nearPaw,-4f*ready);Shift(farPaw,-.16f*snap,0);Scale(roots,1+.04f*snap,1);
            }
            else if (move == "AROCKFALL")
            {
                if (step == "breathe")
                {
                    float inhale=Ease(p);
                    OpenMouth(.30f*inhale); Scale(body,1f+.025f*inhale,1f+.045f*inhale);
                    Scale(mouth,1f+.045f*inhale,1f+.08f*inhale);
                    Rotate(nearPaw,-5f*inhale); Scale(roots,1f+.035f*inhale,1f);
                }
                else if (step == "roar")
                {
                    float burst=Mathf.Sin(Mathf.PI*Mathf.Clamp01(p));
                    OpenMouth(.9f); Rotate(jaw,17f); Scale(mouth,1.12f,1.19f);
                    Shift(body,.045f*Mathf.Sin(p*48f)*burst,.06f);
                    Rotate(body,-2f); Shift(nearPaw,-.12f,0f); Shift(farPaw,-.1f,0f);
                    Scale(roots,1.07f,1f);
                }
            }
            else if (move == "BBURROWLEAP")
            {
                if (step == "submerge")
                {
                    float dig=Ease(p);
                    Rotate(body,-11f*dig); Shift(nearPaw,-.32f*dig,.10f*dig); Rotate(nearPaw,21f*dig);
                    Shift(farPaw,-.24f*dig,.08f*dig); Rotate(farPaw,16f*dig);
                    Scale(roots,1f-.16f*dig,1f-.25f*dig);
                }
                else if (step == "rise" || step == "departure" || step == "flight")
                {
                    float height=Mathf.Max(0f,transform.position.y-groundY);
                    float tuck=Mathf.Clamp01(height/1.2f);
                    Shift(nearPaw,.18f*tuck,.63f*tuck); Rotate(nearPaw,-35f*tuck);
                    Shift(farPaw,.14f*tuck,.51f*tuck); Rotate(farPaw,-27f*tuck);
                    Scale(roots,1f-.17f*tuck,1f-.15f*tuck);
                    OpenMouth(.35f*tuck);Rotate(body,-12f*(1-2*p));
                    if (airborneRoot != null)
                    {
                        // The new forward arc is an airborne body, not a vertical pillar tethered to the floor.
                        airborneRootVisible=false;
                    }
                }
                else if (step == "land" || step == "wave_delay" || step == "wave" || recover)
                {
                    float settle=step=="land"?1f-.45f*Ease(p):step=="wave_delay"?.55f:step=="wave"?.55f*(1f-Ease(p)):0f;
                    Shift(body,0f,-.17f*settle); Scale(body,1f+.05f*settle,1f-.08f*settle);
                    Shift(nearPaw,-.18f*settle,0f); Shift(farPaw,-.1f*settle,0f);
                    Scale(roots,1f+.13f*settle,1f-.09f*settle);
                }
            }
        }

        private Vector2 HoofPoint(Part paw)
        {
            if (paw==null || paw.graphics.Count==0) return transform.position;
            SpriteRenderer graphic=paw.graphics[0];
            Bounds bounds=graphic.sprite.bounds;
            return graphic.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.10f,0f));
        }

        private void AlignAttackLimbs(string move,string state)
        {
            if (!hasAttackGeometry || presentationUnderground || state=="link") return;
            string step=presentationAttackStage;
            float p=presentationAttackProgress;
            bool tell=step=="tell", strike=step=="strike", recover=step=="recover";
            if (!tell && !strike && !recover) return;
            float blend=tell?Ease(p):strike?1f:1f-Ease(p);
            Vector2 target;
            if (move.StartsWith("DT01"))
            {
                float fall=strike?Ease(Mathf.Clamp01(p/.28f)):recover?1f:0f;
                target=new Vector2(attackWarning.center.x-lastFacing*.65f*(1f-fall),Mathf.Lerp(groundY+3.0f,groundY+.18f,fall));
            }
            else if (move.StartsWith("DT02"))
            {
                Bounds footprint=attackWarning;
                float inner=lastFacing<0f?footprint.max.x:footprint.min.x;
                float outer=lastFacing<0f?footprint.min.x:footprint.max.x;
                float travel=strike?Ease(p):recover?1f:0f;
                target=new Vector2(Mathf.Lerp(inner+lastFacing*.55f,outer-lastFacing*.55f,travel),groundY+.22f);
            }
            else return;
            // Aim from the original shoulder so a distant strike never becomes a detached hoof.
            Vector2 desired=Vector2.Lerp(HoofPoint(nearPaw),target,blend);
            ResetPart(nearPaw);
            SpriteRenderer hoof=nearPaw.graphics[0];
            Bounds spriteBounds=hoof.sprite.bounds;
            Vector3 toe=hoof.transform.TransformPoint(new Vector3(spriteBounds.center.x,spriteBounds.min.y+spriteBounds.size.y*.10f,0f));
            Vector2 restToe=nearPaw.joint.InverseTransformPoint(toe);
            Vector2 localTarget=nearPaw.joint.parent.InverseTransformPoint(desired)-(Vector3)nearPaw.restPosition;
            float extension=localTarget.magnitude/Mathf.Max(.01f,restToe.magnitude);
            float angle=Mathf.Atan2(localTarget.y,localTarget.x)*Mathf.Rad2Deg-Mathf.Atan2(restToe.y,restToe.x)*Mathf.Rad2Deg;
            Rotate(nearPaw,angle); Scale(nearPaw,1f,extension);
            // The cuff follows the shoulder but retains its own plate proportions.
            if(armorParts[1]!=null) Scale(armorParts[1],1f,1f/Mathf.Max(.1f,extension));
        }
        private void BuildV2Rig()
        {
            body = MakePart("BareIvoryBody", new Vector2(-.08f,.48f));
            Rect bodyRect=v2Regions[0];
            float fraction=.61f;
            // The lower tip belongs behind the separate jaw; crop only the front segment.
            const float frontBottomCut = 72f;
            float frontHeight = 3.1f * (bodyRect.height-frontBottomCut) / bodyRect.height;
            float frontY = -.372f + 3.1f * frontBottomCut / bodyRect.height;
            Rect front=new Rect(bodyRect.x,bodyRect.y,bodyRect.width*fraction,bodyRect.height-frontBottomCut);
            Rect back=new Rect(bodyRect.x+bodyRect.width*fraction,bodyRect.y,bodyRect.width*(1f-fraction),bodyRect.height);
            AddV2Visual(body,"BodyFront",atlas,front,Vector2.zero,new Vector2(-2.457f,frontY),new Vector2(4.55f*fraction,frontHeight),13,0);
            AddV2Visual(body,"BodyRear",atlas,back,Vector2.zero,new Vector2(-2.457f+4.55f*fraction,-.372f),new Vector2(4.55f*(1f-fraction),3.1f),13,2);
            roots=MakePart("RearRoots_NoHindLegs",new Vector2(1.20f,2.16f));
            AddV2(roots,"Roots",5,new Vector2(.4f,.9f),new Vector2(3.75f,2.40f),10,3);
            // Reuse the existing root region behind the fan: no new anatomy or atlas region.
            airborneRoot=MakePart("AirborneRoot_GroundConnection",new Vector2(1.22f,2.16f));
            AddV2(airborneRoot,"ConnectedRearRoots",5,new Vector2(.5f,1f),new Vector2(.88f,2.16f),10,3);
            farPaw=MakeAttachedPart("FarForeleg_ClovenHoof",new Vector2(-1.95f,1.08f),body);
            AddV2(farPaw,"FarHoof",4,new Vector2(.5f,.86f),new Vector2(1.34f,1.25f),11,1);
            mouth=MakeAttachedPart("MouthCavity",new Vector2(-1.25f,1.62f),body);
            AddV2(mouth,"Mouth",2,new Vector2(.5f,.5f),new Vector2(2.05f,2.70f),12,0);
            jaw=MakeAttachedPart("LowerJaw",new Vector2(-.30f,1.56f),body);
            AddV2(jaw,"Jaw",1,new Vector2(.90f,.80f),new Vector2(2.30f,1.65f),14,0);
            nearPaw=MakeAttachedPart("NearForeleg_ClovenHoof",new Vector2(.22f,1.48f),body);
            AddV2(nearPaw,"NearHoof",3,new Vector2(.5f,.86f),new Vector2(1.48f,1.70f),15,1);
            hornFar=MakeAttachedPart("FarWoodHorn",new Vector2(-2.04f,2.42f),body);
            AddV2(hornFar,"FarHorn",7,new Vector2(.26f,.04f),new Vector2(1.20f,1.45f),16,0);
            hornNear=MakeAttachedPart("NearWoodHorn",new Vector2(-.50f,2.98f),body);
            AddV2(hornNear,"NearHorn",6,new Vector2(.24f,.06f),new Vector2(2.08f,1.30f),16,0);
            dorsalTrees=MakeAttachedPart("TwoDorsalRedTrees_Fixed",new Vector2(.90f,2.25f),body);
            AddV2(dorsalTrees,"TwoTrees",8,new Vector2(.35f,.1f),new Vector2(2.45f,1.55f),11,2);
            armorParts[0]=MakeAttachedPart("Armor_0_ForeheadCrown",new Vector2(-.65f,2.69f),body);
            AddV2(armorParts[0],"ForeheadArmor",9,new Vector2(.5f,.5f),new Vector2(3.18f,1.02f),17,0,0);
            armorParts[1]=MakeAttachedPart("Armor_1_ShoulderCuff",new Vector2(.30f,1.52f),nearPaw);
            AddV2(armorParts[1],"ShoulderArmor",11,new Vector2(.5f,.5f),new Vector2(1.50f,1.10f),17,1,1);
            armorParts[2]=MakeAttachedPart("Armor_2_CheekPlate",new Vector2(.32f,2.35f),body);
            AddV2(armorParts[2],"CheekArmor",10,new Vector2(.5f,.5f),new Vector2(1.38f,1.75f),17,2,2);
            armorParts[3]=MakeAttachedPart("Armor_3_DorsalPlate",new Vector2(1.20f,1.25f),body);
            AddV2(armorParts[3],"DorsalArmor",12,new Vector2(.5f,.5f),new Vector2(2.65f,2.20f),17,3,3);
        }

        private Part MakeAttachedPart(string label,Vector2 absoluteRest,Part parent)
        {
            Part part=MakePart(label,absoluteRest);
            Vector3 parentOrigin=rig.InverseTransformPoint(parent.joint.position);
            part.restPosition-=parentOrigin;
            part.joint.SetParent(parent.joint,false);
            ResetPart(part);
            return part;
        }
        private void AddV2(Part part,string label,int index,Vector2 pivot,Vector2 size,int order,int region,int armorIndex=-1)
        {
            Texture2D texture=index<3?atlas:index<9?supportsAtlas:armorAtlas;
            AddV2Visual(part,label,texture,v2Regions[index],pivot,Vector2.zero,size,order,region,armorIndex);
        }
        private void AddV2Visual(Part part,string label,Texture2D texture,Rect source,Vector2 pivot,Vector2 position,Vector2 size,int order,int region,int armorIndex=-1)
        {
            Rect pixels=new Rect(source.x,texture.height-source.yMax,source.width,source.height);
            if(pixels.xMin<0f||pixels.yMin<0f||pixels.xMax>texture.width||pixels.yMax>texture.height||pixels.width<=0f||pixels.height<=0f)
                throw new System.ArgumentOutOfRangeException(label,"Atlas part extends outside its texture.");
            Sprite sprite=Sprite.Create(texture,pixels,pivot,100f,0,SpriteMeshType.FullRect);
            sprite.name="DocheolV2_"+label;sprites.Add(sprite);
            GameObject obj=new GameObject(label);obj.transform.SetParent(part.joint,false);
            obj.transform.localPosition=new Vector3(position.x,position.y,0f);
            obj.transform.localScale=new Vector3(size.x/sprite.bounds.size.x,size.y/sprite.bounds.size.y,1f);
            SpriteRenderer renderer=obj.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sortingOrder=order;
            BindSpriteTexture(renderer,texture);
            renderer.maskInteraction=SpriteMaskInteraction.VisibleInsideMask;
            renderers.Add(renderer);part.graphics.Add(renderer);
            regionByRenderer[renderer]=region;
            if(armorIndex>=0)armorByRenderer[renderer]=armorIndex;
        }

        private void ApplyV2CombatPose(string move,string state)
        {
            string step=presentationAttackStage;
            if(state=="idle" && Mathf.Abs(horizontalSpeed)>.08f)
            {
                float cycle=Time.time*(presentationPhase==2?8.6f:4.6f);
                float light=presentationPhase==2?1.6f:1f;
                Shift(nearPaw,Mathf.Sin(cycle)*.12f,Mathf.Max(0f,Mathf.Cos(cycle))*.10f*light);
                Shift(farPaw,-Mathf.Sin(cycle)*.12f,Mathf.Max(0f,-Mathf.Cos(cycle))*.10f*light);
                Shift(body,0f,Mathf.Abs(Mathf.Sin(cycle))*.025f*light);
                Scale(roots,1f+Mathf.Sin(cycle-.7f)*.035f,1f);
            }
            if(state=="transition")
            {
                ResetPart(body);ResetPart(roots);ResetPart(nearPaw);ResetPart(farPaw);ResetPart(jaw);ResetPart(mouth);
                bool roaring=step=="phase_roar";
                float slump=roaring?1-Ease(presentationAttackProgress):1f;
                Shift(body,0,-.23f*slump);Rotate(body,2.5f*slump);Rotate(nearPaw,8f*slump);
                OpenMouth(roaring?.95f:.12f);
                if(roaring){Rotate(jaw,17);Scale(mouth,1.12f,1.18f);Shift(body,Mathf.Sin(Time.time*38)*.025f,.03f);}
            }
            // Dorsal trees are one fixed two-tree sprite; losing armor never adds foliage or swaps hooves.
            if(dorsalTrees!=null)Rotate(dorsalTrees,Mathf.Sin(Time.time*1.2f)*.65f);
        }

        private void ApplyIntroPose()
        {
            if(!built)return;
            string stage=presentationIntroStage;
            float p=presentationIntroProgress;
            if(stage=="done")
            {
                if(introApplied)SetPose(lastPosePattern,lastPoseStage,lastPoseProgress,lastFacing);
                return;
            }
            introApplied=true;
            float emergence=stage=="buried"?0f:stage=="emerge"?Ease(p):1f;
            rig.localPosition=new Vector3(0f,Mathf.Lerp(-2.20f,0f,emergence),0f);
            mainVisible=stage!="buried";
            if(stage=="emerge")
            {
                Rotate(body,-3f*(1f-emergence));
                Shift(nearPaw,-.30f*(1f-emergence),.18f*(1f-emergence));
                Shift(farPaw,-.20f*(1f-emergence),.10f*(1f-emergence));
            }
            if(stage=="inhale")
            {
                float swell=Mathf.Sin(Mathf.PI*p);
                OpenMouth(.50f+.30f*swell);
                Scale(mouth,1f+.07f*swell,1f+.10f*swell);
                Scale(body,1f+.015f*swell,1f+.015f*swell);
            }
            if(stage=="silence")
            {
                ResetPart(body);ResetPart(jaw);ResetPart(mouth);ResetPart(nearPaw);ResetPart(farPaw);ResetPart(roots);
            }
            float hornGrowth=stage=="roar"?Mathf.Lerp(.025f,1f,Ease(Mathf.Clamp01(p/.72f))):.025f;
            if(hornNear!=null)Scale(hornNear,hornGrowth,hornGrowth);
            if(hornFar!=null)Scale(hornFar,hornGrowth,hornGrowth);
            if(stage=="roar")
            {
                float roar=Mathf.Sin(Mathf.PI*p);
                OpenMouth(.8f*roar);Rotate(body,-1.7f*roar);
                Shift(nearPaw,-.1f*roar,.07f*roar);Scale(roots,1f+.07f*roar,1f);
            }
            ApplyVisibility(true);ApplyColors();UpdateFloorMask();
        }

        private void ApplyVisibility(bool preserveIntro=false)
        {
            if(!preserveIntro)mainVisible=!presentationUnderground;
            foreach(SpriteRenderer renderer in renderers)
            {
                bool visible=mainVisible && (airborneRoot==null || !airborneRoot.graphics.Contains(renderer) || airborneRootVisible);
                if(armorByRenderer.TryGetValue(renderer,out int index))visible&=(presentationArmorMask&(1<<index))!=0;
                renderer.enabled=visible;
            }
        }

        private void BuildFloorMask()
        {
            Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            texture.name="Docheol_FloorClip";texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply(false,true);
            runtimeTextures.Add(texture);
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),2f);sprites.Add(sprite);
            GameObject obj=new GameObject("WorldFloorClip");obj.transform.SetParent(transform,false);
            floorMask=obj.AddComponent<SpriteMask>();floorMask.sprite=sprite;
            floorMask.isCustomRangeActive=true;floorMask.backSortingOrder=9;floorMask.frontSortingOrder=17;
            UpdateFloorMask();
        }
        private void UpdateFloorMask()
        {
            if(floorMask==null)return;
            floorMask.transform.position=new Vector3(0f,groundY+12f,transform.position.z);
            floorMask.transform.localScale=new Vector3(48f,24f,1f);
        }

        private void LateUpdate()
        {
            if(!built)return;
            while(pendingArmorBreaks.Count>0)
            {
                ArmorBreakVisualEvent info=pendingArmorBreaks.Dequeue();
                SpawnArmorDebris(info);
            }
            for(int i=0;i<4;i++)localHitFlash[i]=Mathf.MoveTowards(localHitFlash[i],0f,Time.deltaTime*7f);
            for(int i=detachedPlates.Count-1;i>=0;i--)
            {
                PlateDebris plate=detachedPlates[i];plate.age+=Time.deltaTime;
                plate.velocity.y-=7.8f*Time.deltaTime;
                plate.transform.position+=(Vector3)(plate.velocity*Time.deltaTime);
                plate.transform.Rotate(0f,0f,plate.spin*Time.deltaTime);
                Vector3 point=plate.transform.position;
                if(point.y<groundY+.12f)
                {
                    point.y=groundY+.12f;plate.transform.position=point;
                    plate.velocity=new Vector2(plate.velocity.x*.70f,Mathf.Abs(plate.velocity.y)*.18f);plate.spin*=.7f;
                }
                plate.renderer.color=new Color(.88f,.82f,.70f,1f-Mathf.InverseLerp(.65f,1.55f,plate.age));
                if(plate.age>=1.55f){Destroy(plate.transform.gameObject);detachedPlates.RemoveAt(i);}
            }
            ApplyColors();UpdateFloorMask();
        }
        private void SpawnArmorDebris(ArmorBreakVisualEvent info)
        {
            Part plate=armorParts[info.index];
            if(plate==null||plate.graphics.Count==0)return;
            SpriteRenderer source=plate.graphics[0];
            GameObject obj=new GameObject("DetachedArmor_"+info.index);
            obj.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            obj.transform.localScale=source.transform.lossyScale;
            SpriteRenderer renderer=obj.AddComponent<SpriteRenderer>();renderer.sprite=source.sprite;renderer.sortingOrder=17;
            BindSpriteTexture(renderer,source.sprite.texture);renderer.maskInteraction=SpriteMaskInteraction.VisibleInsideMask;
            float direction=(source.transform.position.x-transform.position.x)>=0f?1f:-1f;
            detachedPlates.Add(new PlateDebris{transform=obj.transform,renderer=renderer,velocity=new Vector2(direction*(1.8f+info.index*.25f),3.6f+info.index*.25f),spin=direction*(95f+info.index*24f)});
        }

        // Fade only the red/dark inner wall at the crop join. Cream teeth, lip and shoulder stay crisp.
        // This is a runtime rig mask; the imported artwork is never modified.
        private Texture2D FeatherMouthJoin(Rect pixels)
        {
            int width = Mathf.RoundToInt(pixels.width), height = Mathf.RoundToInt(pixels.height);
            Color[] colors = atlas.GetPixels(Mathf.RoundToInt(pixels.x),Mathf.RoundToInt(pixels.y),width,height);
            for (int y=0; y<height; y++)
            {
                float sourceY = 350f-y*350f/height;
                float blend = Mathf.SmoothStep(0f,1f,(350f-sourceY)/82f);
                for (int x=0; x<width; x++)
                {
                    float sourceX = x*715f/width;
                    if (sourceY < 267f || sourceX < 75f || sourceX > 390f) continue;
                    int index = y*width+x;
                    Color c = colors[index];
                    bool innerWall = (c.r > c.g*1.25f && c.r > c.b*1.08f) || Mathf.Max(c.r,Mathf.Max(c.g,c.b)) < .17f;
                    if (innerWall) { c.a *= blend; colors[index]=c; }
                }
            }
            Texture2D texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
            texture.name="Docheol_CrownMouthJoin";
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.SetPixels(colors);
            texture.Apply(false,true);
            runtimeTextures.Add(texture);
            return texture;
        }

        private void BuildGroundShadow()
        {
            const int width=128,height=32;
            Texture2D texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
            texture.name="Docheol_GroundShadow";
            texture.filterMode=FilterMode.Bilinear;
            texture.wrapMode=TextureWrapMode.Clamp;
            Color[] colors=new Color[width*height];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                float nx=(x+.5f)/width*2f-1f, ny=(y+.5f)/height*2f-1f;
                float alpha=Mathf.Pow(Mathf.Clamp01(1f-nx*nx-ny*ny),1.8f);
                colors[y*width+x]=new Color(1f,1f,1f,alpha);
            }
            texture.SetPixels(colors);texture.Apply(false,true);
            runtimeTextures.Add(texture);
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),128f);
            sprites.Add(sprite);
            GameObject obj=new GameObject("FloorAnchoredShadow");
            obj.transform.SetParent(transform,false);
            groundShadow=obj.AddComponent<SpriteRenderer>();
            groundShadow.sprite=sprite;
            groundShadow.sortingOrder=9;
            BindSpriteTexture(groundShadow, texture);
            UpdateGroundShadow();
        }

        private void UpdateGroundShadow()
        {
            if(groundShadow==null)return;
            float rise=Mathf.Clamp01(Mathf.Max(0f,transform.position.y-groundY)/3.1f);
            float mirror=rig.localScale.x;
            groundShadow.transform.position=new Vector3(transform.position.x+.40f*mirror,groundY+.025f,transform.position.z);
            groundShadow.transform.localScale=new Vector3(Mathf.Lerp(5.7f,4.6f,rise),Mathf.Lerp(1.55f,1.12f,rise),1f);
            groundShadow.color=new Color(.025f,.035f,.022f,Mathf.Lerp(.50f,.22f,rise));
        }

        private void OpenMouth(float amount)
        {
            Shift(jaw,-.03f*amount,.12f*amount);
            Rotate(jaw,8f*amount);
            Shift(body,0f,.035f*amount);
        }
        public void SetFlash(float amount) { flash = Mathf.Clamp01(amount); ApplyColors(); }
        public void SetDead(float progress)
        {
            death = Mathf.Clamp01(progress);
            if (built && death > 0f) SetPose("","dead",death,rig.localScale.x < 0f ? 1f : -1f);
        }
        private void ApplyDeath()
        {
            if (death <= 0f) return;
            float collapse = Ease(death);
            Shift(body,.20f*collapse,-.23f*collapse);
            Rotate(body,-7f*collapse);
            Scale(body,1.02f,1f-.16f*collapse);
            Rotate(nearPaw,15f*collapse);
            Rotate(farPaw,-13f*collapse);
            Shift(jaw,0f,-.12f*collapse);
            Scale(roots,1f+.11f*collapse,1f-.17f*collapse);
            Scale(mouth,1f,1f-.17f*collapse);
        }
        private void ApplyColors()
        {
            // Native Unity objects must be created on the main thread, never during serialization.
            if(flashBinding==null)flashBinding=new MaterialPropertyBlock();
            Color color = Color.Lerp(Color.white,new Color(.35f,.31f,.27f,1f),death*.72f);
            // White flash is a per-renderer shader value on attached rig renderers only.
            // Detached plates, the shadow, background and player never receive it.
            if (!HasV2Artwork) color = Color.Lerp(color,new Color(1f,.57f,.42f,1f),flash*.82f);
            foreach (SpriteRenderer renderer in renderers)
            {
                Color partColor=color;
                if(whiteFlashShader==null && regionByRenderer.TryGetValue(renderer,out int region))
                {
                    bool armored=armorByRenderer.ContainsKey(renderer);
                    float amount=localHitFlash[region];
                    if(armored!=localHitArmor[region])amount=0f;
                    Vector2 hitPoint=transform.TransformPoint(localHitPoints[region]);
                    Vector3 nearest=renderer.bounds.ClosestPoint(hitPoint);
                    float distance=Vector2.Distance(nearest,hitPoint);
                    amount*=Mathf.Clamp01(1f-distance/1.35f);
                    partColor=Color.Lerp(partColor,armored?new Color(1f,.77f,.32f,1f):new Color(1f,.48f,.38f,1f),amount*.85f);
                }
                renderer.color=partColor;
                if(whiteFlashShader!=null)
                {
                    renderer.GetPropertyBlock(flashBinding);flashBinding.SetFloat("_WhiteFlash",flash);
                    renderer.SetPropertyBlock(flashBinding);
                }
            }
        }
        private static float Ease(float value) { return Mathf.SmoothStep(0f,1f,value); }
        private static void ResetPart(Part part) { part.joint.localPosition = part.restPosition; part.joint.localScale = Vector3.one; part.joint.localRotation = Quaternion.identity; }
        private static void Shift(Part part,float x,float y) { part.joint.localPosition += new Vector3(x,y,0f); }
        private static void Rotate(Part part,float angle) { part.joint.localRotation = Quaternion.Euler(0f,0f,angle); }
        private static void Scale(Part part,float x,float y) { part.joint.localScale = new Vector3(x,y,1f); }
        private void ClearRig()
        {
            // Destroy is deferred until end of frame; hide the old rig before rebuilding.
            if (rig != null) { rig.gameObject.SetActive(false); Destroy(rig.gameObject); }
            if (floorMask != null) { floorMask.gameObject.SetActive(false); Destroy(floorMask.gameObject); }
            foreach (PlateDebris plate in detachedPlates) if (plate.transform != null)
            { plate.transform.gameObject.SetActive(false); Destroy(plate.transform.gameObject); }
            detachedPlates.Clear();regionByRenderer.Clear();armorByRenderer.Clear();pendingArmorBreaks.Clear();
            System.Array.Clear(localHitFlash,0,localHitFlash.Length);
            System.Array.Clear(localHitArmor,0,localHitArmor.Length);
            System.Array.Clear(localHitPoints,0,localHitPoints.Length);
            System.Array.Clear(armorParts,0,armorParts.Length);
            hornNear=null;hornFar=null;dorsalTrees=null;airborneRoot=null;
            airborneRootVisible=false;hasAttackGeometry=false;attackWarning=default;attackActive=default;
            if (groundShadow != null) { groundShadow.gameObject.SetActive(false); Destroy(groundShadow.gameObject); }
            foreach (Texture2D texture in runtimeTextures) if (texture != null) Destroy(texture);
            runtimeTextures.Clear();
            foreach (Sprite sprite in sprites) if (sprite != null) Destroy(sprite);
            foreach (Material material in textureMaterials.Values) if (material != null) Destroy(material);
            textureMaterials.Clear();
            sprites.Clear(); renderers.Clear(); parts.Clear(); built = false;
            rig=null;floorMask=null;groundShadow=null;
            body=null;nearPaw=null;farPaw=null;roots=null;jaw=null;mouth=null;
            flash=death=horizontalSpeed=0f;rootDeflectUntil=0;
            lastRootX=transform.position.x;
            lastPosePattern="";lastPoseStage="idle";lastPoseProgress=0f;lastFacing=-1f;
            introApplied=false;mainVisible=true;turning=false;turnProgress=0;turnFrom=turnTo=-1;
            // Authoritative phase/armor inputs are retained; only transient visuals reset.
        }
        private void OnDestroy() { ClearRig(); }
    }
}
