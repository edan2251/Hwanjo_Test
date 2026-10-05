using FourGuardians.CourseContent.Combat;
using UnityEngine;

namespace FourGuardians.BossFight
{
    // Split only the rear root-only strip off the original single body receiver.
    // The fan and body overlap is always credited to the body, independent of trigger order.
    [DefaultExecutionOrder(250)]
    public sealed class DocheolHitRegions : MonoBehaviour
    {
        private DoCheolBossController boss;
        private DocheolVisual visual;
        private BoxCollider2D body,root;
        private Vector2 originalSize;
        public Bounds BodyBounds=>body!=null?body.bounds:default;
        public Bounds RootOnlyBounds=>root!=null?root.bounds:default;
        public Bounds PaintedBodyBounds {get;private set;}
        public Bounds PaintedRootBounds {get;private set;}
        public int MixedRootContactsSuppressed {get;private set;}

        public void Configure(DoCheolBossController controller,Health2D health,BoxCollider2D receiver,DocheolVisual art)
        {
            boss=controller;visual=art;body=receiver;originalSize=receiver.size;
            body.transform.localPosition=new Vector3(boss.Facing*.4f,.95f,0);
            if(root==null)
            {
                var go=new GameObject("RearRoots_DeflectionOnly");go.transform.SetParent(transform,false);
                root=go.AddComponent<BoxCollider2D>();root.isTrigger=true;
                var hurt=go.AddComponent<Hurtbox2D>();hurt.Configure(health);hurt.ReceiverOverride=RootContact;
            }
        }
        private void LateUpdate()
        {
            if(body==null || root==null)return;
            body.size=originalSize;body.offset=Vector2.zero;
            root.enabled=false;
            if(!body.enabled || visual==null || !visual.TryGetDamageArtBounds(out Bounds solid,out Bounds fan))return;
            PaintedBodyBounds=solid;PaintedRootBounds=fan;
            // During the thin center of the existing 2D turn, retain the hittable body.
            if(fan.size.x<.06f)return;
            bool frontRight=solid.center.x>=fan.center.x;
            // Whole-body swings can show the opposite side while the committed controller
            // facing stays fixed. Preserve the same original reach relative to painted front.
            body.transform.localPosition=new Vector3(frontRight?.4f:-.4f,.95f,0);
            float edge=frontRight?solid.min.x-.05f:solid.max.x+.05f;
            Bounds original=body.bounds;
            float min=frontRight?Mathf.Clamp(edge,original.min.x,original.max.x-.1f):original.min.x;
            float max=frontRight?original.max.x:Mathf.Clamp(edge,original.min.x+.1f,original.max.x);
            body.size=new Vector2(max-min,originalSize.y);
            Vector3 center=new Vector3((min+max)*.5f,original.center.y,0);
            body.offset=body.transform.InverseTransformPoint(center);
            float rootMin=frontRight?fan.min.x:max;
            float rootMax=frontRight?min:fan.max.x;
            if(rootMax-rootMin<.035f)return;
            root.transform.position=new Vector3((rootMin+rootMax)*.5f,fan.center.y,transform.position.z);
            root.size=new Vector2(rootMax-rootMin,fan.size.y);root.enabled=true;
        }
        private void RootContact(DamageInfo hit)
        {
            if(boss==null || !boss.FightActive || !body.enabled)return;
            var source=hit.Source!=null?hit.Source.GetComponent<Collider2D>():null;
            // A sword spanning both areas reaches the one body receiver. Do not consume
            // health immunity or the sword's body target, or emit a false invalid-hit cue.
            if(source!=null && source.enabled && source.bounds.Intersects(body.bounds))
            {MixedRootContactsSuppressed++;return;}
            boss.RecordRootBlock(hit.HitPoint);
        }
        private void OnDisable(){if(root!=null)root.enabled=false;}
    }
}
