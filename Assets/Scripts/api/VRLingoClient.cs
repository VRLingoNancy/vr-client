using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using System.Collections.Concurrent;

public class VRLingoClient : MonoBehaviour
{
    private static VRLingoClient instance;

    public static string CurrentConversationId { get; private set; }

    public static void PostSystemMessage(string text)
    {
        if (instance == null) return;
        instance.AddHistory("System", COLOR_SYSTEM, text);
    }

    private WebSocketClient ws;
    private MicrophoneStreamer mic;
    public AudioPlayback playback;
    private ConcurrentQueue<Action> mainThreadActions = new();

    private bool isAiSpeaking = false;
    private bool isWaitingForResponse = false;
    private bool isConnected = false;

    public TMP_Text transcriptText;
    private ScrollRect transcriptScroll;
    private float userScrollCooldown = 0f;
    private string currentStatus = "";
    private readonly List<string> history = new();
    private string liveAi = "";

    const string COLOR_USER = "#7EC8E3";
    const string COLOR_AI = "#F5F5F5";
    const string COLOR_SYSTEM = "#FFC857";
    const string COLOR_STATUS = "#888888";
    const int HISTORY_CAP = 100;

    async void Start()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (SceneManager.GetActiveScene().name == "TestScene")
            await TryInit();
    }

    void SetStatus(string status)
    {
        currentStatus = status;
        UpdateDisplay();
    }

    void AddHistory(string role, string color, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        history.Add($"<color={color}><b>{role}:</b></color> {text.Trim()}");
        if (history.Count > HISTORY_CAP) history.RemoveAt(0);
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (transcriptText == null) return;

        var sb = new StringBuilder();
        foreach (var line in history) sb.AppendLine(line);
        if (!string.IsNullOrEmpty(liveAi))
            sb.AppendLine($"<color={COLOR_AI}><b>AI:</b></color> {liveAi}");
        sb.Append($"\n<color={COLOR_STATUS}><i>{currentStatus}</i></color>");

        transcriptText.text = sb.ToString();

        if (transcriptScroll != null && userScrollCooldown <= 0f)
        {
            Canvas.ForceUpdateCanvases();
            transcriptScroll.verticalNormalizedPosition = 0f;
        }
    }

    async Task Init()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            SetStatus("⚠ No Wi-Fi — check connection");
            return;
        }

        SetStatus("Connecting...");

        ws = new WebSocketClient();
        ws.OnMessage += HandleMessage;

        if (string.IsNullOrEmpty(AuthState.AccessToken))
        {
            AuthState.AccessToken = await AuthApi.Login();
        }
        SetStatus("Fetching ticket...");

        var ticket = await RealtimeTicketApi.Fetch();
        if (string.IsNullOrEmpty(ticket))
        {
            SetStatus("Failed to get WS ticket");
            return;
        }
        SetStatus("Opening WS...");

        await ws.Connect(
            $"{AuthState.wsUrl}/api/realtime/session?lang={AuthState.learningLanguage}",
            ticket
        );
        SetStatus("Connected! Waiting for mic...");

        mic = new MicrophoneStreamer();
        mic.noiseThreshold = AuthState.micNoiseGate;
        mic.Start();
        isConnected = true;
        SetStatus("Ready — speak!");

        mic.OnChunkReady += async (pcm) =>
        {
            if (isAiSpeaking || isWaitingForResponse) return;
            if (playback != null && playback.IsPlaying) return;

            await ws.Send(new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(pcm)
            });
        };
    }

    void Update()
    {
        ws?.Update();
        if (mic != null)
        {
            mic.noiseThreshold = AuthState.micNoiseGate;
            mic.Update();
        }

        if (playback == null)
            playback = FindAnyObjectByType<AudioPlayback>();
        if (transcriptText == null || transcriptScroll == null)
        {
            var go = GameObject.Find("Canvas AI");
            if (go != null)
            {
                if (transcriptText == null)
                    transcriptText = go.GetComponentInChildren<TMP_Text>(true);
                if (transcriptScroll == null)
                    transcriptScroll = go.GetComponentInChildren<ScrollRect>(true);

                if (transcriptScroll == null)
                {
                    Debug.Log($"[Scroll] still no ScrollRect on Canvas AI. hierarchy:");
                    DumpHierarchy(go.transform, 0);
                }
                else
                {
                    Debug.Log($"[Scroll] resolved: text={transcriptText.name}, scroll={transcriptScroll.name}");
                }
            }
        }

        HandleScrollInput();

        while (mainThreadActions.TryDequeue(out var action))
            action();
    }

    void HandleScrollInput()
    {
        if (userScrollCooldown > 0f) userScrollCooldown -= Time.deltaTime;

        bool up = ReadButton(XRNode.RightHand, CommonUsages.secondaryButton)
               || ReadButton(XRNode.LeftHand, CommonUsages.secondaryButton);
        bool down = ReadButton(XRNode.RightHand, CommonUsages.primaryButton)
                 || ReadButton(XRNode.LeftHand, CommonUsages.primaryButton);

        if (up || down)
        {
            Debug.Log($"[Scroll] btn up={up} down={down} scroll={(transcriptScroll != null ? transcriptScroll.name : "NULL")}");

            if (transcriptScroll != null)
            {
                float contentH = transcriptScroll.content != null ? transcriptScroll.content.rect.height : -1f;
                float viewportH = transcriptScroll.viewport != null ? transcriptScroll.viewport.rect.height : ((RectTransform)transcriptScroll.transform).rect.height;
                float vpos = transcriptScroll.verticalNormalizedPosition;
                Debug.Log($"[Scroll] contentH={contentH} viewportH={viewportH} vpos={vpos} scrollable={contentH > viewportH}");

                float dir = (up ? 1f : 0f) - (down ? 1f : 0f);
                transcriptScroll.verticalNormalizedPosition = Mathf.Clamp01(vpos + dir * Time.deltaTime * 1.5f);
                userScrollCooldown = 2f;
            }
        }
    }

    static bool ReadButton(XRNode node, InputFeatureUsage<bool> feature)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return false;
        device.TryGetFeatureValue(feature, out bool value);
        return value;
    }

    static void DumpHierarchy(Transform t, int depth)
    {
        var comps = t.GetComponents<Component>();
        var names = new string[comps.Length];
        for (int i = 0; i < comps.Length; i++)
            names[i] = comps[i] != null ? comps[i].GetType().Name : "null";
        Debug.Log($"[Scroll] {new string(' ', depth * 2)}- {t.name} [{string.Join(",", names)}] active={t.gameObject.activeInHierarchy}");
        for (int i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), depth + 1);
    }

    async void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        transcriptText = null;
        playback = null;

        if (scene.name == "TestScene")
            await TryInit();
    }

    async Task TryInit()
    {
        if (isConnected) return;

        try
        {
            await Init();
        }
        catch (System.Net.Http.HttpRequestException e)
        {
            SetStatus("⚠ Can't reach server — check Wi-Fi");
            Debug.LogError("Network error: " + e);
        }
        catch (Exception e)
        {
            SetStatus($"ERROR: {e.Message}");
            Debug.LogError("INIT CRASH: " + e);
        }
    }

    void HandleMessage(WsMessage evt)
    {
        mainThreadActions.Enqueue(() =>
        {
            if (evt.type == null) return;

            if (evt.type == "response.audio.delta" && evt.delta != null)
            {
                if (!isAiSpeaking) SetStatus("AI speaking...");
                isAiSpeaking = true;

                byte[] pcm = Convert.FromBase64String(evt.delta);
                if (playback != null)
                    playback.PushPCM(pcm);
            }

            if (evt.type == "response.audio_transcript.delta" && evt.delta != null)
            {
                liveAi += evt.delta;
                UpdateDisplay();
            }

            if (evt.type == "input_audio_buffer.speech_stopped")
            {
                isWaitingForResponse = true;
                SetStatus("Processing...");
            }

            if (evt.type == "response.done")
            {
                if (!string.IsNullOrEmpty(evt.conversationId))
                    CurrentConversationId = evt.conversationId;

                if (!string.IsNullOrEmpty(evt.userTranscript))
                    AddHistory("You", COLOR_USER, evt.userTranscript);
                if (!string.IsNullOrEmpty(liveAi))
                {
                    AddHistory("AI", COLOR_AI, liveAi);
                    liveAi = "";
                }

                isAiSpeaking = false;
                isWaitingForResponse = false;
                SetStatus("Ready — speak!");
            }

            if (evt.type == "input_audio_buffer.speech_started")
            {
                isAiSpeaking = false;
                isWaitingForResponse = false;
                SetStatus("Listening...");
            }
        });
    }
}
