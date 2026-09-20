using System;
using System.Numerics;

namespace Game.View
{
    /// <summary>Масса оружия в представлении: силы в кольце, угловой импульс, контакт и трение.</summary>
    public sealed class AnchorHeadDynamics
    {
        public Vector3 Position, Velocity, AngularVelocity;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 EyeLocal;
        public Vector3[] Hull = Array.Empty<Vector3>();
        public float Mass = 12f;
        public Vector3 Inertia = new Vector3(.75f, .55f, .75f);
        public float LastTension { get; private set; }
        public bool Grounded { get; private set; }
        private float _restTime;
        public Vector3 Eye => Position + Vector3.Transform(EyeLocal, Rotation);

        public void Reset(Vector3 position, Quaternion rotation)
        {
            Position = position; Rotation = rotation;
            Velocity = AngularVelocity = Vector3.Zero;
            Grounded = false; LastTension = 0; _restTime = 0;
        }

        private Vector3 InverseInertia(Vector3 torque)
        {
            var local = Vector3.Transform(torque, Quaternion.Conjugate(Rotation));
            return Vector3.Transform(local / Inertia, Rotation);
        }

        public void Impulse(Vector3 impulse, Vector3 worldPoint)
        {
            Velocity += impulse / Mass;
            AngularVelocity += InverseInertia(Vector3.Cross(worldPoint - Position, impulse));
        }

        public void PullEye(Vector3 target, Vector3 targetVelocity, float frequency, float dt)
        {
            Vector3 r = Eye - Position;
            Vector3 velocity = Velocity + Vector3.Cross(AngularVelocity, r);
            // Неявный PD не взрывается при ускоренном исполнении и коротком возврате.
            float w = frequency;
            Vector3 acceleration = ((target - Eye) * (w*w) + (targetVelocity - velocity) * (2*w))
                / (1 + 2*w*dt + w*w*dt*dt);
            float length = acceleration.Length();
            if (length > 900) acceleration *= 900 / length;
            Impulse(acceleration * Mass * dt, Eye);
        }

        public void TurnTowards(Quaternion target, float frequency, float dt)
        {
            Quaternion delta = Quaternion.Normalize(target * Quaternion.Conjugate(Rotation));
            if (delta.W < 0) delta = new Quaternion(-delta.X,-delta.Y,-delta.Z,-delta.W);
            float angle = 2 * (float)Math.Acos(Math.Clamp(delta.W,-1,1));
            Vector3 axis = new Vector3(delta.X,delta.Y,delta.Z);
            if (axis.LengthSquared() > .000001f) axis = Vector3.Normalize(axis);
            AngularVelocity += (axis*angle*frequency*frequency - AngularVelocity*(2*frequency))
                * (dt/(1+2*frequency*dt+frequency*frequency*dt*dt));
        }

        public void Catch(Vector3 center, Vector3 targetVelocity, Quaternion rotation, float dt)
        {
            const float w=40f;
            Vector3 acceleration=((center-Position)*(w*w)+(targetVelocity-Velocity)*(2*w))/(1+2*w*dt+w*w*dt*dt);
            Velocity+=acceleration*dt;
            TurnTowards(rotation,35f,dt);
        }

        public void Advance(float dt, Vector3 gravity)
        {
            Position += Velocity * dt + gravity * (.5f*dt*dt);
            Velocity += gravity * dt;
            Rotate(AngularVelocity*dt);
            Grounded = false; LastTension = 0;
        }

        private void Rotate(Vector3 rotationVector)
        {
            float angle = rotationVector.Length();
            if (angle > .000001f)
                Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(rotationVector/angle,angle)*Rotation);
        }

        public void ConstrainCable(Vector3 grip, Vector3 gripVelocity, float length, float reelVelocity, float dt)
        {
            for (int pass=0;pass<5;pass++)
            {
                Vector3 r = Eye - Position, delta = Eye - grip;
                float distance = delta.Length();
                if (distance <= length || distance < .0001f) break;
                Vector3 n = delta/distance;
                Vector3 lever = Vector3.Cross(r,n);
                float inverseMass = 1/Mass + Vector3.Dot(lever,InverseInertia(lever));
                float correction = (distance-length)/inverseMass;
                Position -= n*(correction/Mass);
                Vector3 turn = -InverseInertia(lever)*correction;
                float angle = turn.Length(); if (angle>.12f) turn *= .12f/angle;
                Rotate(turn);
                float outward = Vector3.Dot(Velocity+Vector3.Cross(AngularVelocity,r)-gripVelocity,n)-reelVelocity;
                if (outward>0)
                {
                    float impulse = outward/inverseMass;
                    Impulse(-n*impulse,Eye);
                    LastTension = Math.Max(LastTension,impulse/dt);
                }
            }
        }

        public void CollideGround(Func<Vector3,float> ground, float dt)
        {
            for (int pass=0;pass<3;pass++)
            {
                Vector3 deepest = Position;float penetration = 0;
                foreach (var local in Hull)
                {
                    Vector3 p = Position+Vector3.Transform(local,Rotation);
                    float depth = ground(p)+.012f-p.Y;
                    if(depth>penetration){penetration=depth;deepest=p;}
                }
                if(penetration<=0)break;
                Grounded=true;Position += Vector3.UnitY*penetration;
                Vector3 r=deepest-Position;
                Vector3 pointVelocity=Velocity+Vector3.Cross(AngularVelocity,r);
                Vector3 lever=Vector3.Cross(r,Vector3.UnitY);
                float inverseMass=1/Mass+Vector3.Dot(lever,InverseInertia(lever));
                float impulse=Math.Max(0,-pointVelocity.Y*1.04f/inverseMass);
                Impulse(Vector3.UnitY*impulse,Position+r);
                Vector3 tangent=new Vector3(pointVelocity.X,0,pointVelocity.Z);
                float speed=tangent.Length();
                if(speed>.0001f)Impulse(-tangent/speed*Math.Min(speed*Mass,.65f*impulse),Position+r);
            }
            if(Grounded)
            {
                AngularVelocity *= (float)Math.Exp(-18*dt);Velocity *= (float)Math.Exp(-8*dt);
                // Покой контакта убирает микрокачание от последовательных импульсов углов корпуса.
                _restTime = Velocity.LengthSquared()<.64f && AngularVelocity.LengthSquared()<2.25f ? _restTime+dt : 0;
                if(_restTime>.18f)Velocity=AngularVelocity=Vector3.Zero;
            }
            else _restTime=0;
        }

        public void CollideBody(Vector3 bottom, Vector3 top, float radius)
        {
            Vector3 axis=top-bottom;
            float u=Math.Clamp(Vector3.Dot(Position-bottom,axis)/Math.Max(.00001f,axis.LengthSquared()),0,1);
            Vector3 center=bottom+axis*u,delta=Position-center;
            float distance=delta.Length();
            if(distance>=radius || distance<.00001f)return;
            Vector3 n=delta/distance;Position=center+n*radius;
            float inward=Vector3.Dot(Velocity,n);
            if(inward<0)Velocity-=n*inward;
        }
    }
}
