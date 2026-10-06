using System;
using Game.Sim;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>
    /// Атлас артефактов (06.10): артефакт, взятый в реальном забеге, открыт навсегда —
    /// «Открыто N из 8». Основ в атласе больше нет. Здесь хранение на стороне лагеря и
    /// открытие из забега через GameSession: реальный открывает, тестовый — нет.
    /// </summary>
    public class CampAtlasTests
    {
        [Test] public void OpenedArtifactsSurviveSave()
        {
            var camp=PrototypeContent.NewCamp();
            camp.OpenArtifact(RunArtifacts.At(0));camp.OpenArtifact(RunArtifacts.At(RunArtifacts.Count-1));
            var save=CampSaveCodec.Encode(camp);
            var back=CampSaveCodec.Decode(save,camp.Items);
            Assert.True(back.ArtifactOpened(RunArtifacts.At(0)));Assert.True(back.ArtifactOpened(RunArtifacts.At(RunArtifacts.Count-1)));
            Assert.False(back.ArtifactOpened(RunArtifacts.At(3)));
            CollectionAssert.AreEqual(save,CampSaveCodec.Encode(back));
        }

        /// <summary>Бит снятого или несуществующего артефакта обрезается, а не роняет загрузку.</summary>
        [Test] public void InvalidArtifactBitsAreMaskedOnLoad()
        {
            var camp=PrototypeContent.NewCamp();camp.OpenArtifact(RunArtifacts.At(1));
            var bytes=CampSaveCodec.Encode(camp);
            var collection=CampSaveFile.Payload(bytes,CampSaveFile.CollectionTag);
            int at=collection.Length-4;uint mask=BitConverter.ToUInt32(collection,at)|0xFFFFFF00u;
            Array.Copy(BitConverter.GetBytes(mask),0,collection,at,4);
            var back=CampSaveCodec.Decode(CampSaveFile.Replace(bytes,CampSaveFile.CollectionTag,collection),camp.Items);
            for(int i=0;i<RunArtifacts.Count;i++)Assert.AreEqual(i==1,back.ArtifactOpened(RunArtifacts.At(i)));
            CollectionAssert.AreEqual(bytes,CampSaveCodec.Encode(back));
        }

        [Test] public void OpenArtifactIgnoresInvalidAndIsIdempotent()
        {
            var camp=PrototypeContent.NewCamp();var before=CampSaveCodec.Encode(camp);
            camp.OpenArtifact(RunArtifact.None);camp.OpenArtifact((RunArtifact)1);camp.OpenArtifact((RunArtifact)200);
            CollectionAssert.AreEqual(before,CampSaveCodec.Encode(camp));
            Assert.False(camp.ArtifactOpened(RunArtifact.None));Assert.False(camp.ArtifactOpened((RunArtifact)200));
            camp.OpenArtifact(RunArtifacts.At(2));var once=CampSaveCodec.Encode(camp);
            camp.OpenArtifact(RunArtifacts.At(2));CollectionAssert.AreEqual(once,CampSaveCodec.Encode(camp));
        }

        static InputFrame Command(RunCommand command)=>new InputFrame{Command=(byte)command};

        /// <summary>Снимает врагов (волны тоже) и ставит героя у выхода: экран награды.</summary>
        static void ReachReward(GameSession session)
        {
            RiftRun run=session.Run;
            for(int guard=0;guard<4000&&run.Phase==RunPhase.Clearing;guard++)
            {
                EntityStore e=run.Sim.Entities;
                for(int i=0;i<e.Count;i++)if(e.Side[i]!=Faction.Wole)e.Alive[i]=false;
                session.Step(InputFrame.Empty);
            }
            Assert.AreEqual(RunPhase.SeekingExit,run.Phase);
            run.Sim.Entities.Position[Simulation.PlayerId]=run.Map.ExitPoint(0);
            session.Step(InputFrame.Empty);
            Assert.AreEqual(RunPhase.ChoosingReward,run.Phase);
        }

        /// <summary>
        /// Восемь простых арен и босс (FormBaselineScenarios) до экрана артефакта. Босс — временный
        /// Хранитель: без вступления его снимает та же правка Alive.
        /// </summary>
        static GameSession AtBossArtifact(Camp camp,ulong seed,bool developer)
        {
            LocationDefinition location=FormBaselineScenarios.Location(true);
            var session=new GameSession(seed,camp,location.Modules,PrototypeContent.ItemBaseIds(),location:location);
            if(developer)session.StartDeveloperRift(location,1,false,seed);else session.EnterRift();
            Assert.AreEqual(developer,session.IsDeveloperRun);
            for(int depth=1;depth<=FormBaselineScenarios.ArenaCount;depth++)
            {
                ReachReward(session);
                session.Step(FormBaselineScenarios.Choice(0));
                if(session.Run.Phase==RunPhase.ReplacingAbility)session.Step(Command(RunCommand.ReplaceSlot2));
                Assert.AreEqual(RunPhase.ChoosingRoute,session.Run.Phase);
                if(depth==FormBaselineScenarios.ArenaCount)session.Run.Sim.ThicketMasterBossEnabled=false;
                session.Step(Command(RunCommand.ChooseRoute1));
            }
            ReachReward(session);
            Assert.True(session.Run.ChoosingArtifact);
            return session;
        }

        [Test] public void ArtifactOpensWhenTakenInRealRun()
        {
            var camp=PrototypeContent.NewCamp();Assert.AreEqual(0,camp.OpenedArtifactCount);
            var session=AtBossArtifact(camp,23,false);
            RunArtifact chosen=session.Run.GetOffer(1).Artifact;Assert.True(RunArtifacts.IsValid(chosen));
            Assert.False(camp.ArtifactOpened(chosen),"предложение ещё ничего не открывает");
            session.Step(FormBaselineScenarios.Choice(1));
            Assert.True(camp.ArtifactOpened(chosen));Assert.AreEqual(1,camp.OpenedArtifactCount);
            for(int i=0;i<RunArtifacts.Count;i++)if(RunArtifacts.At(i)!=chosen)Assert.False(camp.ArtifactOpened(RunArtifacts.At(i)));
            // Открытие переживает сохранение — атлас «Открыто 1 из 8» и после перезагрузки.
            Assert.AreEqual(1,CampSaveCodec.Decode(CampSaveCodec.Encode(camp),camp.Items).OpenedArtifactCount);
        }

        [Test] public void DeveloperRunOpensNothing()
        {
            var camp=PrototypeContent.NewCamp();
            var session=AtBossArtifact(camp,23,true);
            RunArtifact chosen=session.Run.GetOffer(0).Artifact;Assert.True(RunArtifacts.IsValid(chosen));
            session.Step(FormBaselineScenarios.Choice(0));
            Assert.AreEqual(0,camp.OpenedArtifactCount,"тестовый забег атлас не открывает");
            for(int i=0;i<RunArtifacts.Count;i++)Assert.False(camp.ArtifactOpened(RunArtifacts.At(i)));
        }
    }
}
