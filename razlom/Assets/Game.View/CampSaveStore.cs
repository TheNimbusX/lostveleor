using System;
using System.IO;
using Game.Sim;
using UnityEngine;
namespace Game.View
{
    public sealed class CampSaveStore : MonoBehaviour
    {
        static bool _disabled; static bool _recovered; ulong _hash; bool _hasHash; TickDriver _driver;
        ulong _potionState;
        static string PathName=>Path.Combine(Application.persistentDataPath,"camp-v1.sav");
        /// <summary>Есть ли сохранённый лагерь — главное меню решает, «Продолжить» или «Начать».</summary>
        public static bool HasSave=>File.Exists(PathName)||File.Exists(PathName+".bak");
        static bool _resetting;
        /// <summary>
        /// «Новая игра» из главного меню: стирает сохранение и резервную копию. До загрузки новой
        /// сессии запись запрещена — иначе старый лагерь записался бы обратно при перезагрузке сцены.
        /// </summary>
        public static void DeleteForNewGame()
        {
            _resetting=true;
            try{if(File.Exists(PathName))File.Delete(PathName);if(File.Exists(PathName+".bak"))File.Delete(PathName+".bak");}
            catch(Exception e){Debug.LogWarning("[camp-save] Новая игра: "+e.Message);}
        }
        static bool Capture=>Array.IndexOf(Environment.GetCommandLineArgs(),"-capture")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-capture-camp")>=0
#if UNITY_EDITOR
            || UnityEditor.SessionState.GetBool("AlchemyPlaytest.Active",false)
            || UnityEditor.SessionState.GetBool("CampIntegrationPlayCheck",false)
            || UnityEditor.SessionState.GetBool("CampServices.Check",false)
#endif
            ;
        public static GameSession Load(ulong seed, LocationDefinition location = null)
        {
            if(_resetting){_resetting=false;_disabled=false;_recovered=false;}
            _disabled=Capture || CaptureRig.AutoEnterRift;_recovered=false;Camp camp=null;
            if(!_disabled)
            {
                try {if(File.Exists(PathName))camp=CampSaveCodec.Decode(File.ReadAllBytes(PathName),PrototypeContent.Items());}
                catch(NotSupportedException e){_disabled=true;Debug.LogWarning("[camp-save] "+e.Message+"; файл сохранён без изменений");}
                catch(Exception e){Debug.LogWarning("[camp-save] Основной файл: "+e.Message);}
                if(camp==null&&!_disabled&&File.Exists(PathName+".bak"))
                {try{camp=CampSaveCodec.Decode(File.ReadAllBytes(PathName+".bak"),PrototypeContent.Items());_recovered=true;Debug.Log("[camp-save] Восстановлено из резервной копии");}catch(Exception e){_disabled=true;Debug.LogWarning("[camp-save] Резервная копия: "+e.Message);}}
                if(camp==null&&File.Exists(PathName))_disabled=true;
            }
            var modules = location?.Modules ?? PrototypeContent.Modules();
            return new GameSession(seed, camp??PrototypeContent.NewCamp(), modules,
                PrototypeContent.ItemBaseIds(), location: location);
        }
        void Start(){_driver=GetComponent<TickDriver>();}
        void LateUpdate(){Save();}
        void OnApplicationQuit(){Save();}
        void Save()
        {
            if(_disabled||_resetting||_driver?.Session==null||_driver.Session.IsDeveloperRun)return;
            var camp=_driver.Session.Camp;
            ulong potions=Hashing.Offset;
            for(int i=0;i<Camp.PotionKindCount;i++)Hashing.Mix(ref potions,camp.PotionCount((PotionKind)i));
            Hashing.Mix(ref potions,(int)camp.SelectedPotion(0));Hashing.Mix(ref potions,(int)camp.SelectedPotion(1));
            Hashing.Mix(ref potions,camp.HasMetAlchemist?1:0);
            Hashing.Mix(ref potions,(int)camp.AlchemyStatus(AlchemistOrder.Resin));
            Hashing.Mix(ref potions,(int)camp.AlchemyStatus(AlchemistOrder.Surge));
            // В бою пишем изменение расходников и заказов, а не каждую порцию опыта.
            if(_driver.Session.Mode==GameMode.Rift && _hasHash && potions==_potionState)return;
            ulong hash=0;_driver.Session.Camp.HashInto(ref hash);if(_hasHash&&hash==_hash)return;
            try
            {
                var bytes=CampSaveCodec.Encode(_driver.Session.Camp);string temp=PathName+".tmp";
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(PathName))File.Replace(temp,PathName,_recovered?null:PathName+".bak");else File.Move(temp,PathName);
                _recovered=false;_hash=hash;_hasHash=true;_potionState=potions;
            }
            catch(Exception e){_disabled=true;Debug.LogWarning("[camp-save] Запись остановлена: "+e.Message);}
        }
    }
}
