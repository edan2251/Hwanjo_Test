using System;
using System.IO;
using System.Linq;
using FourGuardians.CourseContent.Movement;
using FourGuardians.CourseContent.Combat;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FourGuardians.BossFight.Editor
{
    public static class BossFightBuilder
    {
        public const string ScenePath = "Assets/BossFight/Scenes/DocheolArena.unity";
        private const string Generated = "Assets/BossFight/Generated";
        private static string WorkRoot => Path.GetFullPath(Path.Combine(Application.dataPath,"..",".."));

        [MenuItem("Tools/Boss Fight/Build Docheol Arena")]
        public static void BuildScene()
        {
            Directory.CreateDirectory("Assets/BossFight/Scenes");
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            Scene arena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Scene source = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            BasicPlayerMovement2D original = source.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<BasicPlayerMovement2D>(true)).FirstOrDefault();
            if (original == null) throw new InvalidOperationException("The original SampleScene warrior is missing.");
            GameObject playerObject = UnityEngine.Object.Instantiate(original.gameObject);
            playerObject.name = "Player_OriginalWarrior";
            playerObject.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(playerObject,arena);
            playerObject.transform.position = new Vector3(-5.8f,.025f,0f);
            playerObject.transform.localScale = Vector3.one;
            EditorSceneManager.CloseScene(source,true);
            SceneManager.SetActiveScene(arena);

            Material spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(Generated+"/UnlitSprite.mat");
            if (spriteMaterial == null)
            {
                Shader shader=Shader.Find("Sprites/Default");
                if(shader==null)throw new InvalidOperationException("Sprites/Default shader missing.");
                spriteMaterial=new Material(shader){name="BossFight_Unlit"};
                AssetDatabase.CreateAsset(spriteMaterial,Generated+"/UnlitSprite.mat");
            }
            foreach(SpriteRenderer sr in playerObject.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.sharedMaterial=spriteMaterial;
                sr.sortingOrder=20;
            }
            var body=playerObject.GetComponent<Rigidbody2D>();
            body.interpolation=RigidbodyInterpolation2D.Interpolate;
            var player=playerObject.GetComponent<BossFightPlayerAdapter>();
            if(player==null)player=playerObject.AddComponent<BossFightPlayerAdapter>();

            GameObject environment=new GameObject("01_MinimalArena");
            Sprite square=MakeSquare();
            AddSlab(environment.transform,"Floor",new Vector2(0,-1.6f),new Vector2(22,3.2f),new Color(.12f,.155f,.135f),square,spriteMaterial,true);
            AddSlab(environment.transform,"FloorEdge",new Vector2(0,-.025f),new Vector2(20,.05f),new Color(.48f,.51f,.40f),square,spriteMaterial,false);
            AddSlab(environment.transform,"LeftWall",new Vector2(-10.25f,3.4f),new Vector2(.5f,9f),new Color(.085f,.11f,.097f),square,spriteMaterial,true);
            AddSlab(environment.transform,"RightWall",new Vector2(10.25f,3.4f),new Vector2(.5f,9f),new Color(.085f,.11f,.097f),square,spriteMaterial,true);
            AddSlab(environment.transform,"LeftBoundary",new Vector2(-9.98f,1f),new Vector2(.035f,2f),new Color(.28f,.34f,.29f),square,spriteMaterial,false);
            AddSlab(environment.transform,"RightBoundary",new Vector2(9.98f,1f),new Vector2(.035f,2f),new Color(.28f,.34f,.29f),square,spriteMaterial,false);

            GameObject cameraObject=new GameObject("ArenaCamera",typeof(Camera),typeof(AudioListener));
            Camera camera=cameraObject.GetComponent<Camera>();
            cameraObject.tag="MainCamera";
            cameraObject.transform.position=new Vector3(0,3.6f,-10);
            camera.orthographic=true;
            camera.orthographicSize=5.8f;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.045f,.068f,.059f);
            camera.nearClipPlane=.1f;
            camera.farClipPlane=50f;
            camera.allowHDR=false;
            camera.allowMSAA=false;

            GameObject bossObject=new GameObject("DoCheol_RootDevourer",typeof(Health2D),typeof(DoCheolBossController));
            bossObject.transform.position=new Vector3(3.2f,0,0);
            var boss=bossObject.GetComponent<DoCheolBossController>();
            GameObject visualObject=new GameObject("ArticulatedVisual");
            visualObject.transform.SetParent(bossObject.transform,false);
            DocheolVisual visual=visualObject.AddComponent<DocheolVisual>();
            string[] texturePaths=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/BossFight/Art"})
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string atlasPath=texturePaths.FirstOrDefault(p=>p.ToLowerInvariant().Contains("atlas"))??texturePaths.FirstOrDefault();
            if(string.IsNullOrEmpty(atlasPath))throw new InvalidOperationException("Docheol atlas artwork has not been imported.");
            TextureImporter importer=AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if(importer!=null)
            {
                importer.isReadable=true;importer.filterMode=FilterMode.Point;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
                importer.SaveAndReimport();
            }
            visual.Configure(AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));

            GameObject systems=new GameObject("00_Encounter",typeof(BossFightSession),typeof(BossFightHud),typeof(BossFightFeedback));
            var feedback=systems.GetComponent<BossFightFeedback>();
            systems.GetComponent<BossFightSession>().SetReferences(player,boss,camera,visual,feedback);
            EditorSceneManager.SaveScene(arena,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            PlayerSettings.companyName="Hwanjo";
            PlayerSettings.productName="Hwanjo - Docheol";
            PlayerSettings.defaultScreenWidth=1280;
            PlayerSettings.defaultScreenHeight=720;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;
            PlayerSettings.runInBackground=true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            Debug.Log("[Docheol] Scene built with original warrior: "+ScenePath+" | Atlas: "+atlasPath);
        }

        private static Sprite MakeSquare()
        {
            string path=Generated+"/ArenaPixel.png";
            if(!File.Exists(path))
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();
                File.WriteAllBytes(path,texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=2;
            importer.filterMode=FilterMode.Point;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static void AddSlab(Transform parent,string name,Vector2 position,Vector2 size,Color color,Sprite sprite,Material material,bool collision)
        {
            var obj=new GameObject(name,typeof(SpriteRenderer));
            obj.transform.SetParent(parent,false);
            obj.transform.position=position;
            obj.transform.localScale=new Vector3(size.x,size.y,1);
            var renderer=obj.GetComponent<SpriteRenderer>();
            renderer.sprite=sprite;renderer.color=color;renderer.sortingOrder=-10;renderer.sharedMaterial=material;
            if(collision)obj.AddComponent<BoxCollider2D>().size=Vector2.one;
        }

        public static void BuildSceneFromCommandLine()
        {
            try { BuildScene(); EditorApplication.Exit(0); }
            catch(Exception e) { Debug.LogException(e);EditorApplication.Exit(1); }
        }

        [MenuItem("Tools/Boss Fight/Build Windows Player")]
        public static void BuildWindows()
        {
            try
            {
                BuildScene();
                string destination=Path.Combine(WorkRoot,"Deliverables","Hwanjo_Docheol","Hwanjo_Docheol.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                var options=new BuildPlayerOptions{ scenes=new[]{ScenePath},locationPathName=destination,
                    target=BuildTarget.StandaloneWindows64,options=BuildOptions.None };
                BuildReport report=BuildPipeline.BuildPlayer(options);
                Directory.CreateDirectory(Path.Combine(WorkRoot,"QA"));
                File.WriteAllText(Path.Combine(WorkRoot,"QA","build-summary.json"),
                    JsonUtility.ToJson(new BuildEvidence { result=report.summary.result.ToString(), errors=report.summary.totalErrors,
                    warnings=report.summary.totalWarnings,bytes=report.summary.totalSize,path=destination,
                    unity=Application.unityVersion,utc=DateTime.UtcNow.ToString("o") },true));
                Debug.Log("[Docheol] WINDOWS_BUILD "+report.summary.result+" "+destination);
                if(Application.isBatchMode)EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
            }
            catch(Exception e) { Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);else throw; }
        }
        [Serializable]private class BuildEvidence
        { public string result,path,unity,utc; public int errors,warnings; public ulong bytes; }
    }
}
