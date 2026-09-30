using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Кузнец на одной странице (владелец 29.09, кадр «Кузнец А»): сумка и надетое слева, у выбранной
    /// вещи справа две кнопки рядом — «Перековать» с ценой и «Разобрать» с выходом и «Предмет будет
    /// уничтожен». Вкладок больше нет. Перековка — по выбранной строке свойства (пределы нового
    /// значения видны в строке), разбор — только из сумки и со вторым нажатием.
    /// </summary>
    public sealed partial class CampServicesView
    {
        Camp _smithCamp;
        bool _smithWired;
        int _smithSlot=-1,_smithAffix;bool _confirmDismantle;
        readonly GeneratedItem _smithRoll=new GeneratedItem();
        void ShowSmith(bool show)
        {
            var s=_view.Smith;
            if(!show){s.Group.gameObject.SetActive(false);return;}
            _smithCamp=_driver.Session.Camp;
            WireSmith();
            // Без выбранной ячейки: Enter — кнопка «Перековать», а не Submit ячейке (он выбрал бы вещь и
            // тут же её перековал). Мышь выбор не ставит — у кнопок окна нет навигации.
            Present(s.Group,null);_smithSlot=-1;_smithWorn=false;_smithAffix=0;_confirmDismantle=false;
            // Открытие кузни: один удар по наковальне.
            GameSound.Play("smith_hammer",.55f);
            s.Message.text=CampServiceText.Get("dialogue.smith.open");RefreshSmith();
        }
        static string SmithText(string key)=>CampServiceText.Get("smith."+key);
        void WireSmith()
        {
            if(_smithWired)return;_smithWired=true;var s=_view.Smith;
            s.Title.text=CampServiceText.Get("npc.smith");s.Speaker.text=s.Title.text;
            HideTabs(s);
            for(int i=0;i<s.Cells.Length;i++){int slot=i;s.Cells[i].Button.onClick.AddListener(()=>PickSmith(slot,false));}
            for(int i=0;i<s.Rows.Length;i++)
            {int affix=i;s.Rows[i].onClick.AddListener(()=>{_smithAffix=affix;_confirmDismantle=false;RefreshSmith();});}
            WireWorn(s,slot=>PickSmith(slot,true));
            s.Action.onClick.AddListener(ApplySmith);
            // Вторая кнопка — «Разобрать» (миграция v1 CampShopsWc); у префаба до миграции её нет.
            if(s.Extra!=null)s.Extra.onClick.AddListener(ApplyDismantle);
            s.Back.onClick.AddListener(Close);
            var backLabel=s.Back.GetComponentInChildren<TMPro.TMP_Text>();if(backLabel!=null)backLabel.text=CampServiceText.Get("back");
        }
        /// <summary>
        /// Выбор вещи. Повторный выбор той же — ничего не меняет: Submit выбранной ячейки (Enter) не
        /// сбрасывает подтверждение разбора. Свойство — первое, которое ещё можно перековать.
        /// </summary>
        void PickSmith(int slot,bool worn)
        {
            if(slot==_smithSlot && worn==_smithWorn)return;
            _smithSlot=slot;_smithWorn=worn;_confirmDismantle=false;_view.Smith.Message.text="";
            var item=WornOrBag(_smithCamp,slot,worn);
            int count=!item.IsEmpty && ItemGenerator.Generate(item,_smithCamp.Items,_smithRoll)?_smithRoll.AffixCount:0;
            _smithAffix=CampShopDeals.DefaultAffix(_smithCamp,slot,worn,count);
            RefreshSmith();
        }
        void RefreshSmith()
        {
            RefreshBehindShopModal(RefreshSmithContents);
        }
        void RefreshSmithContents()
        {
            var s=_view.Smith;var camp=_smithCamp;var inventory=GetComponent<CampInventoryView>();
            s.Gold.text=camp.Money(CurrencyType.Gold).ToString();s.Shards.text=camp.Money(CurrencyType.Shards).ToString();s.ShardsGroup.SetActive(true);
            HideTabs(s);
            Say(s.Subtitle,SmithText("subtitle"));
            s.GridCaption.text=Format("trader.bag.count",SmithText("bag.caption"),camp.Bag.Used,camp.Bag.Capacity);s.Info.text=SmithText("bag.hint");
            for(int i=0;i<s.Cells.Length;i++)
            {
                var value=i<camp.Bag.Capacity?camp.Bag.At(i):default;
                s.Cells[i].Button.interactable=!value.IsEmpty;
                s.Cells[i].Show(value.IsEmpty?null:inventory.SpriteFor(value),value.IsEmpty?"":value.ItemLevel.ToString(),(int)value.Rarity,i==_smithSlot&&!_smithWorn);
            }
            // Надетое выбирается всегда: перековка прямо здесь, разбор объяснит запрет.
            ShowWorn(s,camp,_smithWorn?_smithSlot:-1,false);
            foreach(var row in s.Rows)row.gameObject.SetActive(false);
            s.ActionLabel.text="Кузница";
            if(s.ExtraLabel!=null)s.ExtraLabel.text=SmithText(_confirmDismantle?"dismantle.confirm":"dismantle.action");
            s.Preview.text="";s.Note.text="";Tone(s.Note,UiTheme.Role.TextMuted);
            var item=WornOrBag(camp,_smithSlot,_smithWorn);
            if(item.IsEmpty)
            {
                s.Item.Show(null,"",0,false);s.ItemName.text=SmithText("choose");s.ItemMeta.text=SmithText("choose.hint");
                Say(s.ReforgeCount,"");Pips(s,-1);Show(Header(s.AffixCaption),false);
                s.Action.interactable=false;s.Price.SetActive(false);
                if(s.Extra!=null)s.Extra.interactable=false;Show(s.Yield,false);Say(s.ExtraNote,"");
                KeyHints(s,"","",false);return;
            }
            ItemGenerator.Generate(item,camp.Items,_smithRoll);
            s.Item.Show(inventory.SpriteFor(item),item.ItemLevel.ToString(),(int)item.Rarity,false);
            s.ItemName.text=inventory.ItemName(item.BaseId);
            s.ItemMeta.text=RarityAndLevel(item)+(_smithWorn?WornTag:"");
            var reforge=CampShopDeals.PlanReforge(camp,_smithSlot,_smithWorn,_smithAffix);
            var scrap=CampShopDeals.PlanDismantle(camp,_smithSlot,_smithWorn);

            // Перековки: «1 / 3» и три отметки — сделанные горят акцентом.
            Say(s.ReforgeCount,reforge.Used+" / "+CampShopDeals.ReforgeLimit);Pips(s,reforge.Used);
            Show(Header(s.AffixCaption),_smithRoll.AffixCount>0);Say(s.AffixCaption,SmithText("affixes"));
            for(int i=0;i<_smithRoll.AffixCount && i<s.Rows.Length;i++)
            {
                var a=_smithRoll.GetAffix(i);bool chosen=i==_smithAffix;
                var range=_smithWorn?camp.ReforgeRange((EquipSlot)_smithSlot,i,out Fix64 lo,out Fix64 hi):camp.ReforgeRange(_smithSlot,i,out lo,out hi);
                string label=StatLabel(a.Stat)+"  "+Paint(AffixValue(a.Value,a.Op,a.Stat),UiTheme.Role.Text);
                // Предел — у каждой строки сразу, пределы нового значения — у выбранной (кадр «Кузнец А»).
                if(range==SmithResult.AtMaximum)label+="  "+Paint("· "+SmithText("max"),UiTheme.Role.TextMuted);
                else if(chosen && range==SmithResult.Success)label+="  →  "+Paint(AffixValue(lo,a.Op,a.Stat)+"…"+AffixValue(hi,a.Op,a.Stat),UiTheme.Role.Accent);
                s.Rows[i].gameObject.SetActive(true);CampShopView.SetRow(s.Rows[i],chosen);s.RowLabels[i].text=label;
            }

            // Перековка: цена (нехватка — красным), уровень вещи после и причина, если нельзя.
            s.Price.SetActive(reforge.ShowsCost);s.PriceShardsGroup.SetActive(true);
            s.PriceGold.text=reforge.Gold.ToString();s.PriceShards.text=reforge.Shards.ToString();
            Tone(s.PriceGold,reforge.GoldShort>0?UiTheme.Role.Bad:UiTheme.Role.Coins);Tone(s.PriceShards,reforge.ShardsShort>0?UiTheme.Role.Bad:UiTheme.Role.Text);
            if(reforge.Allowed || reforge.Block==CampShopDeals.Block.Funds)s.Preview.text=Format("smith.level.change",reforge.LevelFrom,reforge.LevelTo);
            s.Note.text=ReforgeReason(reforge);Tone(s.Note,reforge.Block==CampShopDeals.Block.Funds?UiTheme.Role.Bad:UiTheme.Role.TextMuted);
            s.Action.interactable=true;

            // Разбор: что получишь и что вещь исчезнет — до нажатия; надетое и «беречь» — с причиной.
            if(s.Extra!=null)s.Extra.interactable=scrap.Allowed;
            Show(s.Yield,scrap.Allowed);Say(s.YieldShards,"+"+ShardsText(scrap.Shards));
            if(!scrap.Allowed)_confirmDismantle=false;
            Say(s.ExtraNote,scrap.Block==CampShopDeals.Block.Worn?SmithText("worn.nodismantle"):scrap.Block==CampShopDeals.Block.Protected?SmithText("protected.short")
                :SmithText(_confirmDismantle?"confirm.note":"destroy.warning"));
            Tone(s.ExtraNote,_confirmDismantle?UiTheme.Role.Bad:UiTheme.Role.TextMuted);
            // Пока ждёт подтверждение разбора, Enter не подписан: он не перекуёт вместо ответа (ShopKeys).
            KeyHints(s,!_confirmDismantle?"Открыть кузницу":"",scrap.Allowed?(_confirmDismantle?CampServiceText.Get("key.confirm"):SmithText("dismantle.action")):"",_confirmDismantle);
        }
        /// <summary>Узел заголовка раздела (надпись лежит в нём): прячется целиком, с нитью света.</summary>
        static GameObject Header(TMPro.TMP_Text label)=>label!=null && label.transform.parent!=null?label.transform.parent.gameObject:null;
        /// <summary>Три отметки перековок: сделанные — акцентом, свободные — тихим кругом; -1 — прятать.</summary>
        static void Pips(CampShopScreen s,int used)
        {
            for(int i=0;i<s.ReforgePips.Length;i++)
            {
                var pip=s.ReforgePips[i];if(pip==null)continue;
                Show(pip.gameObject,used>=0);if(used<0)continue;
                var tint=pip.GetComponent<ThemeColor>();bool done=i<used;
                if(tint!=null)tint.SetRole(done?UiTheme.Role.Accent:UiTheme.Role.TextMuted,done?1f:.35f);
            }
        }
        static string ReforgeReason(in CampShopDeals.Reforge plan)
        {
            switch(plan.Block)
            {
                case CampShopDeals.Block.None:return "";
                case CampShopDeals.Block.Funds:
                    string lack=plan.GoldShort>0?GoldText(plan.GoldShort):"";
                    if(plan.ShardsShort>0)lack+=(lack.Length>0?", ":"")+ShardsText(plan.ShardsShort);
                    return Format("smith.short",lack);
                case CampShopDeals.Block.NoAffix:return SmithFailure(SmithResult.NoAffix);
                case CampShopDeals.Block.AtMaximum:return SmithFailure(SmithResult.AtMaximum);
                case CampShopDeals.Block.Exhausted:return SmithFailure(SmithResult.Exhausted);
                default:return SmithFailure(SmithResult.InvalidItem);
            }
        }
        void ApplySmith()
        {
            if(_smithSlot<0)return;
            var target=_smithWorn?ForgeTarget.Worn((EquipSlot)_smithSlot):ForgeTarget.Bag(_smithSlot);
            CampForgeView.Instance?.Open(_smithCamp,target,_smithAffix,RefreshSmith);
        }
        void ApplyLegacySmith()
        {
            var s=_view.Smith;var camp=_smithCamp;
            if(!s.Action.interactable)return;
            _confirmDismantle=false;
            var before=WornOrBag(camp,_smithSlot,_smithWorn);
            bool hasAffix=ItemGenerator.Generate(before,camp.Items,_smithRoll) && _smithAffix<_smithRoll.AffixCount;
            var old=hasAffix?_smithRoll.GetAffix(_smithAffix):default;
            var result=_smithWorn?camp.Reforge((EquipSlot)_smithSlot,_smithAffix):camp.Reforge(_smithSlot,_smithAffix);
            RefreshSmith();
            // Реплика — только после удачи; числа и отказы — системной строкой (CAMP-NPC-DIALOGUE.md).
            if(result==SmithResult.Success)
            {
                s.Message.text=CampServiceText.Get("dialogue.smith.reforge");
                // Что выпало — сразу под кнопкой: «Урон: 14 → 18»; причина (три из трёх) — строкой ниже.
                if(hasAffix && ItemGenerator.Generate(WornOrBag(camp,_smithSlot,_smithWorn),camp.Items,_smithRoll) && _smithAffix<_smithRoll.AffixCount)
                {
                    var now=_smithRoll.GetAffix(_smithAffix);string reason=s.Note.text;
                    s.Note.text=Paint(Format("smith.reforged",StatLabel(old.Stat),AffixValue(old.Value,old.Op,old.Stat),AffixValue(now.Value,now.Op,now.Stat)),UiTheme.Role.Accent)+(reason.Length>0?"\n"+reason:"");
                }
                Punch(s.Gold);Punch(s.Shards);Punch(s.Item!=null?s.Item.Icon:null,.1f);
                if(_smithAffix<s.RowLabels.Length)Punch(s.RowLabels[_smithAffix],.08f);
                // Перековка: два удара молота, пар, звон готовой вещи.
                GameSound.Sequence(("smith_hammer",0f,.85f),("smith_hammer",.32f,.8f),("smith_sizzle",.62f,.55f),("smith_ring",1f,.5f));
            }
            else{s.Note.text=SmithFailure(result);Tone(s.Note,UiTheme.Role.Bad);}
        }
        /// <summary>Разбор: первое нажатие — подтверждение (кнопка «Точно разобрать?», строка красным), второе — разбор.</summary>
        void ApplyDismantle()
        {
            var s=_view.Smith;var camp=_smithCamp;
            if(s.Extra==null || !s.Extra.interactable)return;
            // Разбор необратим: запрет (надетое, «беречь») проверяется заново, а не по кнопке с прошлого обновления.
            if(!CampShopDeals.PlanDismantle(camp,_smithSlot,_smithWorn).Allowed){_confirmDismantle=false;RefreshSmith();return;}
            if(!_confirmDismantle){_confirmDismantle=true;RefreshSmith();return;}
            var result=camp.Dismantle(_smithSlot,out int shards);
            _confirmDismantle=false;
            if(result==SmithResult.Success){_smithSlot=-1;_smithWorn=false;}
            RefreshSmith();
            if(result==SmithResult.Success)
            {
                s.Message.text=CampServiceText.Get("dialogue.smith.dismantle");
                Say(s.ExtraNote,Paint(Format("smith.dismantled",ShardsText(shards)),UiTheme.Role.Accent));
                Punch(s.Shards);
                // Разбор короткий (владелец: «слишком долгий»): удар и осыпающиеся детали.
                GameSound.Sequence(("smith_break",0f,.8f),("smith_debris",.08f,.6f));
            }
            else{Say(s.ExtraNote,SmithFailure(result));Tone(s.ExtraNote,UiTheme.Role.Bad);}
        }
        static string SmithFailure(SmithResult value)=>SmithText("error."+value);
        static string AffixValue(Fix64 value,ModifierOp op,StatType stat)=>StatText.Modifier(stat,value,op);
        static string StatLabel(StatType stat)=>StatText.Name(stat);
        /// <summary>
        /// Самопроверка без прогресса (CampServicesProbe): на одной странице ничего не выбрано — обе кнопки
        /// спят; выбор вещи в сетке; три перековки с ценами и пределом; разбор рядом — первое нажатие
        /// ждёт подтверждения, Esc его снимает, два нажатия разбирают.
        /// </summary>
        internal bool ProbeSmithTransactions()
        {
            // Изолированный кошелёк и сумка: проверка не записывает тестовые вещи в прогресс.
            var original=_smithCamp;var s=_view.Smith;var action=s.Action;var scrap=s.Extra;
            try
            {
                if(scrap==null)return false;
                _smithCamp=new Camp(PrototypeContent.Items());_smithCamp.Earn(CurrencyType.Gold,200);_smithCamp.Earn(CurrencyType.Shards,20);
                _smithCamp.Bag.Add(new ItemInstance(StableId.Of("base.rusty_sword"),10,ItemRarity.Magic,123));
                _smithSlot=-1;_smithWorn=false;_smithAffix=0;_confirmDismantle=false;RefreshSmith();
                if(action.interactable || scrap.interactable)return false;
                s.Cells[0].Button.onClick.Invoke();
                if(_smithSlot!=0 || _smithWorn || !scrap.interactable || !s.Price.activeSelf)return false;
                for(int i=0;i<3;i++)ApplyLegacySmith();
                if(_smithCamp.Bag.At(0).ReforgeCount!=3 || _smithCamp.Money(CurrencyType.Gold)!=20 || _smithCamp.Money(CurrencyType.Shards)!=2)return false;
                scrap.onClick.Invoke();
                if(_smithCamp.Bag.IsEmpty(0) || !_confirmDismantle)return false;
                // Повторный выбор той же вещи (Submit по Enter) не снимает подтверждение, Esc — снимает.
                s.Cells[0].Button.onClick.Invoke();
                if(!_confirmDismantle || !CancelShopConfirm() || _confirmDismantle || _smithCamp.Bag.IsEmpty(0))return false;
                // «Беречь»: разбор недоступен, перековка — по-прежнему по правилам.
                _smithCamp.Bag.SetKeep(0,true);RefreshSmith();
                if(scrap.interactable)return false;
                _smithCamp.Bag.SetKeep(0,false);RefreshSmith();
                int reward=Inventory.ShardsFor(_smithCamp.Bag.At(0));scrap.onClick.Invoke();scrap.onClick.Invoke();
                return _smithCamp.Bag.IsEmpty(0) && _smithCamp.Money(CurrencyType.Shards)==2+reward && !action.interactable && !scrap.interactable && _smithSlot<0;
            }
            finally{_smithCamp=original;_smithSlot=-1;_smithWorn=false;_confirmDismantle=false;if(_smithCamp!=null)RefreshSmith();_view.Smith.Message.text="";}
        }
    }
}
