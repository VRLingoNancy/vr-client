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

    private bool isWaitingForResponse = false;
    private bool isConnected = false;
    private bool responseCompletedPendingDrain = false;
    public static bool IsUserSpeaking { get; private set; }
    public static bool IsAiSpeaking { get; private set; }

    public TMP_Text transcriptText;
    private ScrollRect transcriptScroll;
    private float userScrollCooldown = 0f;
    private string currentStatus = "";
    private readonly List<string> history = new();
    private string liveAiFull = "";        // full transcript received
    private int liveAiRevealedCount = 0;   // number of chars currently revealed
    private float liveAiNextRevealTime = 0f;

    // Pacing (COMMA/END pauses are universal; per-syllable time varies by language)
    const float COMMA_PAUSE_SEC = 0.12f;
    const float END_PAUSE_SEC = 0.30f;

    static float SyllableSec()
    {
        switch (AuthState.learningLanguage)
        {
            case "it": return 0.13f;  // ~7.7 syl/s
            case "de": return 0.18f;  // ~5.6 syl/s
            case "en": return 0.15f;  // ~6.7 syl/s
            case "fr":
            default:   return 0.16f;  // ~6.2 syl/s
        }
    }

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
        if (liveAiRevealedCount > 0)
            sb.AppendLine($"<color={COLOR_AI}><b>AI:</b></color> {liveAiFull.Substring(0, liveAiRevealedCount)}");
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
            $"{AuthState.wsUrl}/api/realtime/session?lang={AuthState.learningLanguage}&context={AuthState.aiContext}",
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
            if (IsAiSpeaking || isWaitingForResponse) return;
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

        if (responseCompletedPendingDrain && (playback == null || !playback.IsPlaying))
        {
            responseCompletedPendingDrain = false;
            IsAiSpeaking = false;
            if (!string.IsNullOrEmpty(liveAiFull))
            {
                AddHistory("AI", COLOR_AI, liveAiFull);
                liveAiFull = "";
                liveAiRevealedCount = 0;
            }
            SetStatus("Ready — speak!");
        }

        AdvanceTranscriptReveal();
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

    void AdvanceTranscriptReveal()
    {
        if (liveAiRevealedCount >= liveAiFull.Length) return;
        if (Time.time < liveAiNextRevealTime) return;

        // Reveal next "chunk": a word (until next whitespace) or a lone punctuation.
        int start = liveAiRevealedCount;
        int i = start;
        // Skip leading whitespace
        while (i < liveAiFull.Length && char.IsWhiteSpace(liveAiFull[i])) i++;
        // Consume one word OR one punctuation char
        if (i < liveAiFull.Length && IsPunct(liveAiFull[i])) i++;
        else while (i < liveAiFull.Length && !char.IsWhiteSpace(liveAiFull[i]) && !IsPunct(liveAiFull[i])) i++;

        liveAiRevealedCount = i;

        string revealed = liveAiFull.Substring(start, i - start);
        float wait = SyllableCount(revealed) * SyllableSec();
        foreach (char c in revealed)
        {
            if (c == ',' || c == ';' || c == ':') wait += COMMA_PAUSE_SEC;
            else if (c == '.' || c == '!' || c == '?' || c == '…') wait += END_PAUSE_SEC;
        }
        liveAiNextRevealTime = Time.time + wait;
        UpdateDisplay();
    }

    static bool IsPunct(char c) =>
        c == ',' || c == ';' || c == ':' || c == '.' || c == '!' || c == '?' || c == '…';

    static int SyllableCount(string s)
    {
        int count = 0;
        bool prevVowel = false;
        foreach (char raw in s)
        {
            char c = char.ToLowerInvariant(raw);
            bool vowel = "aeiouyàâäéèêëîïôöùûüœæáíóúãõ".IndexOf(c) >= 0;
            if (vowel && !prevVowel) count++;
            prevVowel = vowel;
        }
        return Mathf.Max(1, count);
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
                if (!IsAiSpeaking)
                {
                    if (playback != null) playback.BeginSession();
                    SetStatus("AI speaking...");
                }
                IsAiSpeaking = true;
                IsUserSpeaking = false;

                byte[] pcm = Convert.FromBase64String(evt.delta);
                if (playback != null)
                    playback.PushPCM(pcm);
            }

            if (evt.type == "response.audio_transcript.delta" && evt.delta != null)
            {
                liveAiFull += evt.delta;
            }

            if (evt.type == "input_audio_buffer.speech_stopped")
            {
                isWaitingForResponse = true;
                IsUserSpeaking = false;
                SetStatus("Processing...");
            }

            if (evt.type == "response.done")
            {
                if (!string.IsNullOrEmpty(evt.conversationId))
                    CurrentConversationId = evt.conversationId;

                if (!string.IsNullOrEmpty(evt.userTranscript))
                    AddHistory("You", COLOR_USER, evt.userTranscript);

                isWaitingForResponse = false;
                responseCompletedPendingDrain = true;
            }

            if (evt.type == "input_audio_buffer.speech_started")
            {
                IsAiSpeaking = false;
                isWaitingForResponse = false;
                IsUserSpeaking = true;
                // Discard any leftover partial AI text if user interrupts.
                liveAiFull = "";
                liveAiRevealedCount = 0;
                SetStatus("Listening...");
            }
        });
    }
}
