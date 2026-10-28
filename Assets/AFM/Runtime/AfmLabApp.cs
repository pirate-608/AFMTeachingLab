using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using AfmLab.Core;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AfmLab
{
    public sealed class AfmLabApp : MonoBehaviour
    {
        readonly OpticalLeverModel optical=new OpticalLeverModel();
        CantileverModel forceModel;
        CycleReading forceReading;
        ScanSimulator scan;
        readonly SimulationClock clock=new SimulationClock();
        InstrumentView instrument;
        VisualElement root,viewport,control,bottom,profileBox,readoutBox;
        Image sceneImage;
        Label modeLabel,stateLabel,readingLabel,noteLabel,profileLabel;
        readonly Button[] nav=new Button[3];
        LabPlot plotA,plotB,plotC,profile,signalPlot;
        DetectorView detector;
        Texture2D trueTexture,measuredTexture,errorTexture;
        readonly double[] opticalCurve=new double[100],forceCurve=new double[100],potentialCurve=new double[100],trueLine=new double[64];
        readonly double[] amplitudeHistory=new double[180],targetHistory=new double[180],errorHistory=new double[180],zeroHistory=new double[180];
        bool showErrorHistory;
        Label[] partLabels;
        readonly Vector3[] partPositions={new Vector3(-1.35f,3.5f,0),new Vector3(-.8f,.1f,0),new Vector3(1.9f,-.48f,0),new Vector3(4.05f,3.15f,0),new Vector3(1.3f,-1.5f,0)};
        int lesson,textureRevision=-1;
        double appliedForceNn=10,forceGapNm=28,playback=.3;
        bool paused,sideView;
        float displayTime,refreshTime;
        bool smoke;
        string smokeOutput;
        RenderTexture smokeTexture;
        RenderTexture sceneTexture;
        readonly UniversalRenderPipeline.SingleCameraRequest sceneRequest=new UniversalRenderPipeline.SingleCameraRequest();

        void Awake()
        {
            Application.targetFrameRate=60;
            forceModel=new CantileverModel();
            instrument=new GameObject("Optical instrument").AddComponent<InstrumentView>();instrument.Create();
            var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=Resources.Load<PanelSettings>("AFMPanel");
            root=doc.rootVisualElement;root.AddToClassList("lab");
            var font=Resources.Load<Font>("LabChinese");if(font!=null)root.style.unityFontDefinition=FontDefinition.FromFont(font);
            root.styleSheets.Add(Resources.Load<StyleSheet>("LabStyle"));
            BuildLayout();
            sceneTexture=new RenderTexture(1280,600,24,RenderTextureFormat.ARGB32){name="Instrument view"};sceneTexture.Create();
            instrument.LabCamera.targetTexture=sceneTexture;instrument.LabCamera.enabled=false;sceneImage.image=sceneTexture;sceneRequest.destination=sceneTexture;
            foreach(var name in new[]{"truth","measured","error"})
            {
                var tex=new Texture2D(64,64,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Point};
                if(name=="truth")trueTexture=tex;else if(name=="measured")measuredTexture=tex;else errorTexture=tex;
            }
            SetLesson(0);
            string[] args=Environment.GetCommandLineArgs();
            for(int n=0;n<args.Length;n++)
            {
                if(args[n]=="-afmSmoke")smoke=true;
                if(args[n]=="-afmOutput" && n+1<args.Length)smokeOutput=args[n+1];
            }
            if(smoke)
            {
                // Render both scene and the actual runtime panel offscreen, so hidden-window tests
                // do not depend on a swapchain being visible on the desktop.
                smokeTexture=new RenderTexture(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32);
                smokeTexture.Create();
                doc.panelSettings.targetTexture=smokeTexture;doc.panelSettings.clearColor=true;
                RenderTexture previous=RenderTexture.active;RenderTexture.active=smokeTexture;
                GL.Clear(true,true,new Color(.027f,.071f,.11f));RenderTexture.active=previous;
                StartCoroutine(SmokeTest());
            }
        }
        void BuildLayout()
        {
            var header=Element(root,"header");
            var titleBox=Element(header,"title-box");Label(titleBox,"AFM  /  微观探测实验台","title");
            Label(titleBox,"从一束光，到一幅表面形貌","subtitle");
            var headerRight=Element(header,"header-right");
            Label(headerRight,"INTERACTIVE SCIENCE LAB","eyebrow");
            Label(headerRight,"单模态力学 · 定量读出 · 反馈成像","subtitle");
            var navigation=Element(root,"navigation");
            string[] names={"01   光杠杆读出","02   受力与振动","03   闭环扫描"};
            for(int n=0;n<3;n++){int i=n;nav[n]=Button(navigation,names[n],()=>SetLesson(i),"nav");nav[n].name="lesson-"+n;}
            var body=Element(root,"body");
            viewport=Element(body,"viewport");
            sceneImage=new Image{scaleMode=ScaleMode.StretchToFill};sceneImage.AddToClassList("scene-image");viewport.Add(sceneImage);
            modeLabel=Label(viewport,"","mode-label");
            partLabels=new Label[5];string[] partNames={"激光器","微悬臂","针尖","四象限探测器","Z 轴压电台"};
            for(int i=0;i<5;i++)partLabels[i]=Label(viewport,partNames[i],"part-label");
            var annotation=Element(viewport,"annotation");
            Label(annotation,"结构与光路为放大示意，非等比例","annotation-text");
            Label(annotation,"定量计算使用 nm / nN；显示倍率不参与反馈","annotation-text");
            profileBox=Element(viewport,"profile-box");var profilePane=Element(profileBox,"profile-pane");
            profileLabel=Label(profilePane,"当前扫描剖面  ·  青：重建  /  橙：真值","small-title");
            profile=new LabPlot {Min=-1,Max=6};profilePane.Add(profile);
            var signalPane=Element(profileBox,"profile-pane");
            Button(signalPane,"振幅 / 误差 · 点击切换 · 近 96 ms",()=>showErrorHistory=!showErrorHistory,"history-toggle");
            signalPlot=new LabPlot {Min=10,Max=22,First=()=>showErrorHistory?errorHistory:amplitudeHistory,Second=()=>showErrorHistory?zeroHistory:targetHistory};signalPane.Add(signalPlot);
            var sidebar=Element(body,"sidebar");
            var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.style.flexGrow=1;sidebar.Add(scroll);control=scroll.contentContainer;
            readoutBox=Element(sidebar,"readout-box");
            bottom=Element(root,"plots");
            var footer=Element(root,"footer");
            Label(footer,"AFM Teaching Lab  ·  教学近似：一阶悬臂 / LJ 有效势 / 点针尖 / 均一刚性样品","footer-text");
            Label(footer,"青色＝读出 / 重建    橙色＝参考 / 真值","footer-text");
        }
        void SetLesson(int next)
        {
            lesson=next;paused=false;clock.Reset();displayTime=0;control.Clear();bottom.Clear();readoutBox.Clear();
            plotA=plotB=plotC=null;detector=null;
            for(int n=0;n<3;n++)nav[n].EnableInClassList("selected",n==lesson);
            profileBox.style.display=lesson==2?DisplayStyle.Flex:DisplayStyle.None;
            sceneImage.style.bottom=lesson==2?140:0;
            if(lesson==0)BuildOptical();else if(lesson==1)BuildForces();else BuildScanning();
            readoutBox.Add(stateLabel);readoutBox.Add(readingLabel);
            Button(control,"切换侧视 / 斜视",()=>{sideView=!sideView;instrument.SetSideView(sideView);},"secondary");
            RefreshReadings();
        }
        void BuildOptical()
        {
            modeLabel.text="01  /  把微小弯曲变成可读的光斑位移";
            Label(control,"加载与光学读出","section-title");
            Label(control,"拖动外力，观察悬臂局部斜率与光斑。此步骤为开环实验。","body-copy");
            Slider(control,"末端外力","nN",-30,30,(float)appliedForceNn,v=>appliedForceNn=v,"force");
            Slider(control,"探测器距离","mm",20,80,(float)(optical.DetectorDistanceM*1000),v=>{optical.DetectorDistanceM=v/1000;},"distance");
            var row=Element(control,"button-row");
            Button(row,"释放外力",()=>{appliedForceNn=0;control.Q<Slider>("force").value=0;},"secondary");
            var zero=Button(row,"设为光路零点",()=>optical.CalibrateZero(optical.DeflectionM(appliedForceNn*1e-9)),"primary");zero.name="zero";
            stateLabel=Label(control,"","status");readingLabel=Label(control,"","readings");
            noteLabel=Label(control,"光路零点记录当前光斑位置。\n它不会自动跟随载荷变化。\n\n光杠杆测局部斜率，再由梁模型换算位移。","body-copy");
            var a=Card("四象限探测器 · 中央局部放大","显示范围 ±40 μm · 红：光斑 / 青圈：零点");detector=new DetectorView();a.Insert(1,detector);
            var b=Card("力与静态末端偏转","横轴 −30…30 nN  /  纵轴 −30…30 nm");
            plotB=new LabPlot {Min=-30,Max=30,First=()=>opticalCurve};b.Insert(1,plotB);
            var c=Card("为什么能放大？","近轴条件：Δs ≈ 2D·Δθ");
            Label(c,"反射角变化约为镜面转角的两倍。\n\nL = 200 μm    k = 1 N/m\nF = 10 nN  →  q = 10 nm\nD = 50 mm  →  Δs ≈ 7.49 μm\n\n载荷释放后，偏转恢复；人为零点仍保留。","explanation");
            for(int i=0;i<100;i++)opticalCurve[i]=MathEx.Lerp(-30,30,i/99.0);
        }
        void BuildForces()
        {
            modeLabel.text="02  /  距离改变作用力，作用力改变振动";
            Label(control,"针尖—样品相互作用","section-title");
            Label(control,"从远处逐渐接近。最近一个实际振动周期会在下方显示，并在结构图中慢放。","body-copy");
            Slider(control,"未变形几何间距","nm",14,42,(float)forceGapNm,v=>forceGapNm=v,"gap");
            Slider(control,"品质因数 Q","",20,150,(float)forceModel.Parameters.Q,v=>forceModel.Parameters.Q=v,"quality");
            Slider(control,"有效刚度（f₀ 固定）","N/m",2,6,(float)forceModel.Parameters.Stiffness,v=>forceModel.Parameters.Stiffness=v,"stiffness");
            Slider(control,"驱动 / 固有频率","",.96f,1.04f,(float)forceModel.Parameters.DriveRatio,v=>forceModel.Parameters.DriveRatio=v,"frequency",3);
            var drive=new Toggle("开启周期驱动"){value=forceModel.DriveEnabled};drive.RegisterValueChangedCallback(e=>forceModel.DriveEnabled=e.newValue);control.Add(drive);
            var play=Button(control,"暂停 / 继续",()=>paused=!paused,"secondary");play.name="pause-force";
            Button(control,"复位到远离样品",()=>{forceModel=new CantileverModel();forceGapNm=36;SetLesson(1);},"secondary");
            stateLabel=Label(control,"","status");readingLabel=Label(control,"","readings");
            noteLabel=Label(control,"Fdrive = 0.60 nN，f₀ = 75 kHz\n正力：排斥，负力：吸引。\nLJ 为有效教学势；保守力不等同于耗散。\n\n移动间距滑块过快可能使振动状态跳变。","body-copy");
            var a=Card("最近一个振动周期","横轴 一个驱动周期 / 纵轴 −30…30 nm");plotA=new LabPlot {Min=-30,Max=30,First=()=>forceModel.LastWaveformNm};a.Insert(1,plotA);
            var b=Card("力—距离 F(d)","横轴 4.5…15 nm  /  纵轴 −5…60 nN");plotB=new LabPlot{Min=-5,Max=60,First=()=>forceCurve};b.Insert(1,plotB);
            var c=Card("势能 U(d)","横轴 4.5…15 nm  /  纵轴 −5…30 nN·nm");plotC=new LabPlot{Min=-5,Max=30,First=()=>potentialCurve,FirstColor=new Color(1,.65f,.3f)};c.Insert(1,plotC);
            for(int i=0;i<100;i++){double d=4.5+10.5*i/99;forceCurve[i]=forceModel.Interaction.Force(d);potentialCurve[i]=forceModel.Interaction.Potential(d);}
        }
        void BuildScanning()
        {
            modeLabel.text="03  /  让反馈跟踪表面，观察误差如何形成";
            Label(control,"闭环与扫描参数","section-title");
            Label(control,"先在平面标定，再逐点记录实际 Z 位移。每次对照使用相同起始状态。","body-copy");
            if(scan==null)ResetScan(new ScanSettings());
            var choices=new System.Collections.Generic.List<string>{"平滑台阶","正弦光栅","圆滑凸起"};
            var samples=new DropdownField("样品",choices,(int)scan.Settings.Sample);samples.name="sample";
            samples.RegisterValueChangedCallback(e=>{scan.Settings.Sample=(SampleKind)choices.IndexOf(e.newValue);RestartScan();});control.Add(samples);
            var presets=Element(control,"preset-grid");
            Preset(presets,"稳定跟踪",8,40000,.08);
            Preset(presets,"扫描过快",8,40000,.008);
            Preset(presets,"增益过低",3,1000,.08);
            Preset(presets,"增益过高",40,60000,.08);
            Slider(control,"比例 P","nm",0,80,(float)scan.Settings.Pid.Kp,v=>scan.Settings.Pid.Kp=v,"kp");
            Slider(control,"积分 I","nm/s",0,90000,(float)scan.Settings.Pid.Ki,v=>scan.Settings.Pid.Ki=v,"ki",0);
            Slider(control,"微分 D","nm·s",0,.01f,(float)scan.Settings.Pid.Kd,v=>scan.Settings.Pid.Kd=v,"kd",4);
            Slider(control,"单向行时间","ms",8,200,(float)(scan.Settings.LineSeconds*1000),v=>{pendingLine=v/1000;},"line",0);
            Slider(control,"振幅设定 Aset/A₀","",.65f,.9f,(float)scan.Settings.SetpointRatio,v=>{pendingSetpoint=v;},"setpoint",2);
            var toggle=new Toggle("扫描时启用反馈"){value=scan.Settings.Feedback};toggle.name="feedback";toggle.RegisterValueChangedCallback(e=>scan.Settings.Feedback=e.newValue);control.Add(toggle);
            var row=Element(control,"button-row");
            var restart=Button(row,"重新扫描 / 应用",RestartScan,"primary");restart.name="restart";
            Button(row,"暂停 / 继续",()=>paused=!paused,"secondary");
            Label(control,"行时间与设定值点“应用”后生效；P / I / D 实时生效。","tiny");
            stateLabel=Label(control,"","status");readingLabel=Label(control,"","readings");
            noteLabel=Label(control,"PI 为默认。高增益可能引起振荡；更快的扫描缩短了反馈响应时间。\n\n二维像素与数值误差来自实际采样，灰色代表尚未采样或无效。","body-copy");
            AddMap("真实表面",trueTexture,"固定色标 0…5 nm");AddMap("Z 位移重建",measuredTexture,"同一色标 0…5 nm");AddMap("高度误差",errorTexture,"蓝：偏低 / 红：偏高  ·  −2…2 nm");
            profile.First=()=>scan.Trace;profile.Second=()=>trueLine;profile.Third=()=>scan.Retrace;
            UpdateTextures();
        }
        double pendingLine=.08,pendingSetpoint=.8;
        void Preset(VisualElement parent,string text,double p,double i,double line)
        {
            var b=Button(parent,text,()=>
            {
                var s=new ScanSettings {Sample=scan.Settings.Sample,LineSeconds=line,Pid=new PidSettings{Kp=p,Ki=i}};
                ResetScan(s);SetLesson(2);
            },"preset");b.name="preset-"+text;
        }
        void ResetScan(ScanSettings settings)
        {
            scan=new ScanSimulator(settings);pendingLine=settings.LineSeconds;pendingSetpoint=settings.SetpointRatio;
            clock.Reset();textureRevision=-1;paused=false;
        }
        void RestartScan()
        {
            var s=scan.Settings;
            ResetScan(new ScanSettings{Sample=s.Sample,LineSeconds=pendingLine,SetpointRatio=pendingSetpoint,Feedback=s.Feedback,Pid=new PidSettings{Kp=s.Pid.Kp,Ki=s.Pid.Ki,Kd=s.Pid.Kd}});
        }
        void Update()
        {
            if(!paused)
            {
                displayTime+=Time.unscaledDeltaTime;
                if(lesson==1)
                {
                    forceModel.GapNm=forceGapNm;
                    clock.Advance(Time.unscaledDeltaTime,.002,1/(forceModel.Parameters.FrequencyHz*forceModel.Parameters.DriveRatio),300,()=>forceReading=forceModel.StepCycle(128));
                }
                else if(lesson==2 && scan!=null && scan.State!=ScanState.Complete && scan.State!=ScanState.Fault)
                    clock.Advance(Time.unscaledDeltaTime,playback,scan.CycleSeconds,500,scan.Step);
            }
            double bend=appliedForceNn,stage=0;
            if(lesson==1) bend=SlowWave(forceModel);
            if(lesson==2 && scan!=null){bend=SlowWave(scan.Oscillator);stage=scan.Stage.Position-(scan.State==ScanState.Calibrating?22.676:scan.ReferenceZ);}
            instrument.UpdateView(bend,stage,lesson!=0);
            instrument.SetSample(lesson==2,scan==null?SampleKind.Terrace:scan.Settings.Sample,scan == null ? .5 : scan.X,scan == null ? .5 : scan.Y);
            refreshTime+=Time.unscaledDeltaTime;
            if(refreshTime>.08f) {refreshTime=0;RefreshReadings();}
        }
        double SlowWave(CantileverModel model)
        {
            double[] wave=model.LastWaveformNm;
            int index=(int)(Mathf.Repeat(displayTime*.6f,1)*wave.Length);
            return wave[Math.Min(index,wave.Length-1)];
        }
        void LateUpdate()
        {
            Rect r=sceneImage.worldBound;
            if(r.width>1 && r.height>10)
            {
                instrument.LabCamera.rect=new Rect(0,0,1,1);
                instrument.LabCamera.aspect=r.width/r.height;
                if(RenderPipeline.SupportsRenderRequest(instrument.LabCamera,sceneRequest))
                    RenderPipeline.SubmitRenderRequest(instrument.LabCamera,sceneRequest);
                for(int i=0;i<partLabels.Length;i++)
                {
                    Vector3 uv=instrument.LabCamera.WorldToViewportPoint(partPositions[i]);
                    partLabels[i].style.left=(float)(uv.x*r.width)+12;
                    partLabels[i].style.top=(float)((1-uv.y)*r.height)-12;
                }
            }
        }
        void RefreshReadings()
        {
            if(readingLabel==null)return;
            if(lesson==0)
            {
                var r=optical.Read(optical.DeflectionM(appliedForceNn*1e-9));
                stateLabel.text=r.Valid?"●  光路有效  /  外力开环":"●  光路失锁";
                readingLabel.text=$"末端偏转    {appliedForceNn:F2} nm\n光斑位移    {optical.DisplacementFromZeroM(r)*1e6:F2} μm\n归一化差分    {r.VerticalSignal:+0.0000;-0.0000;0.0000}\n接收光功率    {r.TotalPower*100:F1}%";
                if(detector!=null){detector.SpotUm=r.SpotVerticalM*1e6;detector.ZeroUm=optical.ZeroSpotM*1e6;detector.Valid=r.Valid;detector.MarkDirtyRepaint();}
            }
            else if(lesson==1)
            {
                stateLabel.text=paused?"●  已暂停":!forceReading.Valid?"●  正则化区：当前力不作定量解释":forceReading.MinGapNm<5*Math.Pow(2,1.0/6)?"●  周期内出现排斥力":"●  吸引作用主导";
                readingLabel.text=$"检测振幅    {forceReading.AmplitudeNm:F2} nm\n振动相位    {forceReading.PhaseRad*180/Math.PI:F1}°\n周期最小间距    {forceReading.MinGapNm:F2} nm\n周期峰值 |F|    {forceReading.PeakForceNn:F2} nN\n平均偏转    {forceReading.MeanNm:F3} nm";
            }
            else
            {
                string status=scan.State==ScanState.Calibrating?"平面标定中":scan.State==ScanState.Complete?"扫描完成":scan.State==ScanState.Fault?"已停止：模型工作域超限":"扫描中";
                stateLabel.text="●  "+(paused?"已暂停 / ":"")+status;
                readingLabel.text=$"A / Aset    {scan.MeasuredAmplitude:F2} / {scan.Settings.SetpointRatio*20:F2} nm\n振幅误差    {scan.AmplitudeError:+0.00;-0.00;0.00} nm\n相对 Z    {scan.Stage.Position-scan.ReferenceZ:F2} nm\n高度 RMSE    {scan.Rmse:F3} nm\n有效像素    {scan.ValidFraction*100:F1}%\n进度    {scan.CompletedPixels} / 4096";
                if(scan.State==ScanState.Fault)noteLabel.text=scan.Fault;
                for(int i=0;i<64;i++)trueLine[i]=scan.TrueHeight[scan.CurrentRow*64+i];
                scan.CopyHistory(HistorySignal.Amplitude,amplitudeHistory);scan.CopyHistory(HistorySignal.Target,targetHistory);scan.CopyHistory(HistorySignal.Error,errorHistory);
                signalPlot.Min=showErrorHistory?-4:10;signalPlot.Max=showErrorHistory?4:22;signalPlot.MarkDirtyRepaint();
                profileLabel.text=$"第 {scan.CurrentRow+1} / 64 行   ·   青：正扫 / 紫：反扫 / 橙：真值";
                if(textureRevision!=scan.Revision)UpdateTextures();profile.MarkDirtyRepaint();
            }
            if(plotA!=null)plotA.MarkDirtyRepaint();if(plotB!=null)plotB.MarkDirtyRepaint();if(plotC!=null)plotC.MarkDirtyRepaint();
        }
        void UpdateTextures()
        {
            if(scan==null)return;
            var truth=new Color32[4096];var measured=new Color32[4096];var error=new Color32[4096];
            for(int i=0;i<4096;i++)
            {
                truth[i]=HeightColor(scan.TrueHeight[i]);
                measured[i]=scan.Valid[i]?HeightColor(scan.Reconstructed[i]):new Color(.10f,.15f,.19f);
                float amount=(float)MathEx.Clamp(scan.Residual[i]/2,-1,1);
                error[i]=!scan.Valid[i]?new Color(.10f,.15f,.19f):amount<0?Color.Lerp(new Color(.85f,.88f,.88f),new Color(.15f,.4f,.85f),-amount):Color.Lerp(new Color(.85f,.88f,.88f),new Color(.95f,.25f,.18f),amount);
            }
            trueTexture.SetPixels32(truth);trueTexture.Apply();measuredTexture.SetPixels32(measured);measuredTexture.Apply();errorTexture.SetPixels32(error);errorTexture.Apply();textureRevision=scan.Revision;
        }
        static Color HeightColor(double nm)
        {
            float t=(float)MathEx.Clamp(nm/5,0,1);
            return t<.5f?Color.Lerp(new Color(.045f,.12f,.25f),new Color(.14f,.64f,.64f),t*2):Color.Lerp(new Color(.14f,.64f,.64f),new Color(1,.85f,.43f),(t-.5f)*2);
        }
        void AddMap(string title,Texture2D texture,string caption)
        {
            var c=Card(title,caption);var image=new Image{image=texture,scaleMode=ScaleMode.ScaleToFit};image.AddToClassList("map");c.Insert(1,image);
        }
        VisualElement Card(string title,string caption)
        {
            var c=Element(bottom,"plot-card");Label(c,title,"small-title");Label(c,caption,"plot-caption");return c;
        }
        static VisualElement Element(VisualElement parent,string cls)
        {
            var e=new VisualElement();e.AddToClassList(cls);parent.Add(e);return e;
        }
        static Label Label(VisualElement parent,string text,string cls)
        {
            var label=new Label(text);label.AddToClassList(cls);parent.Add(label);return label;
        }
        static Button Button(VisualElement parent,string text,Action action,string cls)
        {
            var b=new Button(action){text=text};b.AddToClassList(cls);parent.Add(b);return b;
        }
        static Slider Slider(VisualElement parent,string title,string unit,float min,float max,float value,Action<float> changed,string name,int decimals=1)
        {
            var wrap=Element(parent,"slider-wrap");var row=Element(wrap,"slider-label");Label(row,title,"slider-title");
            var readout=Label(row,value.ToString("F"+decimals)+" "+unit,"slider-value");
            var s=new Slider(min,max){value=value,name=name};s.RegisterValueChangedCallback(e=>{readout.text=e.newValue.ToString("F"+decimals)+" "+unit;changed(e.newValue);});wrap.Add(s);return s;
        }
        IEnumerator SmokeTest()
        {
            if(string.IsNullOrEmpty(smokeOutput))smokeOutput=Path.Combine(Application.persistentDataPath,"Smoke");
            Directory.CreateDirectory(smokeOutput);
            yield return null;yield return null;
            root.Q<Slider>("force").value=10;RefreshReadings();
            yield return new WaitForEndOfFrame();CaptureFrame("01-optical.png");
            yield return null;
            Submit(root.Q<Button>("zero"));
            bool zeroWorks=Math.Abs(optical.ZeroSpotM-optical.Read(10e-9).SpotVerticalM)<1e-12;
            root.Q<Slider>("force").value=0;RefreshReadings();
            bool releaseWorks=Math.Abs(appliedForceNn)<1e-12 && Math.Abs(optical.DisplacementFromZeroM(optical.Read(0)))>1e-6;
            Submit(nav[1]);yield return null;
            root.Q<Slider>("gap").value=26;
            for(int i=0;i<600;i++) {forceModel.GapNm=26;forceReading=forceModel.StepCycle(128);}
            RefreshReadings();yield return new WaitForEndOfFrame();CaptureFrame("02-forces.png");
            yield return null;Submit(nav[2]);yield return null;
            bool navWorks=lesson==2;
            while(scan.State==ScanState.Calibrating || scan.State==ScanState.Scanning)
            {
                for(int i=0;i<1500 && scan.State!=ScanState.Complete && scan.State!=ScanState.Fault;i++)scan.Step();
                RefreshReadings();yield return null;
            }
            UpdateTextures();RefreshReadings();yield return new WaitForEndOfFrame();CaptureFrame("03-stable-scan.png");
            yield return null;
            bool scanWorks=scan.State==ScanState.Complete && scan.CompletedPixels==4096 && scan.ValidFraction>.99 && scan.Rmse<.2;
            double stableRmse=scan.Rmse;
            Submit(root.Q<Button>("preset-扫描过快"));yield return null;
            while(scan.State==ScanState.Calibrating || scan.State==ScanState.Scanning)
            {
                for(int i=0;i<1500 && scan.State!=ScanState.Complete && scan.State!=ScanState.Fault;i++)scan.Step();
                RefreshReadings();yield return null;
            }
            UpdateTextures();RefreshReadings();yield return new WaitForEndOfFrame();CaptureFrame("04-fast-scan.png");
            yield return null;double fastRmse=scan.Rmse;
            bool fastWorks=scan.State==ScanState.Complete && fastRmse>stableRmse*3;
            root.Q<DropdownField>("sample").value="圆滑凸起";
            Submit(root.Q<Button>("preset-稳定跟踪"));yield return null;
            while(scan.State==ScanState.Calibrating || scan.State==ScanState.Scanning)
            {
                for(int i=0;i<1500 && scan.State!=ScanState.Complete && scan.State!=ScanState.Fault;i++)scan.Step();
                RefreshReadings();yield return null;
            }
            UpdateTextures();RefreshReadings();yield return new WaitForEndOfFrame();CaptureFrame("05-islands.png");
            yield return null;bool sampleWorks=scan.Settings.Sample==SampleKind.Islands && scan.State==ScanState.Complete && scan.ValidFraction>.99;
            bool urpWorks=GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && RenderPipelineManager.currentPipeline is UniversalRenderPipeline;
            bool materialsValid=true;
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach(var material in renderer.sharedMaterials)
                    materialsValid&=material!=null && material.shader!=null && material.shader.name.StartsWith("Universal Render Pipeline/");
            string report=$"{{\"urpActive\":{urpWorks.ToString().ToLowerInvariant()},\"urpMaterials\":{materialsValid.ToString().ToLowerInvariant()},\"zeroButton\":{zeroWorks.ToString().ToLowerInvariant()},\"forceSlider\":{releaseWorks.ToString().ToLowerInvariant()},\"navigation\":{navWorks.ToString().ToLowerInvariant()},\"scanComplete\":{scanWorks.ToString().ToLowerInvariant()},\"rmseNm\":{stableRmse.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"fastPreset\":{fastWorks.ToString().ToLowerInvariant()},\"fastRmseNm\":{fastRmse.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"sampleDropdown\":{sampleWorks.ToString().ToLowerInvariant()},\"pixels\":{scan.CompletedPixels}}}";
            File.WriteAllText(Path.Combine(smokeOutput,"player-smoke.json"),report);
            Debug.Log("AFM_SMOKE "+report);
            yield return new WaitForSeconds(.5f);
            Application.Quit(urpWorks && materialsValid && zeroWorks && releaseWorks && navWorks && scanWorks && fastWorks && sampleWorks?0:2);
        }
        static void Submit(Button button)
        {
            button.Focus();using(var ev=NavigationSubmitEvent.GetPooled()) {ev.target=button;button.SendEvent(ev);}
        }
        void CaptureFrame(string filename)
        {
            RenderTexture previous=RenderTexture.active;RenderTexture.active=smokeTexture;
            var pixels=new Texture2D(smokeTexture.width,smokeTexture.height,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,smokeTexture.width,smokeTexture.height),0,0);pixels.Apply();
            RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(smokeOutput,filename),pixels.EncodeToPNG());Destroy(pixels);
        }
    }
}
