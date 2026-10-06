using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FourGuardians.CourseContent.Combat;
using FourGuardians.CourseContent.Movement;

namespace FourGuardians.BossFight
{
    [DefaultExecutionOrder(1000)]
    public sealed partial class DocheolV4QaDriver : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name,evidence; public bool passed; }
        [Serializable] public sealed class Case
        {
            public string name,plan; public int incomingHits,acceptedAttacks,acceptedJumps,recoveryDamage;
            public float seconds,initialX,finalX,maximumSeparation; public bool completed,recoveryStationary;
        }
        [Serializable] public sealed class Report
        {
            public string specVersion="docheol-root-reach-v4",mode,utc,unityVersion,assembly,inputStrategy;
            public bool passed,usesControlledSetup,usesSyntheticDamage,usesForcedPatterns,usesTestRepositioning,usesApiRetry,usesModifiedInvulnerability=false;
            public int requestedFps,renderedFrames; public float seconds,averageFps;
            public List<V5Stats> phaseStats=new List<V5Stats>();
            public List<V6TempoSample> tempo=new List<V6TempoSample>();
            public List<Check> checks=new List<Check>();
            public List<Case> cases=new List<Case>();
            public List<string> events=new List<string>(),errors=new List<string>(),screenshots=new List<string>();
        }
        private Report report;
        private BossFightSession session;
        private DoCheolBossController boss;
        private BossFightPlayerAdapter player;
        private string output,captureDirectory;
        private int firstFrame,lastAttack,damageEvents,acceptedJumpCounter,lastCaptureFrame=-1;
        private bool previousRising;
        private float started;
        private readonly HashSet<string> captures=new HashSet<string>();
        private static string Arg(string key,string fallback="")
        {
            string[] args=Environment.GetCommandLineArgs(); int i=Array.IndexOf(args,key);
            return i>=0 && i+1<args.Length && !args[i+1].StartsWith("-")?args[i+1]:fallback;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(!(Arg("-bossqa").StartsWith("v4") || Arg("-bossqa").StartsWith("v5") || Arg("-bossqa").StartsWith("v6")) || FindFirstObjectByType<DocheolV4QaDriver>()!=null)return;
            DontDestroyOnLoad(new GameObject("Docheol V4 QA").AddComponent<DocheolV4QaDriver>().gameObject);
        }
        private IEnumerator Start()
        {
            int fps; if(!int.TryParse(Arg("-bossqa-fps","60"),out fps))fps=60;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=fps;Application.runInBackground=true;
            report=new Report{mode=Arg("-bossqa"),utc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion,
                assembly=typeof(DoCheolBossController).Assembly.ManifestModule.ModuleVersionId.ToString(),requestedFps=fps,
                inputStrategy=Arg("-bossqa").EndsWith("observe")?"passive physical keyboard observer":"ordinary input bot; .2s basic recognition; current geometry and timed rock route API",
                usesApiRetry=Arg("-bossqa")=="v4death",
                usesControlledSetup=!Arg("-bossqa").EndsWith("observe")&&Arg("-bossqa")!="v4natural"&&Arg("-bossqa")!="v4death",
                usesTestRepositioning=!Arg("-bossqa").EndsWith("observe")&&Arg("-bossqa")!="v4natural"&&Arg("-bossqa")!="v4death",
                usesForcedPatterns=Arg("-bossqa")=="v4core"||Arg("-bossqa")=="v4burrow"||Arg("-bossqa")=="v4whole"||Arg("-bossqa")=="v4phase"};
            if(report.mode.StartsWith("v5")){report.specVersion="docheol-root-rhythm-v5";report.usesSyntheticDamage=report.mode=="v5rhythm"||report.mode=="v5scenarios";report.usesModifiedInvulnerability=report.mode=="v5rhythm"||report.mode=="v5scenarios";report.usesForcedPatterns=report.mode=="v5roots";if(report.mode=="v5roots")report.inputStrategy="fixed idle boss pose, initial facing forced; actual ordinary sword; no synthetic damage";}
            if(report.mode.StartsWith("v6"))
            {
                report.specVersion="docheol-burrow-impact-v6";
                report.usesControlledSetup=report.usesTestRepositioning=report.mode!="v6natural"&&report.mode!="v6observe"&&report.mode!="v6death";
                report.usesSyntheticDamage=report.mode=="v6burrow"||report.mode=="v6tempo"||report.mode=="v6impact"||report.mode=="v6whole"||report.mode=="v6phase"||report.mode=="v6scenarios";
                report.usesForcedPatterns=report.mode=="v6burrow"||report.mode=="v6roots"||report.mode=="v6whole"||report.mode=="v6advance"||report.mode=="v6core"||report.mode=="v6phase";
                report.usesModifiedInvulnerability=report.mode=="v6tempo"||report.mode=="v6scenarios";
                report.usesApiRetry=report.mode=="v6death";
            }
            output=Arg("-bossqa-output",Path.Combine(Application.persistentDataPath,"v4qa.json"));
            captureDirectory=Arg("-bossqa-capture");Application.logMessageReceived+=Log;
            started=Time.realtimeSinceStartup;firstFrame=Time.frameCount;
            while(session==null && Time.realtimeSinceStartup-started<30){session=FindFirstObjectByType<BossFightSession>();yield return null;}
            player=FindFirstObjectByType<BossFightPlayerAdapter>();boss=FindFirstObjectByType<DoCheolBossController>();
            if(session==null || player==null || boss==null){Record("setup",false,"missing encounter objects");yield return Finish();yield break;}
            player.Damaged+=OnDamaged;
            boss.PlayerDamagedByAttack+=OnDamageSource;
            if(report.mode.EndsWith("observe"))yield return Observe();
            else
            {
                session.StartFight();player.SetExternalInput(true);yield return null;
                if(report.mode=="v6burrow")yield return V6Burrows();
                else if(report.mode=="v6impact")yield return V6Impact();
                else if(report.mode=="v6tempo")yield return V6Tempo();
                else if(report.mode=="v6roots")yield return V5Roots();
                else if(report.mode=="v6whole")yield return V5WholeMatrix();
                else if(report.mode=="v6advance")yield return V5AdvanceMatrix();
                else if(report.mode=="v6core")yield return Core();
                else if(report.mode=="v6phase")yield return PhaseEvents();
                else if(report.mode=="v6scenarios")yield return V5Scenarios();
                else if(report.mode=="v6natural")yield return Natural();
                else if(report.mode=="v6death")yield return DeathRetry();
                else if(report.mode=="v5roots")yield return V5Roots();
                else if(report.mode=="v5whole"){report.usesForcedPatterns=true;report.usesSyntheticDamage=true;yield return V5WholeMatrix();}
                else if(report.mode=="v5advance"){report.usesForcedPatterns=true;yield return V5AdvanceMatrix();}
                else if(report.mode=="v5rhythm")yield return V5Rhythm();
                else if(report.mode=="v5scenarios")yield return V5Scenarios();
                else if(report.mode=="v4core")yield return Core();
                else if(report.mode=="v4burrow")yield return BurrowMatrix();
                else if(report.mode=="v4whole")yield return WholeMatrix();
                else if(report.mode=="v4phase")yield return PhaseEvents();
                else if(report.mode=="v4natural")yield return Natural();
                else if(report.mode=="v4death")yield return DeathRetry();
                else Record("supported_mode",false,report.mode);
            }
            yield return Finish();
        }
        private void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)report.errors.Add(message+"\n"+stack);}
        private void OnDamaged(DamageInfo hit){damageEvents++;report.events.Add("incoming t="+Time.time.ToString("F3")+" stage="+boss.AttackStage+" id="+boss.AttackId);}
        private void OnDamageSource(int id)
        {
            bool rock=false;foreach(var r in boss.Rockfalls)rock|=r.AttackId==id;
            report.events.Add("damage_source t="+Time.time.ToString("F3")+" acceptedID="+id+" source="+(rock?"rock":boss.Pattern.ToString())+" stage="+boss.AttackStage+" px="+player.Body.position.x+" py="+player.HurtBounds.min.y);
            if(rock && cachedRockRoute!=null && cachedRockRoute.Length>0)
            {
                int sample=Mathf.Clamp(Mathf.RoundToInt((boss.EncounterSeconds-cachedRouteOrigin)/.02f),0,cachedRockRoute.Length-1);
                report.events.Add("route_at_hit planned="+cachedRockRoute[sample]+" actualX="+player.HurtBounds.center.x+" actualY="+player.HurtBounds.min.y+" velocity="+player.Body.linearVelocity+" move="+lastRouteMove);
            }
        }
        private void OnGUI()
        {
            if(report!=null && report.usesControlledSetup)GUI.Box(new Rect(12,137,Screen.width-24,27),report.mode=="v5roots"?"QA FIXED IDLE/WHOLE CONTACT POSE | ordinary sword; body/root partition; no difficulty measurement.":"QA CONTROLLED SETUP | ordinary player input after initial pose/plan; see JSON for synthetic phase setup.");
        }
        private void FixedUpdate()
        {
            if(player==null)return;
            bool rising=player.Movement.Action==WarriorAction.JumpUp && !player.Movement.IsGrounded;
            if(rising && !previousRising)acceptedJumpCounter++;
            previousRising=rising;
        }
        private void Record(string name,bool passed,string evidence)
        {report.checks.Add(new Check{name=name,passed=passed,evidence=evidence});Debug.Log("V4_QA "+(passed?"PASS ":"FAIL ")+name+" "+evidence);}
        private IEnumerator Setup(float bx,float px)
        {
            if(!Application.isBatchMode)yield return new WaitForEndOfFrame();yield return null;
            boss.ResetFight(new Vector2(bx,boss.FloorY));player.ResetForFight(new Vector2(px,boss.FloorY+.025f));
            player.SetExternalInput(true);boss.SetFightActive(true);yield return null;
            lastAttack=(int)player.Movement.AttackSequence;
        }
        private void Capture(string label)
        {
            // Keep focused temporal evidence; the regression assertions do not need every
            // intermediate posture of every naturally selected package as another PNG.
            if((report.mode=="v4natural" || report.mode=="v6natural") && label.StartsWith("natural-P") && !(label.EndsWith("-tell")||label.EndsWith("-whole_tell")||label.EndsWith("-whole_final_windup")||label.EndsWith("-whole_hit")||label.EndsWith("-recover")||label.EndsWith("-phase_roar")||label.EndsWith("-land")||label.EndsWith("-wave")))return;
            if(report.mode=="v4burrow" && !(label.EndsWith("-rise")||label.EndsWith("-flight")||label.EndsWith("-land")||label.EndsWith("-wave")||label.EndsWith("-followup")||label.EndsWith("-result")))return;
            // Unity retains one native screenshot request per frame. Defer duplicate labels
            // rather than report files that the renderer will never write.
            if(Application.isBatchMode || string.IsNullOrEmpty(captureDirectory) || captures.Contains(label) || captures.Count>=80 || lastCaptureFrame==Time.frameCount)return;
            captures.Add(label);lastCaptureFrame=Time.frameCount;
            Directory.CreateDirectory(captureDirectory);string path=Path.Combine(captureDirectory,captures.Count.ToString("D2")+"-"+label+".png");
            ScreenCapture.CaptureScreenshot(path);report.screenshots.Add(path);
        }
        private IEnumerator Snapshot(string label)
        {Capture(label);if(!Application.isBatchMode)yield return new WaitForEndOfFrame();yield return null;}
        private IEnumerator Core()
        {
            yield return Setup(2.8f,.6f);
            Record("retired_slam_rejected",!boss.ForcePattern(BossPattern.DT01Slam),"ForcePattern slam");
            Record("retired_chains_rejected",!boss.ForceCombo("1")&&!boss.ForceCombo("2-1")&&!boss.ForceCombo("3-2-1"),"phase1 retired chains");
            yield return Camping();yield return Turning();yield return Wall();
            yield return Basic("3",false);yield return Basic("2",false);yield return SweepDash();yield return Basic("3-2",false);yield return Basic("3-2",true);
        }
        private IEnumerator Camping()
        {
            yield return Setup(0,0);
            int incoming=damageEvents,hp=boss.Health.CurrentHealth,active=0;float end=Time.time+8f,maxGap=0;
            Case c=new Case{name="center_overlap_camping",initialX=player.Body.position.x};report.cases.Add(c);
            while(Time.time<end && !player.IsDead && !boss.Health.IsDead)
            {
                player.SubmitInput(0,false,true,false);maxGap=Mathf.Max(maxGap,Mathf.Abs(boss.transform.position.x-player.Body.position.x));
                if(boss.State==BossState.Active)active++;
                if(boss.State==BossState.Backdash)Capture("camp-backdash");
                if(boss.State==BossState.Telegraph)Capture("camp-tell");yield return null;
            }
            c.maximumSeparation=maxGap;c.incomingHits=damageEvents-incoming;c.acceptedAttacks=(int)player.Movement.AttackSequence-lastAttack;c.finalX=player.Body.position.x;
            Record("center_camp_repositions_and_attacks",maxGap>1.3f && boss.RepositionCount+boss.BackdashCount>0 && active>0,"maxGap="+maxGap+" backdashes="+boss.BackdashCount+" activeFrames="+active);
            Record("center_camp_is_not_safe",c.incomingHits>0,"ordinary stationary attacks; incoming="+c.incomingHits);
            Record("preparation_boss_remains_hittable",boss.Health.CurrentHealth<hp,"normal stationary attack bossDamage="+(hp-boss.Health.CurrentHealth));
            yield return Snapshot("camp-end");
        }
        private IEnumerator Turning()
        {
            yield return Setup(0,2.2f);
            float end=Time.time+3.5f;bool saw=false,stable=true,completed=false;int goal=1;
            while(Time.time<end)
            {
                float move=saw && player.Body.position.x> -2.1f?-1:0;player.SubmitInput(move,false,false,false);
                if(boss.Turning && !saw){saw=true;goal=boss.TurnToFacing;}
                if(saw && !completed && boss.Turning)stable&=boss.TurnToFacing==goal && boss.Facing==-1;
                if(saw && !boss.Turning && boss.Facing==goal)completed=true;
                if(boss.Turning)
                {
                    if(boss.StateProgress>.20f && boss.StateProgress<.32f)Capture("turn-retract");
                    if(boss.StateProgress>.46f && boss.StateProgress<.54f)Capture("turn-through-root");
                    if(boss.StateProgress>.70f && boss.StateProgress<.82f)Capture("turn-extend");
                }
                yield return null;
            }
            Record("turn_commits_despite_crossing",saw&&stable&&completed,"goal="+goal+" turns="+boss.TurnCount+" crossedToX="+player.Body.position.x);
            yield return Snapshot("turn-complete");
        }
        private IEnumerator Wall()
        {
            yield return Setup(6.3f,6.5f);
            float end=Time.time+4f;bool crossed=false,attack=false;float maxGap=0;
            while(Time.time<end)
            {
                player.SubmitInput(0,false,false,false);
                maxGap=Mathf.Max(maxGap,player.Body.position.x-boss.transform.position.x);
                crossed|=boss.transform.position.x>player.Body.position.x+.1f;
                attack|=boss.State==BossState.Active;yield return null;
            }
            Record("wall_spacing_without_crossing",!crossed&&maxGap>1.3f&&attack,"maxGap="+maxGap+" crossed="+crossed+" active="+attack);
            yield return Snapshot("wall-spacing");
        }
        private float WalkOut(Bounds warning)
        {
            Bounds hurt=player.HurtBounds;
            if(hurt.max.x<warning.min.x-.12f || hurt.min.x>warning.max.x+.12f)return 0;
            float left=warning.min.x-hurt.extents.x-.18f,right=warning.max.x+hurt.extents.x+.18f;
            bool lv=left>=boss.ArenaLeft+hurt.extents.x,rv=right<=boss.ArenaRight-hurt.extents.x;
            if(lv && (!rv || Mathf.Abs(hurt.center.x-left)<=Mathf.Abs(hurt.center.x-right)))return -1;
            return rv?1:0;
        }
        private IEnumerator Basic(string plan,bool greedy)
        {
            yield return Setup(2.8f,.6f);
            Case c=new Case{name=plan+(greedy?"_greed":"_safe"),plan=plan,initialX=player.Body.position.x};report.cases.Add(c);
            int hits=damageEvents;int initialAttack=(int)player.Movement.AttackSequence,initialJump=acceptedJumpCounter;
            bool forced=boss.ForceCombo(plan);Record("start_"+c.name,forced,"controlled initial plan only");
            float began=Time.time,deadline=began+8f,noticed=Time.time;int observedId=boss.AttackId;float lockedTarget=boss.TargetX;int facing=boss.Facing;
            bool stable=true,recover=false,stationary=true;float recoverX=0,recoverEnd=0,recoveryStart=0;int recoveryHp=0;
            while(Time.time<deadline && !player.IsDead)
            {
                if(boss.AttackId!=observedId){observedId=boss.AttackId;noticed=Time.time;lockedTarget=boss.TargetX;facing=boss.Facing;}
                if((boss.State==BossState.Telegraph || boss.State==BossState.Link || boss.State==BossState.Active) && !recover)
                    stable&=Mathf.Abs(boss.TargetX-lockedTarget)<.001f && boss.Facing==facing;
                float move=0;bool jump=false,attack=false;
                if(!recover && Time.time-noticed>=.2f)
                {
                    if(boss.Pattern==BossPattern.DT03Bite)move=WalkOut(boss.WarningBounds);
                    if(boss.Pattern==BossPattern.DT02Sweep)
                    {
                        if(greedy)attack=true;
                        else if(boss.State==BossState.Telegraph && boss.LockedTellDuration*(1-boss.StateProgress)<=.22f)jump=true;
                    }
                }
                if(recover && !greedy && Time.time-recoveryStart>=.06f)attack=true;
                player.SubmitInput(move,jump,attack,false);
                if(boss.State==BossState.Telegraph)Capture(c.name+"-tell-"+boss.ComboIndex);
                if(boss.State==BossState.Active)Capture(c.name+"-active-"+boss.ComboIndex);
                if(boss.State==BossState.Recovery && !recover)
                {recover=true;recoveryStart=Time.time;recoveryHp=boss.Health.CurrentHealth;recoverX=boss.transform.position.x;recoverEnd=Time.time+boss.RecoveryRemaining;Capture(c.name+"-recovery");}
                if(recover && Time.time<recoverEnd-Time.fixedDeltaTime)stationary&=boss.State==BossState.Recovery&&Mathf.Abs(boss.transform.position.x-recoverX)<.001f;
                if(recover)c.recoveryDamage=Mathf.Max(c.recoveryDamage,recoveryHp-boss.Health.CurrentHealth);
                if(recover && Time.time>=recoverEnd-Time.fixedDeltaTime){c.completed=true;break;}
                yield return null;
            }
            c.incomingHits=damageEvents-hits;c.acceptedAttacks=(int)player.Movement.AttackSequence-initialAttack;c.acceptedJumps=acceptedJumpCounter-initialJump;
            c.seconds=Time.time-began;c.finalX=player.Body.position.x;c.recoveryStationary=stationary&&recover;
            Record(c.name+"_target_locked",stable,"fixed per-step facing and target");
            Record(c.name+"_completed_recovery",c.completed&&c.recoveryStationary,"seconds="+c.seconds+" stationary="+c.recoveryStationary);
            if(!greedy)Record(c.name+"_punish_window",c.recoveryDamage>0,"ordinary attack damage during genuine recovery="+c.recoveryDamage);
            Record(c.name+"_input_result",greedy?c.incomingHits>0&&c.acceptedAttacks>0:c.incomingHits==0,"hits="+c.incomingHits+" acceptedAttack="+c.acceptedAttacks+" jumps="+c.acceptedJumps);
            yield return Snapshot(c.name+"-result");
        }
        private IEnumerator Observe()
        {
            float seconds; if(!float.TryParse(Arg("-bossqa-seconds","90"),out seconds))seconds=90;
            float end=Time.realtimeSinceStartup+seconds,next=0;string prior="";
            while(Time.realtimeSinceStartup<end)
            {
                // This observer survives scene reload, while all encounter actors do not.
                // Rebind without changing gameplay, input mode, HP or positions.
                if(session==null || player==null || boss==null)
                {
                    session=FindFirstObjectByType<BossFightSession>();
                    var nextBoss=FindFirstObjectByType<DoCheolBossController>();
                    if(nextBoss!=null && nextBoss!=boss){boss=nextBoss;boss.PlayerDamagedByAttack+=OnDamageSource;}
                    var nextPlayer=FindFirstObjectByType<BossFightPlayerAdapter>();
                    if(nextPlayer!=null && nextPlayer!=player)
                    {player=nextPlayer;player.Damaged+=OnDamaged;}
                    yield return null;continue;
                }
                string state=session.State+"/"+boss.Phase+"/"+boss.State+"/"+boss.ComboCode+"/"+boss.AttackStage+"/"+player.Movement.Action+"/attack="+player.Movement.AttackSequence+"/roots="+boss.RootBlockCount+"/flash="+session.FlashScale+"/shake="+session.CameraShake+"/mute="+session.Muted;
                if(state!=prior){report.events.Add("observe "+(Time.realtimeSinceStartup-started).ToString("F2")+" "+state+" px="+player.Body.position.x.ToString("F2")+" hp="+player.Health.CurrentHealth+" bossHp="+boss.Health.CurrentHealth);prior=state;}
                if(Time.realtimeSinceStartup>=next){next=Time.realtimeSinceStartup+30;Capture("observe-"+captures.Count);}
                yield return null;
            }
            Record("physical_input_observer",true,"no input submission, forced pattern, health mutation or teleport");
        }
        private IEnumerator Finish()
        {
            if(!report.mode.EndsWith("observe"))player?.SubmitInput(0,false,false,false);
            yield return null;if(!Application.isBatchMode)yield return new WaitForEndOfFrame();yield return null;
            // Native PNG encoding is asynchronous. Wait for requested files before judging them.
            float flushDeadline=Time.realtimeSinceStartup+3f;
            while(!Application.isBatchMode && Time.realtimeSinceStartup<flushDeadline)
            {
                bool pending=false;foreach(string path in report.screenshots)pending|=!File.Exists(path)||new FileInfo(path).Length<=100;
                if(!pending)break;yield return null;
            }
            Record("no_runtime_errors",report.errors.Count==0,"errors="+report.errors.Count);
            bool native=!Application.isBatchMode&&!string.IsNullOrEmpty(captureDirectory)&&report.screenshots.Count>0;
            foreach(string path in report.screenshots)native&=File.Exists(path)&&new FileInfo(path).Length>100;
            Record("native_capture_files",native,"files="+report.screenshots.Count);
            report.seconds=Time.realtimeSinceStartup-started;report.renderedFrames=Time.frameCount-firstFrame;report.averageFps=report.renderedFrames/Mathf.Max(.001f,report.seconds);
            report.passed=report.checks.TrueForAll(c=>c.passed);Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("V4_QA_REPORT "+output+" passed="+report.passed);yield return null;Application.Quit(report.passed?0:2);
        }
    }
}
