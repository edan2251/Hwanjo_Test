using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FourGuardians.CourseContent.Movement;

namespace FourGuardians.BossFight
{
    public sealed partial class DocheolV4QaDriver
    {
        private IEnumerator V5AdvanceMatrix()
        {
            foreach(int side in new[]{-1,1})foreach(bool wall in new[]{false,true})foreach(string plan in new[]{"3","2","3-2"})
            {
                float bx=wall?side*6.3f:0,px=bx+side*2.1f;
                yield return Setup(bx,px);string label="advance-"+side+"-"+(wall?"wall":"center")+"-"+plan;
                int hits=damageEvents,completed=0;bool stable=true,recovered=false,insideWarning=true;float x=bx,recoverX=0,goal=0;int id=0;
                Record(label+"_starts",boss.ForceCombo(plan),"ordinary movement/jump; controlled initial plan");
                float began=Time.time,end=began+5f;
                while(Time.time<end && !player.IsDead)
                {
                    if(id!=boss.AttackId){id=boss.AttackId;goal=boss.TargetX;}
                    if(boss.State==BossState.Telegraph||boss.State==BossState.Active||boss.State==BossState.Link)
                    {stable&=boss.Facing==side&&Mathf.Abs(goal-boss.TargetX)<.001f;stable&=boss.ComboAdvanceDistance<=.501f;}
                    Evasion();
                    if(boss.ActiveBounds.size.x>0)insideWarning&=boss.ActiveBounds.min.x>=boss.WarningBounds.min.x-.001f&&boss.ActiveBounds.max.x<=boss.WarningBounds.max.x+.001f;
                    if(boss.State==BossState.Recovery)
                    {if(!recovered){recovered=true;recoverX=boss.transform.position.x;}stable&=Mathf.Abs(boss.transform.position.x-recoverX)<.001f;}
                    if(recovered && boss.State==BossState.ActionWait){completed=1;break;}
                    if(side==-1 && !wall && plan=="3" && boss.State==BossState.Telegraph && boss.StateProgress>.7f)Capture("bite-weight-before");
                    if(side==-1 && !wall && plan=="3" && boss.State==BossState.Active && boss.StateProgress>.50f)Capture("bite-weight-strike");
                    if(side==-1 && !wall && plan=="2" && boss.State==BossState.Active && boss.StateProgress>.5f)Capture("paw-weight-strike");
                    yield return null;
                }
                float advance=Mathf.Abs(boss.transform.position.x-x);
                Record(label+"_committed_limited_advance",stable&&completed==1&&advance>.01f&&advance<=.501f,"advance="+advance+" comboBudget="+boss.ComboAdvanceDistance+" fixedTarget="+stable+" recoveryCompleted="+completed);
                Record(label+"_normal_evasion",damageEvents==hits,"incoming="+(damageEvents-hits)+" actualX="+player.Body.position.x+" speed="+player.Movement.MoveSpeed);
                Record(label+"_trajectory_inside_locked_warning",insideWarning,"mouth travels from initial root to committed end; paw volume follows actual hoof");
            }
        }
        private IEnumerator V5WholeMatrix()
        {
            yield return V5TopReproduction();
            yield return V5WholeCase("walk-left",0,-2.4f);
            yield return V5WholeCase("walk-right",0,2.4f);
            yield return V5WholeCase("dash-center",0,0);
            yield return V5WholeCase("wall-left",-6.3f,-8.4f);
            yield return V5WholeCase("wall-right",6.3f,8.4f);
            yield return V5WholeCase("center-J",0,0);
            yield return V5WholeCase("top-J",0,0);
            yield return V5WholeCase("after-four-greed",0,-5f);
            yield return V5WholeCase("after-five-punish",0,-2.4f);
        }
        private IEnumerator V5TopReproduction()
        {
            yield return V5Fixture(2,0,0);boss.enabled=false;
            FixtureProperty(boss,"State",BossState.Idle);FixtureProperty(boss,"Pattern",BossPattern.None);FixtureProperty(boss,"AttackStage","idle");
            player.ResetForFight(new Vector2(0,boss.FloorY+3.4f),-1);player.SetExternalInput(true);
            bool allTrigger=true,upperGround=false,lift=false,landed=false;int airAttacks=0;
            foreach(var collider in boss.GetComponentsInChildren<Collider2D>())allTrigger&=collider.isTrigger;
            float end=Time.time+2f,previousY=player.Body.position.y,nextAttack=Time.time;
            while(Time.time<end)
            {
                bool attack=Time.time>=nextAttack;if(attack)nextAttack=Time.time+.55f;
                player.SubmitInput(0,false,attack,false);
                if(player.HurtBounds.min.y>boss.FloorY+.35f)upperGround|=player.Movement.IsGrounded;
                if(player.Movement.Action==WarriorAction.JumpAttack)airAttacks++;
                lift|=player.Body.position.y>previousY+.02f;previousY=player.Body.position.y;
                landed|=player.Movement.IsGrounded && player.HurtBounds.min.y<boss.FloorY+.15f;
                if(player.Movement.Action==WarriorAction.JumpAttack)Capture("top-repro-air-J");yield return null;
            }
            Record("top_has_no_solid_boss_platform",allTrigger&&!upperGround,"allBossCollidersTrigger="+allTrigger+" groundedAboveFloor="+upperGround);
            Record("air_J_does_not_lift_or_sustain_top",airAttacks>0&&!lift&&landed,"airAttackFrames="+airAttacks+" upwardMovement="+lift+" landedOnActualFloor="+landed);
            yield return Snapshot("top-repro-floor");boss.enabled=true;
        }
        private IEnumerator V5WholeCase(string behavior,float bx,float px)
        {
            yield return V5Fixture(2,bx,px);
            bool safe=behavior.StartsWith("walk")||behavior.StartsWith("wall")||behavior=="dash-center"||behavior=="after-five-punish";
            int incoming=damageEvents,initialHp=boss.Health.CurrentHealth;
            Record(behavior+"_starts",boss.ForcePattern(BossPattern.WholeBodyBite),"controlled P2 initial whole; ordinary movement/actions; HP damage enabled");
            int facing=boss.Facing;float root=boss.transform.position.x,began=Time.time,end=Time.time+9f;
            Bounds left=boss.WarningBounds,right=boss.SecondaryWarningBounds,both=left;both.Encapsulate(right);
            var ids=new HashSet<int>();var sides=new List<int>();var times=new List<float>();
            bool stable=true,finalWindup=false,prematureRecovery=false,recovered=false,done=false,immune=false,dashRequested=false,dashAccepted=false;
            float recoveryStart=0,recoveryX=0;int recoveryHp=0,punishDamage=0;float nextJ=0;
            while(Time.time<end && !player.IsDead)
            {
                float move=0;bool jump=false,attack=false,dash=false;
                if(boss.State==BossState.Telegraph || boss.State==BossState.Active)
                {
                    stable&=boss.Facing==facing&&Mathf.Abs(boss.transform.position.x-root)<.001f&&boss.TargetLocked;
                    stable&=Mathf.Abs(boss.TargetX-(boss.WholeSwingSide<0?left.center.x:right.center.x))<.001f;
                    if(boss.AttackStage=="whole_hit" && ids.Add(boss.AttackId)){sides.Add(boss.WholeSwingSide);times.Add(Time.time-began);report.events.Add("whole "+behavior+" contact="+sides.Count+" side="+boss.WholeSwingSide+" t="+(Time.time-began)+" bounds="+boss.ActiveBounds);}
                    finalWindup|=boss.AttackStage=="whole_final_windup";
                    if(safe && Time.time-began>=.2f)move=WalkOut(both);
                    if(behavior=="dash-center" && !dashRequested && Time.time-began>=.2f){dash=true;dashRequested=true;}
                    if(behavior=="center-J"||behavior=="top-J")
                    {
                        if(Time.time>=nextJ){attack=true;nextJ=Time.time+.55f;}
                        if(behavior=="top-J" && player.Movement.IsGrounded && player.Movement.Action!=WarriorAction.BasicAttack){jump=true;attack=false;}
                    }
                    if(behavior=="after-four-greed" && boss.WholeContactIndex>=3 && finalWindup)
                    {float delta=bx-2.1f-player.Body.position.x;move=delta>.12f?1:0;if(delta<=.12f)attack=true;}
                    if(behavior=="walk-left")
                    {if(boss.AttackStage=="whole_tell")Capture("five-long-tell");if(boss.AttackStage=="whole_final_windup" && boss.AttackStageProgress>.45f)Capture("five-final-reprepare");if(boss.AttackStage=="whole_hit" && boss.WholeContactIndex==4)Capture("five-last-left");}
                    if(behavior=="walk-right" && boss.AttackStage=="whole_tell" && boss.AttackStageProgress>.10f && boss.AttackStageProgress<.17f)Capture("five-right-to-left-first-preparation");
                    if(behavior=="center-J" && boss.ActiveBounds.size.x>0)Capture("five-center-real-strike");
                    if(behavior=="top-J" && boss.ActiveBounds.size.x>0)Capture("five-air-J-real-strike");
                }
                if(player.Movement.Action==WarriorAction.Dash){dashAccepted=true;immune|=player.Health.IsInvincible;}
                if(boss.State==BossState.Recovery)
                {
                    prematureRecovery|=ids.Count<5;
                    if(!recovered){recovered=true;recoveryStart=Time.time;recoveryX=boss.transform.position.x;recoveryHp=boss.Health.CurrentHealth;}
                    stable&=Mathf.Abs(boss.transform.position.x-recoveryX)<.001f;
                    if(behavior=="after-five-punish")
                    {float delta=bx-2.4f-player.Body.position.x;move=delta>.12f?1:delta<-.12f?-1:0;attack=Mathf.Abs(delta)<=.12f;}
                    punishDamage=Mathf.Max(punishDamage,recoveryHp-boss.Health.CurrentHealth);
                    if(behavior=="walk-left")Capture("five-guaranteed-recovery");
                    if(Time.time-recoveryStart>=boss.WholeRecoveryDuration-.04f){done=true;break;}
                }
                player.SubmitInput(move,jump,attack,dash);yield return null;
            }
            bool sequence=sides.Count==5;for(int i=0;i<sides.Count;i++)sequence&=sides[i]==(i%2==0?-1:1);
            bool rhythm=times.Count==5;if(rhythm){for(int i=1;i<4;i++)rhythm&=Mathf.Abs(times[i]-times[i-1]-boss.WholeFastInterval)<.10f;rhythm&=times[4]-times[3]>boss.WholeFastInterval+.65f;}
            Record(behavior+"_five_world_left_right_contacts",sequence&&stable&&done,"sides="+string.Join(",",sides)+" times="+string.Join(",",times)+" target/rootFixed="+stable);
            Record(behavior+"_fast_four_delayed_fifth",rhythm&&finalWindup&&!prematureRecovery&&boss.WholeInitialTell>=1.7f,"tell="+boss.WholeInitialTell+" fastInterval="+boss.WholeFastInterval+" finalReprepare="+boss.WholeFinalDelay+" recovery="+boss.WholeRecoveryDuration);
            Record(behavior+"_actual_input_result",safe?damageEvents==incoming:damageEvents>incoming,"incoming="+(damageEvents-incoming)+" bossDamage="+(initialHp-boss.Health.CurrentHealth)+" playerHP="+player.Health.CurrentHealth+" jumpSpeed="+player.Movement.JumpSpeed+" moveSpeed="+player.Movement.MoveSpeed);
            if(behavior=="dash-center")Record("five_dash_has_no_immunity",dashAccepted&&!immune,"actualDash="+dashAccepted+" invincible="+immune);
            if(behavior=="after-five-punish")Record("five_long_recovery_allows_two_hits",punishDamage>=2,"real ordinary sword recovery damage="+punishDamage);
            if(!safe||behavior=="after-five-punish")yield return Snapshot("five-"+behavior+"-result");
        }
    }
}
