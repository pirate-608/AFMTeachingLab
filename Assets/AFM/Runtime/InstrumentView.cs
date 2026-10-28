using UnityEngine;
using AfmLab.Core;
using UnityEngine.Rendering.Universal;

namespace AfmLab
{
    public sealed class InstrumentView : MonoBehaviour
    {
        Transform beamRoot,mirror,tip,sample,spot;
        GameObject sampleSurface;
        Mesh surfaceMesh;
        SampleKind lastSample=(SampleKind)(-1);
        Mesh beam;
        Vector3[] vertices;
        LineRenderer incident,reflected,reference;
        Material silver,graphite,teal,red,gold;
        const int Segments=48;
        public Camera LabCamera { get; private set; }
        public void Create()
        {
            silver=Material("Cantilever silicon",new Color(.66f,.76f,.82f),.65f);
            graphite=Material("Graphite",new Color(.055f,.09f,.12f),.5f);
            teal=Material("Sample",new Color(.13f,.50f,.52f),.3f);
            red=Material("Laser",new Color(1,.10f,.055f),0,true);
            gold=Material("Reflective coating",new Color(.95f,.65f,.27f),.85f);
            Box("Optical table",new Vector3(.2f,-1.8f,0),new Vector3(9,.28f,4.5f),graphite);
            Box("Fixed clamp",new Vector3(-3.0f,.2f,0),new Vector3(1.1f,.65f,1.1f),silver);
            Box("Clamp support",new Vector3(-3.1f,-.6f,0),new Vector3(.5f,1.6f,.65f),graphite);
            Box("Z piezo",new Vector3(1.3f,-1.35f,0),new Vector3(1.7f,.5f,1.7f),silver);
            sample=new GameObject("XY and Z sample stage").transform;sample.SetParent(transform,false);
            var substrate=Box("Sample substrate",Vector3.zero,new Vector3(2.5f,.15f,2),teal);substrate.transform.SetParent(sample,false);
            sampleSurface=new GameObject("Measured sample topography");sampleSurface.transform.SetParent(sample,false);
            surfaceMesh=new Mesh{name="Analytic sample height field"};sampleSurface.AddComponent<MeshFilter>().sharedMesh=surfaceMesh;
            sampleSurface.AddComponent<MeshRenderer>().sharedMaterial=teal;sampleSurface.SetActive(false);
            for(int i=0;i<7;i++)Box("Piezo electrode",new Vector3(1.3f,-1.1f-i*.055f,0),new Vector3(1.73f,.015f,1.73f),graphite);
            beamRoot=new GameObject("Cantilever mesh").transform;beamRoot.SetParent(transform,false);
            beam= new Mesh {name="Cantilever deformation mesh"};beam.MarkDynamic();
            vertices=new Vector3[(Segments+1)*4];int[] triangles=new int[Segments*24];int ti=0;
            for(int n=0;n<Segments;n++)for(int face=0;face<4;face++)
            {
                int a=n*4+face,b=n*4+(face+1)%4,c=(n+1)*4+face,d=(n+1)*4+(face+1)%4;
                triangles[ti++]=a;triangles[ti++]=c;triangles[ti++]=b;triangles[ti++]=b;triangles[ti++]=c;triangles[ti++]=d;
            }
            beamRoot.gameObject.AddComponent<MeshFilter>().sharedMesh=beam;
            beamRoot.gameObject.AddComponent<MeshRenderer>().sharedMaterial=silver;
            UpdateBeam(0,false);beam.triangles=triangles;beam.RecalculateNormals();
            mirror=Box("Reflective patch",new Vector3(1.65f,.07f,0),new Vector3(.34f,.018f,.5f),gold).transform;
            tip=MakeTip();
            var laser=Box("Laser source",new Vector3(-1.35f,3.5f,0),new Vector3(.65f,.9f,.65f),graphite);
            laser.transform.rotation=Quaternion.Euler(0,0,38);
            var detector=Box("Quadrant detector",new Vector3(4.05f,3.15f,0),new Vector3(1.7f,.12f,1.7f),graphite);
            detector.transform.rotation=Quaternion.Euler(0,0,-37);
            Box("Detector stand",new Vector3(4.05f,.5f,.3f),new Vector3(.16f,4.2f,.16f),silver);
            spot=GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;spot.name="Schematic laser spot";spot.SetParent(transform,false);spot.localScale=Vector3.one*.14f;spot.GetComponent<Renderer>().sharedMaterial=red;
            incident=Line("Incident beam",red,.025f);
            reflected=Line("Reflected beam",red,.025f);
            reference=Line("Zero reflection",Material("Zero ray",new Color(.18f,.55f,.52f),0,true),.012f);
            reference.SetPositions(new[]{new Vector3(1.65f,.08f,0),new Vector3(4.05f,3.15f,0)});
            var cameraGo=new GameObject("AFM Camera");cameraGo.transform.SetParent(transform,false);
            LabCamera=cameraGo.AddComponent<Camera>();LabCamera.tag="MainCamera";
            LabCamera.orthographic=true;LabCamera.orthographicSize=4.1f;
            LabCamera.backgroundColor=new Color(.018f,.035f,.055f);LabCamera.clearFlags=CameraClearFlags.SolidColor;
            LabCamera.nearClipPlane=.1f;LabCamera.farClipPlane=100;SetSideView(false);
            var cameraData=LabCamera.GetUniversalAdditionalCameraData();cameraData.renderPostProcessing=false;cameraData.renderShadows=true;
            LabCamera.allowHDR=false;
            var lightGo=new GameObject("Key light");lightGo.transform.SetParent(transform,false);lightGo.transform.rotation=Quaternion.Euler(45,-35,0);
            var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;light.color=new Color(.8f,.9f,1);
            RenderSettings.ambientLight=new Color(.3f,.38f,.45f);
            UpdateView(0,0,false);
        }
        public void SetSideView(bool side)
        {
            LabCamera.transform.position=side?new Vector3(.5f,1,-16):new Vector3(7,6,-13);
            LabCamera.transform.LookAt(new Vector3(.5f,.8f,0));
        }
        public void UpdateView(double tipNm,double stageNm,bool dynamicMode)
        {
            // This is explicitly the magnified structure illustration, never a source of physical readings.
            float bend=(float)tipNm*.016f;
            UpdateBeam(bend,dynamicMode);
            double shape=dynamicMode?OpticalLeverModel.ModeShape(.95):OpticalLeverModel.StaticShape(.95);
            double slope=dynamicMode?OpticalLeverModel.ModeSlopeFactor(.95):OpticalLeverModel.StaticSlopeFactor(.95);
            var hit=new Vector3(1.65f,(float)(bend*shape)+.08f,0);
            float angle=Mathf.Atan((float)(bend*slope)/4.8f)*Mathf.Rad2Deg;
            mirror.localPosition=hit;mirror.localRotation=Quaternion.Euler(0,0,angle);
            tip.localPosition=new Vector3(1.9f,bend-.05f,0);
            Vector3 samplePosition=sample.localPosition;samplePosition.y=-.85f-(float)stageNm*.07f;sample.localPosition=samplePosition;
            var source=new Vector3(-1.35f,3.5f,0);
            var ray=Vector3.Reflect((hit-source).normalized,new Vector3(-Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad),0));
            var center=new Vector3(4.05f,3.15f,0);var normal=new Vector3(-.6f,-.8f,0);
            float distance=Vector3.Dot(center-hit,normal)/Vector3.Dot(ray,normal);
            var end=hit+ray*distance;
            incident.SetPositions(new[]{source,hit});reflected.SetPositions(new[]{hit,end});spot.position=end;
        }
        public void SetSample(bool scanning,SampleKind kind,double x=.5,double y=.5)
        {
            sampleSurface.SetActive(scanning);
            Vector3 pos=sample.localPosition;
            pos.x=scanning?1.9f+1.25f-(float)x*2.5f:1.9f;
            pos.z=scanning?1-(float)y*2:0;sample.localPosition=pos;
            if(!scanning || kind==lastSample)return;
            lastSample=kind;const int count=40;
            var points=new Vector3[(count+1)*(count+1)];var faces=new int[count*count*6];int t=0;
            for(int row=0;row<=count;row++)for(int col=0;col<=count;col++)
            {
                double u=(double)col/count,v=(double)row/count;
                points[row*(count+1)+col]=new Vector3((float)u*2.5f-1.25f,.08f+(float)Samples.Height(kind,u,v)*.07f,(float)v*2-1);
                if(row<count && col<count)
                {
                    int a=row*(count+1)+col,b=a+1,c=a+count+1,d=c+1;
                    faces[t++]=a;faces[t++]=c;faces[t++]=b;faces[t++]=b;faces[t++]=c;faces[t++]=d;
                }
            }
            surfaceMesh.Clear();surfaceMesh.vertices=points;surfaceMesh.triangles=faces;surfaceMesh.RecalculateNormals();surfaceMesh.RecalculateBounds();
        }
        void UpdateBeam(float bend,bool dynamicMode)
        {
            for(int n=0;n<=Segments;n++)
            {
                float s=(float)n/Segments,x=-2.9f+4.8f*s;
                float y=bend*(float)(dynamicMode?OpticalLeverModel.ModeShape(s):OpticalLeverModel.StaticShape(s));
                vertices[n*4]=new Vector3(x,y+.025f,-.34f);vertices[n*4+1]=new Vector3(x,y+.025f,.34f);
                vertices[n*4+2]=new Vector3(x,y-.025f,.34f);vertices[n*4+3]=new Vector3(x,y-.025f,-.34f);
            }
            beam.vertices=vertices;beam.RecalculateBounds();beam.RecalculateNormals();
        }
        Transform MakeTip()
        {
            var go=new GameObject("Probe tip");go.transform.SetParent(transform,false);
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.18f,0,-.18f),new Vector3(.18f,0,-.18f),new Vector3(.18f,0,.18f),new Vector3(-.18f,0,.18f),new Vector3(0,-.52f,0)};
            mesh.triangles=new[]{0,4,1,1,4,2,2,4,3,3,4,0,0,1,2,0,2,3};mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=silver;return go.transform;
        }
        Material Material(string name,Color color,float metal,bool emission=false)
        {
            var m=new Material(Shader.Find(emission?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit")){name=name};
            m.SetColor("_BaseColor",color);
            if(!emission){m.SetFloat("_Metallic",metal);m.SetFloat("_Smoothness",.6f);}
            return m;
        }
        GameObject Box(string name,Vector3 position,Vector3 scale,Material mat)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(transform,false);go.transform.localPosition=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=mat;Destroy(go.GetComponent<Collider>());return go;
        }
        LineRenderer Line(string name,Material mat,float width)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();line.positionCount=2;line.sharedMaterial=mat;line.startWidth=width;line.endWidth=width;line.numCapVertices=5;return line;
        }
    }
}
