using System;
using System.Drawing;
using System.Windows.Forms;

public sealed partial class TurtlePet {
    bool climbAndTumble;
    int climbSide=1;
    int topDestinationSide;
    bool topExit;
    double slipRemaining,descentPitchTarget;
    double topHang,fallHang,descentHang,descentAngle;
    double HangingAngle() {return state=="tipBack"?fallHang*(1-Ease(stateTime/.55)):Math.PI*topHang+descentHang*descentAngle;}
    void BeginDescentPose() {
        descentPitchTarget=0;
        descentAngle=climbSide>0?Math.PI/2:(topHang>.1?Math.PI*1.5:-Math.PI/2);
    }
    Rectangle climbArea;
    string climbDisplay="";
    double nextClimb=30,climbAmount,backPitch,climbPeakY,fallStartY;
    double tumbleDuration,tipStartPitch,tipStartX,fallFacing=-.55;
    double recoverStartX,recoverTargetX,recoverStartPitch;
    double landingDust,landingDustDuration=.55,landingDustStrength;
    int landingDustCount=10;
    ToolStripMenuItem tryClimbItem,helpUpItem;

    bool AdventureActive() {
        return state=="climbApproach"||state=="climb"||state=="climbTop"||state=="climbDown"||state=="tipBack"||state=="tumble"||state=="onBack"||state=="helpUp"||state=="recover";
    }
    void SetClimbing(bool enabled) {
        climbAndTumble=enabled;nextClimb=time+10+random.NextDouble()*12;
        if(!enabled&&AdventureActive()) {
            if(state=="onBack") HelpUp();
            else if(state!="helpUp"&&state!="recover") SafeDescent();
        }
        SaveSettings();
    }
    void BeginClimb() {
        if(!climbAndTumble||reducedMotion||AdventureActive()||state=="sleep"||AnyAssistantWorking()||pressed||dragging) return;
        var screen=Screen.FromControl(this);climbArea=screen.WorkingArea;climbDisplay=screen.DeviceName;
        if(climbArea.Height<Height+100) {nextClimb=time+120;Notify("This screen is too short for climbing");return;}
        climbSide=px+Width/2<climbArea.Left+climbArea.Width/2?-1:1;
        py=climbArea.Bottom-Height;backPitch=0;climbAmount=0;topHang=0;velocity=0;
        topExit=false;descentPitchTarget=0;descentHang=0;
        slipRemaining=2-Math.Log(Math.Max(.000001,random.NextDouble()))*45;
        SetState("climbApproach");targetX=climbSide<0?climbArea.Left:climbArea.Right-Width;
        nextClimb=time+60+random.NextDouble()*90;
    }
    void SlipFromEdge() {
        fallStartY=py;vy=velocity=0;tipStartX=px;fallFacing=yaw;fallHang=HangingAngle();topHang=descentHang=0;
        tipStartPitch=climbAmount*Math.PI*.45+backPitch;climbAmount=0;backPitch=tipStartPitch;
        if(tipStartPitch<0) {tipStartPitch+=Math.PI*2;backPitch=tipStartPitch;}
        double distance=Math.Max(1,climbArea.Bottom-Height-py);
        tumbleDuration=Math.Max(.12,(-40+Math.Sqrt(40*40+2*850*distance))/850);
        SetState("tipBack");
    }
    void SafeDescent() {
        backPitch=0;velocity=0;vy=0;
        if(state=="climbTop") {
            if(!topExit) topDestinationSide=px+Width/2<climbArea.Left+climbArea.Width/2?-1:1;
            topExit=true;return;
        }
        BeginDescentPose();
        SetState("climbDown");
    }
    void HelpUp() {
        if(state!="onBack") return;
        StartRecovery(true);
    }
    void StartRecovery(bool helped) {
        if(!helped&&descentAngle>Math.PI) descentAngle-=Math.PI*2;
        // The ceiling route carries the pitch past 180 degrees. Rebase the
        // equivalent orientation before standing, so recovery takes the short turn.
        if(!helped&&climbAmount>2) climbAmount-=2/.45;
        recoverStartX=px;recoverTargetX=Math.Max(climbArea.Left,Math.Min(climbArea.Right-Width,px));
        if(reducedMotion) {ResetAdventure(climbArea);return;}
        recoverStartPitch=backPitch;velocity=0;vy=0;
        SetState(helped?"helpUp":"recover");sleepInhibitUntil=time+15;
    }
    void ClearAdventureOrientation() {climbAmount=backPitch=topHang=descentHang=fallHang=0;}
    void ResetAdventure(Rectangle area) {
        ClearAdventureOrientation();velocity=vy=0;
        px=Math.Max(area.Left,Math.Min(area.Right-Width,px));py=area.Bottom-Height;
        SetState("idle");nextAction=5;nextClimb=time+45+random.NextDouble()*60;
    }
    static double Ease(double t) {t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
    bool UpdateAdventure(double dt,Rectangle area) {
        landingDust=Math.Max(0,landingDust-dt);
        if(!AdventureActive()) {climbAmount=Damp(climbAmount,0,8,dt);return false;}
        double floor=area.Bottom-Height;
        climbAmount=Damp(climbAmount,state=="climb"?1:state=="climbDown"?descentPitchTarget:0,5,dt);
        if(state!="tipBack") topHang=Damp(topHang,state=="climbTop"?1:0,5,dt);
        descentHang=Damp(descentHang,state=="climbDown"?1:0,5,dt);
        if((AnyAssistantWorking()||reducedMotion||!climbAndTumble)&&(state=="climbApproach"||state=="climb"||state=="climbTop")) SafeDescent();
        if(!reducedMotion&&climbAndTumble&&!AnyAssistantWorking()&&!topExit&&(state=="climb"||state=="climbTop"||state=="climbDown")&&floor-py>20) {
            slipRemaining-=dt;
            if(slipRemaining<=0) SlipFromEdge();
        }
        if(state=="climbApproach") {
            double dx=targetX-px;
            double facing=climbSide*(Math.Abs(dx)<20?2.65:1.15);
            // Turn toward the trip before moving, then face the wall before climbing.
            double alignment=Math.Max(0,1-Math.Abs(AngleDifference(yaw,facing))/.8);
            double step=FlipperStep(Math.Sign(dx)*Math.Min(52,Math.Abs(dx)*3)*alignment,dt);
            velocity=dt>0?step/dt:0;px+=step;
            if(Math.Abs(dx)<2&&Math.Abs(AngleDifference(yaw,climbSide*2.65))<.12) {
                velocity=0;SetState("climb");
                climbPeakY=area.Top-modelBounds.Top+12;
            }
        } else if(state=="climb"||state=="climbDown") {
            double edge=climbSide<0?area.Left-modelBounds.Left+10:area.Right-modelBounds.Right-10;
            px=Damp(px,edge,5,dt);velocity=0;
            // Pause while turning, then descend nose first rather than backing down.
            if(state=="climb"||(Math.Abs(climbAmount-descentPitchTarget)<.06&&descentHang>.94)) py+=FlipperStep(state=="climb"?-38:52,dt);
            climbPeakY=area.Top-modelBounds.Top+12;
            if(state=="climb"&&py<=climbPeakY+1) {
                topDestinationSide=-climbSide;topExit=false;SetState("climbTop");
            } else if(state=="climbDown"&&py>=floor) {py=floor;StartRecovery(false);}
        } else if(state=="climbTop") {
            double edge=topDestinationSide<0?area.Left-modelBounds.Left+10:area.Right-modelBounds.Right-10;
            double dx=edge-px,facing=-topDestinationSide*1.15;
            double alignment=Math.Max(0,1-Math.Abs(AngleDifference(yaw,facing))/.8);
            double step=FlipperStep(Math.Sign(dx)*Math.Min(48,Math.Abs(dx)*3)*alignment*topHang,dt);
            velocity=dt>0?step/dt:0;px+=step;
            py=Damp(py,area.Top-modelBounds.Top-8,8,dt);
            if(Math.Abs(dx)<2) {climbSide=topDestinationSide;velocity=0;BeginDescentPose();SetState("climbDown");}
        } else if(state=="tipBack") {
            // First peel away from the wall and tip fully onto the shell at the same height.
            double tip=Ease(stateTime/.55);
            backPitch=tipStartPitch+(Math.PI-tipStartPitch)*tip;climbAmount=0;
            px=tipStartX-climbSide*32*tip;py=fallStartY;
            if(stateTime>=.55) {backPitch=Math.PI;vy=40;velocity=-climbSide*85;SetState("tumble");}
        } else if(state=="tumble") {
            double elapsed=Math.Min(stateTime,tumbleDuration);
            // Then drop in that fixed back-down orientation. No airborne roll or extra turns.
            py=fallStartY+40*elapsed+425*elapsed*elapsed;vy=40+850*elapsed;
            px+=velocity*dt;velocity*=Math.Exp(-.7*dt);
            climbAmount=0;backPitch=Math.PI;
            if(stateTime>=tumbleDuration||py>=floor) {
                py=floor;backPitch=Math.PI;climbAmount=0;velocity=vy=0;SetState("onBack");
                StartLandingDust(Math.Max(0,floor-fallStartY));
                Notify("Oops! Click me to help me up");
            }
        } else if(state=="onBack") {py=floor;backPitch=Math.PI;velocity=0;}
        else if(state=="helpUp"||state=="recover") {
            double t=Ease(stateTime/1.3);px=recoverStartX+(recoverTargetX-recoverStartX)*t;py=floor;
            // Hold the backward orientation; the recovery rotates around the long
            // head-to-tail axis instead of reversing the nose-over-tail fall.
            backPitch=state=="helpUp"?recoverStartPitch:0;
            if(stateTime>=1.3) {
                bool helped=state=="helpUp";climbAmount=backPitch=0;px=recoverTargetX;
                if(helped) {yaw+=Math.PI;restYaw=yaw;fallFacing=yaw;}
                SetState(helped?"happy":"idle");nextAction=6;nextClimb=time+60+random.NextDouble()*90;
            }
        }
        return true;
    }
    V AdventurePose(V v,int part) {
        if(part>=10) return v;
        // Tuck the taller head as he tips, so his shell can rest flat on the ground.
        double recovery=state=="helpUp"?Ease(stateTime/1.3):0;
        if(part==1) v.z-=(float)(headTuck*Ease(backPitch/Math.PI)*(1-recovery));
        v=PitchAround(v,-.10f,.20f,climbAmount*Math.PI*.45+backPitch);
        if(state=="helpUp") {
            double angle=Math.PI*recovery;
            float y=v.y,z=v.z-.20f;
            v.y=(float)(y*Math.Cos(angle)-z*Math.Sin(angle));
            v.z=.20f+(float)(y*Math.Sin(angle)+z*Math.Cos(angle));
            // End at the normal model origin, facing the other way after the roll.
            v.x+=(float)(.20*recovery);
        }
        return v;
    }
    void StartLandingDust(double distance) {
        // Measure the actual drop in turtle lengths so different pet sizes feel alike.
        landingDustStrength=Math.Max(0,Math.Min(1,distance/Math.Max(1,petSize*3.0)));
        landingDustCount=6+(int)Math.Round(landingDustStrength*12);
        landingDustDuration=.38+landingDustStrength*.47;
        landingDust=reducedMotion?0:landingDustDuration;
    }
    void DrawShellGround(Graphics g,float cx,float floor,float scale) {
        // Project the shadow onto the shell's actual support plane instead of placing
        // a screen-space oval below the entire turtle (which looked like levitation).
        PointF[] outline=new PointF[32];
        for(int i=0;i<outline.Length;i++) {
            double angle=i*Math.PI*2/outline.Length;
            V p=Camera(new V(shellGroundX+(float)Math.Cos(angle)*shellShadowX,(float)Math.Sin(angle)*shellShadowY,shellGroundZ));
            outline[i]=new PointF(cx+p.x*scale,floor-p.y*scale);
        }
        using(var brush=new SolidBrush(Color.FromArgb(73,85,71))) g.FillPolygon(brush,outline);
        if(landingDust<=0||reducedMotion) return;
        double t=1-landingDust/landingDustDuration;
        double strength=landingDustStrength;
        for(int i=0;i<landingDustCount;i++) {
            double angle=i*Math.PI*2/landingDustCount;
            double travel=t*(.16+strength*.32)*(1+(i%3)*.08);
            double rise=Math.Sin(t*Math.PI)*(.025+strength*.105)*(1+(i%2)*.2);
            V p=Camera(new V(.0375f+(float)(Math.Cos(angle)*(.30+travel)),(float)(Math.Sin(angle)*(.27+travel*.8)),.025f+(float)rise));
            float size=(float)((1-t)*(3+strength*7)+1)*scale/190;
            using(var brush=new SolidBrush(i%2==0?Color.FromArgb(191,179,139):Color.FromArgb(148,147,113)))
            {
                float x=cx+p.x*scale,y=floor-p.y*scale;
                g.FillRectangle(brush,x-size/2,y-size/2,size,size);
                if(strength>.45) g.FillRectangle(brush,x-size*.8f,y-size*.15f,size*1.6f,size*.55f);
            }
        }
    }
    void DrawAdventureEmotion(Graphics g) {
        if(state=="tumble"||state=="tipBack") {
            // Pixel surprise/sweat marks stay upright during the backward tip and drop.
            int x=Math.Min(Width-34,(int)modelBounds.Right+13);
            int y=Math.Max(40,(int)modelBounds.Top+18);
            using(var dark=new SolidBrush(Color.FromArgb(41,53,43))) g.FillRectangle(dark,x-9,y-16,20,31);
            using(var gold=new SolidBrush(Color.FromArgb(250,222,132))) {g.FillRectangle(gold,x-2,y-11,5,15);g.FillRectangle(gold,x-2,y+8,5,5);}
            int drift=reducedMotion?0:(int)(Math.Sin(stateTime*6)*3);
            using(var blue=new SolidBrush(Color.FromArgb(127,204,226))) {
                g.FillPolygon(blue,new Point[]{new Point(x-20,y+drift),new Point(x-25,y+8+drift),new Point(x-25,y+13+drift),new Point(x-20,y+16+drift),new Point(x-15,y+13+drift),new Point(x-15,y+8+drift)});
            }
            using(var line=new Pen(Color.FromArgb(190,202,169),2)) {
                int left=Math.Max(6,(int)modelBounds.Left-10),middle=(int)(modelBounds.Top+modelBounds.Height*.5);
                g.DrawLine(line,left-4,middle-12,left-4,middle+3);
                g.DrawLine(line,left+2,middle-4,left+2,middle+14);
            }
        } else if(state=="onBack") {
            float cx=modelBounds.Left+modelBounds.Width*.5f,cy=modelBounds.Top+8;
            for(int i=0;i<3;i++) {
                double angle=i*Math.PI*2/3+(reducedMotion?0:stateTime*1.8);
                float x=cx+(float)Math.Cos(angle)*35,y=cy+(float)Math.Sin(angle)*9;
                PointF[] star={new PointF(x,y-7),new PointF(x+2,y-2),new PointF(x+7,y),new PointF(x+2,y+2),new PointF(x,y+7),new PointF(x-2,y+2),new PointF(x-7,y),new PointF(x-2,y-2)};
                using(var brush=new SolidBrush(i==1?Color.FromArgb(247,185,115):Color.FromArgb(246,221,133))) g.FillPolygon(brush,star);
            }
        }
    }
}
