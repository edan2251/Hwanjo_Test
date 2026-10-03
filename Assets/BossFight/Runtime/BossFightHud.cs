using UnityEngine;

namespace FourGuardians.BossFight
{
    [RequireComponent(typeof(BossFightSession))]
    public sealed class BossFightHud : MonoBehaviour
    {
        private BossFightSession session;
        private Font font;
        private GUIStyle small, body, hero, centered, button, smallCenter;
        private readonly Color ink = new Color(.92f,.89f,.80f);
        private readonly Color muted = new Color(.79f,.82f,.76f);
        private readonly Color ember = new Color(.82f,.39f,.22f);
        private readonly Color mint = new Color(.48f,.78f,.65f);
        private float bossTrail = 1f;
        private void Awake() { session = GetComponent<BossFightSession>(); }
        private void OnDestroy() { if (font != null) Destroy(font); }
        private void Styles()
        {
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "\ub9d1\uc740 \uace0\ub515", "Arial" }, 20);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (body != null) return;
            body = Make(18, ink, TextAnchor.MiddleLeft);
            small = Make(15, muted, TextAnchor.MiddleLeft);
            hero = Make(56, ink, TextAnchor.MiddleCenter);
            centered = Make(19, ink, TextAnchor.MiddleCenter);
            smallCenter = Make(14, muted, TextAnchor.MiddleCenter);
            button = Make(17, ink, TextAnchor.MiddleCenter);
            button.normal.background = Texture2D.whiteTexture;
            button.normal.textColor = new Color(.08f,.11f,.10f);
            button.hover.background = Texture2D.whiteTexture;
            button.hover.textColor = Color.black;
            button.active.background = Texture2D.whiteTexture;
            button.active.textColor = Color.black;
        }
        private GUIStyle Make(int size, Color color, TextAnchor alignment)
        {
            return new GUIStyle { font = font, fontSize = size, alignment = alignment,
                normal = { textColor = color }, wordWrap = true };
        }
        private void OnGUI()
        {
            if (session == null || session.Player == null || session.Player.Health == null) return;
            Styles();
            Matrix4x4 old = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width-1280f*scale)*.5f,
                (Screen.height-720f*scale)*.5f,0),Quaternion.identity,Vector3.one*scale);
            Draw(new Rect(0,0,1280,116),new Color(.035f,.052f,.05f,.82f));
            GUI.Label(new Rect(38,20,270,30),"\ud658 \uc870  /  HWANJO",body);
            GUI.Label(new Rect(39,49,250,21),"\ub0a8\uaca8\uc9c4 \uc0dd\uba85\uc758 \ubd88\uc528",small);
            for(int i=0;i<session.Player.Health.MaxHealth;i++)
            {
                bool filled=i<session.Player.Health.CurrentHealth;
                Draw(new Rect(40+i*23,81,17,8),filled?mint:new Color(.17f,.21f,.19f));
                if(filled)Draw(new Rect(40+i*23,81,17,2),new Color(.73f,.94f,.81f));
            }
            GUI.Label(new Rect(1010,22,230,23),$"\uc2dc\ub3c4 {session.Attempts:00}  \u00b7  {Mathf.FloorToInt(session.Elapsed/60):00}:{Mathf.FloorToInt(session.Elapsed%60):00}",small);
            GUI.Label(new Rect(1010,47,230,23),session.Muted?"M \uc18c\ub9ac \uaebc\uc9d0   F11 \ud654\uba74":"M \uc18c\ub9ac \ucf1c\uc9d0   F11 \ud654\uba74",small);
            GUI.Label(new Rect(1010,72,230,23),$"F6 화면 진동 {Mathf.RoundToInt(session.CameraShake*100f)}%",small);
            var boss=session.Boss;
            if(boss!=null&&boss.Health!=null)
            {
                float fraction=boss.Health.CurrentHealth/(float)Mathf.Max(1,boss.Health.MaxHealth);
                if(Event.current.type==EventType.Repaint)bossTrail=Mathf.MoveTowards(bossTrail,fraction,Time.unscaledDeltaTime*.4f);
                GUI.Label(new Rect(385,17,505,30),"\ub3c4 \ucca0   \u9955 \u992e",centered);
                string phase=boss.Phase>=2?"\uc18d\ubc15\uc5d0\uc11c \ud480\ub824\ub098\ub2e4  \u00b7  II":$"\ub098\ubb34 \uac11\uc8fc  {boss.ArmorCount}/4  \u00b7  I";
                GUI.Label(new Rect(385,47,505,20),phase,smallCenter);
                Draw(new Rect(368,78,544,8),new Color(.18f,.19f,.16f));
                Draw(new Rect(368,78,544*bossTrail,8),new Color(.65f,.58f,.39f));
                Draw(new Rect(368,78,544*fraction,8),ember);
                Draw(new Rect(368,78,544*fraction,2),new Color(.97f,.62f,.35f));
                Draw(new Rect(639,78,2,8),new Color(.06f,.08f,.07f,.6f));
                if(boss.Phase==1)for(int i=0;i<4;i++)
                    Draw(new Rect(601+i*22,96,15,4),(boss.ArmorMask&(1<<i))!=0?new Color(.7f,.49f,.26f):new Color(.18f,.22f,.18f));
            }
            GUI.Label(new Rect(38,665,860,25),"A D / \u2190 \u2192 \uc774\ub3d9    SPACE \uc810\ud504    J \uacf5\uaca9    SHIFT / K \ub300\uc2dc",small);
            GUI.Label(new Rect(1015,665,235,25),"ESC \uc77c\uc2dc\uc815\uc9c0   \u00b7   R \uc7ac\ub3c4\uc804",small);
            if(session.State==EncounterState.Fighting)DrawPatternHint();
            if(session.State==EncounterState.Introduction)DrawIntroduction();
            if(session.State==EncounterState.Paused)DrawPause();
            if(session.State==EncounterState.Victory||session.State==EncounterState.Defeat)DrawResult();
            GUI.matrix=old;
        }
        private void DrawPatternHint()
        {
            var boss=session.Boss;
            if(boss==null)return;
            if(boss.RockfallActive)
                GUI.Label(new Rect(365,120,550,26),"낙석 중  ·  위와 바닥의 그림자를 함께 보세요",smallCenter);
            string text="";
            Color accent=new Color(.95f,.65f,.29f);
            if(boss.State==BossState.Link)
                text="동작이 이어진다  ·  다음 자세를 보세요";
            else if(boss.State==BossState.Telegraph || boss.State==BossState.Active)
            {
                text=boss.Pattern switch {
                    BossPattern.DT01Slam => "높이 든 앞발  ·  턱 아래로 파고들기",
                    BossPattern.DT02Sweep => "낮게 휘두르는 앞발  ·  점프 또는 바깥으로 거리 회피",
                    BossPattern.DT03Bite => "벌어지는 입  ·  물기 범위에서 벗어나기",
                    BossPattern.ARockfall => boss.AttackStage=="breathe"?"포효를 준비한다  ·  천장을 살피세요":"낙석이 시작된다  ·  공격할 순간을 찾으세요",
                    BossPattern.BBurrowLeap => boss.AttackStage=="wave_delay" || boss.AttackStage=="wave"
                        ? "착지 뒤 낮은 충격파  ·  뛰어넘기"
                        : boss.TargetLocked ? "착지 위치 고정  ·  표식 밖으로 이동" : "땅속을 따라 움직인다  ·  착지 표식에 주의",
                    _ => ""
                };
            }
            else if(boss.State==BossState.Recovery)
            {
                text=boss.Pattern==BossPattern.DT01Slam || boss.Pattern==BossPattern.BBurrowLeap
                    ? "큰 빈틈  ·  턱과 몸통에 반격" : "짧은 숨 고르기  ·  다음 움직임에 대비";
                accent=mint;
            }
            else if(boss.State==BossState.Transition)
                text="갑주가 떨어지고, 뿌리가 풀려난다";
            if(text.Length==0)return;
            Draw(new Rect(300,597,680,43),new Color(.04f,.06f,.055f,.91f));
            GUI.Label(new Rect(306,601,668,32),text,centered);
            // Only the genuine recovery receives the green opening marker.
            if(boss.State==BossState.Recovery)
                Draw(new Rect(560,641,160*(1-boss.StateProgress),2),accent);
        }
        private void DrawIntroduction()
        {
            string text=session.IntroStage switch {
                "buried"=>"\ub098\ubb34\uac00 \uc790\ub780 \uc5b8\ub355 \uc544\ub798, \uc228\uacb0\uc774 \ud750\ub978\ub2e4.",
                "emerge"=>"\ub545 \uc544\ub798 \uc7a0\ub4e0 \ud0d0\uc695\uc774 \ub208\uc744 \ub72c\ub2e4.",
                "inhale"=>"\uc78e\uacfc \ub3cc, \ub0a8\uaca8\uc9c4 \ubaa8\ub4e0 \uac83\uc744 \uc0bc\ud0a8\ub2e4.",
                "silence"=>"\uadf8\ub9ac\uace0 \uc7a0\uae50\uc758 \uc815\uc801.",
                "roar"=>"\ucc44\uc6cc\uc9c0\uc9c0 \uc54a\ub294 \uc790, \ub3c4\ucca0.",
                _=>"\ubb34\uac70\uc6b4 \uac11\uc8fc \uc544\ub798\uc758 \ube48\ud2c8\uc744 \ub178\ub9ac\uc138\uc694."
            };
            Draw(new Rect(250,594,780,52),new Color(.025f,.043f,.036f,.88f));
            GUI.Label(new Rect(270,601,740,34),text,centered);
            if(Button(new Rect(1044,597,196,42),"\ub4f1\uc7a5 \uac74\ub108\ub6f0\uae30  ENTER",ink))session.StartFight();
        }
        private void DrawPause()
        {
            Draw(new Rect(0,116,1280,604),new Color(.02f,.035f,.029f,.78f));
            GUI.Label(new Rect(390,235,500,70),"\uc7a0\uc2dc \uc228\uc744 \uace0\ub974\uba70",centered);
            if(Button(new Rect(460,333,360,49),"\uacc4\uc18d\ud558\uae30    ESC",ink))session.TogglePause();
            if(Button(new Rect(460,397,360,49),"\ucc98\uc74c\ubd80\ud130    R",muted))session.Retry();
            if(Button(new Rect(460,461,360,44),$"화면 진동 {Mathf.RoundToInt(session.CameraShake*100f)}%    F6",muted))session.CycleCameraShake();
            if(TextButton(new Rect(520,528,240,35),"\uc885\ub8cc"))session.Quit();
        }
        private void DrawResult()
        {
            float alpha=Mathf.Clamp01((session.StateTime-.3f)*2f);
            GUI.color=new Color(1,1,1,alpha);
            bool victory=session.State==EncounterState.Victory;
            Draw(new Rect(0,116,1280,604),new Color(.02f,.035f,.029f,.76f*alpha));
            GUI.Label(new Rect(390,204,500,40),victory?"\uc21c\ud658\uc758 \ubd88\uc528\uac00 \ub418\uc0b4\uc544\ub09c\ub2e4":"\ubd88\uc528\ub294 \uc544\uc9c1 \uaebc\uc9c0\uc9c0 \uc54a\uc558\ub2e4",centered);
            GUI.Label(new Rect(280,257,720,90),victory?"\ud0d0\uc695\uc758 \ub05d":"\ub2e4\uc2dc, \ub9de\uc11c\ub2e4",hero);
            GUI.Label(new Rect(420,355,440,39),victory?$"\ub3c4\ucca0 \ucc98\uce58  \u00b7  {session.Elapsed:F1}\ucd08  \u00b7  \ub0a8\uc740 \uccb4\ub825 {session.Player.Health.CurrentHealth}":"\uc704\ud5d8\uc744 \ud53c\ud558\uace0, \uc9e7\uc740 \ube48\ud2c8\uc5d0 \ubc18\uaca9\ud558\uc138\uc694.",smallCenter);
            if(Button(new Rect(450,424,380,54),victory?"\ub2e4\uc2dc \ub3c4\uc804    R":"\uc7ac\ub3c4\uc804    R",ink))session.Retry();
            if(TextButton(new Rect(520,499,240,36),"\uc885\ub8cc"))session.Quit();
            GUI.color=Color.white;
        }
        private bool Button(Rect rect,string text,Color color)
        {
            Color old=GUI.backgroundColor;GUI.backgroundColor=color;
            bool result=GUI.Button(rect,text,button);GUI.backgroundColor=old;return result;
        }
        private bool TextButton(Rect rect,string text)
        {
            GUI.Label(rect,text,smallCenter);return GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }
        private static void Draw(Rect rect,Color color)
        {
            Color old=GUI.color;GUI.color*=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;
        }
    }
}
