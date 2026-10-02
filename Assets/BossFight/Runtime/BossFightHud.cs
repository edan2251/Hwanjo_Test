using UnityEngine;

namespace FourGuardians.BossFight
{
    [RequireComponent(typeof(BossFightSession))]
    public sealed class BossFightHud : MonoBehaviour
    {
        private BossFightSession session;
        private Font font;
        private GUIStyle small, body, title, hero, centered, button, smallCenter;
        private readonly Color ink = new Color(.90f,.87f,.77f);
        private readonly Color muted = new Color(.76f,.80f,.74f);
        private readonly Color ember = new Color(.79f,.36f,.23f);
        private readonly Color mint = new Color(.48f,.75f,.63f);
        private float bossTrail = 1f;

        private void Awake() { session = GetComponent<BossFightSession>(); }
        private void OnDestroy() { if (font != null) Destroy(font); }
        private void Styles()
        {
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 20);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (body != null) return;
            body = Make(18, ink, TextAnchor.MiddleLeft);
            small = Make(14, muted, TextAnchor.MiddleLeft);
            title = Make(28, ink, TextAnchor.MiddleLeft);
            hero = Make(62, ink, TextAnchor.MiddleLeft);
            centered = Make(19, ink, TextAnchor.MiddleCenter);
            smallCenter = Make(13, muted, TextAnchor.MiddleCenter);
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
            float offsetX = (Screen.width - 1280f * scale) * .5f;
            float offsetY = (Screen.height - 720f * scale) * .5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);
            Draw(new Rect(0,0,1280,116), new Color(.035f,.052f,.05f,.79f));
            GUI.Label(new Rect(38,20,260,30), "환 조  /  HWANJO", body);
            GUI.Label(new Rect(39,49,200,21), "남겨진 생명의 불씨", small);
            for (int i=0; i<session.Player.Health.MaxHealth; i++)
            {
                bool filled = i < session.Player.Health.CurrentHealth;
                Draw(new Rect(40+i*23,81,17,8), filled ? mint : new Color(.17f,.21f,.19f));
                if(filled) Draw(new Rect(40+i*23,81,17,2), new Color(.73f,.94f,.81f));
            }
            GUI.Label(new Rect(1010,22,230,23), $"시도 {session.Attempts:00}   ·   {Mathf.FloorToInt(session.Elapsed/60):00}:{Mathf.FloorToInt(session.Elapsed%60):00}", small);
            GUI.Label(new Rect(1010,47,230,23), session.Muted ? "M  소리 꺼짐     F11  화면" : "M  소리 켜짐     F11  화면", small);

            if (session.Boss != null && session.Boss.Health != null)
            {
                float fraction = session.Boss.Health.CurrentHealth/(float)Mathf.Max(1,session.Boss.Health.MaxHealth);
                if (Event.current.type == EventType.Repaint)
                    bossTrail = Mathf.MoveTowards(bossTrail,fraction,Time.unscaledDeltaTime*.33f);
                GUI.Label(new Rect(385,17,505,30),"도 철   饕 餮",centered);
                GUI.Label(new Rect(385,47,505,20),session.Boss.Phase>=2 ? "굶주림의 순환  ·  II" : "끝없이 삼키는 공허  ·  I",smallCenter);
                Draw(new Rect(368,78,544,8), new Color(.18f,.19f,.16f));
                Draw(new Rect(368,78,544*bossTrail,8),new Color(.65f,.58f,.39f));
                Draw(new Rect(368,78,544*fraction,8),ember);
                Draw(new Rect(368,78,544*fraction,2),new Color(.97f,.62f,.35f));
                Draw(new Rect(639,78,2,8),new Color(.06f,.08f,.07f,.6f));
            }

            GUI.Label(new Rect(38,661,820,25),"A D / ← → 이동     SPACE 점프     J 공격     SHIFT / K 대시",small);
            GUI.Label(new Rect(1015,661,235,25),"ESC 일시정지   ·   R 재도전",small);
            if (session.State==EncounterState.Fighting) DrawPatternHint();
            if (session.State==EncounterState.Introduction) DrawIntroduction();
            if (session.State==EncounterState.Paused) DrawPause();
            if (session.State==EncounterState.Victory || session.State==EncounterState.Defeat) DrawResult();
            GUI.matrix = old;
        }

        private void DrawPatternHint()
        {
            var boss=session.Boss;
            if (boss==null) return;
            if(boss.State==BossState.Telegraph)
            {
                string text=boss.Pattern switch
                {
                    BossPattern.DT01Slam => "앞발 내려찍기  ·  표식 밖으로 이동",
                    BossPattern.DT02Sweep => "낮은 휩쓸기  ·  파동을 뛰어넘기",
                    BossPattern.DT03Bite => "큰 물기  ·  입에서 멀어지기",
                    BossPattern.DT05BodySlam => "뿌리가 솟는다  ·  낙하 표식 밖으로",
                    _ => "움직임을 읽고 빈틈을 노리세요"
                };
                Draw(new Rect(375,592,530,47),new Color(.04f,.06f,.055f,.92f));
                GUI.Label(new Rect(384,597,512,32),text,centered);
                Draw(new Rect(375,638,530*boss.StateProgress,2),new Color(.95f,.65f,.29f));
            }
            else if(boss.State==BossState.Recovery)
            {
                GUI.color = new Color(1,1,1,.88f);
                GUI.Label(new Rect(375,600,530,32),"빈틈  ·  앞발과 입을 공격하세요",centered);
                GUI.color=Color.white;
                Draw(new Rect(560,638,160*(1-boss.StateProgress),2),mint);
            }
            else if(boss.State==BossState.Transition)
            {
                GUI.Label(new Rect(350,595,580,38),"포식은 멈추지 않는다",centered);
            }
        }
        private void DrawIntroduction()
        {
            Draw(new Rect(0,116,1280,604),new Color(.02f,.035f,.029f,.40f));
            Draw(new Rect(46,167,496,444),new Color(.045f,.06f,.052f,.96f));
            Draw(new Rect(46,167,3,444),ember);
            GUI.Label(new Rect(76,193,430,24),"첫 번째 보스  /  THE FIRST DEVOURER",small);
            GUI.Label(new Rect(72,225,450,93),"도 철",hero);
            GUI.Label(new Rect(77,320,425,32),"먹어도 먹어도, 채워지지 않는다.",body);
            GUI.Label(new Rect(77,370,416,88),"뿌리가 움직이고, 몸이 솟아오릅니다.\n공격 전의 몸짓과 바닥 표식을 읽으세요.\n회피 뒤 드러나는 앞발과 입이 공격 기회입니다.",body);
            GUI.Label(new Rect(77,476,414,32),"대시에도 피해를 받습니다. 안전한 곳으로 피하세요.",small);
            if(Button(new Rect(77,527,260,52),"전투 시작    ENTER",ink)) session.StartFight();
            if(TextButton(new Rect(355,536,148,34),"종료")) session.Quit();
        }
        private void DrawPause()
        {
            Draw(new Rect(0,116,1280,604),new Color(.02f,.035f,.029f,.78f));
            GUI.Label(new Rect(390,235,500,70),"잠시 숨을 고르다",centered);
            if(Button(new Rect(460,333,360,49),"계속하기    ESC",ink)) session.TogglePause();
            if(Button(new Rect(460,397,360,49),"처음부터    R",muted)) session.Retry();
            if(TextButton(new Rect(520,473,240,35),"종료")) session.Quit();
        }
        private void DrawResult()
        {
            float alpha=Mathf.Clamp01((session.StateTime-.3f)*2f);
            GUI.color=new Color(1,1,1,alpha);
            bool victory=session.State==EncounterState.Victory;
            Draw(new Rect(0,116,1280,604),new Color(.02f,.035f,.029f,.76f*alpha));
            GUI.Label(new Rect(390,204,500,40),victory?"순환의 불씨가 되살아났다":"불씨는 아직 꺼지지 않았다",centered);
            var resultStyle=new GUIStyle(hero){alignment=TextAnchor.MiddleCenter,fontSize=54};
            GUI.Label(new Rect(280,257,720,90),victory?"포식자 격파":"다시, 맞서다",resultStyle);
            GUI.Label(new Rect(420,355,440,39),victory?$"도철 처치  ·  {session.Elapsed:F1}초  ·  남은 체력 {session.Player.Health.CurrentHealth}":"표식이 멈추면 피하고, 회복할 때 공격하세요.",smallCenter);
            if(Button(new Rect(450,424,380,54),victory?"다시 도전    R":"재도전    R",ink))session.Retry();
            if(TextButton(new Rect(520,499,240,36),"종료"))session.Quit();
            GUI.color=Color.white;
        }
        private bool Button(Rect rect,string text,Color color)
        {
            Color old=GUI.backgroundColor; GUI.backgroundColor=color;
            bool result=GUI.Button(rect,text,button);GUI.backgroundColor=old;return result;
        }
        private bool TextButton(Rect rect,string text)
        {
            GUI.Label(rect,text,smallCenter);
            return GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }
        private static void Draw(Rect rect,Color color)
        {
            Color old=GUI.color;GUI.color*=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;
        }
    }
}
