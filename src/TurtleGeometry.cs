using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;

public sealed partial class TurtlePet {
    public static string AssetDirectory=Path.Combine(Environment.CurrentDirectory,"assets");
    readonly Color[,] oakPixels=new Color[16,16];
    Color[,] headTop,headFront,headSide,headBottom,shellTop,bodyBottom;
    readonly List<Triangle> workMesh=new List<Triangle>();
    readonly V[] modelPivots={new V(-.2375f,0,.2f),new V(.1f,0,.275f),new V(0,.275f,.05f),new V(0,-.275f,.05f),new V(-.445f,.275f,.05f),new V(-.445f,-.275f,.05f)};
    bool importedModel;
    public string ModelSource {get{return importedModel?"Blender":"procedural";}}
    float eyeCenterZ=.275f,headTuck=.15f,shellGroundZ=.018f,shellGroundX=.0375f,shellShadowX=.39f,shellShadowY=.34f;
    static Dictionary<string,object> MeshObject(object value) {
        var result=value as Dictionary<string,object>;
        if(result==null) throw new InvalidDataException("Invalid turtle mesh object");return result;
    }
    static object[] MeshArray(object value,int maximum) {
        var result=value as object[];
        if(result==null||result.Length>maximum) throw new InvalidDataException("Invalid turtle mesh array");return result;
    }
    static float MeshNumber(object value) {
        float result=Convert.ToSingle(value,System.Globalization.CultureInfo.InvariantCulture);
        if(float.IsNaN(result)||float.IsInfinity(result)||Math.Abs(result)>10) throw new InvalidDataException("Invalid turtle coordinate");return result;
    }
    static V MeshVertex(object value) {
        var a=MeshArray(value,3);if(a.Length!=3) throw new InvalidDataException("Invalid turtle vertex");
        return new V(MeshNumber(a[0]),MeshNumber(a[1]),MeshNumber(a[2]));
    }
    static Color MeshColor(object value) {
        var a=MeshArray(value,3);if(a.Length!=3) throw new InvalidDataException("Invalid turtle color");
        return Color.FromArgb(Convert.ToInt32(a[0]),Convert.ToInt32(a[1]),Convert.ToInt32(a[2]));
    }
    bool LoadBlenderModel() {
        string path=Path.Combine(AssetDirectory,"baby-turtle.mesh.json");
        if(!File.Exists(path)) return false;
        if(new FileInfo(path).Length>4*1024*1024) throw new InvalidDataException("Turtle mesh file is too large");
        var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024};
        var data=MeshObject(json.DeserializeObject(File.ReadAllText(path)));
        if(Convert.ToInt32(data["formatVersion"])!=1) throw new InvalidDataException("Unsupported turtle mesh version");
        var parts=MeshArray(data["parts"],6);if(parts.Length!=6) throw new InvalidDataException("Expected six turtle parts");
        Color eyelid=MeshColor(data["eyeBackingColor"]);
        float[] minX=new float[6],maxX=new float[6],maxZ=new float[6],maxY=new float[6],minY=new float[6];
        for(int part=0;part<6;part++) {
            var source=MeshObject(parts[part]);if(Convert.ToInt32(source["id"])!=part) throw new InvalidDataException("Unexpected turtle part order");
            modelPivots[part]=MeshVertex(source["pivot"]);
            var raw=MeshArray(source["vertices"],20000);if(raw.Length<3) throw new InvalidDataException("Empty turtle part");
            V[] vertices=new V[raw.Length];minX[part]=minY[part]=float.MaxValue;maxX[part]=maxY[part]=maxZ[part]=float.MinValue;
            for(int i=0;i<vertices.Length;i++) {
                V v=vertices[i]=MeshVertex(raw[i]);minX[part]=Math.Min(minX[part],v.x);maxX[part]=Math.Max(maxX[part],v.x);
                minY[part]=Math.Min(minY[part],v.y);maxY[part]=Math.Max(maxY[part],v.y);maxZ[part]=Math.Max(maxZ[part],v.z);
            }
            var triangles=MeshArray(source["triangles"],40000);if(triangles.Length==0) throw new InvalidDataException("Empty turtle faces");
            foreach(var item in triangles) {
                var face=MeshObject(item);var indices=MeshArray(face["indices"],3);if(indices.Length!=3) throw new InvalidDataException("Invalid turtle face");
                int a=Convert.ToInt32(indices[0]),b=Convert.ToInt32(indices[1]),c=Convert.ToInt32(indices[2]);
                if(a<0||b<0||c<0||a>=vertices.Length||b>=vertices.Length||c>=vertices.Length) throw new InvalidDataException("Turtle face index out of range");
                bool eye=part==1&&Convert.ToBoolean(face["eye"]);V va=vertices[a],vb=vertices[b],vc=vertices[c];
                if(eye) {
                    V normal=V.Cross(vb-va,vc-va);float length=(float)Math.Sqrt(normal.x*normal.x+normal.y*normal.y+normal.z*normal.z);
                    if(length>0) {
                        V inset=new V(normal.x/length*.0008f,normal.y/length*.0008f,normal.z/length*.0008f);
                        mesh.Add(new Triangle{a=va-inset,b=vb-inset,c=vc-inset,color=eyelid,part=part});
                    }
                }
                mesh.Add(new Triangle{a=va,b=vb,c=vc,color=MeshColor(face["color"]),part=part,eye=eye});
            }
        }
        eyeCenterZ=MeshNumber(data["eyeCenterZ"]);headTuck=Math.Max(0,maxZ[1]-maxZ[0]);
        shellGroundZ=.4f-maxZ[0]-.007f;shellGroundX=-.2f-(minX[0]+maxX[0])/2;
        shellShadowX=(maxX[0]-minX[0])/2+.0475f;shellShadowY=(maxY[0]-minY[0])/2+.04f;
        importedModel=true;return true;
    }
    Bitmap raster;
    int[] rasterPixels;
    float[] rasterDepth;
    void PrepareRaster() {
        if(raster==null||raster.Width!=Width||raster.Height!=Height) {
            if(raster!=null) raster.Dispose();
            raster=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            rasterPixels=new int[Width*Height];rasterDepth=new float[Width*Height];
        }
        Array.Clear(rasterPixels,0,rasterPixels.Length);
        for(int i=0;i<rasterDepth.Length;i++) rasterDepth[i]=float.NegativeInfinity;
    }
    void RasterTriangle(V a,V b,V c,int color) {
        float d=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
        if(Math.Abs(d)<.00001) return;
        int x0=Math.Max(0,(int)Math.Floor(Math.Min(a.x,Math.Min(b.x,c.x))));
        int x1=Math.Min(Width-1,(int)Math.Ceiling(Math.Max(a.x,Math.Max(b.x,c.x))));
        int y0=Math.Max(0,(int)Math.Floor(Math.Min(a.y,Math.Min(b.y,c.y))));
        int y1=Math.Min(Height-1,(int)Math.Ceiling(Math.Max(a.y,Math.Max(b.y,c.y))));
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) {
            float u=((b.y-c.y)*(x+.5f-c.x)+(c.x-b.x)*(y+.5f-c.y))/d;
            float v=((c.y-a.y)*(x+.5f-c.x)+(a.x-c.x)*(y+.5f-c.y))/d;
            float w=1-u-v;if(u<-.00001||v<-.00001||w<-.00001) continue;
            float depth=u*a.z+v*b.z+w*c.z;int i=y*Width+x;
            if(depth>rasterDepth[i]) {rasterDepth[i]=depth;rasterPixels[i]=color;}
        }
    }
    void PresentRaster(Graphics g) {
        var data=raster.LockBits(new Rectangle(0,0,Width,Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try {System.Runtime.InteropServices.Marshal.Copy(rasterPixels,0,data.Scan0,rasterPixels.Length);} finally {raster.UnlockBits(data);}
        g.DrawImageUnscaled(raster,0,0);
    }
    // Image-free fallback model. The active character loads the owner's Blender mesh.
    // x points forward, y across the body, z up. Every face has an explicit rigid part.
    static readonly Color[] HeadGreen = { Color.FromArgb(110,182,75), Color.FromArgb(139,209,86), Color.FromArgb(77,152,64), Color.FromArgb(56,127,66) };
    static readonly Color[] ShellGreen = { Color.FromArgb(56,127,66), Color.FromArgb(110,182,75), Color.FromArgb(77,152,64), Color.FromArgb(139,209,86) };
    static readonly Color[] BellyGold = { Color.FromArgb(212,200,124), Color.FromArgb(242,234,162), Color.FromArgb(187,159,107) };
    void BuildReferenceModel() {
        mesh.Clear();
        importedModel=false;eyeCenterZ=.275f;headTuck=.15f;shellGroundZ=.018f;shellGroundX=.0375f;shellShadowX=.39f;shellShadowY=.34f;
        modelPivots[0]=new V(-.2375f,0,.2f);modelPivots[1]=new V(.1f,0,.275f);
        modelPivots[2]=new V(0,.275f,.05f);modelPivots[3]=new V(0,-.275f,.05f);modelPivots[4]=new V(-.445f,.275f,.05f);modelPivots[5]=new V(-.445f,-.275f,.05f);
        BuildOriginalTextures();
        if(LoadBlenderModel()) {BuildWorkstation();return;}
        // Head: deliberately a full cube, with a tan lower band.
        Box(new V(.10f,-.25f,.025f),new V(.60f,.25f,.525f),1,3,3,3);
        // Body: broader, lower shell and gold underside.
        Box(new V(-.58f,-.30f,.025f),new V(.105f,.30f,.375f),0,4,4,2);
        // Four independent flat flippers, slightly embedded at the roots to hide joints.
        Box(new V(-.065f,.27f,.025f),new V(.065f,.58f,.075f),2,1,2,1);
        Box(new V(-.065f,-.58f,.025f),new V(.065f,-.27f,.075f),3,1,2,1);
        Box(new V(-.51f,.27f,.025f),new V(-.38f,.58f,.075f),4,1,2,1);
        Box(new V(-.51f,-.58f,.025f),new V(-.38f,-.27f,.075f),5,1,2,1);
        BuildWorkstation();
    }
    void BuildOriginalTextures() {
        // Code-generated fallback colors and workstation planks; no image loading.
        headTop=new Color[3,3];headFront=new Color[3,3];headSide=new Color[3,3];headBottom=new Color[3,3];
        shellTop=new Color[4,4];bodyBottom=new Color[4,4];
        for(int y=0;y<3;y++) for(int x=0;x<3;x++) {
            headTop[x,y]=HeadGreen[(x+y*2)%HeadGreen.Length];
            headFront[x,y]=y==2?BellyGold[x%3]:HeadGreen[(x+y)%4];
            headSide[x,y]=y==2?BellyGold[(x+1)%3]:HeadGreen[(x*2+y)%4];
            headBottom[x,y]=BellyGold[(x+y)%3];
        }
        headSide[1,1]=Color.FromArgb(30,39,26);
        for(int y=0;y<4;y++) for(int x=0;x<4;x++) {
            shellTop[x,y]=ShellGreen[(x*3+y+(x==y?1:0))%4];
            bodyBottom[x,y]=BellyGold[(x+y*2)%3];
        }
        for(int y=0;y<16;y++) for(int x=0;x<16;x++) {
            bool seam=y%4==0;
            int shade=seam?-30:((x*7+(y/4)*3)%5)*7-10;
            if((x+(y/4)*5)%13==0&&!seam) shade-=18;
            oakPixels[x,y]=Color.FromArgb(170+shade,137+shade,82+shade);
        }
    }
    void SolidBox(V min,V max,int part,Color color) {
        AddQuad(new V(max.x,min.y,min.z),new V(max.x,max.y,min.z),new V(max.x,max.y,max.z),new V(max.x,min.y,max.z),color,part);
        AddQuad(new V(min.x,max.y,min.z),new V(min.x,min.y,min.z),new V(min.x,min.y,max.z),new V(min.x,max.y,max.z),color,part);
        AddQuad(new V(max.x,max.y,min.z),new V(min.x,max.y,min.z),new V(min.x,max.y,max.z),new V(max.x,max.y,max.z),color,part);
        AddQuad(new V(min.x,min.y,min.z),new V(max.x,min.y,min.z),new V(max.x,min.y,max.z),new V(min.x,min.y,max.z),color,part);
        AddQuad(new V(min.x,min.y,max.z),new V(max.x,min.y,max.z),new V(max.x,max.y,max.z),new V(min.x,max.y,max.z),color,part);
        AddQuad(new V(min.x,max.y,min.z),new V(max.x,max.y,min.z),new V(max.x,min.y,min.z),new V(min.x,min.y,min.z),color,part);
    }
    void BuildWorkstation() {
        int start=mesh.Count;workMesh.Clear();
        // Pixel plank colors are generated locally on a sixteen-pixel grid.
        OakBlock(new V(.02f,-.25f,0),new V(.52f,.25f,.50f));
        // Monitor stand, chunky casing, and a screen facing the seated turtle (-x).
        SolidBox(new V(.40f,-.115f,.373f),new V(.59f,.115f,.395f),11,Color.FromArgb(56,65,69));
        SolidBox(new V(.48f,-.035f,.395f),new V(.52f,.035f,.47f),11,Color.FromArgb(66,76,78));
        SolidBox(new V(.465f,-.265f,.46f),new V(.525f,.265f,.79f),11,Color.FromArgb(39,49,53));
        AddQuad(new V(.463f,.239f,.486f),new V(.463f,-.239f,.486f),new V(.463f,-.239f,.764f),new V(.463f,.239f,.764f),Color.FromArgb(22,52,57),12);
        // Generic terminal lines, never real chat content.
        for(int i=0;i<5;i++) {
            float z=.718f-i*.039f;
            AddQuad(new V(.462f,.202f,z),new V(.462f,-.035f-(i%3)*.045f,z),new V(.462f,-.035f-(i%3)*.045f,z+.009f),new V(.462f,.202f,z+.009f),i%2==0?Color.FromArgb(143,215,137):Color.FromArgb(110,178,184),12);
        }
        // Keyboard: a small dark base with separate pixel keys.
        SolidBox(new V(.025f,-.235f,.374f),new V(.325f,.235f,.392f),13,Color.FromArgb(48,56,58));
        for(int row=0;row<4;row++) for(int col=0;col<7;col++) {
            float x=.043f+row*.066f,y=-.215f+col*.062f;
            SolidBox(new V(x,y,.392f),new V(x+.05f,y+.045f,.405f),13,Color.FromArgb(132,144,143));
        }
        // Bring the computer and keyboard up to the top of the taller plank cube.
        for(int i=start;i<mesh.Count;i++) if(mesh[i].part!=10) {
            mesh[i].a.z+=.128f;mesh[i].b.z+=.128f;mesh[i].c.z+=.128f;
            // Keep the monitor stand/casing within the back edge of the block.
            mesh[i].a.x-=.025f;mesh[i].b.x-=.025f;mesh[i].c.x-=.025f;
        }
        workMesh.AddRange(mesh.GetRange(start,mesh.Count-start));mesh.RemoveRange(start,mesh.Count-start);
    }
    void OakFace(V a,V b,V c,V d) {
        for(int row=0;row<16;row++) for(int col=0;col<16;col++) {
            Color color=oakPixels[col,row];
            V lo=Lerp(a,d,row/16f),hi=Lerp(b,c,row/16f),nextLo=Lerp(a,d,(row+1)/16f),nextHi=Lerp(b,c,(row+1)/16f);
            AddQuad(Lerp(lo,hi,col/16f),Lerp(lo,hi,(col+1)/16f),Lerp(nextLo,nextHi,(col+1)/16f),Lerp(nextLo,nextHi,col/16f),color,10);
        }
    }
    void OakBlock(V min,V max) {
        OakFace(new V(max.x,min.y,min.z),new V(max.x,max.y,min.z),new V(max.x,max.y,max.z),new V(max.x,min.y,max.z));
        OakFace(new V(min.x,max.y,min.z),new V(min.x,min.y,min.z),new V(min.x,min.y,max.z),new V(min.x,max.y,max.z));
        OakFace(new V(max.x,max.y,min.z),new V(min.x,max.y,min.z),new V(min.x,max.y,max.z),new V(max.x,max.y,max.z));
        OakFace(new V(min.x,min.y,min.z),new V(max.x,min.y,min.z),new V(max.x,min.y,max.z),new V(min.x,min.y,max.z));
        OakFace(new V(min.x,min.y,max.z),new V(max.x,min.y,max.z),new V(max.x,max.y,max.z),new V(min.x,max.y,max.z));
        OakFace(new V(min.x,max.y,min.z),new V(max.x,max.y,min.z),new V(max.x,min.y,min.z),new V(min.x,min.y,min.z));
    }
    IEnumerable<Triangle> SceneTriangles() {
        foreach(var tri in mesh) yield return tri;
        if(workAmount>.015) foreach(var tri in workMesh) yield return tri;
    }
    static V PitchAround(V v,float x,float z,double angle) {
        float dx=v.x-x,dz=v.z-z;
        return new V(x+(float)(dx*Math.Cos(angle)-dz*Math.Sin(angle)),v.y,z+(float)(dx*Math.Sin(angle)+dz*Math.Cos(angle)));
    }
    V WorkPose(V v,int part,bool animate) {
        double amount=workAmount;
        if(part>=10) {
            // Props grow out of the floor as the turtle sits down, then shrink away.
            return new V(.30f+(v.x-.30f)*(float)amount,v.y*(float)amount,v.z*(float)amount);
        }
        if(amount<.00001) return v;
        double angle=amount*Math.PI/3;
        if(part==1) v=PitchAround(v,modelPivots[1].x,modelPivots[1].z,-angle); // Keep the face level.
        if(part>=2) {
            int side=part%2==0?1:-1;bool front=part<4;
            V pivot=modelPivots[part];float root=pivot.x;
            if(front) {
                double sweep=-side*1.95*amount;float x=v.x-pivot.x,y=v.y-pivot.y;
                v.x=pivot.x+(float)(x*Math.Cos(sweep)-y*Math.Sin(sweep));v.y=pivot.y+(float)(x*Math.Sin(sweep)+y*Math.Cos(sweep));
            }
            v=PitchAround(v,root,pivot.z,-angle); // Flat feet and a level typing surface.
        }
        v=PitchAround(v,(modelPivots[4].x+modelPivots[5].x)/2,(modelPivots[4].z+modelPivots[5].z)/2,angle);
        if(part==1) v.z+=(float)(.128*amount);
        if(part==2||part==3) {
            double tap=animate&&!reducedMotion?Math.Max(0,Math.Sin(time*15+(part==2?0:Math.PI)))*.018:0;
            v=PitchAround(v,-.2225f,.435f,.28*amount);
            v.z+=(float)((.012+tap)*amount);
        }
        return v;
    }
    static V Lerp(V a,V b,float t) {return new V(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);}
    void Surface(V a,V b,V c,V d,int cols,int rows,int part,int face) {
        for(int i=0;i<cols;i++) for(int j=0;j<rows;j++) {
            V left=Lerp(a,d,(float)j/rows),right=Lerp(b,c,(float)j/rows);
            V nextLeft=Lerp(a,d,(float)(j+1)/rows),nextRight=Lerp(b,c,(float)(j+1)/rows);
            V q0=Lerp(left,right,(float)i/cols),q1=Lerp(left,right,(float)(i+1)/cols),q2=Lerp(nextLeft,nextRight,(float)(i+1)/cols),q3=Lerp(nextLeft,nextRight,(float)i/cols);
            float z=(q0.z+q1.z+q2.z+q3.z)/4;
            Color[] palette=(part==0?z<.145f:z<.15f)?BellyGold:part==0?ShellGreen:HeadGreen;
            if(part>=2) palette=face==5||face<4&&z<.045f?BellyGold:HeadGreen;
            int shade=(i*7+j*11+face*3+part*5+(i*j)%3)%palette.Length;
            Color color=palette[shade];bool eye=false;
            if(part==1) {
                color=face==4?headTop[j,2-i]:face==5?headBottom[i,j]:face==0||face==1?headFront[i,2-j]:headSide[face==3?2-i:i,2-j];
                eye=color.R<65&&color.G<65&&color.B<65;
            } else if(part==0) {
                if(face==4) color=shellTop[i,j];
                else if(face==5) color=bodyBottom[i,j];
                else color=j==0?BellyGold[0]:ShellGreen[(i+face)%ShellGreen.Length];
            } else if(part>=2) color=face==4?HeadGreen[(j+part)%2]:BellyGold[0];
            if(eye) {
                // Keep a green face beneath the eye so blinking never opens a hole in the head.
                AddQuad(q0,q1,q2,q3,HeadGreen[0],part);
                float offset=face==2?.001f:-.001f;
                q0.y+=offset;q1.y+=offset;q2.y+=offset;q3.y+=offset;
            }
            AddQuad(q0,q1,q2,q3,color,part,eye);
        }
    }
    void Box(V min,V max,int part,int nx,int ny,int nz) {
        // Quad winding is outward on each face. Tiled faces share exact edge coordinates.
        Surface(new V(max.x,min.y,min.z),new V(max.x,max.y,min.z),new V(max.x,max.y,max.z),new V(max.x,min.y,max.z),ny,nz,part,0);
        Surface(new V(min.x,max.y,min.z),new V(min.x,min.y,min.z),new V(min.x,min.y,max.z),new V(min.x,max.y,max.z),ny,nz,part,1);
        Surface(new V(max.x,max.y,min.z),new V(min.x,max.y,min.z),new V(min.x,max.y,max.z),new V(max.x,max.y,max.z),nx,nz,part,2);
        Surface(new V(min.x,min.y,min.z),new V(max.x,min.y,min.z),new V(max.x,min.y,max.z),new V(min.x,min.y,max.z),nx,nz,part,3);
        Surface(new V(min.x,min.y,max.z),new V(max.x,min.y,max.z),new V(max.x,max.y,max.z),new V(min.x,max.y,max.z),nx,ny,part,4);
        Surface(new V(min.x,max.y,min.z),new V(max.x,max.y,min.z),new V(max.x,min.y,min.z),new V(min.x,min.y,min.z),nx,ny,part,5);
    }
    V Pose(V v,int part) {
        if(part>=10) return WorkPose(v,part,false);
        double motion=reducedMotion?0:1;
        double surprise=state=="surprised"?Math.Sin(Math.PI*Math.Min(1,stateTime/.75)):0;
        double greeting=state=="greet"?Math.Sin(Math.PI*Math.Min(1,stateTime/1.9)):0;
        if(part==1) {
            // One rigid transform for the head and every eye face. No per-triangle guessing.
            double angle=headYaw*motion*.55;
            V pivot=modelPivots[1];float x=v.x-pivot.x,y=v.y-pivot.y;
            v.x=pivot.x+(float)(x*Math.Cos(angle)-y*Math.Sin(angle));v.y=pivot.y+(float)(x*Math.Sin(angle)+y*Math.Cos(angle));
            double pitch=(headPitch*.5+(state=="curious"?Math.Sin(stateTime*2)*.045:0)+(state=="working"?Math.Sin(time*2.2)*.025:0)+Math.Sin(phase)*.012*walkAmount+greeting*.055+surprise*.13)*motion;
            x=v.x-pivot.x;float z=v.z-pivot.z;
            v.x=pivot.x+(float)(x*Math.Cos(pitch)-z*Math.Sin(pitch));v.z=pivot.z+(float)(x*Math.Sin(pitch)+z*Math.Cos(pitch));
        } else if(part>=2) {
            int side=(part%2==0)?1:-1;bool front=part<4;
            V pivot=modelPivots[part];float pivotX=pivot.x,pivotY=pivot.y;
            // All flippers plant and push together, then reach forward while the
            // body rests. This is the same stroke curve used by FlipperStep.
            double cycle=phase%(Math.PI*2)/(Math.PI*2);
            double sweep,lift;
            if(cycle<.60) {sweep=-.26+.52*Ease(cycle/.60);lift=0;}
            else {double recovery=(cycle-.60)/.40;sweep=.26-.52*Ease(recovery);lift=.24*Math.Sin(recovery*Math.PI);}
            sweep*=side*walkAmount*motion;
            lift=(lift*walkAmount+(state=="carried"?-.12:0))*motion;
            float x=v.x-pivotX,y=v.y-pivotY;
            v.x=pivotX+(float)(x*Math.Cos(sweep)-y*Math.Sin(sweep));v.y=pivotY+(float)(x*Math.Sin(sweep)+y*Math.Cos(sweep));
            if(state=="wave"&&part==2) lift+=(.40+.18*Math.Sin(stateTime*7))*Math.Sin(Math.PI*Math.Min(1,stateTime/2.8))*motion;
            if(state=="greet"&&part==2) lift+=(.27+.12*Math.Sin(stateTime*8))*greeting*motion;
            if(state=="dance") lift+=(.10+.10*Math.Sin(stateTime*8+(side>0?0:Math.PI)))*motion;
            if(state=="onBack") lift+=Math.Sin(time*5+(part%2==0?0:Math.PI))*.16*motion;
            if(state=="tumble") lift+=Math.Sin(stateTime*17+(part%2==0?0:Math.PI))*.25*motion;
            lift+=surprise*.35*motion;
            y=v.y-pivotY;float z=v.z-pivot.z;double a=side*lift;
            v.y=pivotY+(float)(y*Math.Cos(a)-z*Math.Sin(a));v.z=pivot.z+(float)(y*Math.Sin(a)+z*Math.Cos(a));
        }
        // Whole-pet motion is translation only; lengths and right angles stay intact.
        double bob=Math.Sin(time*1.8)*.002;
        bob+=Math.Pow(Math.Sin(phase),2)*.003*walkAmount;
        v.y+=(float)(Math.Sin(phase)*.005*walkAmount*motion);
        if(state=="dance") bob+=Math.Abs(Math.Sin(stateTime*7))*.04;
        if(state=="happy") bob+=Math.Abs(Math.Sin(stateTime*4))*.025;
        if(state=="settle") bob+=Math.Abs(Math.Sin(stateTime*9))*Math.Exp(-stateTime*5)*.02;
        bob+=surprise*.035;
        v.z+=(float)(bob*motion);
        return AdventurePose(WorkPose(v,part,true),part);
    }
    void DrawGreeting(Graphics g) {
        // One short pixel heart instead of a permanently bouncing indicator.
        if(stateTime<.25||stateTime>1.65) return;
        int x=(int)(modelBounds.Left+modelBounds.Width*.35);
        int y=(int)(modelBounds.Top-8-(stateTime-.25)*11);
        Point[] heart={new Point(x,y+3),new Point(x+3,y),new Point(x+6,y+3),new Point(x+9,y),new Point(x+12,y+3),new Point(x+12,y+6),new Point(x+6,y+12),new Point(x,y+6)};
        using(var b=new SolidBrush(Color.FromArgb(235,164,140))) g.FillPolygon(b,heart);
    }
}
