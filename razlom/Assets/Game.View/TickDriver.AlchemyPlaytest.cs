#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Game.Sim;
using UnityEngine;

namespace Game.View
{
    public sealed partial class TickDriver
    {
        public void StartAlchemyBudTrial(LocationTheme theme, ulong seed)
        {
            if (theme == null || theme.Gameplay == null)
                throw new System.ArgumentException("Для стенда алхимика нужен профиль лесной локации.");
            theme.Style.Validate();
            GetComponent<ArenaView>().PrepareForestBud();
            Session.StartAlchemyBudTrial(theme.Gameplay.ToDefinition(), seed);
            GetComponent<LayoutView>().Configure(theme);
            ClearCapturedInput();
            SyncGeneration();
            Debug.Log("[alchemy-playtest] Обычный бой с гарантированным Лесным бутоном; прогресс заказов включён.");
        }
    }
}
#endif
