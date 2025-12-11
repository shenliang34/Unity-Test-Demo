using System.Text;
using UnityEngine;

// Simple in-Unity tester for UnityWebRequestStreamer.
// Attach this to any GameObject in the scene. It provides an OnGUI interface
// to start/stop a POST stream and shows received chunks in a scrollable log.
public class UnityStreamTester : MonoBehaviour
{
    public string url = "http://localhost:8000/stream";
    [TextArea(3, 10)]
    public string jsonBody = "{}";

    UnityWebRequestStreamer streamer;
    StringBuilder log = new StringBuilder();
    Vector2 scrollPos;
    bool running = false;

    void Awake()
    {
        streamer = FindObjectOfType<UnityWebRequestStreamer>();
        if (streamer == null)
        {
            // 如果场景里没有，则创建一个 GameObject 并挂上
            var go = new GameObject("UnityWebRequestStreamer");
            streamer = go.AddComponent<UnityWebRequestStreamer>();
            DontDestroyOnLoad(go);
        }
    }

    void AppendLog(string text)
    {
        log.Append(text);
        // 保持行尾换行便于阅读
        if (!text.EndsWith("\n")) log.Append('\n');
    }

    public void StartTest()
    {
        if (running) return;
        running = true;
        log.Clear();
        AppendLog($"Starting POST -> {url}");

        streamer.PostStream(url, jsonBody,
            onChunk: chunk => { AppendLog("CHUNK: " + chunk); },
            onComplete: () => { AppendLog("--- Stream complete ---"); running = false; },
            onError: err => { AppendLog("ERROR: " + err); running = false; });
    }

    public void CancelTest()
    {
        if (!running) return;
        streamer.CancelCurrent();
        AppendLog("--- Cancelled by user ---");
        running = false;
    }

    public void ClearLog()
    {
        log.Clear();
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 480, 360), GUI.skin.box);
        GUILayout.Label("Unity Stream Tester");
        GUILayout.BeginHorizontal();
        GUILayout.Label("URL:", GUILayout.Width(40));
        url = GUILayout.TextField(url, GUILayout.Width(360));
        GUILayout.EndHorizontal();

        GUILayout.Label("Request Body:");
        jsonBody = GUILayout.TextArea(jsonBody, GUILayout.Height(60));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start", GUILayout.Width(80))) StartTest();
        if (GUILayout.Button("Cancel", GUILayout.Width(80))) CancelTest();
        if (GUILayout.Button("Clear", GUILayout.Width(80))) ClearLog();
        GUILayout.EndHorizontal();

        GUILayout.Label("Log:");
        scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(440), GUILayout.Height(200));
        GUILayout.Label(log.ToString());
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        // 如果测试正在运行，取消它
        if (running) streamer.CancelCurrent();
    }
}
