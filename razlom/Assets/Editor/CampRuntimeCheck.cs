using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Game.View;
using System.IO;

public static class CampRuntimeCheck
{
    [MenuItem("Разлом/Лагерь/Снимок и диагностика %#F9")]
    public static void Capture()
    {
        string dir=Path.GetFullPath("../artifacts/camp-check");Directory.CreateDirectory(dir);
        var camp=CampPlayerView.Instance;
        var body=GameObject.Find("Pelag - Camp");
        var agent=body!=null?body.GetComponent<NavMeshAgent>():null;
        File.WriteAllText(Path.Combine(dir,"state.txt"),
            $"playing={Application.isPlaying}\ncamp={camp!=null}\nactive={camp?.Active}\nposition={camp?.Position}\ntent={camp?.Tent}\nagent={agent!=null}\nonNavMesh={(agent!=null&&agent.isOnNavMesh)}\nnavVertices={NavMesh.CalculateTriangulation().vertices.Length}\n");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir,"game.png"));
    }
}
