using Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Game.EditorTools.CampPolishUiBuilder;
using Role=Game.View.UiTheme.Role;
namespace Game.EditorTools
{
    public static class CampForgeUiBuilder
    {
        public static void Build()
        {
            const string path="Assets/Resources/UI/Prefabs/CampForge.prefab";
            if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)
            {
                var existing=PrefabUtility.LoadPrefabContents(path);
                try{Upgrade(existing);PrefabUtility.SaveAsPrefabAsset(existing,path);}
                finally{PrefabUtility.UnloadPrefabContents(existing);}
                CampChoiceUiAuthoring.Build();return;
            }
            var card=Canvas("CampForge",out var root,out var group,1660,920);var panel=root.AddComponent<CampForgePanel>();panel.Group=group;
            Text(card,"Заголовок","Кузница Эни",42,24,1560,62,40);
            Text(card,"Цель","Цель",42,110,470,40,28);Text(card,"Действие","Действие",558,110,484,40,28);Text(card,"Результат","Результат",1090,110,514,40,28);
            panel.Before=Text(card,"Вещь до","",42,170,470,278,25);
            panel.After=Text(card,"Вещь после","",1090,170,514,278,25,Role.Accent);
            panel.Changes=Text(card,"Характеристики","",1090,474,514,238,21);
            panel.Operations=new Button[4];panel.OperationLabels=new TMPro.TMP_Text[4];
            for(int i=0;i<4;i++)panel.Operations[i]=Button(card,"Операция "+i,558+i%2*250,170+i/2*74,234,62,out panel.OperationLabels[i]);
            panel.Affixes=new Button[4];panel.AffixLabels=new TMPro.TMP_Text[4];
            for(int i=0;i<4;i++)panel.Affixes[i]=Button(card,"Свойство цели "+i,42,484+i*54,470,46,out panel.AffixLabels[i],font:20);
            panel.Options=new Button[3];panel.OptionLabels=new TMPro.TMP_Text[3];
            for(int i=0;i<3;i++)panel.Options[i]=Button(card,"Предложение "+i,558,340+i*64,484,54,out panel.OptionLabels[i],font:20);
            panel.DonorCaption=Text(card,"Доноры","",558,320,484,30,19,Role.TextMuted);
            panel.Donors=new Button[6];panel.DonorLabels=new TMPro.TMP_Text[6];
            for(int i=0;i<6;i++)panel.Donors[i]=Button(card,"Донор "+i,558,356+i*42,484,36,out panel.DonorLabels[i],font:18);
            panel.DonorAffixes=new Button[4];panel.DonorAffixLabels=new TMPro.TMP_Text[4];
            for(int i=0;i<4;i++)panel.DonorAffixes[i]=Button(card,"Свойство донора "+i,558,640+i*40,484,34,out panel.DonorAffixLabels[i],font:18);
            panel.PreviousDonors=Button(card,"Назад по донорам",558,608,232,30,out _,font:15);
            panel.NextDonors=Button(card,"Далее по донорам",808,608,234,30,out _,font:15);
            panel.Price=Text(card,"Цена","",1090,738,514,96,23);
            panel.Status=Text(card,"Причина","",42,752,1000,60,21,Role.TextMuted);
            panel.Back=Button(card,"Назад",42,842,250,54,out _);
            panel.Confirm=Button(card,"Подтвердить",1090,842,514,54,out _,true,26);
            Upgrade(root);root.SetActive(false);try{PrefabUtility.SaveAsPrefabAsset(root,path);}finally{Object.DestroyImmediate(root);}
            CampChoiceUiAuthoring.Build();
        }
        static void Upgrade(GameObject root)
        {
            var p=root.GetComponent<CampForgePanel>();if(p.LayoutVersion>=2)return;
            var card=(RectTransform)root.transform.Find("Окно");
            p.BeforeItem=ItemCell(card,"Цель: иконка",42,162,100);
            p.AfterItem=ItemCell(card,"Результат: иконка",1090,162,100);
            Place(p.Before,160,168,352,88);Place(p.After,1208,168,388,88);
            p.After.GetComponent<ThemeColor>()?.SetRole(Role.Text);
            p.BeforeProperties=Text(card,"Свойства цели","",42,286,470,144,23);
            p.AfterProperties=Text(card,"Изменённые свойства","",1090,286,514,144,23);
            p.TargetAffixCaption=Text(card,"Выбор свойства","Выбери свойство",42,432,470,30,18,Role.TextMuted);
            for(int i=0;i<p.Affixes.Length;i++)UiKitBuilder.TopLeft((RectTransform)p.Affixes[i].transform,42,474+i*54,470,46);
            Place(p.Changes,1090,452,514,170);
            p.DonorItem=ItemCell(card,"Донор: иконка",1090,632,58);
            p.DonorInfo=Text(card,"Выбранный донор","",1164,635,440,60,18,Role.TextMuted);
            Place(p.Price,1090,708,514,112);
            p.LayoutVersion=2;CampChoiceFeedback.Install(root);
        }
        static void Place(TMPro.TMP_Text text,float x,float y,float w,float h)
            =>UiKitBuilder.TopLeft((RectTransform)text.transform.parent,x,y,w,h);
        static CampShopCell ItemCell(RectTransform parent,string name,float x,float y,float size)
        {
            var rect=UiInkKit.Cell(parent,name,size,true);UiKitBuilder.TopLeft(rect,x,y,size,size);
            rect.Find("Предмет").gameObject.SetActive(false);
            var cell=rect.gameObject.AddComponent<CampShopCell>();
            var art=UiKitBuilder.Stretch(UiKitBuilder.Node("Иконка",rect),size*.1f);art.SetSiblingIndex(rect.Find("Рамка").GetSiblingIndex());
            cell.Icon=art.gameObject.AddComponent<Image>();cell.Icon.preserveAspect=true;cell.Icon.raycastTarget=false;cell.Icon.material=UiInkKit.Art;
            UiInkKit.Inked(cell.Icon,delay:.12f);cell.Rarity=rect.GetComponent<WcRarity>();cell.State=rect.GetComponent<WcSlotState>();
            rect.Find("Ловец").GetComponent<Image>().raycastTarget=false;return cell;
        }
    }
}
