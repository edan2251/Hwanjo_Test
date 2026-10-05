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
        [Serializable] public sealed class V5Stats
        {
            public string scenario;public int phase,seed,packages,contacts,chains,turns,movementSegments,maxMovesPerDecision,maxTurnsPerDecision,sideCrossings,burrowPackages,wholePackages;
            public float measuredSeconds,preparationSeconds,actionWaitSeconds,averageCompletedWait,maximumContinuousPreparation,bodyDamage;
        }
        private static void FixtureProperty(DoCheolBossController target,string name,object value)
        {typeof(DoCheolBossController).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
        private IEnumerator V5Fixture(int phase,float bx,float px)
        {
            boss.enabled=true;yield return Setup(bx,px);
            if(phase==2)
            {
                // Explicit steady-P2 measurement fixture. Transition/rocks use separate real V4 regression.
                FixtureProperty(boss,"Phase",2);FixtureProperty(boss,"ArmorMask",0);FixtureProperty(boss,"EntryRockfallUsed",true);
                boss.Health.TakeDamage(new DamageInfo(20,boss.GetArmorWorldPoint(0),Vector2.zero,gameObject));
            }
            yield return null;
        }
        private IEnumerator V5Roots()
        {
            foreach(int side in new[]{-1,1})
                foreach(string area in new[]{"roots","body","boundary","front"})yield return V5RootCase(side,area);
            // Opposite painted side during a whole contact, with unchanged controller facing.
            foreach(int initial in new[]{-1,1})foreach(string area in new[]{"roots","body","boundary"})
                yield return V5RootCase(initial,area,-initial);
            boss.enabled=true;
        }
        private IEnumerator V5RootCase(int side,string area,int shownSide=0)
        {
            boss.enabled=true;yield return Setup(0,side*2.2f);
            boss.ForceCombo("3");boss.enabled=false;
            FixtureProperty(boss,"State",BossState.Idle);FixtureProperty(boss,"Pattern",BossPattern.None);FixtureProperty(boss,"AttackStage","idle");
            string pose="idle";
            if(shownSide!=0)
            {
                pose="whole-contact";
                FixtureProperty(boss,"Pattern",BossPattern.WholeBodyBite);FixtureProperty(boss,"AttackStage","whole_hit");
                FixtureProperty(boss,"WholeFirstSide",-1);FixtureProperty(boss,"WholeSwingSide",shownSide);side=shownSide;
            }
            typeof(DoCheolBossController).GetMethod("HideMarkers",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,null);
            typeof(DoCheolBossController).GetMethod("ClearBounds",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,null);
            yield return null;yield return new WaitForSeconds(.1f);
            var regions=boss.GetComponent<DocheolHitRegions>();
            Bounds root=regions.RootOnlyBounds,body=regions.BodyBounds;
            float edge=side>0?body.min.x:body.max.x;
            int playerFacing=side;float center;
            if(area=="roots")center=side>0?root.min.x-.15f:root.max.x+.15f;
            else if(area=="boundary")center=edge;
            else if(area=="front"){center=side>0?body.max.x-.3f:body.min.x+.3f;playerFacing=-side;}
            else center=edge+side*1.1f;
            player.ResetForFight(new Vector2(center-playerFacing*.8f,boss.FloorY+.025f),playerFacing);
            player.SetExternalInput(true);yield return new WaitForSeconds(.12f);
            int hp=boss.Health.CurrentHealth,blocks=boss.RootBlockCount;uint accepted=player.Movement.AttackSequence;
            int events=0;Action<DamageInfo> listener=d=>events++;boss.Health.DamageTaken+=listener;
            int repetitions=area=="roots"?3:1;
            for(int n=0;n<repetitions;n++)
            {
                player.SubmitInput(0,false,true,false);yield return null;
                float end=Time.time+.68f;
                while(Time.time<end){player.SubmitInput(0,false,false,false);if(area=="roots"?boss.RootBlockCount>blocks:boss.Health.CurrentHealth<hp)Capture("root-"+side+"-"+pose+"-"+area+"-contact");yield return null;}
            }
            boss.Health.DamageTaken-=listener;
            int damage=hp-boss.Health.CurrentHealth,blocked=boss.RootBlockCount-blocks;
            Record("root_"+side+"_"+pose+"_"+area,area=="roots"?damage==0&&events==0&&blocked>=2&&boss.ArmorMask==15:damage==1&&events==1&&blocked==0,
                "ordinary attacks="+(player.Movement.AttackSequence-accepted)+" hpDamage="+damage+" damageEvents="+events+" deflections="+blocked+" body="+body+" root="+root+" controllerFacing="+boss.Facing+" paintedSide="+side);
            if(area=="boundary")Record("mixed_"+side+"_"+pose+"_one_body_hit",damage==1&&blocked==0,"root/body trigger order cannot consume body damage; suppressed="+regions.MixedRootContactsSuppressed);
            if(shownSide==0)yield return Snapshot("root-"+side+"-"+pose+"-"+area+"-result");
            boss.enabled=true;
        }
        private IEnumerator V5Measure(int phase,int seed,string scenario,float seconds)
        {
            float bx=scenario=="wall"?6.3f:0;
            float px=scenario=="overlap"?bx:scenario=="rear_still"?bx+2f:scenario=="flee"?-6f:scenario=="wall"?6.5f:scenario=="front_close"?-1.3f:-2.55f;
            yield return V5Fixture(phase,bx,px);UnityEngine.Random.InitState(seed);
            var result=new V5Stats{phase=phase,seed=seed,scenario=scenario};report.phaseStats.Add(result);
            float began=Time.time,end=began+seconds,waitStart=-1,completedWaitTotal=0,prepStart=-1;int completedWaits=0;
            int initialHp=boss.Health.CurrentHealth,initialTurns=boss.TurnCount,initialMoves=boss.RepositionCount+boss.BackdashCount;
            float initialPrep=boss.PreparationSeconds;
            var tells=new HashSet<int>();var contacts=new HashSet<int>();BossState previous=boss.State;float crossing=1;int previousSide=0;
            while(Time.time<end && !boss.Health.IsDead)
            {
                // Only AI cadence/exploit measurement is damage-disabled. No player speed/action changes.
                player.Health.DamageEnabled=false;
                float sideOffset=player.Body.position.x-boss.transform.position.x;
                int currentSide=sideOffset>.3f?1:sideOffset< -.3f?-1:0;
                if(currentSide!=0){if(previousSide!=0 && previousSide!=currentSide)result.sideCrossings++;previousSide=currentSide;}
                float move=0;
                if(scenario=="flee")move=-1;
                if(scenario=="cross")
                {
                    float offset=player.Body.position.x-boss.transform.position.x;
                    if(offset>1.35f)crossing=-1;else if(offset< -1.35f)crossing=1;move=crossing;
                }
                player.SubmitInput(move,false,scenario=="front_close"||scenario=="overlap",false);
                bool tell=boss.State==BossState.Telegraph;
                if(tell && tells.Add(boss.AttackId))
                {
                    if(!boss.IsComboFollowup){result.packages++;if(boss.ComboCode=="3-2")result.chains++;if(boss.Pattern==BossPattern.BBurrowLeap)result.burrowPackages++;if(boss.Pattern==BossPattern.WholeBodyBite)result.wholePackages++;}
                    report.events.Add("v5_choice p="+phase+" case="+scenario+" id="+boss.AttackId+" plan="+boss.ComboCode+" pattern="+boss.Pattern+" target="+boss.TargetX+" decision="+boss.DecisionTargetX);
                    if(result.packages==1)Capture("P"+phase+"-"+scenario+"-tell");
                }
                if(boss.ActiveBounds.size.x>0 && contacts.Add(boss.AttackId))result.contacts++;
                bool prep=boss.State==BossState.Reposition||boss.State==BossState.Backdash;
                if(prep && prepStart<0)prepStart=Time.time;
                if(!prep && prepStart>=0){result.maximumContinuousPreparation=Mathf.Max(result.maximumContinuousPreparation,Time.time-prepStart);prepStart=-1;}
                if(boss.State==BossState.ActionWait)
                {result.actionWaitSeconds+=Time.deltaTime;if(previous!=BossState.ActionWait)waitStart=Time.time;}
                else if(previous==BossState.ActionWait && waitStart>=0)
                {completedWaits++;completedWaitTotal+=Time.time-waitStart;waitStart=-1;}
                result.maxMovesPerDecision=Mathf.Max(result.maxMovesPerDecision,boss.MovementSegmentsThisDecision);
                result.maxTurnsPerDecision=Mathf.Max(result.maxTurnsPerDecision,boss.TurnSegmentsThisDecision);
                previous=boss.State;yield return null;
            }
            result.measuredSeconds=Time.time-began;result.averageCompletedWait=completedWaits>0?completedWaitTotal/completedWaits:0;
            result.preparationSeconds=boss.PreparationSeconds-initialPrep;result.turns=boss.TurnCount-initialTurns;result.movementSegments=boss.RepositionCount+boss.BackdashCount-initialMoves;
            result.bodyDamage=initialHp-boss.Health.CurrentHealth;
            Record("P"+phase+"_"+scenario+"_bounded_decisions",result.maxMovesPerDecision<=1&&result.maxTurnsPerDecision<=1&&result.maximumContinuousPreparation<=.85f,
                "moves/turns per decision="+result.maxMovesPerDecision+"/"+result.maxTurnsPerDecision+" longestApproach="+result.maximumContinuousPreparation+" prepTotal="+result.preparationSeconds);
            Record("P"+phase+"_"+scenario+"_keeps_acting",result.packages>=2&&result.contacts>=1,
                "packages="+result.packages+" contacts="+result.contacts+" chains="+result.chains+" turns="+result.turns+" damage="+result.bodyDamage);
            if(scenario=="rear_still")Record("P"+phase+"_rear_still_faces_then_attacks",(result.turns>0||result.burrowPackages>0)&&result.packages>=2,"turns="+result.turns+" burrows="+result.burrowPackages);
            if(scenario=="cross")Record("P"+phase+"_cross_keeps_attacking",result.sideCrossings>=2&&result.packages>=2&&result.contacts>=2,"crossings="+result.sideCrossings+" contacts="+result.contacts+" turns="+result.turns+" whole="+result.wholePackages+"; whole-body turn does not require another preparation turn");
            if(scenario=="front_close"||scenario=="overlap")Record("P"+phase+"_"+scenario+"_body_hittable",result.bodyDamage>0,"ordinary body damage="+result.bodyDamage);
            yield return Snapshot("P"+phase+"-"+scenario+"-end");
        }
        private IEnumerator V5Scenarios()
        {
            foreach(int phase in new[]{1,2})foreach(string scenario in new[]{"front_close","overlap","rear_still","cross","flee","wall"})
                yield return V5Measure(phase,20261005,scenario,8f);
        }
        private IEnumerator V5Rhythm()
        {
            foreach(int seed in new[]{17041,20261005})foreach(int phase in new[]{1,2})yield return V5Measure(phase,seed,"cadence",24f);
            float p1wait=0,p2wait=0,p1contacts=0,p2contacts=0,p1chains=0,p2chains=0,p1packages=0,p2packages=0;
            foreach(var r in report.phaseStats)
            {if(r.phase==1){p1wait+=r.averageCompletedWait;p1contacts+=r.contacts;p1chains+=r.chains;p1packages+=r.packages;}else{p2wait+=r.averageCompletedWait;p2contacts+=r.contacts;p2chains+=r.chains;p2packages+=r.packages;}}
            Record("phase_wait_comparison",p1wait>p2wait*2,"P1 mean="+p1wait/2+" P2 mean="+p2wait/2+" separate from .30/.34 basic recovery");
            Record("phase_chain_comparison",p2chains/Mathf.Max(1,p2packages)>p1chains/Mathf.Max(1,p1packages),"P1="+p1chains+"/"+p1packages+" P2="+p2chains+"/"+p2packages);
            Record("phase_attack_contact_comparison",p2contacts>p1contacts,"P1 contacts="+p1contacts+" P2 contacts="+p2contacts+" matched two seeds/24s stationary input; damage disabled, not difficulty proof");
        }
    }
}
