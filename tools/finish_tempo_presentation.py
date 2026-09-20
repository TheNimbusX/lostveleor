from pathlib import Path
r=Path(__file__).resolve().parent.parent
def change(path,old,new):
 p=r/path;s=p.read_text(encoding='utf-8-sig');assert old in s,path;p.write_text(s.replace(old,new),encoding='utf-8')
change('razlom/Assets/Game.View/CampInventoryView.cs','case StatType.Damage:return "Урон";','case StatType.AbilitySpeed:return "Исполнение";case StatType.CooldownRecovery:return "Восстановление";case StatType.LavidiumRegen:return "Лавидий/с";case StatType.MaxLavidium:return "Лавидий";case StatType.Damage:return "Урон";')
change('razlom/Assets/Editor/RazlomPelagV5AnimatorBuilder.cs','v30.AnchorSlam','v31.CombatTempo')
change('ART/PELAG/animation/mobility/build_mobility.py','        keyframe(frame,previous)', '''        if kind.startswith('Wreck') or kind in ('Backblast','FireFlask'):
            for finger in ['Index','Middle','Ring','Pinky','Thumb']:
                for joint in range(1,4):
                    bone=rig.pose.bones.get(f'mixamorig:LeftHand{finger}{joint}')
                    if bone is None:continue
                    rest=bone.bone.matrix_local.to_quaternion()
                    axis=(rest@Vector((0,1,0))).cross(Vector((0,-1,0))).normalized()
                    angle=([65,85,60] if finger!='Thumb' else [20,35,30])[joint-1]
                    # После выпуска бутылки ладонь раскрывается, у цепи хват остаётся.
                    hold=1 if kind.startswith('Wreck') else (1-min(1,max(0,(frame-(2 if kind=='Backblast' else 6))/2)))
                    bone.rotation_quaternion=Quaternion(rest.inverted()@axis,math.radians(angle*hold))
        keyframe(frame,previous)''')
change('ART/PELAG/animation/mobility/build_mobility.py',"else 3 if kind=='Backblast' else frames", "else 3 if kind=='Backblast' else 7 if kind=='FireFlask' else frames")
# Дописываем новые аффиксы после прежних: старые рецепты не меняют порядок каталога.
p=r/'razlom/Assets/Game.Sim/Run/PrototypeContent.cs';s=p.read_text(encoding='utf-8');a=s.index('                new AffixDefinition(StableId.Of("affix.ability_speed")');b=s.index('                new AffixDefinition(StableId.Of("affix.flat_damage_t1")',a);new=s[a:b];s=s[:a]+s[b:];end=s.index('            };',a);s=s[:end]+new+s[end:];p.write_text(s,encoding='utf-8')
print('Updated animation recipe, labels, catalog order and live animator revision')
