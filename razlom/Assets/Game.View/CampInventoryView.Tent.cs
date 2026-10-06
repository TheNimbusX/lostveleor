using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Палатка снаряжения на Canvas (концепт O, 22 сентября): карточка героя с листом
    /// характеристик и зельями, вкладки «Сумка / Атлас». Внешний вид — CampTentView,
    /// здесь что показать и что сделать по клику.
    /// </summary>
    public sealed partial class CampInventoryView
    {
        CampTentView _tent;
        readonly CampTentCell[] _tentBag=new CampTentCell[48];
        CampAtlasEntry[] _atlas;
        // «Атлас ли сейчас»: его читает CampWalkCapture отражением; источник правды — _page (CampInventoryView.Oaths).
        bool _atlasPage;
        int _hoverIndex=-1; bool _hoverWorn;
        // Атлас 3×4 (06.10): карточка справа показывает наведённое место, без наведения — выбранное кликом.
        int _atlasPicked, _atlasHover=-1;
        bool RoundAtlas=>_tent!=null&&_tent.AtlasSeals!=null&&_tent.AtlasSeals.Length>0&&_tent.AtlasSeals[0]!=null;

        /// <summary>Порядок строк листа героя; совпадает с подписями в CampTentBuilder.</summary>
        static readonly StatType[] StatRows=
        {
            StatType.MaxHealth,StatType.Damage,StatType.Armor,StatType.AttackSpeed,StatType.CritChance,StatType.CritMultiplier,
            StatType.MaxLavidium,StatType.LavidiumRegen,StatType.MoveSpeed,StatType.AbilitySpeed,StatType.CooldownRecovery,StatType.FireResist,
        };
        static readonly string[] PotionFiles={"potion_health_small","potion_health_large","potion_lavidium_small","potion_lavidium_large"};
        readonly Sprite[] _potionSprites=new Sprite[4];

        bool BuildTent(GameObject prefab)
        {
            _root=Instantiate(prefab);
            _root.name="Camp Tent";
            UiScaleFollower.Attach(_root);
            _tent=_root.GetComponentInChildren<CampTentView>(true);
            if(_tent==null||_tent.BagTemplate==null||_tent.BagGrid==null){Destroy(_root);_root=null;_tent=null;return false;}
            EnsureEventSystem();
            LoadIcons();
            if(_tent.Close!=null)_tent.Close.onClick.AddListener(Close);
            for(int f=0;f<_tent.Filters.Length;f++)
            {
                int tab=f,filter=f-1;
                if(_tent.Filters[f]!=null)_tent.Filters[f].onClick.AddListener(()=>{_filter=filter;UiSound.Play(UiSoundEvent.Tab);_tent.ShowFilter(tab);Refresh();});
            }
            if(_tent.BagTab!=null)_tent.BagTab.onClick.AddListener(()=>ShowPage(TentPage.Bag));
            if(_tent.AtlasTab!=null)_tent.AtlasTab.onClick.AddListener(()=>ShowPage(TentPage.Atlas));
            BuildOaths();
            for(int i=0;i<_tent.Worn.Length&&i<4;i++)
            {
                var cell=_tent.Worn[i];if(cell==null)continue;
                if(cell.Placeholder!=null)cell.Placeholder.sprite=_icons[i];
                _wornCells[i]=AddCell((RectTransform)cell.transform,i,true);
            }
            for(int i=0;i<48;i++)
            {
                var cell=Instantiate(_tent.BagTemplate,_tent.BagGrid);
                cell.name="Bag "+i;
                cell.gameObject.SetActive(true);
                _tentBag[i]=cell;
                _bagCells[i]=AddCell((RectTransform)cell.transform,i,false);
            }
            for(int i=0;i<_tent.StatRows.Length&&i<StatRows.Length;i++)
            {
                if(_tent.StatLabels[i]!=null)_tent.StatLabels[i].text=StatText.Short(StatRows[i]);
                if(_tent.StatRows[i]==null)continue;
                int row=i;var relay=_tent.StatRows[i].gameObject.AddComponent<CampHoverRelay>();
                relay.Hover=on=>{if(on)ShowStatTooltip(row);else _tent.ShowTooltip(false);};
            }
            for(int i=0;i<_tent.Potions.Length&&i<4;i++)
            {
                if(_tent.Potions[i]==null)continue;
                int potion=i;
                _tent.Potions[i].onClick.AddListener(()=>ShowPotionTooltip(potion));
                var relay=_tent.Potions[i].gameObject.AddComponent<CampHoverRelay>();
                relay.Hover=on=>{if(on)ShowPotionTooltip(potion);else _tent.ShowTooltip(false);};
                var tex=Resources.Load<Texture2D>("UI/Items/"+PotionFiles[i]);
                if(tex!=null)_potionSprites[i]=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100);
                if(_tent.PotionIcons[i]!=null)_tent.PotionIcons[i].sprite=_potionSprites[i];
            }
            // Атлас (06.10) — только артефакты набора акта I; основ в нём больше нет.
            int entries=RunArtifacts.Count;
            _atlas=new CampAtlasEntry[entries];
            // Атлас 3×4 (v103): места собраны в префабе, клонов нет — только подписка. Иначе — прежние клоны ячеек.
            if(RoundAtlas)
                foreach(var seal in _tent.AtlasSeals)
                {
                    if(seal==null)continue;
                    seal.Hovered=(s,on)=>HoverAtlas(s.Index,on);
                    seal.Clicked=(s,right)=>{if(!right){_atlasPicked=s.Index;RefreshAtlas();}};
                }
            else if(_tent.AtlasTemplate!=null&&_tent.AtlasGrid!=null)
                for(int i=0;i<entries;i++)
                {
                    var entry=Instantiate(_tent.AtlasTemplate,_tent.AtlasGrid);
                    entry.name="Atlas "+RunArtifacts.At(i);entry.gameObject.SetActive(true);_atlas[i]=entry;
                    int index=i;var relay=entry.gameObject.AddComponent<CampHoverRelay>();
                    relay.Hover=on=>{if(on)ShowAtlasTooltip(index);else _tent.ShowTooltip(false);};
                }
            // «Дым и свет»: группы проявления собрали части до клонов ячеек — пересобрать, иначе
            // клоны (с шаблона они берут «скрыто») не проявятся до следующего открытия.
            foreach(var group in _root.GetComponentsInChildren<UiInkGroup>(true))group.Collect();
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-capture-tent-rarities")>=0)AddRaritySample();
            _tent.ShowFilter(0);
            _page=TentPage.Bag;_atlasPage=false;
            _tent.ShowPage((int)TentPage.Bag);
            Refresh();
            return true;
        }

        /// <summary>Сумка или атлас — прежний вызов (CampWalkCapture); страницы теперь три, см. ShowPage(TentPage).</summary>
        internal void ShowAtlas(bool atlas)=>ShowPage(atlas?TentPage.Atlas:TentPage.Bag);

        System.Collections.IEnumerator RevealTent()
        {
            _tent.HideForOpen();
            yield return null;
            _tent.PlayOpen();
        }

        /// <summary>
        /// Только для съёмки (-capture-tent-rarities): все 16 основ набора — обычные и
        /// редкие, редкие отмечены «беречь», карточка у первой редкой.
        /// </summary>
        void AddRaritySample()
        {
            var bag=_driver.Session.Camp.Bag;
            int rare=-1;
            for(int i=0;i<16&&i<ItemTexts.Count;i++)
            {
                bool isRare=i%4>=2;
                int slot=bag.Add(new ItemInstance(StableId.Of("base."+ItemTexts.KeyAt(i)),(short)(4+i),isRare?ItemRarity.Magic:ItemRarity.Normal,(ulong)(9000+i)));
                if(slot<0||!isRare)continue;
                bag.SetKeep(slot,true);if(rare<0)rare=slot;
            }
            if(rare>=0){_selection=rare;_selectedWorn=false;_hoverIndex=rare;_hoverWorn=false;}
        }

        /// <summary>Мышь над ячейкой: карточка предмета показывает её.</summary>
        public void Hover(int index,bool worn,bool on)
        {
            if(_tent==null)return;
            if(on){_hoverIndex=index;_hoverWorn=worn;}
            else if(_hoverIndex==index&&_hoverWorn==worn){_hoverIndex=-1;_tent.ShowTooltip(false);return;}
            RefreshTooltip();
        }

        /// <summary>ПКМ по вещи в сумке: «беречь» — разбор её не заберёт.</summary>
        public void ToggleKeep(int index)
        {
            var bag=_driver.Session.Camp.Bag;
            if(bag.IsEmpty(index))return;
            bag.SetKeep(index,!bag.IsKept(index));
            UiSound.Play(UiSoundEvent.Toggle);
            SetFeedback(bag.IsKept(index)?"Вещь отмечена — в разбор не уйдёт.":"");
            Refresh();
        }

        void RefreshTent()
        {
            var camp=_driver.Session.Camp;
            int count=0;
            for(int i=0;i<48;i++)
            {
                var item=camp.Bag.At(i);if(!item.IsEmpty)count++;
                int category=Category(item);bool visible=_filter<0||category==_filter;
                _bagCells[i].Selectable=visible||item.IsEmpty;
                _tentBag[i].Show(item.IsEmpty?_tent.EmptyFrame:_tent.FrameFor((int)item.Rarity),Color.white,
                    item.IsEmpty?null:ItemSprite(i,false),item.IsEmpty?"":item.ItemLevel.ToString(),
                    !_selectedWorn&&i==_selection&&!item.IsEmpty,!item.IsEmpty&&!visible,!item.IsEmpty&&camp.Bag.IsKept(i),
                    item.IsEmpty?-1:(int)item.Rarity,_tent.ColourFor((int)item.Rarity));
            }
            for(int i=0;i<_tent.Worn.Length&&i<4;i++)
            {
                var cell=_tent.Worn[i];if(cell==null)continue;
                var item=camp.Worn.Worn((EquipSlot)i);
                // Рамка слота — плитка паузы; редкость показывают свечение и камень.
                cell.Show(null,Color.white,item.IsEmpty?null:ItemSprite(i,true),item.IsEmpty?"":item.ItemLevel.ToString(),
                    _selectedWorn&&i==_selection&&!item.IsEmpty,false,false,
                    item.IsEmpty?-1:(int)item.Rarity,_tent.ColourFor((int)item.Rarity));
            }
            if(_tent.BagCount!=null)_tent.BagCount.text=count+" / 48";

            if(_tent.Level!=null)_tent.Level.text=camp.Level.ToString();
            int need=Mathf.Max(1,camp.ExperienceToNextLevel);
            if(_tent.XpFill!=null)_tent.XpFill.anchorMax=new Vector2(Mathf.Clamp01(camp.Experience/(float)need),1f);
            if(_tent.XpText!=null)_tent.XpText.text=camp.Experience+" / "+need+" опыта";

            var stats=_driver.Session.CampSim.Entities.Stats[0];
            for(int i=0;i<_tent.StatValues.Length&&i<StatRows.Length;i++)
            {
                var stat=StatRows[i];
                _tent.ShowStat(i,StatText.Shown(stat,stats.Get(stat)),v=>StatText.Value(stat,v));
            }

            for(int i=0;i<_tent.Potions.Length&&i<4;i++)
            {
                var kind=(PotionKind)i;int have=camp.PotionCount(kind);
                if(_tent.PotionCounts[i]!=null)_tent.PotionCounts[i].text=have.ToString();
                if(_tent.PotionIcons[i]!=null)_tent.PotionIcons[i].color=have>0?Color.white:new Color(.55f,.6f,.7f,.55f);
                bool quick=camp.SelectedPotion(i/2)==kind;
                if(_tent.PotionStates.Length>i&&_tent.PotionStates[i]!=null)_tent.PotionStates[i].Set(WcSlotState.Plain,quick);
                else if(_tent.PotionSelected[i]!=null)_tent.PotionSelected[i].SetActive(quick);
            }

            RefreshAsh();
            RefreshOaths();
            if(RoundAtlas)RefreshAtlas();
            else if(_atlas!=null)
            {
                for(int i=0;i<_atlas.Length;i++)
                {
                    if(_atlas[i]==null)continue;
                    var artifact=RunArtifacts.At(i);
                    _atlas[i].Show(_tent.FrameFor(ArtifactRarity),ArtifactSprite(i),RunArtifactTexts.Name(artifact),camp.ArtifactOpened(artifact),ArtifactRarity);
                }
                if(_tent.AtlasCount!=null)_tent.AtlasCount.text="Открыто "+camp.OpenedArtifactCount+" из "+RunArtifacts.Count;
            }
            RefreshTooltip();
        }

        /// <summary>Артефакт — вещь одного забега вне лестницы редкостей; плитка берёт цвет уникальной.</summary>
        const int ArtifactRarity=(int)ItemRarity.Unique;
        readonly Sprite[] _artifactSprites=new Sprite[RunArtifacts.Count];

        /// <summary>Картинка артефакта (текстура из Resources/UI/Artifacts) как спрайт плитки; грузится раз.</summary>
        Sprite ArtifactSprite(int index)
        {
            if(_artifactSprites[index]!=null)return _artifactSprites[index];
            var tex=RunArtifactTexts.Icon(RunArtifacts.At(index));
            if(tex!=null)_artifactSprites[index]=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100);
            return _artifactSprites[index];
        }

        /// <summary>Копия листа статов без модификаторов, которые выбрасывает drop.</summary>
        static StatSheet CloneSheet(StatSheet stats,System.Predicate<StatModifier> drop)
        {
            var sheet=new StatSheet(Mathf.Max(32,stats.ModifierCount+16));
            for(int s=0;s<(int)StatType.Count;s++)sheet.SetBase((StatType)s,stats.GetBase((StatType)s));
            for(int m=0;m<stats.ModifierCount;m++){var mod=stats.GetModifier(m);if(drop==null||!drop(mod))sheet.Add(in mod);}
            return sheet;
        }

        /// <summary>Свойства вещи строками: встроенное и аффиксы.</summary>
        string ItemProperties(ItemInstance item)
        {
            var camp=_driver.Session.Camp;
            if(!ItemGenerator.Generate(in item,camp.Items,_roll))return "";
            string text="";
            if(_roll.HasImplicit)text+=StatText.Name(_roll.ImplicitStat)+"  <b>"+StatText.Modifier(_roll.ImplicitStat,_roll.ImplicitValue,_roll.ImplicitOp)+"</b>\n";
            for(int i=0;i<_roll.AffixCount;i++){var a=_roll.GetAffix(i);text+=StatText.Name(a.Stat)+"  <b>"+StatText.Modifier(a.Stat,a.Value,a.Op)+"</b>\n";}
            return text.TrimEnd();
        }

        /// <summary>Карточка у ячейки под мышью; пустая ячейка карточку прячет.</summary>
        void RefreshTooltip()
        {
            var camp=_driver.Session.Camp;
            if(_hoverIndex<0){return;}
            int index=_hoverIndex;
            bool worn=_hoverWorn;
            if(worn&&index>=_tent.Worn.Length){_tent.ShowTooltip(false);return;}
            var item=worn?camp.Worn.Worn((EquipSlot)index):camp.Bag.At(index);
            if(item.IsEmpty){_tent.ShowTooltip(false);return;}
            int kind=Category(item);
            int rarity=(int)item.Rarity;
            // Попытки закалки — по редкости (06.10, API кузницы), трещины — только если есть.
            int cracks=camp.CrackCount(item);
            string kindText=(kind>=0?Names[kind]:"Предмет")+"  ·  ур. "+item.ItemLevel+"  ·  "+CampServiceText.Get("smith.attempts")+" "
                +camp.AttemptsUsed(item)+"/"+Camp.TemperAttempts(item.Rarity)+(cracks>0?"  ·  трещин "+cracks:"");
            string text;
            var stats=_driver.Session.CampSim.Entities.Stats[0];
            string properties=ItemProperties(item);
            if(worn)text=properties+(properties.Length>0?"\n\n":"")+"<color=#93A2BC>Надето на Пелаге</color>";
            else
            {
                string compare=CompareStats(item,stats,"#8FE3A8","#FF6A5A","#F4F7FB",true).Replace("\n\n","\n").Trim();
                text=properties+(properties.Length>0?"\n\n":"")+(compare.Length==0?"<color=#93A2BC>Если надеть — без изменений</color>":"<color=#93A2BC>Если надеть:</color>\n"+compare);
            }
            FillTooltip(ItemName(item.BaseId),_tent.NameFor(rarity),_tent.ColourFor(rarity),kindText,text,rarity,ItemSprite(index,worn));
            var cell=worn?_tent.Worn[index]:_tentBag[index];
            if(cell!=null)_tent.PlaceTooltip((RectTransform)cell.transform);
            _tent.ShowTooltip(true);
        }

        /// <param name="frameRarity">Редкость рамки картинки: 0..3, -1 — без редкости (стат, зелье).</param>
        void FillTooltip(string title,string rarity,Color rarityColour,string kind,string body,int frameRarity,Sprite art)
        {
            if(_tent.TooltipAutoLayout)
            {
                // Новая карточка (CampTentWc) раскладывается сама: только тексты и видимость строк.
                _tent.ItemTitle.text=title;
                _tent.ItemRarity.text=rarity;_tent.ItemRarity.color=rarityColour;_tent.ItemRarity.gameObject.SetActive(rarity.Length>0);
                _tent.ItemKind.text=kind;_tent.ItemKind.gameObject.SetActive(kind.Length>0);
                _tent.ItemStats.text=body;_tent.ItemStats.gameObject.SetActive(body.Length>0);
                bool picture=art!=null;
                _tent.ItemArt.sprite=art;_tent.ItemArt.enabled=picture;
                _tent.SetItemFrame(frameRarity);
                _tent.ItemFrame.transform.parent.gameObject.SetActive(picture);
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_tent.Tooltip);
                return;
            }
            if(_tent.ItemTitle!=null)_tent.ItemTitle.text=title;
            if(_tent.ItemRarity!=null){_tent.ItemRarity.text=rarity;_tent.ItemRarity.color=rarityColour;}
            if(_tent.ItemKind!=null)_tent.ItemKind.text=kind;
            bool hasArt=art!=null;
            if(_tent.ItemFrame!=null){_tent.ItemFrame.gameObject.SetActive(hasArt);_tent.SetItemFrame(frameRarity);}
            // Без картинки (стат, пустое зелье) текст встаёт к левому краю, а не рядом с пустым местом.
            float left=hasArt&&_tent.ItemFrame!=null?_tent.ItemFrame.rectTransform.anchoredPosition.x+_tent.ItemFrame.rectTransform.sizeDelta.x+16f:32f;
            foreach(var label in new TMPro.TMP_Text[]{_tent.ItemTitle,_tent.ItemRarity,_tent.ItemKind})
                if(label!=null){var r=label.rectTransform;r.anchoredPosition=new Vector2(left,r.anchoredPosition.y);r.sizeDelta=new Vector2(_tent.Tooltip.sizeDelta.x-left-30f,r.sizeDelta.y);}
            if(_tent.ItemArt!=null){_tent.ItemArt.sprite=art;_tent.ItemArt.enabled=hasArt;}
            if(_tent.ItemStats==null||_tent.Tooltip==null)return;
            _tent.ItemStats.text=body;
            _tent.ItemStats.ForceMeshUpdate();
            var statsRect=_tent.ItemStats.rectTransform;
            float statsHeight=_tent.ItemStats.preferredHeight;
            statsRect.sizeDelta=new Vector2(statsRect.sizeDelta.x,statsHeight);
            _tent.Tooltip.sizeDelta=new Vector2(_tent.Tooltip.sizeDelta.x,Mathf.Max(200f,-statsRect.anchoredPosition.y+statsHeight+22f));
        }

        /// <summary>
        /// Откуда складывается стат: основа героя и вклад каждой надетой вещи. Уровень лагеря
        /// с 29 сентября статов не даёт: база эталонного героя (бывший 5-й уровень, источник
        /// ModifierSource.Level) входит в «Основу», отдельной строки «Уровень N» больше нет.
        /// </summary>
        internal void ShowStatTooltip(int row)
        {
            if(row<0||row>=StatRows.Length)return;
            var camp=_driver.Session.Camp;var stat=StatRows[row];
            var stats=_driver.Session.CampSim.Entities.Stats[0];
            Fix64 total=stats.Get(stat);
            // Вклад каждого источника — разница с листом без него (так честно считаются и проценты).
            string Part(string name,System.Predicate<StatModifier> source)
            {
                Fix64 delta=total-CloneSheet(stats,source).Get(stat);
                if(Mathf.Abs(StatText.Shown(stat,delta))<.005f)return "";
                return "\n"+name+"<pos=74%><color="+(delta>Fix64.Zero?"#8FE3A8":"#FF6A5A")+">"+StatText.Delta(stat,delta)+"</color>";
            }
            // Основа — лист только с базой героя: без вещей и бафов, чьи проценты иначе легли бы и на неё.
            Fix64 core=CloneSheet(stats,m=>m.Source!=ModifierSource.Level).Get(stat);
            string body=StatText.Hint(stat)+"\n\n<color=#93A2BC>"+CampServiceText.Get("stat.sources")+":</color>\n"
                +"Основа<pos=74%>"+StatText.Value(stat,core);
            for(int s=0;s<(int)EquipSlot.Count;s++)
            {
                var item=camp.Worn.Worn((EquipSlot)s);if(item.IsEmpty)continue;
                int slot=s;body+=Part(ItemName(item.BaseId),m=>m.Source==ModifierSource.Equipment&&m.SourceId==slot);
            }
            body+=Part(CampServiceText.Get("stat.buffs"),m=>m.Source==ModifierSource.Buff);
            _hoverIndex=-1;
            // Концепт tent-stats: значок стата в плитке, «Итого» бирюзой под названием.
            var icon=_tent.StatIcons!=null&&row<_tent.StatIcons.Length?_tent.StatIcons[row]:null;
            FillTooltip(StatText.Name(stat),"Итого: "+StatText.Value(stat,total),UiTheme.Current.Get(UiTheme.Role.Rare),"",body,-1,icon);
            _tent.PlaceTooltip(_tent.StatRows[row]);
            _tent.ShowTooltip(true);
        }

        void ShowPotionTooltip(int potion)
        {
            var camp=_driver.Session.Camp;var kind=(PotionKind)potion;
            // Ресурс способностей игроку — «концентрация» (владелец 01.10).
            string title=(potion%2==0?"Малое":"Большое")+" зелье "+(potion<2?"здоровья":"концентрации");
            string body="Запас: "+camp.PotionCount(kind)+"\nВыбор двух видов — у походного стола"
                +"\n<color=#9DB6CB>Купить — у алхимика</color>";
            _hoverIndex=-1;
            FillTooltip(title,"",Color.white,"Восстанавливает "+Camp.PotionPercent(kind)+"% "+(potion<2?"здоровья":"концентрации"),body,-1,_potionSprites[potion]);
            if(_tent.Potions[potion]!=null)_tent.PlaceTooltip((RectTransform)_tent.Potions[potion].transform);
            _tent.ShowTooltip(true);
        }

        internal void ShowAtlasTooltip(int index)
        {
            // Атлас 3×4: всплывающей карточки нет — карточка справа постоянная, место выбирается.
            if(RoundAtlas){if((uint)index<(uint)_tent.AtlasSeals.Length){_atlasPicked=index;RefreshAtlas();}return;}
            if(_atlas==null||(uint)index>=(uint)_atlas.Length)return;
            var camp=_driver.Session.Camp;var artifact=RunArtifacts.At(index);
            bool open=camp.ArtifactOpened(artifact);
            // Открытый — что делает; закрытый — только где искать: действие узнаётся, когда взят.
            string body=(open?RunArtifactTexts.Effect(artifact)+"\n\n":"")
                +"<color=#9DB6CB>Где искать:</color>\nНаграда босса\nТайник в Разломе — редко";
            _hoverIndex=-1;
            FillTooltip(open?RunArtifactTexts.Name(artifact):"Не открыто","Артефакт",_tent.ColourFor(ArtifactRarity),
                open?RunArtifactTexts.Use(artifact):"",body,ArtifactRarity,open?ArtifactSprite(index):null);
            if(_atlas[index]!=null)_tent.PlaceTooltip((RectTransform)_atlas[index].transform);
            _tent.ShowTooltip(true);
        }

        // ---------------------------------------------------------------- атлас 3×4 (06.10, концепт atlas-a)

        void HoverAtlas(int place,bool on)
        {
            if(on)_atlasHover=place;
            else if(_atlasHover==place)_atlasHover=-1;
            else return;
            RefreshAtlas();
        }

        void RefreshAtlas()
        {
            if(!RoundAtlas||_driver?.Session==null)return;
            PaintAtlas(_tent,_driver.Session.Camp,_atlasHover>=0?_atlasHover:_atlasPicked,_atlasPicked);
        }

        /// <summary>
        /// Атлас по лагерю: восемь мест акта I (найденный — расписная картинка в огне, неизвестный — силуэт и «?»), четыре
        /// тёмных места акта II, «Открыто N из 8» и карточка справа. Статический — его же зовёт кадр сборщика без Play.
        /// </summary>
        public static void PaintAtlas(CampTentView tent,Camp camp,int card,int picked)
        {
            if(tent.AtlasCount!=null)tent.AtlasCount.text=CampWindowText.Format("tent.atlas.count","Открыто {0} из {1}",camp.OpenedArtifactCount,RunArtifacts.Count);
            if(tent.AtlasAct!=null)tent.AtlasAct.text=CampWindowText.Get("tent.atlas.act","Акт I · Чаща");
            for(int i=0;i<tent.AtlasSeals.Length;i++)
            {
                var seal=tent.AtlasSeals[i];if(seal==null)continue;
                if(CampOathRules.AtlasPlaceIsFuture(i))
                {
                    seal.SetArt(null);
                    seal.SetHoverCaption(CampWindowText.Get("tent.atlas.act2","Акт II"));
                    seal.Show(CampOathSeal.Look.Empty,i==picked);
                    continue;
                }
                var artifact=RunArtifacts.At(i);bool open=camp.ArtifactOpened(artifact);
                seal.SetArt(ArtifactArt(i));
                seal.SetHoverCaption("");
                seal.Show(open?CampOathSeal.Look.Active:CampOathSeal.Look.Unknown,i==picked);
            }
            PaintAtlasCard(tent,camp,card);
        }

        static void PaintAtlasCard(CampTentView tent,Camp camp,int place)
        {
            bool future=CampOathRules.AtlasPlaceIsFuture(place);
            var artifact=future?RunArtifact.None:RunArtifacts.At(place);
            bool open=!future&&camp.ArtifactOpened(artifact);
            if(tent.AtlasCardSeal!=null)
            {
                tent.AtlasCardSeal.SetArt(future?null:ArtifactArt(place));
                tent.AtlasCardSeal.Show(future?CampOathSeal.Look.Empty:open?CampOathSeal.Look.Active:CampOathSeal.Look.Unknown,false);
            }
            if(tent.AtlasCardName!=null)tent.AtlasCardName.text=future?CampWindowText.Get("tent.atlas.act2","Акт II")
                :open?RunArtifactTexts.Name(artifact):CampWindowText.Get("tent.atlas.unknown","Не открыто");
            // Открытый — что делает и как включается; неизвестный — только где искать: действие узнаётся, когда взят.
            string effect=future?CampWindowText.Get("tent.atlas.act2-note","Эти места откроются во втором акте."):open?RunArtifactTexts.Effect(artifact):"";
            Line(tent.AtlasCardEffect,effect);
            Line(tent.AtlasCardUse,open?RunArtifactTexts.Use(artifact):"");
            if(tent.AtlasCardSourceRow!=null)tent.AtlasCardSourceRow.SetActive(!future);
            if(tent.AtlasCardSource!=null)tent.AtlasCardSource.text=CampWindowText.Get("tent.atlas.source","Источник: награда босса · редко — тайник в Разломе");
            if(tent.AtlasCardCarryRow!=null)tent.AtlasCardCarryRow.SetActive(open);
            if(tent.AtlasCardCarry!=null)tent.AtlasCardCarry.text=CampOathRules.ArtifactsCarryUnlocked(camp)
                ?CampWindowText.Get("tent.atlas.carry","Можно взять с собой на столе сборов")
                :CampWindowText.Get("tent.atlas.carry-later","После первого босса — можно взять с собой на столе сборов");
            // Нити между строками — по числу видимых строк: у неизвестного под именем одна нить, у акта II — одна.
            if(tent.AtlasCardLines!=null)
                for(int i=0;i<tent.AtlasCardLines.Length;i++)
                    if(tent.AtlasCardLines[i]!=null)tent.AtlasCardLines[i].SetActive(i==0||(i==1&&!future)||(i==2&&open));
        }

        // Расписные картинки артефактов (Resources/UI/Artifacts): грузятся раз на запуск, а не на каждое обновление окна.
        static readonly Texture2D[] AtlasArt=new Texture2D[RunArtifacts.Count];
        static Texture2D ArtifactArt(int index)
        {
            if((uint)index>=(uint)AtlasArt.Length)return null;
            if(AtlasArt[index]==null)AtlasArt[index]=RunArtifactTexts.Icon(RunArtifacts.At(index));
            return AtlasArt[index];
        }

        static void Line(TMPro.TMP_Text label,string text)
        {
            if(label==null)return;
            label.text=text;
            label.gameObject.SetActive(text.Length>0);
        }
    }
}
