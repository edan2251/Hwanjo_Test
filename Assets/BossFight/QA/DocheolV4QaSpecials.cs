using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;

namespace FourGuardians.BossFight
{
    public sealed partial class DocheolV4QaDriver
    {
        private int reactingId=-1,punishId=-1,rockSeen;
        private float recognizedAt;
        private readonly HashSet<int> seenRocks=new HashSet<int>();
        private Vector3[] cachedRockRoute;
        private float cachedRouteOrigin,lastRouteMove;
        private string cachedSignature;
        private void Evasion(bool retaliate=false)
        {
            if(boss.ControlsLocked){player.SubmitInput(0,false,false,false);return;}
            if(reactingId!=boss.AttackId){reactingId=boss.AttackId;recognizedAt=Time.time;}
            bool ready=Time.time-recognizedAt>=.2f,jump=false,attack=false;float move=0;
            if(boss.RockfallActive)
            {
                foreach(DocheolRockfall rock in boss.Rockfalls)
                    if(seenRocks.Add(rock.AttackId)){rockSeen++;report.events.Add("rock id="+rock.AttackId+" warning="+rock.WarningDuration+" x="+rock.LockedX);}
                string signature=boss.AttackId+"/"+boss.RockfallEventCount;
                foreach(DocheolRockfall rock in boss.Rockfalls)signature+="/"+rock.AttackId+"/"+rock.Stage;
                if(cachedRockRoute==null || cachedSignature!=signature || boss.EncounterSeconds-cachedRouteOrigin>=cachedRockRoute[cachedRockRoute.Length-1].z)
                {
                    if(boss.TryGetRockfallEscapeRoute(out Vector3[] route) && route.Length>0)
                    {cachedRockRoute=route;cachedRouteOrigin=boss.EscapeRouteOriginTime;cachedSignature=signature;}
                }
                if(cachedRockRoute!=null)
                {
                    float t=boss.EncounterSeconds-cachedRouteOrigin;
                    int index=Mathf.Clamp(Mathf.CeilToInt((t+.02f)/.02f),0,cachedRockRoute.Length-1);
                    Vector3 sample=cachedRockRoute[index];
                    float delta=sample.x-player.HurtBounds.center.x;
                    move=t<.20f?lastRouteMove:Mathf.Abs(delta)>.025f?Mathf.Sign(delta):0;
                    jump=sample.y>player.HurtBounds.min.y+.12f && player.Movement.IsGrounded;
                    lastRouteMove=move;
                }
            }
            else {cachedRockRoute=null;cachedSignature=null;lastRouteMove=0;}
            if(ready && (boss.State==BossState.Telegraph || boss.State==BossState.Active || boss.State==BossState.Link))
            {
                if(boss.Pattern==BossPattern.DT03Bite && !boss.RockfallActive)move=WalkOut(boss.WarningBounds);
                // The timed rock route already includes the committed sweep and its jump.
                // An independent early sweep jump can land inside a later rock's impact.
                else if(boss.Pattern==BossPattern.DT02Sweep && !boss.RockfallActive && boss.State==BossState.Telegraph && boss.LockedTellDuration*(1-boss.StateProgress)<=.22f)jump=true;
                else if(boss.Pattern==BossPattern.WholeBodyBite)move=WalkOut(boss.WarningBounds);
                else if(boss.Pattern==BossPattern.BBurrowLeap)
                {
                    if(boss.AttackStage=="submerge" || boss.AttackStage=="travel" || boss.AttackStage=="rise" || boss.AttackStage=="flight")move=WalkOut(boss.WarningBounds);
                    else if(boss.AttackStage=="wave_delay" || boss.AttackStage=="wave")
                    {
                        float span=Mathf.Max(boss.TargetX-boss.ArenaLeft,boss.ArenaRight-boss.TargetX);
                        float arrival=Mathf.Abs(player.HurtBounds.center.x-boss.TargetX)/Mathf.Max(.01f,span)*boss.WaveTravelDuration;
                        float eta=boss.AttackStage=="wave_delay"?(1-boss.AttackStageProgress)*boss.WaveDelayDuration+arrival:arrival-boss.AttackStageProgress*boss.WaveTravelDuration;
                        if(eta<=.22f && eta>=-.04f && player.Movement.IsGrounded)jump=true;
                    }
                }
            }
            if(retaliate && boss.State==BossState.Recovery && !boss.RockfallActive)
            {
                float gap=boss.transform.position.x-player.Body.position.x;
                move=Mathf.Abs(gap)>2.6f?Mathf.Sign(gap):Mathf.Sign(gap)*.02f;
                if(Mathf.Abs(gap)<=3.1f && punishId!=boss.AttackId){attack=true;punishId=boss.AttackId;}
            }
            player.SubmitInput(move,jump,attack,false);
        }
        private IEnumerator PhaseSeed(float bx,float px)
        {
            yield return Setup(bx,px);report.usesSyntheticDamage=true;
            boss.Health.TakeDamage(new DamageInfo(20,boss.GetArmorWorldPoint(0),Vector2.zero,gameObject));
            float end=Time.time+12f;
            while((boss.Phase!=2 || boss.RockfallActive) && Time.time<end && !player.IsDead){Evasion();yield return null;}
            Record("phase2_fixture_ready",boss.Phase==2 && !boss.RockfallActive && !player.IsDead,"phase="+boss.Phase+" hp="+player.Health.CurrentHealth);
            boss.SetFightActive(false);boss.transform.position=new Vector3(bx,boss.FloorY,0);player.ResetForFight(new Vector2(px,boss.FloorY+.025f));
            player.SetExternalInput(true);boss.SetFightActive(true);yield return null;reactingId=-1;
        }
        private IEnumerator SweepDash()
        {
            yield return Setup(2.8f,.6f);int hits=damageEvents;float initial=player.Body.position.x;
            Record("sweep_dash_starts",boss.ForceCombo("2"),"controlled initial sweep");
            float began=Time.time,end=began+2f;bool accepted=false,immune=false,complete=false;
            while(Time.time<end)
            {
                bool request=Time.time-began>=.20f && !accepted;
                player.SubmitInput(request?boss.Facing:0,false,false,request);
                if(player.Movement.Action==WarriorAction.Dash){accepted=true;immune|=player.Health.IsInvincible;Capture("sweep-dash-outside");}
                if(boss.State==BossState.Recovery){complete=true;break;}
                yield return null;
            }
            Record("sweep_dash_physical_evasion",accepted&&complete&&damageEvents==hits&&Mathf.Abs(player.Body.position.x-initial)>2.5f,"accepted="+accepted+" distance="+Mathf.Abs(player.Body.position.x-initial)+" incoming="+(damageEvents-hits));
            Record("dash_has_no_added_immunity",accepted&&!immune,"Health.IsInvincible during actual dash="+immune);
            yield return Snapshot("sweep-dash-result");
        }
        private IEnumerator BurrowMatrix()
        {
            int phase; if(!int.TryParse(Arg("-bossqa-phase","1"),out phase))phase=1;
            foreach(float x in new[]{0f,-6.4f,6.4f})
                yield return Burrow(phase,x,x==0?-2.1f:x,"rest");
            yield return Burrow(phase,0,-2.1f,"3-2");
        }
        private IEnumerator Burrow(int phase,float bx,float px,string exit)
        {
            if(phase==2)yield return PhaseSeed(bx,px);else yield return Setup(bx,px);
            string label="P"+phase+"-B-"+bx.ToString("F1")+"-"+exit;
            int incoming=damageEvents,jumps=acceptedJumpCounter;bool force=boss.ForceBurrowPlan(exit);
            Record(label+"_starts",force,"initial package only; exit preselected="+exit);
            float target=boss.TargetX;int facing=boss.Facing,initial=boss.AttackId;float end=Time.time+11f;
            bool fixedTarget=true,wave=false,land=false,done=false,followup=false;int landingSteps=0,waveSteps=0;
            while(Time.time<end && !player.IsDead)
            {
                Evasion();
                if(boss.Pattern==BossPattern.BBurrowLeap)
                {
                    fixedTarget&=Mathf.Abs(target-boss.TargetX)<.001f && facing==boss.Facing && boss.TargetLocked;
                    land|=boss.AttackStage=="land";wave|=boss.AttackStage=="wave";
                    if(boss.AttackStage=="land")landingSteps++;
                    if(boss.AttackStage=="wave")waveSteps++;
                    Capture(label+"-"+boss.AttackStage);
                }
                else if(boss.BurrowPackageActive && boss.State==BossState.Telegraph){followup=true;Capture(label+"-followup");}
                if(wave && (exit=="rest" && boss.State==BossState.Recovery || exit!="rest" && followup && boss.State==BossState.Recovery)){done=true;break;}
                yield return null;
            }
            Record(label+"_fixed_first_tell",fixedTarget,"target="+target+" fixed facing="+facing);
            Record(label+"_full_package",land&&wave&&done,"landFrames="+landingSteps+" waveFrames="+waveSteps+" finalRecovery="+done);
            Record(label+"_normal_evasion",damageEvents==incoming,"incoming="+(damageEvents-incoming)+" acceptedJumps="+(acceptedJumpCounter-jumps));
            if(exit!="rest")Record(label+"_prepared_followup",followup,"normal tell after landing and wave");
            if(Mathf.Abs(bx)>5)Record(label+"_opens_wall_space",Mathf.Abs(target)<Mathf.Abs(bx)-1,"wall target="+target);
            yield return Snapshot(label+"-result");
        }
        private IEnumerator WholeMatrix()
        {
            yield return PhaseSeed(0,-2.4f);yield return Whole(false,0,-2.4f);
            boss.SetFightActive(false);boss.transform.position=Vector3.zero;player.ResetForFight(new Vector2(-2.4f,boss.FloorY+.025f));player.SetExternalInput(true);boss.SetFightActive(true);
            yield return Whole(true,0,-2.4f);
        }
        private IEnumerator Whole(bool greedy,float bx,float px)
        {
            string label=greedy?"whole-greed":"whole-safe";int incoming=damageEvents;
            Record(label+"_starts",boss.ForcePattern(BossPattern.WholeBodyBite),"controlled phase2 initial special");
            int initialFacing=boss.Facing,firstId=boss.AttackId,lastId=firstId;float firstX=boss.WarningBounds.center.x,secondX=boss.SecondaryWarningBounds.center.x;
            float end=Time.time+7f,recoverEnd=0,recoverX=0;int recoverHp=0,damage=0;bool recovered=false,done=false,stable=true;HashSet<int> contacts=new HashSet<int>();
            while(Time.time<end && !player.IsDead)
            {
                if(greedy)player.SubmitInput(0,false,boss.State!=BossState.Recovery,false);else Evasion();
                if(boss.State==BossState.Telegraph || boss.State==BossState.Active)
                {
                    stable&=boss.Facing==initialFacing;
                    stable&=Mathf.Abs(boss.TargetX-(boss.AttackId==firstId?firstX:secondX))<.001f;
                    if(boss.AttackStage=="whole_hit")contacts.Add(boss.AttackId);
                    Capture(label+"-"+boss.AttackStage+"-"+boss.WholeSwingSide);
                    if(boss.AttackStage=="whole_cross" && boss.AttackStageProgress>.46f && boss.AttackStageProgress<.54f)Capture(label+"-whole-cross-mid");
                }
                if(boss.State==BossState.Recovery)
                {
                    if(!recovered){recovered=true;recoverEnd=Time.time+boss.RecoveryRemaining;recoverX=boss.transform.position.x;recoverHp=boss.Health.CurrentHealth;Record(label+"_long_recovery",boss.RecoveryRemaining>=1.65f,"remaining="+boss.RecoveryRemaining);}
                    stable&=Mathf.Abs(boss.transform.position.x-recoverX)<.001f;
                    if(!greedy)player.SubmitInput(Mathf.Sign(boss.transform.position.x-player.Body.position.x)*.02f,false,true,false);
                    damage=Mathf.Max(damage,recoverHp-boss.Health.CurrentHealth);Capture(label+"-recovery");
                    if(Time.time>=recoverEnd-Time.fixedDeltaTime){done=true;break;}
                }
                yield return null;
            }
            Record(label+"_two_committed_contacts",contacts.Count==2&&stable&&done,"contactIDs="+contacts.Count+" lockedTargets="+stable);
            Record(label+"_evasion_risk",greedy?damageEvents>incoming:damageEvents==incoming,"incoming="+(damageEvents-incoming));
            if(!greedy)Record(label+"_long_punish",damage>=2,"ordinary recovery damage="+damage);
            yield return Snapshot(label+"-result");
        }
        private IEnumerator PhaseEvents()
        {
            yield return Entry(-8.5f,0);
            // Controlled initial basic; the late event then uses the production pending queue.
            boss.SetFightActive(false);boss.transform.position=Vector3.zero;player.ResetForFight(new Vector2(-2.1f,boss.FloorY+.025f));player.SetExternalInput(true);boss.SetFightActive(true);
            bool plan=boss.ForceCombo("3-2");int before=boss.RockfallEventCount,id=boss.AttackId;
            boss.Health.TakeDamage(new DamageInfo(10,boss.GetArmorWorldPoint(2),Vector2.zero,gameObject));
            Record("late_queued_without_interrupt",plan&&boss.LateRockfallPending&&boss.AttackId==id&&boss.ComboCode=="3-2","pending="+boss.LateRockfallPending+" hp="+boss.Health.CurrentHealth);
            float end=Time.time+12f,recoveryEnds=-1;bool sawRecovery=false,afterRecovery=false,excluded=true;int hits=damageEvents;
            while(Time.time<end && !player.IsDead)
            {
                Evasion();
                if(boss.State==BossState.Recovery && boss.ComboIndex==boss.ComboLength-1 && !sawRecovery){sawRecovery=true;recoveryEnds=Time.time+boss.RecoveryRemaining;}
                if(boss.RockfallEventCount>before)afterRecovery|=sawRecovery&&Time.time>=recoveryEnds-Time.fixedDeltaTime;
                if(boss.RockfallActive)excluded&=boss.Pattern!=BossPattern.BBurrowLeap&&boss.Pattern!=BossPattern.WholeBodyBite;
                Capture("late-"+boss.State+"-"+boss.AttackStage);
                if(boss.RockfallEventCount==2 && !boss.RockfallActive)break;
                yield return null;
            }
            Record("late_once_after_recovery",boss.RockfallEventCount==2&&afterRecovery,"events="+boss.RockfallEventCount+" afterFullRecovery="+afterRecovery);
            Record("late_rockfall_excludes_specials",excluded,"only basic family during active rocks");
            Record("late_rockfall_safe_route",damageEvents==hits,"incoming="+(damageEvents-hits));
            yield return Entry(8.5f,3);
            yield return PhaseSeed(0,-2.1f);
            boss.ForceCombo("3-2");boss.Health.TakeDamage(new DamageInfo(10,boss.GetArmorWorldPoint(1),Vector2.zero,gameObject));
            yield return new WaitForSeconds(.14f);int count=boss.RockfallEventCount;
            boss.Health.TakeDamage(new DamageInfo(boss.Health.CurrentHealth,boss.GetArmorWorldPoint(0),Vector2.zero,gameObject));
            yield return new WaitForSeconds(.8f);
            Record("death_skips_pending_late_event",boss.Health.IsDead&&!boss.LateRockfallPending&&boss.RockfallEventCount==count,"hp="+boss.Health.CurrentHealth+" events="+boss.RockfallEventCount);
            yield return Snapshot("pending-late-death");
        }
        private IEnumerator Entry(float px,int region)
        {
            yield return Setup(0,px);report.usesSyntheticDamage=true;List<int> order=new List<int>();
            Action<int,int,Vector2> armor=(i,m,point)=>order.Add(i);boss.ArmorBroken+=armor;
            boss.Health.TakeDamage(new DamageInfo(20,boss.GetArmorWorldPoint(region),Vector2.zero,gameObject));
            float end=Time.time+12f,returnAt=-1,firstWarning=-1;bool protectedState=true,sawLock=false,allOff=true,excluded=true;int hp=player.Health.CurrentHealth,hits=damageEvents,rocksBefore=rockSeen;float lockedX=player.Body.position.x;
            while(Time.time<end && !player.IsDead)
            {
                if(boss.ControlsLocked)
                {
                    sawLock=true;protectedState&=!player.FightActive&&!player.Health.DamageEnabled&&!boss.Health.DamageEnabled;
                    allOff&=boss.ArmorMask==0;protectedState&=Mathf.Abs(player.Body.position.x-lockedX)<.08f;
                    player.SubmitInput(1,true,true,true);Capture("entry-"+px+"-"+boss.AttackStage);
                }
                else
                {
                    if(sawLock && returnAt<0)returnAt=Time.time;
                    Evasion();
                    if(boss.Rockfalls.Count>0 && firstWarning<0)firstWarning=Time.time;
                }
                if(boss.Phase==2)allOff&=boss.ArmorMask==0;
                if(boss.RockfallActive)excluded&=boss.Pattern!=BossPattern.BBurrowLeap&&boss.Pattern!=BossPattern.WholeBodyBite;
                if(boss.RockfallEventCount==1&&!boss.RockfallActive&&boss.Phase==2)break;
                yield return null;
            }
            boss.ArmorBroken-=armor;
            Record("entry_"+px+"_protected_stages",sawLock&&protectedState&&allOff,"controls/damage locked="+protectedState+" armor off="+allOff);
            Record("entry_"+px+"_fixed_armor_order",order.Count==4&&order[0]==0&&order[1]==1&&order[2]==2&&order[3]==3,"hitRegion="+region+" order="+string.Join(",",order));
            Record("entry_"+px+"_return_then_warning",returnAt>=0&&firstWarning-returnAt>=.35f,"firstWarningAfterReturn="+(firstWarning-returnAt));
            Record("entry_"+px+"_one_event_safe_path",boss.RockfallEventCount==1&&excluded&&damageEvents==hits&&rockSeen>rocksBefore,"events="+boss.RockfallEventCount+" incoming="+(damageEvents-hits)+" rocksSeen="+(rockSeen-rocksBefore));
            yield return Snapshot("entry-"+px+"-complete");
        }
        private IEnumerator Natural()
        {
            float seconds;if(!float.TryParse(Arg("-bossqa-seconds","150"),out seconds))seconds=150;
            int initial=boss.Health.CurrentHealth,hits=damageEvents;float end=Time.time+seconds;HashSet<int> choices=new HashSet<int>();
            while(Time.time<end && !player.IsDead && !boss.Health.IsDead)
            {
                if(boss.State==BossState.Telegraph && boss.ComboIndex==0 && choices.Add(boss.AttackId))report.events.Add("choice t="+boss.EncounterSeconds.ToString("F2")+" phase="+boss.Phase+" pattern="+boss.Pattern+" plan="+boss.ComboCode);
                Evasion(true);Capture("natural-P"+boss.Phase+"-"+boss.Pattern+"-"+boss.AttackStage);yield return null;
            }
            Record("natural_normal_input_progress",boss.Health.CurrentHealth<initial,"boss hp="+boss.Health.CurrentHealth+" incoming="+(damageEvents-hits)+" phase="+boss.Phase+" rockEvents="+boss.RockfallEventCount+" whole="+boss.WholeBodyAttackCount);
            Record("natural_victory",boss.Health.IsDead&&!player.IsDead,"state="+session.State+" playerHp="+player.Health.CurrentHealth);
            yield return new WaitForSecondsRealtime(.7f);yield return Snapshot("natural-result");
        }
        private IEnumerator DeathRetry()
        {
            float end=Time.time+60;while(!player.IsDead && Time.time<end){player.SubmitInput(0,false,false,false);Capture("death-"+boss.AttackStage);yield return null;}
            Record("natural_death",player.IsDead&&session.State==EncounterState.Defeat,"hp="+player.Health.CurrentHealth+" incoming="+damageEvents);
            float x=player.Body.position.x;for(int i=0;i<15;i++){player.SubmitInput(1,true,true,true);yield return null;}
            Record("death_blocks_input",Mathf.Abs(player.Body.position.x-x)<.08f,"dead movement="+(player.Body.position.x-x));yield return Snapshot("death-screen");
            session.Retry();yield return null;
            float deadline=Time.realtimeSinceStartup+10;
            do{session=FindFirstObjectByType<BossFightSession>();player=FindFirstObjectByType<BossFightPlayerAdapter>();boss=FindFirstObjectByType<DoCheolBossController>();yield return null;}
            while((session==null||player==null||boss==null||session.State!=EncounterState.Fighting)&&Time.realtimeSinceStartup<deadline);
            if(player!=null){player.Damaged+=OnDamaged;player.SetExternalInput(true);}
            Record("retry_resets_encounter",player!=null&&boss!=null&&player.Health.CurrentHealth==8&&boss.Health.CurrentHealth==40&&boss.ArmorMask==15&&boss.Phase==1&&boss.RockfallEventCount==0,"fresh shared HP and no stale rocks");
            yield return Snapshot("retry-ready");
        }
    }
}
