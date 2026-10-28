using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AfmLab.Editor
{
    public static class LabBuilder
    {
        const string Scene="Assets/AFM/Scenes/AFMLab.unity";
        [MenuItem("AFM Lab/Create or refresh lab scene")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/AFM/Scenes");Directory.CreateDirectory("Reports");
            ConfigureUrp();
            var panel=AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/AFM/Resources/AFMPanel.asset");
            if(panel==null){panel=ScriptableObject.CreateInstance<PanelSettings>();AssetDatabase.CreateAsset(panel,"Assets/AFM/Resources/AFMPanel.asset");}
            panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1600,1000);panel.match=.5f;
            panel.themeStyleSheet=AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/AFM/Resources/LabTheme.tss");
            EditorUtility.SetDirty(panel);
            const string oldMaterial="Assets/AFM/Resources/StandardReference.mat";
            const string litMaterial="Assets/AFM/Resources/URPLitReference.mat";
            if(File.Exists(oldMaterial) && !File.Exists(litMaterial))AssetDatabase.MoveAsset(oldMaterial,litMaterial);
            EnsureShaderReference(litMaterial,"Universal Render Pipeline/Lit");
            EnsureShaderReference("Assets/AFM/Resources/URPUnlitReference.mat","Universal Render Pipeline/Unlit");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("AFM Teaching Lab").AddComponent<AfmLabApp>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Scene,true)};
            PlayerSettings.companyName="AFM Learning";PlayerSettings.productName="AFM Teaching Lab";PlayerSettings.bundleVersion="0.1.0";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=1000;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            QualitySettings.vSyncCount=0;PlayerSettings.colorSpace=ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            File.WriteAllText("Reports/editor-setup.json","{\"scene\":\""+Scene+"\",\"unity\":\""+Application.unityVersion+"\",\"pipeline\":\"URP 17.5.0\",\"allQualityLevelsUseUrp\":true,\"configured\":true}");
            Debug.Log("AFM_SETUP_OK");
        }
        static void ConfigureUrp()
        {
            Directory.CreateDirectory("Assets/AFM/Rendering");
            const string path="Assets/AFM/Rendering/AFMUniversalPipeline.asset";
            var asset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if(asset==null)
            {
                var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.renderingMode=RenderingMode.Forward;
                AssetDatabase.CreateAsset(renderer,"Assets/AFM/Rendering/AFMUniversalRenderer.asset");
                asset=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(asset,path);
            }
            asset.supportsHDR=false;asset.msaaSampleCount=4;asset.renderScale=1;
            asset.useSRPBatcher=true;asset.supportsCameraDepthTexture=false;asset.supportsCameraOpaqueTexture=false;
            asset.shadowDistance=25;asset.shadowCascadeCount=1;asset.mainLightShadowmapResolution=1024;
            GraphicsSettings.defaultRenderPipeline=asset;
            int current=QualitySettings.GetQualityLevel();
            for(int i=0;i<QualitySettings.names.Length;i++)
            {
                QualitySettings.SetQualityLevel(i,false);QualitySettings.renderPipeline=asset;
            }
            QualitySettings.SetQualityLevel(current,false);EditorUtility.SetDirty(asset);
        }
        static void EnsureShaderReference(string path,string shader)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(material,path);}
            else material.shader=Shader.Find(shader);
            EditorUtility.SetDirty(material);
        }
        [MenuItem("AFM Lab/Build Windows player")]
        public static void BuildWindows()
        {
            Setup();Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{Scene},locationPathName="Builds/Windows/AFMTeachingLab.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText("Reports/build.json","{\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+",\"warnings\":"+report.summary.totalWarnings+",\"bytes\":"+report.summary.totalSize+"}");
            if(report.summary.result!=BuildResult.Succeeded)throw new System.Exception("AFM Windows build failed");
            Debug.Log("AFM_BUILD_OK");
        }
    }
}
