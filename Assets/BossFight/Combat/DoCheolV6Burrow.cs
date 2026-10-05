using System;
using System.Collections.Generic;
using UnityEngine;

namespace FourGuardians.BossFight
{
    public enum BurrowVariant { None, QuickSlam, FiveBite, ArcLeap }
    public sealed partial class DoCheolBossController
    {
        [Header("V6: bounded variation and mobile phase two")]
        [SerializeField] private int behaviourSeed=20261005;
        [SerializeField] private float phaseOneWaitVariation=.18f,phaseTwoWaitVariation=.12f,phaseTwoRepositionSpeed=4.8f;
        [SerializeField] private float phaseTwoBurrowCooldown=6.8f,quickTell=.62f,departureTell=.68f,departureContact=.12f;
        [SerializeField] private int quickRepeatLimit=2;
        private System.Random behaviourRandom;
        private bool previousWaitLong,quickReturning,quickReemerging;
        private BurrowVariant forcedBurrowVariant;
        private float burrowSubmerge,burrowTravel,burrowPrepare,burrowFlight;
        private int quickSide;
        private readonly List<float> actionWaitSamples=new List<float>();
        public IReadOnlyList<float> ActionWaitSamples=>actionWaitSamples.AsReadOnly();
        public float LastSampledActionWait {get;private set;}
        public float PhaseRepositionSpeed=>Phase==2?phaseTwoRepositionSpeed:repositionSpeed;
        public int BehaviourSeed=>behaviourSeed;
        public BurrowVariant BurrowBranch {get;private set;}
        public int QuickSlamCount {get;private set;}
        public int BurrowFiveCount {get;private set;}
        public int ArcLeapCount {get;private set;}
        public int ConsecutiveQuickSlams {get;private set;}
        public int MaximumConsecutiveQuickSlams {get;private set;}
        public float UndergroundSeconds {get;private set;}
        public float DepartureX {get;private set;}
        public float LockedLandingX {get;private set;}
        public Bounds DepartureWarningBounds {get;private set;}
        public Bounds LandingWarningBounds {get;private set;}
        public bool V6Underground => AttackStage=="travel" || AttackStage=="arc_warning" ||
            (AttackStage=="submerge" && AttackStageProgress>.3f) ||
            ((AttackStage=="quick_tell" || AttackStage=="burrow_five_tell") && AttackStageProgress<.90f) ||
            (AttackStage=="quick_resubmerge" && AttackStageProgress>.3f) ||
            (AttackStage=="quick_reemerge" && AttackStageProgress<.90f);

        public bool ConfigureBehaviourSeed(int seed)
        {
            if(fightActive)return false;
            behaviourSeed=seed;behaviourRandom=new System.Random(seed);return true;
        }
        private float BehaviourDraw(){if(behaviourRandom==null)behaviourRandom=new System.Random(behaviourSeed);return (float)behaviourRandom.NextDouble();}
        private void ResetV6Behaviour()
        {
            behaviourRandom=new System.Random(behaviourSeed);actionWaitSamples.Clear();previousWaitLong=false;
            LastSampledActionWait=UndergroundSeconds=0;QuickSlamCount=BurrowFiveCount=ArcLeapCount=0;
            ConsecutiveQuickSlams=MaximumConsecutiveQuickSlams=0;BurrowBranch=forcedBurrowVariant=BurrowVariant.None;
            quickReturning=quickReemerging=false;DepartureWarningBounds=LandingWarningBounds=default;
        }
        private void BeginActionWait()
        {
            float u=BehaviourDraw();if(previousWaitLong)u*=.55f;
            previousWaitLong=u>.70f;
            LastSampledActionWait=PhaseActionWait+(Phase==1?phaseOneWaitVariation:phaseTwoWaitVariation)*u*u;
            actionWaitSamples.Add(LastSampledActionWait);
            SetState(BossState.ActionWait,LastSampledActionWait);SetStage("action_wait",0);
        }
        public bool ForceV6Burrow(BurrowVariant branch)
        {
            if(!CanForce() || RockfallActive || branch==BurrowVariant.None ||
                Phase==1 && branch!=BurrowVariant.ArcLeap || branch==BurrowVariant.QuickSlam && ConsecutiveQuickSlams>=quickRepeatLimit)return false;
            forcedBurrowVariant=branch;BeginSpecial(BossPattern.BBurrowLeap);return true;
        }
        private BurrowVariant ChooseBurrowBranch()
        {
            if(Phase==1)return BurrowVariant.ArcLeap;
            float u=BehaviourDraw();
            if(ConsecutiveQuickSlams>=quickRepeatLimit)return u<.47f?BurrowVariant.FiveBite:BurrowVariant.ArcLeap;
            return u<.40f?BurrowVariant.QuickSlam:u<.68f?BurrowVariant.FiveBite:BurrowVariant.ArcLeap;
        }
        private void BeginV6Burrow(bool alreadyBuried=false)
        {
            BurrowBranch=forcedBurrowVariant!=BurrowVariant.None?forcedBurrowVariant:ChooseBurrowBranch();forcedBurrowVariant=BurrowVariant.None;
            quickReturning=quickReemerging=false;landed=false;ClearBounds();HideMarkers();BurrowExitPlan="rest";BurrowPackageActive=true;
            if(BurrowBranch==BurrowVariant.QuickSlam)
            {QuickSlamCount++;ConsecutiveQuickSlams++;MaximumConsecutiveQuickSlams=Mathf.Max(MaximumConsecutiveQuickSlams,ConsecutiveQuickSlams);}
            else{ConsecutiveQuickSlams=0;if(BurrowBranch==BurrowVariant.FiveBite)BurrowFiveCount++;else ArcLeapCount++;}
            // The direction and both destinations are decided once before travelling underground.
            float px=player.HurtBounds.center.x;
            int direction=Mathf.Abs(px)>6f?(px>0?-1:1):Mathf.Abs(px-transform.position.x)>.15f?(px>transform.position.x?1:-1):Facing;
            quickSide=direction;
            if(BurrowBranch==BurrowVariant.QuickSlam)WholeFirstSide=WholeSwingSide=quickSide;
            emergenceX=ClampBodyX(px-direction*(BurrowBranch==BurrowVariant.ArcLeap?2.8f:1.45f));
            if(BurrowBranch==BurrowVariant.FiveBite)emergenceX=ClampBodyX(px);
            DepartureX=emergenceX;
            LockedLandingX=BurrowBranch==BurrowVariant.ArcLeap?ClampBodyX(DepartureX+direction*5.6f):emergenceX;
            if(BurrowBranch==BurrowVariant.ArcLeap && Mathf.Abs(LockedLandingX-DepartureX)<4.4f)
                DepartureX=emergenceX=ClampBodyX(LockedLandingX-direction*5.6f);
            TargetX=LockedLandingX;TargetLocked=true;
            burrowSubmerge=alreadyBuried?0:Phase==1?submergeDuration:.28f;
            burrowTravel=Phase==1?travelDuration:.38f;
            burrowPrepare=BurrowBranch==BurrowVariant.ArcLeap?departureTell:BurrowBranch==BurrowVariant.FiveBite?wholeTell:quickTell;
            burrowFlight=Phase==1?.98f:.82f;
            leapFlightTime=burrowFlight;
            nextBurrowTime=encounterTime+(Phase==1?burrowCooldown:phaseTwoBurrowCooldown);
            riseOrigin=new Vector3(transform.position.x,FloorY,transform.position.z);
            DepartureWarningBounds=Box(DepartureX,FloorY+.8f,3.4f,1.6f);
            LandingWarningBounds=Box(LockedLandingX,FloorY+.7f,landingWidth,1.4f);
            if(BurrowBranch==BurrowVariant.ArcLeap){WarningBounds=DepartureWarningBounds;SecondaryWarningBounds=LandingWarningBounds;}
            else
            {
                wholeFirstBounds=V6SweptWarning(emergenceX,-1);wholeSecondBounds=V6SweptWarning(emergenceX,1);
                WarningBounds=BurrowBranch==BurrowVariant.FiveBite||quickSide<0?wholeFirstBounds:wholeSecondBounds;
                SecondaryWarningBounds=BurrowBranch==BurrowVariant.FiveBite?wholeSecondBounds:default;
            }
            LockedTellDuration=burrowPrepare;
            SetState(BossState.Telegraph,burrowSubmerge+burrowTravel+burrowPrepare);
            SetStage(alreadyBuried?"travel":"submerge",0);Cue("submerge",riseOrigin,.65f);ShowWarning();DrawV6BurrowWarnings();UpdateHurtbox();
        }
        private Bounds V6SweptWarning(float rootX,int side)
        {
            // Cover the damaging half of the moving arc, not just its final footprint.
            Bounds swept=DocheolWholeMotion.Bounds(new Vector2(rootX,FloorY),side,.5f);
            swept.Encapsulate(DocheolWholeMotion.Bounds(new Vector2(rootX,FloorY),side,1));
            return Box(swept.center.x,FloorY+.65f,swept.size.x,1.3f);
        }
        private void DrawV6BurrowWarnings()
        {
            Color tint=BurrowBranch==BurrowVariant.QuickSlam?new Color(1,.37f,.18f):BurrowBranch==BurrowVariant.FiveBite?new Color(1,.66f,.20f):new Color(.40f,.83f,.91f);
            if(warningLine!=null)warningLine.color=tint;
            if(SecondaryWarningBounds.size.x>0)DrawRect(wholeSecondWarning,new Vector3(SecondaryWarningBounds.center.x,FloorY+.065f,0),new Vector3(SecondaryWarningBounds.size.x,.045f,1),tint);
        }
        private void TickV6BurrowTelegraph()
        {
            if(quickReturning)
            {
                SetStage("quick_resubmerge",StateProgress);
                transform.position=new Vector3(emergenceX,FloorY-4.7f*Mathf.SmoothStep(0,1,StateProgress),riseOrigin.z);return;
            }
            if(quickReemerging)
            {
                SetStage("quick_reemerge",StateProgress);
                transform.position=new Vector3(emergenceX,FloorY-4.7f*(1-Mathf.SmoothStep(0,1,StateProgress)),riseOrigin.z);return;
            }
            if(timer<burrowSubmerge)
            {SetStage("submerge",timer/burrowSubmerge);transform.position=new Vector3(riseOrigin.x,FloorY-4.7f*Mathf.SmoothStep(0,1,AttackStageProgress),riseOrigin.z);}
            else if(timer<burrowSubmerge+burrowTravel)
            {
                if(AttackStage!="travel")Cue("travel",new Vector2(emergenceX,FloorY),burrowTravel);
                SetStage("travel",(timer-burrowSubmerge)/burrowTravel);Facing=quickSide;
                transform.position=new Vector3(Mathf.Lerp(riseOrigin.x,emergenceX,AttackStageProgress),FloorY-4.7f,riseOrigin.z);DrawBurrowTrace();
            }
            else
            {
                float p=Mathf.Clamp01((timer-burrowSubmerge-burrowTravel)/burrowPrepare);
                string stage=BurrowBranch==BurrowVariant.ArcLeap?"arc_warning":BurrowBranch==BurrowVariant.FiveBite?"burrow_five_tell":"quick_tell";
                if(AttackStage!=stage)Cue("burrow_warning",new Vector2(emergenceX,FloorY),burrowPrepare);
                SetStage(stage,p);
                float rise=BurrowBranch==BurrowVariant.ArcLeap?0:Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.68f)/.32f));
                if(BurrowBranch==BurrowVariant.FiveBite)Facing=-1;
                transform.position=new Vector3(emergenceX,FloorY-4.7f*(1-rise),riseOrigin.z);AnimateWarning();DrawV6BurrowWarnings();
            }
        }
        private void BeginV6BurrowActive()
        {
            if(quickReturning)
            {
                quickReturning=false;
                if(BehaviourDraw()<.60f)
                {AttackId=++nextAttackId;BeginV6Burrow(true);return;}
                quickReemerging=true;SetState(BossState.Telegraph,.26f);SetStage("quick_reemerge",0);return;
            }
            if(quickReemerging)
            {
                quickReemerging=false;BurrowPackageActive=false;transform.position=new Vector3(emergenceX,FloorY,riseOrigin.z);
                Pattern=BossPattern.None;TargetLocked=false;ClearBounds();HideMarkers();BeginActionWait();return;
            }
            transform.position=new Vector3(emergenceX,FloorY,riseOrigin.z);
            if(BurrowBranch==BurrowVariant.FiveBite)
            {
                // The 1.80-second first tell was already earned underground. Do not repeat it.
                Pattern=BossPattern.WholeBodyBite;WholeFirstSide=WholeSwingSide=-1;Facing=-1;WholeBodyAttackCount++;
                wholeFirstBounds=WholeBounds(-1);wholeSecondBounds=WholeBounds(1);
                WarningBounds=wholeFirstBounds;SecondaryWarningBounds=wholeSecondBounds;TargetX=wholeFirstBounds.center.x;
                allocatedWholeIndex=0;WholeContactIndex=0;AttackId=++nextAttackId;nextWholeTime=encounterTime+wholeCooldown;
                SetState(BossState.Active,5*(wholeArc+wholeContact)+3*wholeBridge+wholeFinalDelay);SetStage("whole_arc",0);return;
            }
            if(BurrowBranch==BurrowVariant.QuickSlam)
            {WholeSwingSide=quickSide;SetState(BossState.Active,wholeArc+wholeContact);SetStage("quick_slam_arc",0);return;}
            // Departure, landing and both waves share this one package ID. At most one accepted hit.
            WarningBounds=LandingWarningBounds;ShowWarning();SetState(BossState.Active,burrowFlight+landingDuration+waveDelay+waveDuration);
            SetStage("departure",0);Cue("departure",new Vector2(DepartureX,FloorY),.85f);
        }
        private void TickV6BurrowAttack()
        {
            if(BurrowBranch==BurrowVariant.QuickSlam)
            {
                float p=Mathf.Clamp01(timer/wholeArc);
                SetStage(timer<wholeArc?"quick_slam_arc":"quick_slam_hit",timer<wholeArc?p:(timer-wholeArc)/wholeContact);
                if(timer>=wholeArc*.5f)WholeDamage(p);
                if(timer>=wholeArc && !landed){landed=true;Cue("impact",new Vector2(WarningBounds.center.x,FloorY),.60f);}return;
            }
            if(timer<burrowFlight)
            {
                float p=Mathf.Clamp01(timer/burrowFlight);SetStage(timer<departureContact?"departure":"flight",p);
                transform.position=new Vector3(Mathf.Lerp(DepartureX,LockedLandingX,p),FloorY+leapHeight*4*p*(1-p),riseOrigin.z);
                if(timer<departureContact){ActiveBounds=DepartureWarningBounds;DamageVolume(ActiveBounds,AttackId);}
                AnimateWarning();return;
            }
            transform.position=new Vector3(LockedLandingX,FloorY,riseOrigin.z);
            float elapsed=timer-burrowFlight;
            if(!landed){landed=true;Cue("land",new Vector2(LockedLandingX,FloorY),1);}
            if(elapsed<landingDuration)
            {SetStage("land",elapsed/landingDuration);ActiveBounds=LandingWarningBounds;DrawAttackEffect(ActiveBounds);DamageVolume(ActiveBounds,AttackId);return;}
            if(elapsed<landingDuration+waveDelay)
            {
                if(AttackStage!="wave_delay"){HideMarkers();WarningBounds=Box((ArenaLeft+ArenaRight)*.5f,FloorY+.21f,ArenaRight-ArenaLeft,.42f);ShowWarning();Cue("wave_delay",new Vector2(LockedLandingX,FloorY),waveDelay);}
                SetStage("wave_delay",(elapsed-landingDuration)/waveDelay);AnimateWarning();return;
            }
            if(AttackStage!="wave")Cue("wave",new Vector2(LockedLandingX,FloorY),waveDuration);
            float progress=Mathf.Clamp01((elapsed-landingDuration-waveDelay)/waveDuration);SetStage("wave",progress);
            float travel=Mathf.Max(LockedLandingX-ArenaLeft,ArenaRight-LockedLandingX)*progress;
            ActiveBounds=WaveBounds(LockedLandingX-travel);SecondaryActiveBounds=WaveBounds(LockedLandingX+travel);
            DamageVolume(ActiveBounds,AttackId);DamageVolume(SecondaryActiveBounds,AttackId);
            if(!fightActive || player.IsDead)return;
            DrawRect(attackFill,ActiveBounds.center,ActiveBounds.size,Color.white);DrawRect(dustTrail[0],SecondaryActiveBounds.center,SecondaryActiveBounds.size,new Color(.8f,.6f,.3f));
        }
        private void FinishV6BurrowAttack()
        {
            LastCompletedAttackId=AttackId;HideMarkers();ClearBounds();completedSequences++;
            if(BurrowBranch==BurrowVariant.QuickSlam)
            {quickReturning=true;SetState(BossState.Telegraph,.26f);SetStage("quick_resubmerge",0);Cue("submerge",transform.position,.40f);return;}
            BurrowPackageActive=false;SetState(BossState.Recovery,leapRecovery);Cue("recover",transform.position,leapRecovery);
        }
    }
}
