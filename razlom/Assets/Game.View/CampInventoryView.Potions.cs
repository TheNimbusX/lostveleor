using Game.Sim;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed partial class CampInventoryView
    {
        readonly Text[] _stockLabels=new Text[4];
        void RefreshPotionStock()
        {
            var camp=_driver.Session.Camp;
            for(int i=0;i<4;i++)
            {
                var kind=(PotionKind)i;
                if(_stockLabels[i]==null)
                {
                    var tile=new GameObject("Potion stock "+kind,typeof(RectTransform),typeof(Image),typeof(Button));
                    tile.transform.SetParent(_root.transform,false);var rect=(RectTransform)tile.transform;
                    rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.pivot=new Vector2(.5f,0);
                    rect.anchoredPosition=new Vector2(-100+i*190,10);rect.sizeDelta=new Vector2(180,42);
                    tile.GetComponent<Image>().color=new Color(.19f,.24f,.20f,.96f);
                    tile.GetComponent<Button>().onClick.AddListener(()=>{camp.SelectPotion(kind);RefreshPotionStock();});
                    var label=new GameObject("Label",typeof(RectTransform),typeof(Text));label.transform.SetParent(tile.transform,false);
                    var lr=(RectTransform)label.transform;lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.sizeDelta=Vector2.zero;
                    var text=label.GetComponent<Text>();text.font=GameTypography.Regular;text.fontSize=15;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;_stockLabels[i]=text;
                }
                bool selected=camp.SelectedPotion(i/2)==kind;
                _stockLabels[i].text=(i<2?"Здоровье ":"Лавидий ")+Camp.PotionPercent(kind)+"% · "+camp.PotionCount(kind)+(selected?" ✓":"");
                _stockLabels[i].color=selected?new Color(1f,.87f,.55f):new Color(.86f,.87f,.8f);
            }
        }
    }
}
