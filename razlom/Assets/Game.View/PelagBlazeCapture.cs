using System.Collections;
using System.Globalization;
using System.IO;
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed class PelagBlazeCapture : MonoBehaviour
    {
        private StreamWriter _writer;
        public void Initialize(string directory)
        {
            _writer=new StreamWriter(Path.Combine(directory,"blaze.csv"));
            _writer.WriteLine("time,tick,casting,active,start,ignite,end,gesture,bottle,streamLength,x,z");
            StartCoroutine(Record());
        }
        private IEnumerator Record()
        {
            var wait=new WaitForEndOfFrame();
            while(true)
            {
                yield return wait;
                var driver=FindAnyObjectByType<TickDriver>();var view=FindAnyObjectByType<PelagBlazeView>();
                var sim=driver!=null?driver.Sim:null;if(sim==null || view==null)continue;
                _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0:F6},{1},{2},{3},{4},{5},{6},{7:F6},{8},{9:F6},{10:F6},{11:F6}",
                    Time.time,sim.Tick,sim.BlazeCasting,sim.BlazeActive,sim.BlazeStartTick,sim.BlazeIgniteTick,sim.BlazeEndTick,
                    view.GestureTime,view.BottleVisible,view.PourStreamLength,sim.Entities.Position[0].X.ToFloat(),sim.Entities.Position[0].Y.ToFloat()));
                _writer.Flush();
            }
        }
        private void OnDestroy(){_writer?.Dispose();}
    }
}
