using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AfmLab
{
    public sealed class LabPlot : VisualElement
    {
        public Func<double[]> First,Second,Third;
        public double Min=-1,Max=1;
        public Color FirstColor=new Color(.25f,.88f,.8f),SecondColor=new Color(1,.65f,.3f);
        public LabPlot() { style.flexGrow=1; style.minHeight=90; generateVisualContent+=Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;
            float w=contentRect.width,h=contentRect.height;
            if(w<10 || h<10)return;
            p.strokeColor=new Color(.18f,.26f,.32f);p.lineWidth=1;
            for(int i=0;i<=4;i++)
            {
                float y=8+(h-16)*i/4;
                p.BeginPath();p.MoveTo(new Vector2(8,y));p.LineTo(new Vector2(w-8,y));p.Stroke();
            }
            Line(p,First==null?null:First(),FirstColor,w,h);
            Line(p,Second==null?null:Second(),SecondColor,w,h);
            Line(p,Third==null?null:Third(),new Color(.74f,.52f,.98f),w,h);
        }
        void Line(Painter2D p,double[] values,Color color,float w,float h)
        {
            if(values==null || values.Length<2)return;
            p.strokeColor=color;p.lineWidth=2;bool started=false;
            for(int i=0;i<values.Length;i++)
            {
                if(double.IsNaN(values[i]) || double.IsInfinity(values[i])) { if(started)p.Stroke();started=false;continue; }
                var pos=new Vector2(8+(w-16)*i/(values.Length-1),8+(h-16)*(1-(float)Math.Clamp((values[i]-Min)/(Max-Min),0,1)));
                if(!started) {p.BeginPath();p.MoveTo(pos);started=true;} else p.LineTo(pos);
            }
            if(started)p.Stroke();
        }
    }

    public sealed class DetectorView : VisualElement
    {
        public double SpotUm,ZeroUm;
        public bool Valid=true;
        public DetectorView() { style.flexGrow=1;style.minHeight=90; generateVisualContent+=Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;float w=contentRect.width,h=contentRect.height;
            if(w<10 || h<10)return;
            float size=Mathf.Min(w-30,h-16),cx=w/2,cy=h/2;
            p.fillColor=new Color(.035f,.08f,.11f);p.strokeColor=new Color(.3f,.5f,.56f);p.lineWidth=1;
            p.BeginPath();p.MoveTo(new Vector2(cx-size/2,cy-size/2));p.LineTo(new Vector2(cx+size/2,cy-size/2));p.LineTo(new Vector2(cx+size/2,cy+size/2));p.LineTo(new Vector2(cx-size/2,cy+size/2));p.ClosePath();p.Fill();p.Stroke();
            p.BeginPath();p.MoveTo(new Vector2(cx-size/2,cy));p.LineTo(new Vector2(cx+size/2,cy));p.MoveTo(new Vector2(cx,cy-size/2));p.LineTo(new Vector2(cx,cy+size/2));p.Stroke();
            // A labelled +/-40 um zoom, not the entire 3 mm physical detector.
            float sy=cy-(float)(SpotUm/80)*size,zy=cy-(float)(ZeroUm/80)*size;
            p.strokeColor=new Color(.25f,.9f,.8f);p.lineWidth=2;
            p.BeginPath();p.Arc(new Vector2(cx,zy),7,0,360);p.Stroke();
            p.fillColor=Valid?new Color(1,.28f,.18f):Color.gray;
            p.BeginPath();p.Arc(new Vector2(cx,Mathf.Clamp(sy,cy-size/2,cy+size/2)),5,0,360);p.Fill();
        }
    }
}
