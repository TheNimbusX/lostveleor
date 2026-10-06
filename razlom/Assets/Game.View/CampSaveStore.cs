using System;
using System.IO;
using Game.Sim;
using UnityEngine;
namespace Game.View
{
    /// <summary>
    /// Сохранение лагеря v10 (план 06.10). Старые версии 1–9 не читаются: файл уходит копией
    /// «.v{N}-{время}.old», начинается новая игра. Битое — сначала резерв .bak, иначе копия
    /// «.corrupt-{время}», окно в главном меню и новая игра с работающей записью. Файл от более
    /// новой версии игры не трогаем вовсе: запись выключена до перезапуска.
    /// </summary>
    public sealed class CampSaveStore : MonoBehaviour
    {
        // Повторяют закрытые константы CampSaveCodec: меню решает «Продолжить» или «Начать»
        // до загрузки лагеря, по восьми байтам заголовка, без разбора всего файла.
        const int SaveMagic=0x43575254,SaveVersion=10;
        /// <summary>В Разломе пепел и опыт меняются на каждом убийстве: пишем не чаще, чем раз в столько секунд.</summary>
        const float RiftSaveIntervalSeconds=5f;
        static bool _disabled; static bool _recovered; ulong _hash; bool _hasHash; bool _failed; TickDriver _driver;
        ulong _stamp; bool _hasStamp; float _savedAt=float.NegativeInfinity; RunPhase _phase;
        static string PathName=>Path.Combine(Application.persistentDataPath,"camp-v1.sav");
        /// <summary>Есть ли сохранённый лагерь нынешней версии — главное меню решает, «Продолжить» или «Начать».</summary>
        public static bool HasSave=>PeekVersion(PathName)==SaveVersion||PeekVersion(PathName+".bak")==SaveVersion;
        /// <summary>Сохранение было повреждено и лагерь начат заново; главное меню показывает окно, пока его не закроют.</summary>
        public static bool CorruptNotice { get; private set; }
        /// <summary>Сохранение записано более новой версией игры: файл не тронут, запись выключена.</summary>
        public static bool FutureVersionNotice { get; private set; }
        /// <summary>Куда легла копия повреждённого файла (для окна и лога); null — копии нет.</summary>
        public static string NoticeCopyPath { get; private set; }
        /// <summary>«Понятно» в окне главного меню: предупреждение показано.</summary>
        public static void AcknowledgeNotice(){CorruptNotice=false;FutureVersionNotice=false;}
        static bool _resetting;
        /// <summary>
        /// «Новая игра» из главного меню: стирает сохранение и резервную копию. До загрузки новой
        /// сессии запись запрещена — иначе старый лагерь записался бы обратно при перезагрузке сцены.
        /// Файл от более новой версии игры не стирается (правило 06.10): после отката на старую
        /// сборку он остаётся целым, а загрузка выключит запись поверх него.
        /// </summary>
        public static void DeleteForNewGame()
        {
            _resetting=true;
            try{DeleteUnlessFuture(PathName);DeleteUnlessFuture(PathName+".bak");}
            catch(Exception e){Debug.LogWarning("[camp-save] Новая игра: "+e.Message);}
        }
        static void DeleteUnlessFuture(string path)
        {
            if(!File.Exists(path))return;
            if(PeekVersion(path)>SaveVersion){Debug.LogWarning("[camp-save] Новая игра: "+Path.GetFileName(path)+" от более новой версии игры, не тронут");return;}
            File.Delete(path);
        }
        static bool Capture=>Array.IndexOf(Environment.GetCommandLineArgs(),"-capture")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-capture-camp")>=0
#if UNITY_EDITOR
            || UnityEditor.SessionState.GetBool("AlchemyPlaytest.Active",false)
            || UnityEditor.SessionState.GetBool("CampIntegrationPlayCheck",false)
            || UnityEditor.SessionState.GetBool("CampServices.Check",false)
            || UnityEditor.SessionState.GetBool("CampBridgePolish.Check",false)
#endif
            ;
        public static GameSession Load(ulong seed, LocationDefinition location = null)
        {
            if(_resetting){_resetting=false;_disabled=false;_recovered=false;}
            _disabled=Capture || CaptureRig.AutoEnterRift;_recovered=false;Camp camp=null;
            CorruptNotice=false;FutureVersionNotice=false;NoticeCopyPath=null;
            if(!_disabled)camp=LoadOwnerCamp();
            var modules = location?.Modules ?? PrototypeContent.Modules();
            return new GameSession(seed, camp??PrototypeContent.NewCamp(), modules,
                PrototypeContent.ItemBaseIds(), location: location);
        }
        /// <summary>Лагерь владельца или null — тогда новая игра. Решает, писать ли дальше (_disabled).</summary>
        static Camp LoadOwnerCamp()
        {
            string bak=PathName+".bak",stamp=DateTime.Now.ToString("yyyyMMdd-HHmm");
            var items=PrototypeContent.Items();
            if(File.Exists(PathName))
            {
                if(!TryRead(PathName,out var bytes))return null;
                try{return CampSaveCodec.Decode(bytes,items);}
                catch(CampSaveObsoleteException e)
                {
                    // Профиль до 06.10 сбрасывается (решение владельца): прежний файл и его резерв остаются копиями.
                    Debug.LogWarning("[camp-save] "+e.Message+"; начинается новая игра");
                    if(Keep(PathName,PathName+".v"+e.Version+"-"+stamp+".old")==null)return null;
                    int bakVersion=PeekVersion(bak);
                    if(bakVersion>=1 && bakVersion<SaveVersion)Keep(bak,bak+".v"+bakVersion+"-"+stamp+".old");
                    return null;
                }
                catch(CampSaveFutureException e)
                {
                    _disabled=true;FutureVersionNotice=true;
                    Debug.LogWarning("[camp-save] "+e.Message+"; файл не тронут, запись выключена");
                    return null;
                }
                catch(Exception e){Debug.LogWarning("[camp-save] Основной файл: "+e.Message);}
            }
            if(File.Exists(bak))
            {
                if(!TryRead(bak,out var bytes))return null;
                try{var camp=CampSaveCodec.Decode(bytes,items);_recovered=true;Debug.Log("[camp-save] Восстановлено из резервной копии");return camp;}
                catch(CampSaveObsoleteException e)
                {
                    // Резерв от версии до 06.10 — не порча: молча уходит копией «.old», как и основной файл.
                    // Окно о повреждении поднимется ниже, только если битым оказался сам основной файл.
                    Debug.LogWarning("[camp-save] Резервная копия: "+e.Message);
                    if(Keep(bak,bak+".v"+e.Version+"-"+stamp+".old")==null)return null;
                }
                catch(CampSaveFutureException e)
                {
                    // Резерв от более новой версии не трогаем, как и основной файл: запись выключена.
                    _disabled=true;FutureVersionNotice=true;
                    Debug.LogWarning("[camp-save] Резервная копия: "+e.Message+"; файл не тронут, запись выключена");
                    return null;
                }
                catch(Exception e){Debug.LogWarning("[camp-save] Резервная копия: "+e.Message);}
            }
            // Нечего восстанавливать: файлов нет, или был только устаревший резерв (уже убран копией).
            if(!File.Exists(PathName) && !File.Exists(bak))return null;
            // Ни основной файл, ни резерв не читаются: оба уходят копиями, лагерь начинается заново,
            // а запись остаётся включённой — иначе игрок молча терял бы новый прогресс.
            CorruptNotice=true;
            if(File.Exists(PathName) && (NoticeCopyPath=Keep(PathName,PathName+".corrupt-"+stamp))==null)return null;
            if(File.Exists(bak)){string copy=Keep(bak,bak+".corrupt-"+stamp);if(copy==null)return null;if(NoticeCopyPath==null)NoticeCopyPath=copy;}
            return null;
        }
        static bool TryRead(string path,out byte[] bytes)
        {
            // Файл есть, но не читается (занят, нет прав): содержимое неизвестно, поэтому не перезаписываем.
            try{bytes=File.ReadAllBytes(path);return true;}
            catch(Exception e){bytes=null;_disabled=true;Debug.LogWarning("[camp-save] Не читается "+Path.GetFileName(path)+": "+e.Message+"; запись выключена");return false;}
        }
        /// <summary>
        /// Убирает файл под имя-копию переименованием: места на диске не нужно, а основное имя освобождается
        /// под новую игру. Возвращает путь копии; null — не вышло, и запись выключается, чтобы не затереть
        /// единственный экземпляр.
        /// </summary>
        static string Keep(string path,string copy)
        {
            try
            {
                if(File.Exists(copy))copy=copy+"-"+DateTime.Now.ToString("ssfff");
                File.Move(path,copy);Debug.LogWarning("[camp-save] Прежний файл сохранён как "+Path.GetFileName(copy));return copy;
            }
            catch(Exception e){_disabled=true;Debug.LogWarning("[camp-save] Не удалось убрать "+Path.GetFileName(path)+" в копию: "+e.Message+"; запись выключена");return null;}
        }
        /// <summary>Версия по заголовку файла; −1 — файла нет или это не сохранение лагеря.</summary>
        static int PeekVersion(string path)
        {
            try
            {
                if(!File.Exists(path))return -1;
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var reader=new BinaryReader(stream))
                    return stream.Length>=8 && reader.ReadInt32()==SaveMagic?reader.ReadInt32():-1;
            }
            catch(Exception){return -1;}
        }
        void Start(){_driver=GetComponent<TickDriver>();}
        void LateUpdate(){Save(false);}
        void OnApplicationQuit(){Save(true);}
        void Save(bool force)
        {
            if(_disabled||_resetting||_driver?.Session==null||_driver.Session.IsDeveloperRun)return;
            var session=_driver.Session;var camp=session.Camp;
            ulong stamp=camp.PersistStamp,hash=0;
            if(session.Mode==GameMode.Rift)
            {
                // В бою полный HashInto каждый кадр дорог: смотрим дешёвый отпечаток того, что меняет Разлом,
                // и пишем не чаще раза в 5 с, сразу — по концу зачистки арены и при выходе из игры.
                var phase=session.Run!=null?session.Run.Phase:RunPhase.Idle;
                bool clearingEnded=_phase==RunPhase.Clearing && phase!=RunPhase.Clearing;_phase=phase;
                if(_hasStamp && stamp==_stamp)return;
                if(!force && !clearingEnded && Time.unscaledTime-_savedAt<RiftSaveIntervalSeconds)return;
            }
            else
            {
                _phase=RunPhase.Idle;
                camp.HashInto(ref hash);if(_hasHash&&hash==_hash)return;
                // После сбоя записи (диск полон) хеш прежний, и лагерь писался бы каждый кадр: повтор
                // не чаще раза в 5 с, а выход из игры (force) пробует сразу.
                if(_failed && !force && Time.unscaledTime-_savedAt<RiftSaveIntervalSeconds)return;
            }
            string temp=PathName+".tmp";
            _savedAt=Time.unscaledTime;
            try
            {
                var bytes=CampSaveCodec.Encode(camp);
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(PathName))File.Replace(temp,PathName,_recovered?null:PathName+".bak");else File.Move(temp,PathName);
                _recovered=false;
            }
            catch(Exception e)
            {
                // Запись не выключаем навсегда (диск мог временно заполниться). Хеш и отпечаток не
                // запоминаем — иначе сорванная запись считалась бы сделанной, и состояние, не дошедшее
                // до файла, молча терялось бы при выходе. Повтор — через 5 с или сразу при выходе.
                Debug.LogWarning("[camp-save] Запись не удалась, повтор через "+RiftSaveIntervalSeconds+" с: "+e.Message);
                try{if(File.Exists(temp))File.Delete(temp);}catch(Exception){}
                _failed=true;return;
            }
            _failed=false;
            // В Разломе полный хеш не считался: после возвращения лагерь запишется ещё раз по полному хешу.
            _hash=hash;_hasHash=session.Mode!=GameMode.Rift;_stamp=stamp;_hasStamp=true;
        }
    }
}
