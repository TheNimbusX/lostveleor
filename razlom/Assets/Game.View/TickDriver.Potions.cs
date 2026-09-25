using Game.Sim;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
namespace Game.View
{
    public sealed partial class TickDriver
    {
        byte _potionLatch;CombatHudView _potionHud;
        bool _artifactLatch;

        /// <summary>
        /// Клавиша артефакта забега (F, на геймпаде — левый курок). Нажатие доживает до
        /// ближайшего тика и уходит флагом InputFlags.UseArtifact; решает симуляция.
        /// </summary>
        void CaptureArtifact()
        {
            if(Session==null || GameplayPaused || Session.Mode!=GameMode.Rift || Run==null)return;
            if(Run.Phase==RunPhase.ChoosingReward || Run.Phase==RunPhase.ReplacingAbility)return;
            bool pressed=GameKeyBindings.Pressed(GameAction.UseArtifact);
#if ENABLE_INPUT_SYSTEM
            var pad=Gamepad.current;if(pad!=null && _usingGamepad)pressed|=pad.leftTrigger.wasPressedThisFrame;
#endif
            if(pressed)_artifactLatch=true;
        }
        void CapturePotions()
        {
            if(Session==null || GameplayPaused || CampPlayerView.Instance?.InputBlocked==true)return;
            // На экране награды крестовина выбирает карточки, а не пьёт зелья.
            if(Session.Mode==GameMode.Rift && Run!=null &&
                (Run.Phase==RunPhase.ChoosingReward || Run.Phase==RunPhase.ReplacingAbility))return;
            if(_potionHud==null)_potionHud=FindAnyObjectByType<CombatHudView>();
            bool health=GameKeyBindings.Pressed(GameAction.HealthPotion),lavidium=GameKeyBindings.Pressed(GameAction.LavidiumPotion);
            bool click=false,toggle=false;Vector2 pointer=Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var pad=Gamepad.current;if(pad!=null && _usingGamepad){health|=pad.dpad.left.wasPressedThisFrame;lavidium|=pad.dpad.right.wasPressedThisFrame;}
            var mouse=Mouse.current;if(mouse!=null){click=mouse.leftButton.wasPressedThisFrame;toggle=mouse.rightButton.wasPressedThisFrame;pointer=mouse.position.ReadValue();}
#else
            click=Input.GetMouseButtonDown(0);toggle=Input.GetMouseButtonDown(1);pointer=Input.mousePosition;
#endif
            int slot=_potionHud!=null?_potionHud.PotionHit(pointer):-1;
            if(slot>=0)
            {
                if(toggle)_potionLatch^=(byte)(16<<slot);
                if(click){if(slot==0)health=true;else lavidium=true;}
            }
            if(health)_potionLatch|=Camp.PotionInputBit(Session.Camp.SelectedPotion(0));
            if(lavidium)_potionLatch|=Camp.PotionInputBit(Session.Camp.SelectedPotion(1));
        }
    }
}
