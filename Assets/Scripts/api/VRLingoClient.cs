using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Threading.Tasks;
using TMPro;
using System.Collections.Concurrent;

public class VRLingoClient : MonoBehaviour
{
    private static VRLingoClient instance;

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

        if (SceneManager.GetActiveScene().name == "TestScene")
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
        SetStatus("Connecting...");

        ws = new WebSocketClient();
        ws.OnMessage += HandleMessage;

        string token = await AuthApi.Login();
        SetStatus("Logged in, opening WS...");

        await ws.Connect(
            $"{AuthState.wsUrl}/api/realtime/session?lang={learningLanguage}",
            token
        );
        SetStatus("Connected! Waiting for mic...");

        mic = new MicrophoneStreamer();
        mic.Start();
        isConnected = true;
        SetStatus("Ready — speak!");

        mic.OnChunkReady += async (pcm) =>
        {
            if (isAiSpeaking || isWaitingForResponse) return;

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
}
