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
        private int consumedHitSequence;
        private string lastPosePattern = "", lastPoseStage = "idle";
        private float lastPoseProgress, lastFacing = -1f;
        private bool introApplied;
        private bool mainVisible = true;
        private sealed class PlateDebris
        {
            public Transform transform;
            public SpriteRenderer renderer;
            public Vector2 velocity;
            public float age, spin;
        }
        public float VisualHeight => 4.3f;
        private Transform rig;
        private Part body, nearPaw, farPaw, roots, jaw, mouth;
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        private readonly List<Part> parts = new List<Part>();
        private readonly Dictionary<Texture2D, Material> textureMaterials = new Dictionary<Texture2D, Material>();
        private SpriteRenderer groundShadow;
        private readonly List<Texture2D> runtimeTextures = new List<Texture2D>();
        private float flash, death, groundY;
        private bool built;
        // Authoritative presentation inputs; visuals are integrated after reference review.
        private int presentationPhase = 1, presentationArmorMask = 15;
        private string presentationAttackStage = "idle", presentationIntroStage = "hidden";
        private float presentationAttackProgress, presentationTransitionProgress, presentationIntroProgress;
        private bool presentationUnderground, presentationTargetLocked, presentationHitHasArmor;
        private Vector2 presentationHitPoint;
        private int presentationHitRegion, presentationHitSequence;
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
            presentationHitPoint = point; presentationHitRegion = Mathf.Clamp(region, 0, 3);
            presentationHitHasArmor = hasArmor; presentationHitSequence++;
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
                Shader shader=Shader.Find("Sprites/Default");
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
            rig.localPosition = Vector3.zero;
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
            bool tell = state == "telegraph", active = state == "active", recovery = state == "recovery";
            float prepare = tell ? Ease(p) : active ? 1f : recovery ? 1f-Ease(p) : 0f;
            float strike = active ? Ease(Mathf.Clamp01(p*4.5f)) : recovery ? 1f : 0f;
            float recoil = recovery ? 1f-Ease(p) : 0f;
            if (move.StartsWith("DT01"))
            {
                float lift = prepare*(1f-strike);
                Shift(nearPaw,-1f*lift-1.90f*strike*prepare,1.24f*lift+.12f*strike*prepare);
                Rotate(nearPaw,-56f*lift+12f*strike*prepare);
                Rotate(farPaw,7f*prepare);
                Shift(body,-.18f*strike*prepare,-.10f*strike*prepare);
                Rotate(jaw,5f*lift);
            }
            else if (move.StartsWith("DT02"))
            {
                float sweep = active ? Ease(p) : recovery ? 1f : 0f;
                Shift(nearPaw,.35f*prepare-2.10f*sweep*prepare,.35f*prepare*(1f-sweep)+.22f*sweep*prepare);
                Rotate(nearPaw,-22f*prepare+47f*sweep*prepare);
                Shift(farPaw,-.35f*sweep*prepare,.12f*prepare);
                Rotate(body,1.5f*prepare);
                Scale(roots,1f+.07f*prepare,1f-.07f*prepare);
            }
            else if (move.StartsWith("DT03"))
            {
                float gape = prepare*(1f-strike);
                OpenMouth(gape);
                Shift(body,-.34f*strike*prepare,.04f*gape);
                Shift(jaw,-.24f*strike*prepare,.02f*gape);
                Rotate(jaw,8f*gape-8f*strike*prepare);
                Scale(mouth,1f+.11f*gape,1f+.15f*gape-.32f*strike*prepare);
                Rotate(nearPaw,-7f*gape);
                Shift(farPaw,-.12f*strike*prepare,0f);
            }
            else if (move.StartsWith("DT04"))
            {
                OpenMouth(prepare);
                Scale(mouth,1f+.10f*prepare,1f+.08f*prepare);
                Rotate(nearPaw,8f*prepare);
                Rotate(farPaw,-6f*prepare);
                Scale(roots,1f+.06f*prepare,1f);
            }
            else if (move.StartsWith("DT05"))
            {
                float rise = Mathf.Max(0f,transform.position.y-groundY);
                float heightRatio = Mathf.Clamp01(rise/3.1f);
                float tuck = Mathf.Max(heightRatio,tell ? Ease(p)*.6f : 0f);
                Shift(nearPaw,.18f*tuck,.65f*tuck);
                Rotate(nearPaw,-46f*tuck);
                Shift(farPaw,.22f*tuck,.55f*tuck);
                Rotate(farPaw,-33f*tuck);
                // Keep the lower roots grounded during ascent, then let them tear free at the apex.
                float detach = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.82f,1f,heightRatio));
                float stretch = rise*(1f-.23f*detach);
                Shift(roots,.08f*heightRatio,-.10f*heightRatio);
                Scale(roots,1f-.28f*heightRatio,1f+stretch/2.385f);
                Rotate(roots,Mathf.Sin(Time.time*15f)*heightRatio*1.3f);
                OpenMouth(.30f*tuck);
                if (recovery)
                {
                    Scale(body,1f+.07f*recoil,1f-.11f*recoil);
                    Shift(body,0f,-.10f*recoil);
                    Scale(roots,1f+.16f*recoil,1f-.13f*recoil);
                    Shift(nearPaw,-.28f*recoil,-.08f*recoil);
                }
            }
            else if (move.StartsWith("MV01"))
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
            ApplyDeath();
            ApplyVisibility();
            ApplyColors();
            UpdateGroundShadow();
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
            float p=presentationAttackProgress;
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
            if(move.StartsWith("DT05") && presentationPhase==2)
            {
                ResetPart(roots);ResetPart(nearPaw);ResetPart(farPaw);
                float compression=step=="compress"?Ease(p):0f;
                float tuck=step=="rise"?Ease(p):step=="lock"||step=="fall"?1f:0f;
                if(step=="compress")
                {
                    Shift(body,0f,-.20f*compression);
                    Scale(body,1f+.035f*compression,1f-.065f*compression);
                    Scale(roots,1f-.18f*compression,1f-.42f*compression);
                    Shift(roots,-.12f*compression,-.90f*compression);
                }
                if(tuck>0f)
                {
                    Shift(nearPaw,.18f*tuck,.57f*tuck);Rotate(nearPaw,-33f*tuck);
                    Shift(farPaw,.17f*tuck,.52f*tuck);Rotate(farPaw,-27f*tuck);
                    Scale(roots,1f-.30f*tuck,1f-.40f*tuck);
                    Shift(roots,-.12f*tuck,.45f*tuck);
                    Rotate(roots,-5f*tuck);
                }
                if(step=="land"||step=="recover")
                {
                    float settle=1f-Ease(p);
                    Scale(roots,1f+.08f*settle,1f-.10f*settle);
                    Shift(body,0f,-.12f*settle);Scale(body,1f+.05f*settle,1f-.09f*settle);
                }
            }
            if(move.StartsWith("DT06"))
            {
                if(step=="submerge")
                {
                    Rotate(body,-12f*Ease(p));
                    Shift(nearPaw,-.36f*Ease(p),.10f*Ease(p));Rotate(nearPaw,23f*Ease(p));
                    Shift(farPaw,-.24f*Ease(p),.08f*Ease(p));Rotate(farPaw,17f*Ease(p));
                    Scale(roots,1f-.20f*Ease(p),1f-.38f*Ease(p));
                }
                else if(step=="erupt")
                {
                    float burst=1f-Ease(p);
                    Shift(body,0f,.15f*burst);Rotate(body,5f*burst);
                    Rotate(nearPaw,-16f*burst);Rotate(farPaw,-10f*burst);
                    Scale(roots,1f+.14f*burst,1f-.14f*burst);
                    OpenMouth(.55f*burst);
                }
            }
            if(state=="transition"||step=="transition")
            {
                float t=presentationTransitionProgress;
                ResetPart(body);ResetPart(roots);ResetPart(nearPaw);ResetPart(farPaw);ResetPart(jaw);
                float slump=Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/.65f));
                float twist=Mathf.Sin(t*38f)*Mathf.SmoothStep(0f,1f,t/.18f)*(1f-Ease(Mathf.InverseLerp(.45f,.75f,t)));
                Shift(body,twist*.055f,-.22f*slump);Rotate(body,twist*2.8f);
                Rotate(nearPaw,9f*slump);Rotate(farPaw,-5f*slump);
                OpenMouth(Mathf.Sin(Mathf.PI*t)*.8f);
                float retract=Ease(Mathf.InverseLerp(.50f,1f,t));
                Scale(roots,1f-.14f*retract,1f-.18f*retract);
                Shift(roots,-.10f*retract,-.38f*retract);
                for(int i=0;i<4;i++)if(armorParts[i]!=null && (presentationArmorMask&(1<<i))!=0)
                    Rotate(armorParts[i],twist*(i%2==0?3f:-3f));
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
                bool visible=mainVisible;
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
            if(consumedHitSequence!=presentationHitSequence)
            {
                consumedHitSequence=presentationHitSequence;
                localHitFlash[presentationHitRegion]=1f;
                localHitArmor[presentationHitRegion]=presentationHitHasArmor;
            }
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
            Color color = Color.Lerp(Color.white,new Color(.35f,.31f,.27f,1f),death*.72f);
            color = Color.Lerp(color,new Color(1f,.57f,.42f,1f),flash*(HasV2Artwork ? .12f : .82f));
            foreach (SpriteRenderer renderer in renderers)
            {
                Color partColor=color;
                if(regionByRenderer.TryGetValue(renderer,out int region))
                {
                    bool armored=armorByRenderer.ContainsKey(renderer);
                    float amount=localHitFlash[region];
                    if(armored!=localHitArmor[region])amount*=.12f;
                    Vector3 nearest=renderer.bounds.ClosestPoint(presentationHitPoint);
                    float distance=Vector2.Distance(nearest,presentationHitPoint);
                    amount*=Mathf.Clamp01(1f-distance/1.35f);
                    partColor=Color.Lerp(partColor,armored?new Color(1f,.77f,.32f,1f):new Color(1f,.48f,.38f,1f),amount*.85f);
                }
                renderer.color=partColor;
            }
        }
        private static float Ease(float value) { return Mathf.SmoothStep(0f,1f,value); }
        private static void ResetPart(Part part) { part.joint.localPosition = part.restPosition; part.joint.localScale = Vector3.one; part.joint.localRotation = Quaternion.identity; }
        private static void Shift(Part part,float x,float y) { part.joint.localPosition += new Vector3(x,y,0f); }
        private static void Rotate(Part part,float angle) { part.joint.localRotation = Quaternion.Euler(0f,0f,angle); }
        private static void Scale(Part part,float x,float y) { part.joint.localScale = new Vector3(x,y,1f); }
        private void ClearRig()
        {
            if (rig != null) Destroy(rig.gameObject);
            if (floorMask != null) Destroy(floorMask.gameObject);
            foreach (PlateDebris plate in detachedPlates) if (plate.transform != null) Destroy(plate.transform.gameObject);
            detachedPlates.Clear();regionByRenderer.Clear();armorByRenderer.Clear();pendingArmorBreaks.Clear();
            System.Array.Clear(localHitFlash,0,localHitFlash.Length);
            System.Array.Clear(localHitArmor,0,localHitArmor.Length);
            System.Array.Clear(armorParts,0,armorParts.Length);
            hornNear=null;hornFar=null;dorsalTrees=null;
            if (groundShadow != null) Destroy(groundShadow.gameObject);
            foreach (Texture2D texture in runtimeTextures) if (texture != null) Destroy(texture);
            runtimeTextures.Clear();
            foreach (Sprite sprite in sprites) if (sprite != null) Destroy(sprite);
            foreach (Material material in textureMaterials.Values) if (material != null) Destroy(material);
            textureMaterials.Clear();
            sprites.Clear(); renderers.Clear(); parts.Clear(); built = false;
            consumedHitSequence = presentationHitSequence;
        }
        private void OnDestroy() { ClearRig(); }
    }
}
