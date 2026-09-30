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
    public void UpperRingBodyContactDoesNotForceTheAttachedChainToStretch()
    {
        // Captured ordinary-attack -> new back-mounted anchor draw, at 0.0667 s.
        var bottom = new Vector3(22.590550f, .870703f, -1.993831f);
        var top = new Vector3(22.622040f, 1.237718f, -1.982745f);
        var ring = new Vector3(22.575390f, .886861f, -2.041343f);
        var grip = new Vector3(22.710360f, .924587f, -1.511662f);
        var body = new AnchorHeadDynamics { EyeLocal = new Vector3(0, .405f, 0) };
        body.Reset(ring - body.EyeLocal, Quaternion.Identity);
        body.Velocity = new Vector3(2, 0, 2);
        var rotation = body.Rotation;
        body.CollideEyeBody(bottom, top, .22f);
        var axis = top - bottom;
        var center = bottom + axis * Math.Clamp(Vector3.Dot(body.Eye - bottom, axis) / axis.LengthSquared(), 0, 1);
        Assert.That(Vector3.Distance(body.Eye, center), Is.GreaterThanOrEqualTo(.2199f));
        Assert.That(Quaternion.Dot(rotation, body.Rotation), Is.LessThan(.9999f), "Contact at the ring must turn the mass.");
        var chain = new AnchorChainSolver();
        chain.Advance(grip, body.Eye, .920370f, 0, bottom, top, .2f, true);
        Assert.That(chain.MaxStrain, Is.LessThanOrEqualTo(.02f));
        Assert.That(chain.AttachmentError, Is.Zero);
    }

    [Test]
    public void BackDrawStockAllowsChainToRouteAroundTorsoWithBothEndsOutside()
    {
        // Following captured frame: the ring is outside, but the straight line
        // crosses the torso. Constant stock must cover the curved route too.
        var bottom = new Vector3(22.600950f, .872362f, -1.992010f);
        var top = new Vector3(22.607580f, 1.239762f, -1.961046f);
        var grip = new Vector3(22.515210f, 1.037196f, -1.445144f);
        var ring = new Vector3(22.622580f, 1.009935f, -2.200665f);
        var chain = new AnchorChainSolver();
        chain.Advance(grip, ring, 1.284921f, 0, bottom, top, .2f, true);
        Assert.That(chain.MaxStrain, Is.LessThanOrEqualTo(.02f));
        Assert.That(chain.AttachmentError, Is.Zero);
        for (int i = 1; i < chain.Count - 1; i++)
        {
            var axis = top - bottom;
            var center = bottom + axis * Math.Clamp(Vector3.Dot(chain[i] - bottom, axis) / axis.LengthSquared(), 0, 1);
            Assert.That(Vector3.Distance(chain[i], center), Is.GreaterThanOrEqualTo(.1999f));
        }
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
