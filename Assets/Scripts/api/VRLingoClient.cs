using UnityEngine;
using System;
using System.Threading.Tasks;
using TMPro;
using System.Collections.Concurrent;

public class VRLingoClient : MonoBehaviour
{
    private WebSocketClient ws;
    private MicrophoneStreamer mic;
    public AudioPlayback playback;
    private ConcurrentQueue<Action> mainThreadActions = new();

    private bool isAiSpeaking = false;
    private string learningLanguage = "fr-FR";

    public TMP_Text transcriptText;

    async void Start()
    {
        try
        {
            await Init();
        }
        catch (Exception e)
        {
            Debug.LogError("INIT CRASH: " + e);
        }
    }

    async Task Init()
    {
        ws = new WebSocketClient();

        ws.OnMessage += HandleMessage;

        string token = await AuthApi.Login();
        Debug.Log("test");

        await ws.Connect(
            $"{AuthState.wsUrl}/api/realtime/session?lang={learningLanguage}",
            token
        );

        mic = new MicrophoneStreamer();
        mic.Start();

        mic.OnChunkReady += async (pcm) =>
        {
            if (isAiSpeaking) return;

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
    }

    void HandleMessage(WsMessage evt)
    {
        mainThreadActions.Enqueue(() =>
        {
            if (evt.type == null) return;

            if (evt.type == "response.audio.delta" && evt.delta != null)
            {
                isAiSpeaking = true;

                byte[] pcm = Convert.FromBase64String(evt.delta);
                playback.PushPCM(pcm);
            }

            if (evt.type == "response.audio_transcript.delta" && evt.delta != null)
            {
                transcriptText.text += evt.delta;
            }

            if (evt.type == "response.done")
            {
                isAiSpeaking = false;
                transcriptText.text += "\n";
            }

            if (evt.type == "input_audio_buffer.speech_started")
            {
                isAiSpeaking = false;
                transcriptText.text = "";
            }
        });
    }
}
