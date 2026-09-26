using System.Collections.Generic;
using Game.Sim;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ForestWendigoTests
    {
        private static Simulation Arena(int distance=6, bool leapReady=true)
        {
            var sim=new Simulation(55,64);sim.SetupWendigoEncounter(null,55);
            sim.Entities.Position[0]=FixVec2.Zero;
            sim.Entities.Position[1]=new FixVec2(Fix64.FromInt(distance),Fix64.Zero);
            sim.Entities.Facing[1]=new FixVec2(-Fix64.One,Fix64.Zero);
            sim.Entities.NextAttackTick[1]=0;
            sim.Entities.Stats[0].SetBase(StatType.MaxHealth,Fix64.FromInt(10000));
            sim.Entities.Stats[0].SetBase(StatType.Armor,Fix64.Zero);
            sim.Entities.RefreshStats(0);sim.Entities.Health[0]=sim.Entities.MaxHealth[0];
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            if(leapReady && distance>=3) Until(sim,Simulation.WendigoLeapCooldownTicks);
            return sim;
        }
        private static void Until(Simulation sim,int tick){while(sim.Tick<tick)sim.Step(InputFrame.Empty);}
        // Коготь 26 и прыжок 34 — таблица видов (стенд, проход 2), а не литералы.
        [TestCase(2,WendigoAction.Claw,18,26)]
        [TestCase(6,WendigoAction.Leap,33,34)]
        public void OneContactAtAuthoredTick(int distance,WendigoAction kind,int contact,int damage)
        {
            var sim=Arena(distance);int start=sim.Tick;int health=sim.Entities.Health[0];var impacts=new List<int>();
            for(int t=0;t<=contact+3;t++)
            {
                sim.Step(InputFrame.Empty);
                foreach(var e in sim.Events)if(e.Type==SimEventType.WendigoImpact)impacts.Add(t);
                if(t<contact)Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
            }
            Assert.That(impacts,Is.EqualTo(new[]{contact}));Assert.That(health-sim.Entities.Health[0],Is.EqualTo(damage));
            Assert.That(sim.TryGetWendigoAction(1,out var action),Is.True);Assert.That(action.Kind,Is.EqualTo(kind));
        }
        [Test]
        public void LeapLocksTargetAndDoesNotHitDuringFlight()
        {
            var sim=Arena();sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            var target=a.Target;sim.Entities.Position[0]=new FixVec2(Fix64.Zero,Fix64.FromInt(5));int health=sim.Entities.Health[0];
            Until(sim,a.ImpactTick+1);
            Assert.That(sim.Entities.Health[0],Is.EqualTo(health));Assert.That(sim.Entities.Position[1],Is.EqualTo(target));
        }
        [Test]
        public void ClawDoesNotTurnTowardPlayerDuringWindup()
        {
            var sim=Arena(2);sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            sim.Entities.Position[0]=new FixVec2(Fix64.FromInt(4),Fix64.Zero);int health=sim.Entities.Health[0];
            Until(sim,20);Assert.That(sim.Entities.Facing[1],Is.EqualTo(a.Direction));Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [TestCase(2,5,false)] [TestCase(2,17,true)]
        [TestCase(6,10,false)] [TestCase(6,27,false)] [TestCase(6,32,true)]
        public void CancellationRemovesFutureContact(int distance,int at,bool death)
        {
            var sim=Arena(distance);int start=sim.Tick;Until(sim,start+at);int health=sim.Entities.Health[0];
            if(death)sim.Entities.Alive[1]=false;else sim.Statuses.ApplyStun(1,1000);
            Until(sim,start+65);Assert.That(sim.Entities.Health[0],Is.EqualTo(health));Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
        }
        [Test]
        public void DeathAfterContactDoesNotReplayDamage()
        {
            var sim=Arena();int start=sim.Tick;Until(sim,start+34);int health=sim.Entities.Health[0];sim.Entities.Alive[1]=false;
            Until(sim,start+70);Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [Test]
        public void RepeatSetupClearsPendingActionsAndLeapCooldown()
        {
            var sim=Arena();Until(sim,sim.Tick+15);sim.SetupWendigoEncounter(null,55);
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            Assert.That(sim.Entities.Count,Is.EqualTo(2));
            Assert.That(sim.Entities.Health[1],Is.EqualTo(EnemyArchetypes.Get(EnemyKind.ForestWendigo).BaseHealth));
        }
        [Test]
        public void ClawCannotDamageAcrossClosedGapBetweenRooms()
        {
            var room=new ModuleDefinition("module.wendigo_wall",8,8,new ModuleConnector[0],weight:0,isEntrance:true);
            var map=new LayoutMap(new ModuleSet(new[]{room}),2);
            map.TryPlace(0,0,0,0);map.TryPlace(0,0,9,0,parent:-1);
            var sim=Arena(2);sim.SetupWendigoEncounter(map,55);
            sim.Entities.Position[1]=new FixVec2(Fix64.Ratio(158,10),Fix64.FromInt(8));
            sim.Entities.Position[0]=new FixVec2(Fix64.Ratio(182,10),Fix64.FromInt(8));
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            int health=sim.Entities.Health[0];Until(sim,20);
            Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
        }
        [Test]
        public void FirstLeapWaitsSixAndHalfSecondsAndRepeatRestartsDelay()
        {
            var sim=Arena(6,leapReady:false);
            Until(sim,195);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var first),Is.True);
            Assert.That(first.Kind,Is.EqualTo(WendigoAction.Leap));Assert.That(first.StartTick,Is.EqualTo(195));
            sim.SetupWendigoEncounter(null,55);
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(1);
            int start=sim.Tick;Until(sim,start+195);
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var repeat),Is.True);
            Assert.That(repeat.StartTick,Is.EqualTo(start+195));
        }
        [Test]
        public void ConsecutiveLeapsAreAtLeast195TicksApart()
        {
            var sim=Arena();sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var first);
            Until(sim,first.EndTick+1);
            sim.Entities.Position[0]=sim.Entities.Position[1]+new FixVec2(Fix64.FromInt(6),Fix64.Zero);
            Until(sim,first.StartTick+195);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var second),Is.True);
            Assert.That(second.Kind,Is.EqualTo(WendigoAction.Leap));
            Assert.That(second.StartTick-first.StartTick,Is.EqualTo(195));
        }
        [Test]
        public void ClawHasFreeMovementWindowAndLandsEvery45Ticks()
        {
            var sim=Arena(2);int health=sim.Entities.Health[0];
            Until(sim,31);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            Assert.That(health-sim.Entities.Health[0],Is.EqualTo(EnemyArchetypes.WendigoClawDamage));
            Assert.That(Simulation.WendigoClawCycleTicks,Is.EqualTo(45));
            Until(sim,45);Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var second),Is.True);
            Assert.That(second.StartTick,Is.EqualTo(45));
        }
        [Test]
        public void WalkSpeedIsThreeMetresAndCooldownDoesNotFreezeMovement()
        {
            var sim=new Simulation(55,64);sim.SetupWendigoEncounter(null,55);
            Assert.That(sim.Entities.MoveStep[1].ToFloat()*30,Is.EqualTo(3).Within(.002));
            sim.Entities.Position[0]=FixVec2.Zero;
            sim.Entities.Position[1]=new FixVec2(Fix64.FromInt(2),Fix64.Zero);
            Until(sim,31);var before=sim.Entities.Position[1];
            sim.Entities.Position[0]=new FixVec2(Fix64.FromInt(-4),Fix64.Zero);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[1],Is.Not.EqualTo(before));
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
        }
        // Урон Вендиго растёт вместе с листом статов: глубина, «Сложно» и
        // ярость меняют Damage, и коготь с прыжком обязаны это увидеть.
        // Раньше здесь стояли литералы 45/60 — на восьмой арене как на первой.
        [TestCase(100,26,34)]
        [TestCase(172,45,59)]
        [TestCase(125,33,43)]
        public void ClawAndLeapDamageComeFromStatsAndScale(int percent,int claw,int leap)
        {
            foreach(var distance in new[]{2,6})
            {
                var sim=Arena(distance);
                sim.Entities.Stats[1].SetBase(StatType.Damage,
                    Fix64.FromInt(EnemyArchetypes.WendigoClawDamage)*Fix64.Ratio(percent,100));
                sim.Entities.RefreshStats(1);
                Assert.That(sim.WendigoClawDamageOf(1),Is.EqualTo(claw));
                Assert.That(sim.WendigoLeapDamageOf(1),Is.EqualTo(leap));
                int health=sim.Entities.Health[0];
                Until(sim,sim.Tick+(distance==2?19:34));
                Assert.That(health-sim.Entities.Health[0],Is.EqualTo(distance==2?claw:leap),"distance "+distance);
            }
        }

        [Test]
        public void EncounterWendigoGrowsWithDepthDamageAndUsesArchetypeHealthAndBody()
        {
            var modules=PrototypeContent.Modules();var map=new LayoutMap(modules,64);
            new LayoutGenerator().Generate(modules,42,map,12);
            var wendigo=new EncounterGroup(EnemyKind.ForestWendigo,1,1,elite:true);
            var guard=new EncounterGroup(EnemyKind.ForestGuardian,1,1,elite:true);
            var pack=new EncounterPack(1,100,new[]{wendigo});
            // Девятая арена: здоровье 156%, урон 164%.
            var settings=new EncounterSettings(new[]{pack},new[]{pack},new[]{pack},
                new[]{new EncounterPack(2,100,new[]{guard,wendigo})},3,0,
                EnemyArchetypes.DepthDamagePercent(9),Fix64.FromInt(5));
            var sim=new Simulation(42);
            sim.SetupEncounters(map,42,EnemyArchetypes.DepthHealthPercent(9),settings);
            int found=0;
            for(int id=1;id<sim.Entities.Count;id++)
            {
                if(sim.Entities.Kind[id]!=EnemyKind.ForestWendigo)continue;
                found++;
                Assert.That(sim.Entities.MaxHealth[id],Is.EqualTo(3120));
                // Коготь 26 × 164% = 42,6; прыжок и вой — той же долей от когтя.
                Assert.That(sim.Entities.Damage[id],Is.EqualTo(43));
                Assert.That(sim.WendigoLeapDamageOf(id),Is.EqualTo(56));
                Assert.That(sim.WendigoHowlDamageOf(id),Is.EqualTo(50));
                Assert.That(sim.Entities.BodyRadius[id],Is.EqualTo(Simulation.WendigoBodyRadius));
                Assert.That(map.IsWalkable(sim.Entities.Position[id],sim.Entities.BodyRadius[id]),Is.True);
            }
            Assert.That(found,Is.GreaterThan(0));
        }

        [Test]
        public void SameInputsProduceSameDeterministicState()
        {
            var a=Arena();var b=Arena();
            for(int tick=0;tick<400;tick++)
            {
                a.Step(InputFrame.Empty);b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(),Is.EqualTo(b.StateHash()),"tick "+tick);
            }
        }

        // ---- походка: сначала разворот, потом шаг вдоль взгляда ----

        private static double Degrees(FixVec2 a,FixVec2 b)
        {
            double ax=a.X.ToDouble(),ay=a.Y.ToDouble(),bx=b.X.ToDouble(),by=b.Y.ToDouble();
            double dot=(ax*bx+ay*by)/(System.Math.Sqrt(ax*ax+ay*ay)*System.Math.Sqrt(bx*bx+by*by));
            return System.Math.Acos(System.Math.Max(-1.0,System.Math.Min(1.0,dot)))*180.0/System.Math.PI;
        }

        /// <summary>Вендиго на обычной скорости, без атак: только ходьба.</summary>
        private static Simulation Walker()
        {
            var sim=new Simulation(55,64);sim.SetupWendigoEncounter(null,55);
            sim.PlayerInvulnerable=true;
            sim.Entities.Position[0]=FixVec2.Zero;
            sim.Entities.Position[1]=new FixVec2(Fix64.FromInt(8),Fix64.Zero);
            sim.Entities.Facing[1]=new FixVec2(-Fix64.One,Fix64.Zero);
            sim.Entities.NextAttackTick[1]=int.MaxValue;
            return sim;
        }

        [Test]
        public void WalkVelocityStaysWithin35DegreesOfFacing()
        {
            var sim=Walker();int moving=0;FixVec2 behind=FixVec2.Zero;
            for(int t=0;t<360;t++)
            {
                // Герой бежит по кругу в 7 м, а дважды за прогон оказывается за спиной зверя.
                if(t==120||t==240)behind=sim.Entities.Position[1]-sim.Entities.Facing[1]*Fix64.FromInt(6);
                if(t%120<40&&t>=120)sim.Entities.Position[0]=behind;
                else{double angle=System.Math.PI+t*.05;sim.Entities.Position[0]=new FixVec2(Fix64.FromDouble(7*System.Math.Cos(angle)),Fix64.FromDouble(7*System.Math.Sin(angle)));}
                sim.Step(InputFrame.Empty);
                var v=sim.Entities.Velocity[1];
                if(v.LengthSq<=Fix64.Ratio(1,1000000))continue;
                moving++;
                Assert.That(Degrees(v,sim.Entities.Facing[1]),Is.LessThanOrEqualTo(35),"tick "+t);
            }
            Assert.That(moving,Is.GreaterThan(100),"Вендиго должен был ходить");
        }

        [Test]
        public void WalkWaitsForTheTurnAndRampsWithAlignment()
        {
            // Спиной к герою: пока взгляд дальше cos 0,6 (≈53°) от героя — ни шага.
            var sim=Walker();sim.Entities.Facing[1]=new FixVec2(Fix64.One,Fix64.Zero);
            int firstStep=-1;var full=sim.Entities.MoveStep[1];
            for(int t=0;t<30;t++)
            {
                var before=sim.Entities.Position[1];
                var wanted=(sim.Entities.Position[0]-before).Normalized();
                sim.Step(InputFrame.Empty);
                var dot=FixVec2.Dot(sim.Entities.Facing[1],wanted);
                var speed=(sim.Entities.Position[1]-before).Length;
                if(dot<=Simulation.WendigoWalkAlignFrom)Assert.That(speed,Is.EqualTo(Fix64.Zero),"tick "+t);
                else if(firstStep<0)firstStep=t;
                // Скорость не больше доли совпадения взгляда: max(0, dot−0,6)/0,4.
                var share=(dot-Simulation.WendigoWalkAlignFrom)/(Fix64.One-Simulation.WendigoWalkAlignFrom);
                Assert.That(speed.ToDouble(),Is.LessThanOrEqualTo(full.ToDouble()*System.Math.Max(0,share.ToDouble())+1e-4),"tick "+t);
            }
            // 180° по 12° за тик: до 48° от героя — одиннадцать тиков.
            Assert.That(firstStep,Is.EqualTo(10));
            Assert.That(sim.Entities.Velocity[1].Length.ToDouble(),Is.EqualTo(full.ToDouble()).Within(1e-3));
        }

        // ---- «Вой чащи» ----

        /// <summary>Вендиго в 4 м, прыжок перезаряжается, вой готов.</summary>
        private static Simulation HowlArena()
        {
            var sim=Arena(4,leapReady:false);
            sim.SetWendigoCooldowns(1,10000,0);
            return sim;
        }

        private static bool TelegraphOf(Simulation sim,int source,out EnemyTelegraph found)
        {
            found=default;
            for(int slot=0;slot<sim.TelegraphHighWater;slot++)
                if(sim.TryGetTelegraph(slot,out var t)&&t.Source==source&&t.Serial>found.Serial)found=t;
            return found.Serial!=0;
        }

        [Test]
        public void HowlOpensSharedRingAndStandsStillThroughRecovery()
        {
            var sim=HowlArena();
            // Может ходить — но во время воя стоит, даже если герой уходит.
            sim.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.FromInt(3));sim.Entities.RefreshStats(1);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(1,out var a),Is.True);
            Assert.That(a.Kind,Is.EqualTo(WendigoAction.Howl));
            Assert.That(a.ImpactTick-a.StartTick,Is.EqualTo(Simulation.WendigoHowlWindupTicks));
            Assert.That(a.EndTick-a.ImpactTick,Is.EqualTo(Simulation.WendigoHowlRecoveryTicks));
            Assert.That(Simulation.WendigoHowlWindupTicks,Is.EqualTo(30));
            Assert.That(Simulation.WendigoHowlRecoveryTicks,Is.EqualTo(24));
            bool started=false;
            foreach(var e in sim.Events)
                if(e.Type==SimEventType.WendigoStarted&&e.ActionVariant==(int)WendigoAction.Howl)started=true;
            Assert.That(started,Is.True);

            Assert.That(TelegraphOf(sim,1,out var ring),Is.True);
            Assert.That(ring.Shape,Is.EqualTo(TelegraphShape.Ring));
            Assert.That(ring.SharedView,Is.True,"кольцо рисует общий вид меток");
            Assert.That(ring.Origin,Is.EqualTo(a.Origin));
            Assert.That(ring.InnerRadius,Is.EqualTo(Fix64.FromInt(2)));
            Assert.That(ring.Radius,Is.EqualTo(Fix64.Ratio(11,2)));
            Assert.That(ring.ImpactTick,Is.EqualTo(a.ImpactTick));

            var origin=sim.Entities.Position[1];
            sim.Entities.Position[0]=new FixVec2(Fix64.FromInt(-5),Fix64.FromInt(3));
            while(sim.Tick<a.EndTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.Position[1],Is.EqualTo(origin),"tick "+(sim.Tick-1));
                Assert.That(sim.Entities.Facing[1],Is.EqualTo(a.Direction),"tick "+(sim.Tick-1));
            }
        }

        // Тело героя 0,45: кольцо 2–5,5 задевает его от 1,55 до 5,95 м от центра.
        [TestCase(1.45,false)] [TestCase(1.6,true)] [TestCase(4.0,true)]
        [TestCase(5.9,true)] [TestCase(6.0,false)]
        public void HowlHitsOnlyTheDrawnRingOnceAtContact(double heroDistance,bool hit)
        {
            var sim=HowlArena();int health=sim.Entities.Health[0];
            sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            // Пока кольцо заполняется, герой переходит — в дыру у ног, на кромки или наружу.
            var hero=a.Origin-new FixVec2(Fix64.FromDouble(heroDistance),Fix64.Zero);
            sim.Entities.Position[0]=hero;
            Assert.That(Simulation.TelegraphContains(Simulation.WendigoHowlRing(a.Origin),hero,sim.Entities.BodyRadius[0]),Is.EqualTo(hit));
            var contacts=new List<int>();
            while(sim.Tick<a.EndTick)
            {
                int tick=sim.Tick;sim.Step(InputFrame.Empty);
                foreach(var e in sim.Events)
                {
                    if(e.Type==SimEventType.Damage&&e.Source==1&&e.Target==0)contacts.Add(tick);
                    if(e.Type==SimEventType.WendigoImpact)Assert.That(tick,Is.EqualTo(a.ImpactTick));
                }
                if(tick<a.ImpactTick)Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
                if(tick!=a.ImpactTick)continue;
                // Метка сработала и доживает вспышку, попал вой или нет.
                Assert.That(TelegraphOf(sim,1,out var ring),Is.True);
                Assert.That(ring.State,Is.EqualTo(TelegraphState.Resolved));
                Assert.That(sim.WendigoHowlSlowTicksLeft>0,Is.EqualTo(hit));
            }
            Assert.That(contacts,Is.EqualTo(hit?new[]{a.ImpactTick}:new int[0]));
            Assert.That(health-sim.Entities.Health[0],Is.EqualTo(hit?EnemyArchetypes.WendigoHowlDamage:0));
            Assert.That(sim.WendigoHowlDamageOf(1),Is.EqualTo(30));
        }

        [Test]
        public void HowlSlowsHeroThirtyPercentForOneSecond()
        {
            var sim=HowlArena();var full=sim.Entities.MoveStep[0];
            sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            while(sim.Tick<=a.ImpactTick)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.Entities.MoveStep[0],Is.EqualTo(full),"до удара замедления нет");
            }
            Assert.That(sim.WendigoHowlSlowTicksLeft,Is.EqualTo(Simulation.WendigoHowlSlowTicks));
            int slowed=0;
            for(int k=0;k<45;k++)
            {
                sim.Step(InputFrame.Empty);
                if(sim.Entities.MoveStep[0]==full)continue;
                slowed++;
                Assert.That(sim.Entities.MoveStep[0].ToDouble(),Is.EqualTo(full.ToDouble()*.7).Within(1e-3));
            }
            // Ровно секунда: тридцать шагов героя на 70% скорости.
            Assert.That(slowed,Is.EqualTo(30));
            Assert.That(sim.WendigoHowlSlowTicksLeft,Is.Zero);
            Assert.That(sim.Entities.MoveStep[0],Is.EqualTo(full));
        }

        [Test]
        public void HowlSlowIsTheSharedHeroSlow_StrongestAndLatestWin()
        {
            var sim=HowlArena();var full=sim.Entities.MoveStep[0];
            sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            Until(sim,a.ImpactTick+1);
            Assert.That(sim.HeroSlowPercent,Is.EqualTo(Simulation.WendigoHowlSlowPercent));
            int left=sim.HeroSlowTicksLeft;
            Assert.That(sim.WendigoHowlSlowTicksLeft,Is.EqualTo(left),"прежнее имя — то же замедление");
            // Слабее, но дольше: процент остаётся сильнейшим, срок — самым поздним.
            sim.ApplyHeroSlow(20,90);
            Assert.That(sim.HeroSlowPercent,Is.EqualTo(30));
            Assert.That(sim.HeroSlowTicksLeft,Is.GreaterThan(left));
            // Сильнее и короче: корни поверх, срок не укорачивается.
            int longer=sim.HeroSlowTicksLeft;
            sim.ApplyHeroSlow(Simulation.HeroRootPercent,5);
            Assert.That(sim.HeroRooted,Is.True);
            Assert.That(sim.HeroSlowTicksLeft,Is.EqualTo(longer));
            sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.MoveStep[0],Is.EqualTo(Fix64.Zero));
            Until(sim,sim.Tick+longer);
            Assert.That(sim.HeroSlowTicksLeft,Is.Zero);
            Assert.That(sim.HeroSlowPercent,Is.Zero);
            Assert.That(sim.Entities.MoveStep[0],Is.EqualTo(full));
        }

        [Test]
        public void RootedHeroStandsButStillRolls()
        {
            var sim=new Simulation(1234,64);sim.SetupTestArena(0);new RunLoadout().ApplyTo(sim);
            sim.ApplyHeroSlow(Simulation.HeroRootPercent,60);
            var walk=InputFrame.Empty;walk.Flags=(byte)InputFlags.MoveOrder;walk.Aim=new FixVec2(Fix64.FromInt(5),Fix64.Zero);
            for(int k=0;k<10;k++)sim.Step(walk);
            Assert.That(sim.HeroRooted,Is.True);
            Assert.That(sim.Entities.Position[0],Is.EqualTo(FixVec2.Zero),"в корнях герой не идёт");
            // Кувырок — принудительное движение, скорость бега ему не нужна.
            var roll=InputFrame.Empty;roll.AbilityMask=(byte)(1<<PelagKit.DashSlot);roll.Aim=walk.Aim;
            sim.Step(roll);
            for(int k=0;k<12;k++)sim.Step(InputFrame.Empty);
            Assert.That(sim.Entities.Position[0].X.ToDouble(),Is.GreaterThan(1.0),"кувырок в корнях");
            Assert.That(sim.HeroRooted,Is.True);
        }

        [Test]
        public void HowlMissDoesNotSlow()
        {
            var sim=HowlArena();var full=sim.Entities.MoveStep[0];
            sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var a);
            sim.Entities.Position[0]=a.Origin-new FixVec2(Fix64.FromInt(7),Fix64.Zero);
            Until(sim,a.EndTick);
            Assert.That(sim.WendigoHowlSlowTicksLeft,Is.Zero);
            Assert.That(sim.Entities.MoveStep[0],Is.EqualTo(full));
        }

        // Причина: 0 — оглушение, 1 — смерть, 2 — волок.
        [TestCase(5,0)] [TestCase(29,0)] [TestCase(12,1)] [TestCase(29,1)] [TestCase(20,2)]
        public void HowlIsCancelledByStunDeathAndForcedMotion(int at,int reason)
        {
            var sim=HowlArena();int start=sim.Tick;
            sim.Step(InputFrame.Empty);Assert.That(sim.TryGetWendigoAction(1,out var a),Is.True);
            Until(sim,start+at);int health=sim.Entities.Health[0];
            if(reason==0)sim.Statuses.ApplyStun(1,sim.Tick+3);
            else if(reason==1)sim.Entities.Alive[1]=false;
            // Волок на три тика: Вендиго решает свои действия в конце тика, когда
            // однотиковый волок уже разрешён, — поэтому не на один, как у Камнекопыта.
            else ForcedMotion.Begin(sim.Entities,1,sim.Entities.Position[1],3,ForcedMotionKind.Dragged);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(1,out _),Is.False);
            Assert.That(TelegraphOf(sim,1,out var ring),Is.True);
            Assert.That(ring.State,Is.EqualTo(TelegraphState.Cancelled));
            Until(sim,start+80);
            Assert.That(sim.Entities.Health[0],Is.EqualTo(health));
            Assert.That(sim.WendigoHowlSlowTicksLeft,Is.Zero);
        }

        [Test]
        public void HowlOnlyWhileLeapCoolsDownAndEvery270Ticks()
        {
            // Прыжок готов — Вендиго прыгает, а не воет.
            var leap=Arena(4,leapReady:false);leap.SetWendigoCooldowns(1,0,0);
            leap.Step(InputFrame.Empty);leap.TryGetWendigoAction(1,out var first);
            Assert.That(first.Kind,Is.EqualTo(WendigoAction.Leap));

            // Ближе 2 м — коготь: герой уже внутри дыры кольца.
            var close=new Simulation(55,64);close.SetupWendigoEncounter(null,55);
            close.Entities.Position[0]=FixVec2.Zero;close.Entities.Position[1]=new FixVec2(Fix64.Ratio(19,10),Fix64.Zero);
            close.Entities.NextAttackTick[1]=0;close.Entities.Stats[1].SetBase(StatType.MoveSpeed,Fix64.Zero);close.Entities.RefreshStats(1);
            close.SetWendigoCooldowns(1,10000,0);close.Step(InputFrame.Empty);
            close.TryGetWendigoAction(1,out var claw);Assert.That(claw.Kind,Is.EqualTo(WendigoAction.Claw));

            // Дальше 6 м — ничего: ни воя, ни когтя.
            var far=Arena(7,leapReady:false);far.SetWendigoCooldowns(1,10000,0);far.Step(InputFrame.Empty);
            Assert.That(far.TryGetWendigoAction(1,out _),Is.False);

            // Перезарядка 270 тиков от начала воя.
            var sim=HowlArena();int start=sim.Tick;
            sim.Step(InputFrame.Empty);sim.TryGetWendigoAction(1,out var howl);
            Assert.That(howl.Kind,Is.EqualTo(WendigoAction.Howl));
            Until(sim,howl.EndTick+1);
            while(sim.Tick<start+Simulation.WendigoHowlCooldownTicks)
            {
                sim.Step(InputFrame.Empty);
                Assert.That(sim.TryGetWendigoAction(1,out _),Is.False,"tick "+(sim.Tick-1));
            }
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(1,out var again),Is.True);
            Assert.That(again.Kind,Is.EqualTo(WendigoAction.Howl));
            Assert.That(again.StartTick-howl.StartTick,Is.EqualTo(270));
        }

        [Test]
        public void FirstHowlComesAfterTheFirstLeap()
        {
            // Без подсказок: первый прыжок на 195-м тике, первый вой — не раньше 270-го.
            var sim=Arena(5,leapReady:false);
            var kinds=new List<WendigoAction>();var starts=new List<int>();int serial=0;
            while(sim.Tick<420)
            {
                sim.Step(InputFrame.Empty);
                if(!sim.TryGetWendigoAction(1,out var a)||a.Serial==serial)continue;
                serial=a.Serial;kinds.Add(a.Kind);starts.Add(a.StartTick);
                // Вендиго не двигается (скорость 0), а прыжок уносит его к герою: возвращаем на 5 м.
                if(a.Kind==WendigoAction.Leap)sim.Entities.Position[0]=a.Target+new FixVec2(-Fix64.FromInt(5),Fix64.Zero);
            }
            Assert.That(kinds.Count,Is.GreaterThanOrEqualTo(2));
            Assert.That(kinds[0],Is.EqualTo(WendigoAction.Leap));Assert.That(starts[0],Is.EqualTo(195));
            Assert.That(kinds[1],Is.EqualTo(WendigoAction.Howl));Assert.That(starts[1],Is.EqualTo(270));
        }

        [Test]
        public void HowlWaitsForTheBigToken()
        {
            // Первый Вендиго прыгает и держит крупный жетон до приземления;
            // второй готов выть, но ждёт.
            var sim=Arena(5,leapReady:false);sim.SetWendigoCooldowns(1,0,10000);
            int other=sim.SpawnEnemy(new FixVec2(Fix64.Zero,Fix64.FromInt(4)),2000,EnemyKind.ForestWendigo);
            sim.Entities.Stats[other].SetBase(StatType.MoveSpeed,Fix64.Zero);sim.Entities.RefreshStats(other);
            sim.Entities.Aggro[other]=true;sim.Entities.NextAttackTick[other]=0;
            sim.SetWendigoCooldowns(other,10000,0);
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(1,out var leap),Is.True);Assert.That(leap.Kind,Is.EqualTo(WendigoAction.Leap));
            while(sim.Tick<=leap.ImpactTick)
            {
                Assert.That(sim.TryGetWendigoAction(other,out _),Is.False,"вой поверх прыжка, тик "+(sim.Tick-1));
                sim.Step(InputFrame.Empty);
            }
            sim.Step(InputFrame.Empty);
            Assert.That(sim.TryGetWendigoAction(other,out var howl),Is.True);
            Assert.That(howl.Kind,Is.EqualTo(WendigoAction.Howl));
            Assert.That(howl.StartTick,Is.EqualTo(leap.ImpactTick+1));
        }

        [Test]
        public void HowlRunIsDeterministic()
        {
            var a=HowlArena();var b=HowlArena();
            for(int tick=0;tick<320;tick++)
            {
                a.Step(InputFrame.Empty);b.Step(InputFrame.Empty);
                Assert.That(a.StateHash(),Is.EqualTo(b.StateHash()),"tick "+tick);
            }
            Assert.That(a.Entities.Health[0],Is.LessThan(a.Entities.MaxHealth[0]),"вой должен был попасть");
        }
    }
}
