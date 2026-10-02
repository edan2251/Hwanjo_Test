using System.Collections.Generic;
using UnityEngine;

namespace FourGuardians.BossFight
{
    /// <summary>Articulated atlas rig. Combat and world displacement belong to the parent controller.</summary>
    public sealed class DocheolVisual : MonoBehaviour
    {
        [SerializeField] private Texture2D atlas;
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

        private sealed class Part
        {
            public Transform joint;
            public Vector3 restPosition;
        }

        public void Configure(Texture2D texture)
        {
            atlas = texture;
            // Scene serialization retains only the texture, never transient Sprite.Create results.
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
            ApplyDeath();
            ApplyColors();
            UpdateGroundShadow();
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
            color = Color.Lerp(color,new Color(1f,.57f,.42f,1f),flash*.82f);
            foreach (SpriteRenderer renderer in renderers) renderer.color = color;
        }
        private static float Ease(float value) { return Mathf.SmoothStep(0f,1f,value); }
        private static void ResetPart(Part part) { part.joint.localPosition = part.restPosition; part.joint.localScale = Vector3.one; part.joint.localRotation = Quaternion.identity; }
        private static void Shift(Part part,float x,float y) { part.joint.localPosition += new Vector3(x,y,0f); }
        private static void Rotate(Part part,float angle) { part.joint.localRotation = Quaternion.Euler(0f,0f,angle); }
        private static void Scale(Part part,float x,float y) { part.joint.localScale = new Vector3(x,y,1f); }
        private void ClearRig()
        {
            if (rig != null) Destroy(rig.gameObject);
            if (groundShadow != null) Destroy(groundShadow.gameObject);
            foreach (Texture2D texture in runtimeTextures) if (texture != null) Destroy(texture);
            runtimeTextures.Clear();
            foreach (Sprite sprite in sprites) if (sprite != null) Destroy(sprite);
            foreach (Material material in textureMaterials.Values) if (material != null) Destroy(material);
            textureMaterials.Clear();
            sprites.Clear(); renderers.Clear(); parts.Clear(); built = false;
        }
        private void OnDestroy() { ClearRig(); }
    }
}
