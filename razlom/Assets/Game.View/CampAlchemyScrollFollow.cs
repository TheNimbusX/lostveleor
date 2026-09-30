using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class CampAlchemyScrollFollow : MonoBehaviour
    {
        GameObject _selected;ScrollRect _scroll;
        void LateUpdate()
        {
            if(!TickDriver.GamepadLastUsed || EventSystem.current==null)return;
            var selected=EventSystem.current.currentSelectedGameObject;if(selected==null || selected==_selected)return;_selected=selected;
            if(_scroll==null)_scroll=GetComponent<ScrollRect>();if(!selected.transform.IsChildOf(_scroll.content))return;
            var card=selected.GetComponentInParent<CampPotionCard>();if(card==null)return;
            var rect=(RectTransform)card.transform;
            float top=-rect.anchoredPosition.y,bottom=top+rect.rect.height;
            float y=_scroll.content.anchoredPosition.y,height=_scroll.viewport.rect.height;
            if(top<y)y=top;else if(bottom>y+height)y=bottom-height;
            _scroll.content.anchoredPosition=new Vector2(0,Mathf.Clamp(y,0,_scroll.content.rect.height-height));
        }
    }
}
