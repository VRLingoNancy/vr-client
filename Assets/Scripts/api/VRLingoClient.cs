using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Threading.Tasks;
using TMPro;
using System.Collections.Concurrent;

public class VRLingoClient : MonoBehaviour
{
    private static VRLingoClient instance;
    private const string DesktopDefaultBackend = "127.0.0.1:3000";

    [Header("Backend")]
    [SerializeField] private string backendApiUri = "";
    [SerializeField] private bool verboseLogs = true;
    [SerializeField] private string targetSceneName = "TestScene";

    private WebSocketClient ws;
    private MicrophoneStreamer mic;
    public AudioPlayback playback;
    private ConcurrentQueue<Action> mainThreadActions = new();

    private bool isAiSpeaking = false;
    private bool isWaitingForResponse = false;
    private bool isConnected = false;
    private string learningLanguage = "fr-FR";

    public TMP_Text transcriptText;
    private string currentStatus = "";
    private string currentTranscript = "";

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

        if (SceneManager.GetActiveScene().name == targetSceneName)
            await TryInit();
    }

    void SetStatus(string status)
    {
        currentStatus = status;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (transcriptText != null)
            transcriptText.text = $"<b>{currentStatus}</b>\n{currentTranscript}";
    }

    async Task Init()
    {
        ApplyBackendConfiguration();
        SetStatus("Connecting...");
        Log($"Init started. HTTP={AuthState.httpUrl}, WS={AuthState.wsUrl}, scene={SceneManager.GetActiveScene().name}");

        ws = new WebSocketClient();
        ws.OnMessage += HandleMessage;

        string token = await AuthApi.Login();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Login failed against {AuthState.httpUrl}/auth/login");

        AuthState.AccessToken = token;
        Log("Login OK. JWT received.");
        SetStatus("Logged in, requesting WS ticket...");

        string ticket = await AuthApi.CreateRealtimeTicket(token);
        if (string.IsNullOrWhiteSpace(ticket))
            throw new InvalidOperationException($"Realtime ticket request failed against {AuthState.httpUrl}/api/realtime/ws-ticket");

        Log("Realtime ticket OK.");
        SetStatus("Opening WS...");

        await ws.Connect(
            $"{AuthState.wsUrl}/api/realtime/session?lang={learningLanguage}",
            token,
            ticket
        );
        Log("WebSocket connected.");
        SetStatus("Connected! Waiting for mic...");

        mic = new MicrophoneStreamer();
        mic.Start();
        isConnected = true;
        SetStatus("Ready — speak!");

        mic.OnChunkReady += async (pcm) =>
        {
            if (isAiSpeaking || isWaitingForResponse) return;

            Log($"Mic chunk ready: {pcm.Length} bytes");
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
        mic?.Update();

        if (playback == null)
            playback = FindAnyObjectByType<AudioPlayback>();
        if (transcriptText == null)
        {
            var go = GameObject.Find("Canvas AI");
            if (go != null)
                transcriptText = go.GetComponentInChildren<TMP_Text>();
        }

        while (mainThreadActions.TryDequeue(out var action))
            action();
    }

    async void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        transcriptText = null;
        playback = null;
        Log($"Scene loaded: {scene.name}");

        if (scene.name == targetSceneName)
            await TryInit();
    }

    async Task TryInit()
    {
        if (isConnected) return;

        try
        {
            await Init();
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
            Log($"WS event: {evt.type}");

            if (evt.type == "response.audio.delta" && evt.delta != null)
            {
                if (!isAiSpeaking)
                {
                    currentTranscript = "";
                    SetStatus("AI speaking...");
                }
                isAiSpeaking = true;

                byte[] pcm = Convert.FromBase64String(evt.delta);
                if (playback != null)
                    playback.PushPCM(pcm);
            }

            if (evt.type == "response.audio_transcript.delta" && evt.delta != null)
            {
                currentTranscript += evt.delta;
                UpdateDisplay();
            }

            if (evt.type == "input_audio_buffer.speech_stopped")
            {
                isWaitingForResponse = true;
                SetStatus("Processing...");
            }

            if (evt.type == "response.done")
            {
                isAiSpeaking = false;
                isWaitingForResponse = false;
                currentTranscript += "\n";
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

    void ApplyBackendConfiguration()
    {
        string resolvedBackendUri = ResolveBackendApiUri();
        AuthState.SetApiUri(resolvedBackendUri);
        Log($"Using backend endpoint: {AuthState.apiUri}");
    }

    string ResolveBackendApiUri()
    {
        string configuredUri = backendApiUri?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(configuredUri))
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                throw new InvalidOperationException(
                    "Missing backendApiUri. On Quest, set the Player prefab field to the backend PC LAN IP, e.g. 192.168.x.x:3000."
                );
            }

            return DesktopDefaultBackend;
        }

        if (Application.platform == RuntimePlatform.Android && AuthState.IsLoopbackUri(configuredUri))
        {
            throw new InvalidOperationException(
                $"Invalid backendApiUri '{configuredUri}' on Quest. Use the backend PC LAN IP instead of localhost/127.0.0.1."
            );
        }

        return configuredUri;
    }

    void Log(string message)
    {
        if (!verboseLogs) return;
        Debug.Log($"VRLingoClient: {message}");
    }
}
