using System;
using System.Reflection;
using Game.View;
using UnityEditor;
using UnityEngine;

public static class PelagTempoValidation
{
    public static void Validate()
    {
        // Проверка выполняется в отдельном редакторе сборки. Все назначения возвращаются побайтно по ключам.
        GameUserSettings.Load();
        bool mode=GameUserSettings.WasdMovement;
        string[] keys=new string[21]; bool[] exists=new bool[21]; int[] values=new int[21];
        for(int i=0;i<10;i++){keys[i]="settings.keys."+i;keys[i+10]="settings.keys.wasd."+i;}
        keys[20]="settings.controls.wasd";
        for(int i=0;i<keys.Length;i++){exists[i]=PlayerPrefs.HasKey(keys[i]);values[i]=PlayerPrefs.GetInt(keys[i]);}
        try
        {
            GameUserSettings.SetWasdMovement(false);
            Require(GameKeyBindings.Rebind(GameAction.Ability1,KeyCode.K),"Назначение мышь/K");
            GameUserSettings.SetWasdMovement(true); GameKeyBindings.ResetAll();
            Require(GameKeyBindings.KeyFor(GameAction.Ability1)==KeyCode.Alpha1
                && GameKeyBindings.KeyFor(GameAction.Ability4)==KeyCode.Alpha4
                && GameKeyBindings.KeyFor(GameAction.Dash)==KeyCode.Space,"WASD: 1–4 / Space");
            Require(!GameKeyBindings.Rebind(GameAction.Ability2,KeyCode.W),"W остаётся движением");
            Require(GameKeyBindings.Rebind(GameAction.Ability1,KeyCode.L),"Назначение WASD/L");
            GameUserSettings.SetWasdMovement(false);
            Require(GameKeyBindings.KeyFor(GameAction.Ability1)==KeyCode.K,"Сохранено старое назначение");
            GameUserSettings.SetWasdMovement(true);
            Require(GameKeyBindings.KeyFor(GameAction.Ability1)==KeyCode.L,"Сохранено новое назначение");
            var forward=TickDriver.CameraMovement(0,1,Quaternion.Euler(55,90,0));
            var diagonal=TickDriver.CameraMovement(1,1,Quaternion.Euler(55,90,0));
            Require(Math.Abs(forward.X.ToDouble()-1)<.002 && Math.Abs(forward.Y.ToDouble())<.002,"Направление камеры");
            Require(diagonal.LengthSq.ToDouble()<=1.002 && diagonal.LengthSq.ToDouble()>.99,"Диагональ нормализована");
            Require(EditorApplication.ExecuteMenuItem("Разлом/Пелаг/Темп боя"),"Меню Темп боя зарегистрировано");
            EditorWindow.GetWindow<PelagTempoTestWindow>().Close();
            foreach(string icon in new[]{"Skewer","Backblast","Wreck","FireFlask"})
                Require(Resources.Load<Texture2D>("UI/Abilities/Icon_"+icon)!=null,"Иконка "+icon);
            Debug.Log("[pelag-tempo-qa] PASS: menu, four icons, camera movement, normalized diagonal, mouse/WASD bindings and legacy preservation.");
        }
        finally
        {
            GameUserSettings.SetWasdMovement(mode);
            for(int i=0;i<keys.Length;i++)if(exists[i])PlayerPrefs.SetInt(keys[i],values[i]);else PlayerPrefs.DeleteKey(keys[i]);
            typeof(GameKeyBindings).GetField("_loaded",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);
            PlayerPrefs.Save();
        }
    }
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException("[pelag-tempo-qa] "+message);}
}
