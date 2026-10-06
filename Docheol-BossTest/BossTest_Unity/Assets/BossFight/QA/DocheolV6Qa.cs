using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FourGuardians.CourseContent.Combat;

namespace FourGuardians.BossFight
{
    public sealed partial class DocheolV4QaDriver
    {
        [Serializable] public sealed class V6TempoSample
        {
            public int phase,seed,packages,contacts,counterWindows,a,b,c,maxQuick;
            public float seconds,underground,waitMean,waitMinimum,waitMaximum,repositionSpeed;
            public List<float> waits=new List<float>();
        }
        private IEnumerator V6Burrows()
        {
            yield return Setup(0,-2.2f);
            Record("p1_rejects_fast_A_B",!boss.ForceV6Burrow(BurrowVariant.QuickSlam)&&!boss.ForceV6Burrow(BurrowVariant.FiveBite),"phase-one heavy arc only");
            foreach(int phase in new[]{1,2})foreach(float px in new[]{-2.2f,-8.2f,8.2f})
                yield return V6BurrowCase(phase,BurrowVariant.ArcLeap,px);
            foreach(float px in new[]{-2.2f,-8.2f,8.2f})yield return V6BurrowCase(2,BurrowVariant.QuickSlam,px);
            yield return V6BurrowCase(2,BurrowVariant.FiveBite,-2.2f);
            yield return V6BurrowCase(2,BurrowVariant.FiveBite,8.2f);
            yield return V5Fixture(2,0,-2.2f);
            FixtureProperty(boss,"ConsecutiveQuickSlams",2);
            Record("A_cap_rejects_third_A",!boss.ForceV6Burrow(BurrowVariant.QuickSlam),"controlled consecutive counter=2; initial tuning cap");
            yield return V6ArcDuplicate();
        }
        private IEnumerator V6BurrowCase(int phase,BurrowVariant branch,float px)
        {
            yield return V5Fixture(phase,0,px);yield return new WaitForSeconds(.10f);
            string name="P"+phase+"-"+branch+"-"+px.ToString("F1");
            Record(name+"_starts",boss.ForceV6Burrow(branch),"controlled initial branch; ordinary evasion thereafter");
            float departure=boss.DepartureX,landing=boss.LockedLandingX;
            int incoming=damageEvents,initialId=boss.AttackId;
            float end=Time.time+12,tellStart=-1,tellEnd=-1,firstContact=-1,fifthContact=-1,recoveryStart=-1,recoveryEnd=-1;
            int previousIndex=-1;var indices=new HashSet<int>();var ids=new HashSet<int>();
            bool stable=true,tellSafe=true,airSafe=true,land=false,wave=false,departureHit=false,quickSubmerge=false,done=false,recoveryFixed=true;
            float rx=0,arcPeak=0;string previous="";
            while(Time.time<end && !player.IsDead)
            {
                Evasion();
                if(branch!=BurrowVariant.QuickSlam || boss.AttackId==initialId)
                    stable&=Mathf.Abs(departure-boss.DepartureX)<.001f&&Mathf.Abs(landing-boss.LockedLandingX)<.001f;
                if(boss.State==BossState.Telegraph && boss.AttackStage!="quick_resubmerge")tellSafe&=damageEvents==incoming;
                if(boss.AttackStage=="burrow_five_tell" && tellStart<0)tellStart=Time.time;
                if(branch==BurrowVariant.FiveBite && boss.Pattern==BossPattern.WholeBodyBite && tellEnd<0)tellEnd=Time.time;
                if(boss.Pattern==BossPattern.WholeBodyBite && boss.ActiveBounds.size.x>0)
                {
                    indices.Add(boss.WholeContactIndex);ids.Add(boss.AttackId);
                    if(previousIndex!=boss.WholeContactIndex)
                    {report.events.Add(name+" contact="+boss.WholeContactIndex+" t="+Time.time.ToString("F3"));previousIndex=boss.WholeContactIndex;}
                    if(firstContact<0)firstContact=Time.time;
                    if(boss.WholeContactIndex==4)fifthContact=Time.time;
                }
                departureHit|=boss.AttackStage=="departure"&&boss.ActiveBounds.size.x>0;
                if(boss.AttackStage=="flight")airSafe&=boss.ActiveBounds.size.x==0&&boss.SecondaryActiveBounds.size.x==0;
                arcPeak=Mathf.Max(arcPeak,boss.transform.position.y-boss.FloorY);
                land|=boss.AttackStage=="land";wave|=boss.AttackStage=="wave";
                if(boss.AttackStage=="quick_resubmerge")
                {quickSubmerge=true;if(boss.AttackStageProgress>.7f){done=true;break;}}
                if(branch==BurrowVariant.QuickSlam && !quickSubmerge)recoveryFixed&=boss.State!=BossState.Recovery;
                if(boss.State==BossState.Recovery)
                {
                    if(recoveryStart<0){recoveryStart=Time.time;recoveryEnd=Time.time+boss.RecoveryRemaining;rx=boss.transform.position.x;}
                    recoveryFixed&=Mathf.Abs(rx-boss.transform.position.x)<.001f;
                }
                if(recoveryStart>=0 && boss.State==BossState.ActionWait){done=true;break;}
                if(px==-2.2f && previous!=boss.AttackStage)
                {
                    previous=boss.AttackStage;
                    if(previous=="arc_warning"||previous=="departure"||previous=="flight"||previous=="land"||previous=="wave"||previous=="quick_slam_hit"||previous=="quick_resubmerge"||previous=="burrow_five_tell"||previous=="whole_final_windup")Capture(name+"-"+previous);
                }
                yield return null;
            }
            Record(name+"_locked_destinations",stable,"departure="+departure+" landing="+landing+" moving player="+player.Body.position.x);
            Record(name+"_tell_has_no_damage",tellSafe,"incoming before active="+(tellSafe?0:1));
            Record(name+"_ordinary_evasion",done&&damageEvents==incoming,"complete="+done+" incoming="+(damageEvents-incoming));
            if(branch==BurrowVariant.ArcLeap)
            {
                Record(name+"_arc_depart_land_wave",departureHit&&land&&wave&&arcPeak>3.8f&&Mathf.Abs(landing-departure)>4.3f,"departure="+departureHit+" peak="+arcPeak+" horizontal="+Mathf.Abs(landing-departure)+" land="+land+" wave="+wave);
                Record(name+"_airborne_no_contact_volume",airSafe,"only short departure, landing and delayed ground wave damage");
                Record(name+"_guaranteed_recovery",recoveryFixed&&recoveryEnd-recoveryStart>=1.10f,"full stationary recovery="+(recoveryEnd-recoveryStart));
            }
            else if(branch==BurrowVariant.QuickSlam)Record(name+"_immediate_resubmerge_no_false_recovery",quickSubmerge&&recoveryFixed,"short active -> resubmerge, no advertised long recovery");
            else
            {
                Record(name+"_single_underground_first_tell",tellEnd-tellStart>=1.75f&&tellEnd-tellStart<=1.87f,"observed underground tell="+(tellEnd-tellStart)+" first contact latency="+(firstContact-tellEnd));
                Record(name+"_five_distinct_contacts",indices.Count==5&&ids.Count==5&&fifthContact-firstContact>2.5f,"indices="+indices.Count+" IDs="+ids.Count+" fifth-first="+(fifthContact-firstContact));
                Record(name+"_long_recovery",recoveryFixed&&recoveryEnd-recoveryStart>=1.85f,"stationary recovery="+(recoveryEnd-recoveryStart));
            }
            yield return Snapshot(name+"-result");
        }
        private IEnumerator V6ArcDuplicate()
        {
            yield return Setup(0,-2.2f);boss.ForceV6Burrow(BurrowVariant.ArcLeap);
            float depart=boss.DepartureX,landing=boss.LockedLandingX;int before=damageEvents;bool relocated=false,land=false,wave=false;
            player.Body.position=new Vector2(depart,boss.FloorY+.025f);
            float end=Time.time+6;
            while(Time.time<end&&!player.IsDead)
            {
                player.SubmitInput(0,false,false,false);
                if(boss.AttackStage=="flight"&&!relocated)
                {relocated=true;player.Body.position=new Vector2(landing,boss.FloorY+.025f);player.Body.linearVelocity=Vector2.zero;}
                land|=boss.AttackStage=="land";wave|=boss.AttackStage=="wave";
                if(wave&&boss.State==BossState.Recovery)break;
                yield return null;
            }
            Record("C_departure_landing_wave_one_accepted_hit",land&&wave&&damageEvents-before==1,"disclosed relocation into departure then landing; accepted="+(damageEvents-before)+" shared package ID");
            yield return Snapshot("C-shared-hit-policy");
        }
        private static float WhiteAmount(SpriteRenderer sprite)
        {var block=new MaterialPropertyBlock();sprite.GetPropertyBlock(block);return block.GetFloat("_WhiteFlash");}
        private IEnumerator V6Impact()
        {
            yield return Setup(0,-2.2f);var visual=boss.GetComponentInChildren<DocheolVisual>();var feedback=FindFirstObjectByType<BossFightFeedback>();
            Record("HP5_initial",player.Health.MaxHealth==5&&player.Health.CurrentHealth==5,"player maximum and initial HP=5");
            Record("white_shader_configured",visual.WholeWhiteFlashEnabled,"serialized white sprite material");
            int rootStops=session.HitStopRequests,rootConfirms=feedback.BossHitCount;
            yield return V5RootCase(-1,"roots");
            yield return new WaitForSecondsRealtime(.15f);
            Record("root_invalid_has_no_white_or_strong_stop",visual.WholeFlashAmount==0&&session.HitStopRemaining==0&&session.HitStopRequests==rootStops&&feedback.BossHitCount==rootConfirms,"root-only sword: no white, hitstop request or body contact confirmation");
            int events=feedback.BossHitCount;bool acceptedStop=false;float requested=0;
            Action<Vector2,int,bool> hit=(point,region,armor)=>{acceptedStop|=Time.timeScale==0&&session.HitStopRemaining>0;requested=Mathf.Max(requested,session.HitStopRemaining);};
            boss.LocalHit+=hit;yield return V5RootCase(-1,"body");boss.LocalHit-=hit;
            Record("body_event_drives_hit_feedback",feedback.BossHitCount==events+1&&acceptedStop,"contact confirmations="+(feedback.BossHitCount-events)+" same-event stop="+acceptedStop+" duration="+requested);
            boss.enabled=false;session.enabled=false;
            SpriteRenderer plate=null;foreach(var sr in visual.GetComponentsInChildren<SpriteRenderer>())if(sr.name=="ForeheadArmor")plate=sr;
            visual.OnArmorBroken(0,14,plate.bounds.center);yield return null;
            var originals=new Dictionary<SpriteRenderer,Color>();
            foreach(var sr in visual.GetComponentsInChildren<SpriteRenderer>())originals[sr]=sr.color;
            visual.SetFlash(1);bool allWhite=true;int visible=0;
            foreach(var sr in visual.GetComponentsInChildren<SpriteRenderer>())if(sr.enabled&&sr.name!="FloorAnchoredShadow"){visible++;allWhite&=WhiteAmount(sr)==1;}
            bool outsideClean=true;int detached=0;
            foreach(var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))if(!sr.transform.IsChildOf(visual.transform)){outsideClean&=WhiteAmount(sr)==0;if(sr.name.StartsWith("DetachedArmor_"))detached++;}
            Record("whole_white_includes_armor_only_attached_rig",visible>=12&&allWhite&&outsideClean&&detached>0,"white attached sprites="+visible+" outside/debris/background/player clean="+outsideClean+" actual detached sprites="+detached);
            yield return Snapshot("whole-attached-white-flash");
            visual.SetFlash(0);bool restored=true;foreach(var pair in originals)restored&=pair.Key.color==pair.Value&&WhiteAmount(pair.Key)==0;
            Record("white_flash_restores_exact_colors",restored,"original renderer colors and zero material flash restored");yield return Snapshot("white-flash-restored");
            session.enabled=true;boss.enabled=true;yield return Setup(0,-2.2f);
            var stop=typeof(BossFightSession).GetMethod("HitStop",BindingFlags.Instance|BindingFlags.NonPublic);
            stop.Invoke(session,new object[]{.028f});stop.Invoke(session,new object[]{.045f});
            Record("simultaneous_hitstop_uses_max",session.HitStopRemaining<=.046f&&session.HitStopRemaining>.02f,"two same-frame requests .028/.045 => remaining="+session.HitStopRemaining);
            yield return new WaitForSecondsRealtime(.08f);
            player.ReceiveAttack(901001,1,Vector2.zero,gameObject);
            stop.Invoke(session,new object[]{.055f});float clock=boss.EncounterSeconds;Vector2 position=player.Body.position;float scaled=Time.time;
            player.SubmitInput(1,true,true,true);yield return null;
            bool stopped=session.HitStopRemaining>0;
            Record("hitstop_freezes_combat_clock_and_physics",stopped&&Time.time==scaled&&boss.EncounterSeconds==clock&&player.Body.position==position&&player.Health.IsInvincible,"live-frame stop="+stopped+" scaled delta="+(Time.time-scaled)+" encounter delta="+(boss.EncounterSeconds-clock)+" actual hurt immunity remains="+player.Health.IsInvincible);
            yield return new WaitForSecondsRealtime(.09f);
            for(int i=0;i<20;i++)stop.Invoke(session,new object[]{.055f});
            Record("hitstop_cap_and_window_budget",session.MaximumHitStopRequested<=.0551f&&session.HitStopBudgetSpent<=.1601f,"max="+session.MaximumHitStopRequested+" window spend="+session.HitStopBudgetSpent);
            yield return new WaitForSecondsRealtime(.1f);
            feedback.ShakeScale=0;feedback.FlashScale=0;feedback.PlayerHit(player.transform.position,Vector2.left);yield return null;
            Record("feedback_settings_off",feedback.CurrentCameraOffset<.001f&&session.FlashScale==0,"camera offset="+feedback.CurrentCameraOffset+" flash="+session.FlashScale);
            feedback.ShakeScale=1;feedback.FlashScale=1;feedback.PlayerHit(player.transform.position,Vector2.right);yield return null;
            Record("directional_shake_bounded",feedback.LastImpactDirection.x>0&&feedback.CurrentCameraOffset<=.181f&&feedback.ShakeTimeRemaining<=.221f,"direction="+feedback.LastImpactDirection+" offset="+feedback.CurrentCameraOffset+" remaining="+feedback.ShakeTimeRemaining);
            feedback.ResetEffects();yield return null;
            Record("feedback_reset_clears_camera",feedback.CurrentCameraOffset<.001f,"offset="+feedback.CurrentCameraOffset);
            feedback.ShakeScale=.65f;feedback.FlashScale=1;yield return Snapshot("feedback-settings-restored");
        }
        private IEnumerator V6Tempo()
        {
            var stop=typeof(DoCheolBossController).GetMethod("BeginActionWait",BindingFlags.Instance|BindingFlags.NonPublic);
            var baseline=new List<float>();bool repeat=true,varied=false,bounded=true,noLongPair=true;float previous=0;
            for(int run=0;run<2;run++)
            {
                boss.SetFightActive(false);boss.ConfigureBehaviourSeed(20261005);yield return Setup(0,-2.2f);
                for(int i=0;i<24;i++)
                {
                    stop.Invoke(boss,null);float wait=boss.LastSampledActionWait;
                    if(run==0){baseline.Add(wait);bounded&=wait>=1.15f&&wait<=1.33f;if(i>0){varied|=Mathf.Abs(wait-previous)>.005f;noLongPair&=!(wait>1.15f+.18f*.49f&&previous>1.15f+.18f*.49f);}previous=wait;}
                    else repeat&=wait==baseline[i];
                }
            }
            Record("wait_seed_reproduces_variation",repeat&&varied&&bounded&&noLongPair,"24 once-sampled waits replay exactly; P1 bounds1.15..1.33; no consecutive upper band");
            foreach(int phase in new[]{1,2})
            {
                boss.SetFightActive(false);boss.ConfigureBehaviourSeed(17041);yield return V5Fixture(phase,0,-2.55f);
                report.usesModifiedInvulnerability=true;float began=Time.time,end=began+26;var packages=new HashSet<int>();var contacts=new HashSet<int>();var counters=new HashSet<int>();
                float min=float.MaxValue,max=0,total=0;int count=0;
                while(Time.time<end)
                {
                    player.Health.DamageEnabled=false;player.SubmitInput(0,false,false,false);
                    if(boss.State==BossState.Telegraph&&!boss.IsComboFollowup)packages.Add(boss.AttackId);
                    if(boss.ActiveBounds.size.x>0)contacts.Add(boss.AttackId);
                    if(boss.State==BossState.Recovery)counters.Add(boss.LastCompletedAttackId);
                    yield return null;
                }
                foreach(float wait in boss.ActionWaitSamples){min=Mathf.Min(min,wait);max=Mathf.Max(max,wait);total+=wait;count++;}
                float mean=count>0?total/count:0,lower=phase==1?1.15f:.32f,upper=phase==1?1.33f:.44f;
                var sample=new V6TempoSample{phase=phase,seed=17041,packages=packages.Count,contacts=contacts.Count,counterWindows=counters.Count,a=boss.QuickSlamCount,b=boss.BurrowFiveCount,c=boss.ArcLeapCount,maxQuick=boss.MaximumConsecutiveQuickSlams,seconds=Time.time-began,underground=boss.UndergroundSeconds,waitMean=mean,waitMinimum=min,waitMaximum=max,repositionSpeed=boss.PhaseRepositionSpeed};
                foreach(float wait in boss.ActionWaitSamples)sample.waits.Add(wait);report.tempo.Add(sample);
                Record("P"+phase+"_tempo_observation",count>=2&&min>=lower-.001f&&max<=upper+.001f&&packages.Count>=3&&contacts.Count>=2&&counters.Count>=2&&boss.MaximumConsecutiveQuickSlams<=2,"wait mean="+mean+" n="+count+" packages="+packages.Count+" contacts="+contacts.Count+" counters="+counters.Count+" underground="+boss.UndergroundSeconds+"/"+(Time.time-began));
                yield return Snapshot("P"+phase+"-tempo-result");
            }
            Record("P2_faster_wait_and_reposition",report.tempo[1].waitMean<report.tempo[0].waitMean&&report.tempo[1].repositionSpeed>report.tempo[0].repositionSpeed,"P1/P2 wait="+report.tempo[0].waitMean+"/"+report.tempo[1].waitMean+" speed="+report.tempo[0].repositionSpeed+"/"+report.tempo[1].repositionSpeed);
            player.Health.DamageEnabled=true;
        }
    }
}
