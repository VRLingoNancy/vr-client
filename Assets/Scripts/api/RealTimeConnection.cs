using UnityEngine;
using System;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Security.Cryptography;
//using Newtonsoft.Json;

public class VRLingoClient : MonoBehaviour
{
    private string lang = "fr-FR";

    private ClientWebSocket ws;
    private CancellationTokenSource cancellation = new CancellationTokenSource();

    private bool isAiSpeaking = false;
    private float silenceTimer = 0f;

    async void Start()
    {
        Debug.Log("VRLingo Client");

        string token = await AuthApi.Login();

        if (token == null)
        {
            Debug.LogError("Login failed");
            return;
        }

        await ConnectWebSocket(token);
    }

    async Task ConnectWebSocket(string token)
    {
        ws = new ClientWebSocket();

        ws.Options.SetRequestHeader("Authorization", $"Bearer {token.Trim()}");

        string realtimeSessionUrl = $"{AuthState.wsUrl}/api/realtime/session?lang={this.lang}";

        await ws.ConnectAsync(new Uri(realtimeSessionUrl), cancellation.Token);

        Debug.Log("WebSocket connecté");

        ReceiveLoop();
        SendAudioLoop();
    }

    async void ReceiveLoop()
    {
        byte[] buffer = new byte[8192];

        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);

            string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

            HandleMessage(msg);
        }
    }

    void HandleMessage(string data)
    {
        try
        {
            Debug.Log("RAW WS: " + data);

            ServerEvent evt = JsonUtility.FromJson<ServerEvent>(data);
            //ServerEvent evt = JsonConvert.DeserializeObject<ServerEvent>(data);

            if (evt == null || string.IsNullOrEmpty(evt.type))
            {
                Debug.LogWarning("Message invalide ou non parsé: " + data);
                return;
            }

            if (evt.type == "response.audio.delta")
            {
                isAiSpeaking = true;
                silenceTimer = 1.5f;

                byte[] audio = Convert.FromBase64String(evt.delta);

                Debug.Log("Audio chunk reçu: " + audio.Length);
            }

            if (evt.type == "response.audio_transcript.delta")
            {
                Debug.Log(evt.delta);
            }

            if (evt.type == "response.done")
            {
                isAiSpeaking = false;
            }

            if (evt.type == "input_audio_buffer.speech_started")
            {
                isAiSpeaking = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    async void SendAudioLoop()
    {
        while (ws != null && ws.State == WebSocketState.Open)
        {
            if (!isAiSpeaking)
            {
                byte[] fakeAudio = GenerateFakeAudio(); // temporaire

                await SendAudio(fakeAudio);
            }

            await Task.Delay(50); // ~20 FPS audio
        }
    }

    public async Task SendAudio(byte[] pcm)
    {
        if (isAiSpeaking || ws == null || ws.State != WebSocketState.Open) return;

        string base64 = Convert.ToBase64String(pcm);

        AudioEvent evt = new AudioEvent
        {
            type = "input_audio_buffer.append",
            audio = base64
        };

        string json = JsonUtility.ToJson(evt);

        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellation.Token
        );
    }

    byte[] GenerateFakeAudio()
    {
        byte[] buffer = new byte[512];

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)UnityEngine.Random.Range(0, 255);

        return buffer;
    }

    [Serializable]
    class ServerEvent
    {
        public string type;
        public string delta;
    }

    [Serializable]
    class AudioEvent
    {
        public string type;
        public string audio;
    }
}
