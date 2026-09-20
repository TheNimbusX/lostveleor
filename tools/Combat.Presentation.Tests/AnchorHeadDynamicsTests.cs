using System;
using System.Numerics;
using Game.View;
using NUnit.Framework;

public sealed class AnchorHeadDynamicsTests
{
    [Test]
    public void ForceAtRingRotatesMassWhileCenterImpulseDoesNot()
    {
        var a=new AnchorHeadDynamics { EyeLocal=new Vector3(0,.4f,0) };
        var b=new AnchorHeadDynamics { EyeLocal=a.EyeLocal };
        a.Impulse(new Vector3(12,0,0),a.Eye);
        b.Impulse(new Vector3(12,0,0),b.Position);
        Assert.That(a.Velocity.X,Is.EqualTo(1).Within(.00001f));
        Assert.That(a.AngularVelocity.Z,Is.LessThan(-1));
        Assert.That(b.AngularVelocity.Length(),Is.Zero);
    }

    [TestCase(30)] [TestCase(60)] [TestCase(120)]
    public void UnforcedFlightKeepsMomentumAndBallisticContact(int fps)
    {
        var body=new AnchorHeadDynamics();
        Vector3 origin=new Vector3(0,2,0),target=new Vector3(0,.5f,4),gravity=new Vector3(0,-30,0);
        const float duration=.18f;
        body.Reset(origin,Quaternion.Identity);
        body.Velocity=(target-origin)/duration-gravity*(duration*.5f);
        float time=0;
        while(time<duration-.000001f)
        {
            float frame=Math.Min(1f/fps,duration-time);
            while(frame>.000001f){float dt=Math.Min(1f/240,frame);body.Advance(dt,gravity);frame-=dt;time+=dt;}
        }
        Assert.That(Vector3.Distance(body.Position,target),Is.LessThan(.00002f));
    }

    [Test]
    public void TautCableTransfersMomentumAtEyeAndPreservesReach()
    {
        var body=new AnchorHeadDynamics { EyeLocal=new Vector3(0,.4f,0) };
        body.Reset(new Vector3(0,0,2),Quaternion.Identity);
        body.Velocity=new Vector3(0,0,5);
        body.ConstrainCable(Vector3.Zero,Vector3.Zero,1.5f,0,1f/240);
        Assert.That(body.Eye.Length(),Is.LessThanOrEqualTo(1.501f));
        Assert.That(body.LastTension,Is.GreaterThan(0));
        Assert.That(body.AngularVelocity.Length(),Is.GreaterThan(.1f));
    }

    [Test]
    public void SlackCableDoesNotMoveOrSpinHead()
    {
        var body=new AnchorHeadDynamics { EyeLocal=new Vector3(0,.4f,0) };
        body.Reset(new Vector3(0,1,1),Quaternion.Identity);
        var initial=body.Position;
        body.ConstrainCable(Vector3.Zero,Vector3.Zero,4,0,1f/240);
        Assert.That(body.Position,Is.EqualTo(initial));
        Assert.That(body.LastTension,Is.Zero);
    }

    [Test]
    public void HeavyHeadSettlesAboveGroundWithoutGainingEnergy()
    {
        var body=new AnchorHeadDynamics { Hull=new[]{new Vector3(-.3f,-.4f,-.1f),new Vector3(.3f,-.4f,.1f),new Vector3(0,.4f,0)} };
        body.Reset(new Vector3(0,2,0),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.3f));
        body.Velocity=new Vector3(2,-5,0);
        for(int i=0;i<720;i++){body.Advance(1f/240,new Vector3(0,-30,0));body.CollideGround(_=>0,1f/240);}
        foreach(var p in body.Hull)Assert.That((body.Position+Vector3.Transform(p,body.Rotation)).Y,Is.GreaterThanOrEqualTo(-.002f));
        Assert.That(body.Velocity.Length(),Is.LessThan(.3f));
        Assert.That(body.AngularVelocity.Length(),Is.LessThan(.3f));
    }

    [Test]
    public void CatchTracksMovingHolsterWithoutPositionSnap()
    {
        var body=new AnchorHeadDynamics();
        body.Reset(new Vector3(0,1,.65f),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,1));
        const float dt=1f/240;
        Vector3 target=Vector3.Zero;
        for(int i=0;i<120;i++)
        {
            target=new Vector3(i*dt*.5f,1,0);var previous=body.Position;
            body.Catch(target,new Vector3(.5f,0,0),Quaternion.Identity,dt);
            body.Advance(dt,Vector3.Zero);
            Assert.That(Vector3.Distance(previous,body.Position),Is.LessThan(.055f));
        }
        Assert.That(Vector3.Distance(body.Position,target),Is.LessThan(.01f));
        Assert.That(Math.Abs(body.Rotation.Z),Is.LessThan(.02f));
    }
}
