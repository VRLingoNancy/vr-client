using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

/// <summary>
/// Avatar speech controller with A/B backend mode switch:
/// - REST pipeline: STT -> Conversation -> TTS
/// - Realtime pipeline: direct WebSocket to /api/realtime/session
/// </summary>
public class AIScript : MonoBehaviour
{
    private enum ConversationMode
    {
        RestSttChatTts = 0,
        RealtimeAudioModel = 1,
    }

    [Header("Backend")]
    [SerializeField] private string backendBaseUrl = "http://localhost:3000";
    [SerializeField] private string userId = "demo-user";
    [SerializeField] private string sessionId = "unity-session";
    [SerializeField] private string locale = "fr-FR";
    [SerializeField] private string targetLanguage = "fr-FR";
    [SerializeField] private string proficiencyLevel = "A2";
    [SerializeField] private string ttsVoice = "alloy";

    [Header("Avatar Output")]
    [SerializeField] private AudioSource avatarAudioSource;
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private string talkingBoolParameter = "IsTalking";
    [SerializeField] private TMP_Text subtitleLabel;

    [Header("Mode")]
    [SerializeField] private ConversationMode conversationMode = ConversationMode.RestSttChatTts;
    [SerializeField] private AIRealTime realtimeClient;

    [Header("Input Settings")]
    [SerializeField] private InputActionProperty pushToTalkKey;
    [SerializeField] private float maxRecordSeconds = 10f;
    [SerializeField] private int microphoneSampleRate = 24000;

    private string microphoneDevice;
    private AudioClip recordingClip;
    private bool isRecording;

    private void Awake()
    {
        if (avatarAudioSource == null)
        {
            avatarAudioSource = GetComponent<AudioSource>();
        }

        if (string.IsNullOrWhiteSpace(backendBaseUrl) && !string.IsNullOrWhiteSpace(AuthState.httpUrl))
        {
            backendBaseUrl = AuthState.httpUrl;
        }
        else if (!string.IsNullOrWhiteSpace(AuthState.httpUrl) && backendBaseUrl.Contains("localhost"))
        {
            backendBaseUrl = AuthState.httpUrl;
        }

        if (realtimeClient == null)
        {
            realtimeClient = GetComponent<AIRealTime>();
        }
        if (realtimeClient == null)
        {
            realtimeClient = FindObjectOfType<AIRealTime>();
        }
        realtimeClient?.SetLanguage(targetLanguage);

        if (Microphone.devices.Length > 0)
        {
            microphoneDevice = Microphone.devices[0];
        }
        else
        {
            Debug.LogWarning("AIScript: No microphone detected.");
        }
    }

    private void Update()
    {
        if (microphoneDevice == null) { return; }

        if (pushToTalkKey.action.WasPressedThisFrame() && !isRecording)
        {
            StartRecording();
        }
        else if (pushToTalkKey.action.WasReleasedThisFrame() && isRecording)
        {
            StopRecordingAndProcess();
        }
    }

    public void SubmitTextPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) { return; }
        var trimmed = prompt.Trim();
        subtitleLabel?.SetText($"AIScript: sending text prompt \"{trimmed}\" ({conversationMode})");

        if (conversationMode == ConversationMode.RealtimeAudioModel)
        {
            StartCoroutine(RealtimeTextPromptPipeline(trimmed));
        }
        else
        {
            StartCoroutine(ConversationPipeline(trimmed));
        }
    }

    private void StartRecording()
    {
        recordingClip = Microphone.Start(microphoneDevice, false, Mathf.CeilToInt(maxRecordSeconds), microphoneSampleRate);
        isRecording = true;
        SetTalkingAnimation(true);
        subtitleLabel?.SetText("🎙️ Parlez...");
    }

    private void StopRecordingAndProcess()
    {
        int position = Microphone.GetPosition(microphoneDevice);
        subtitleLabel?.SetText($"AIScript: captured {position} samples (device={microphoneDevice})");
        Microphone.End(microphoneDevice);
        isRecording = false;

        if (position <= 0 || recordingClip == null)
        {
            subtitleLabel?.SetText("Aucune capture détectée.");
            SetTalkingAnimation(false);
            return;
        }

        float[] samples = new float[position * recordingClip.channels];
        recordingClip.GetData(samples, 0);

        AudioClip cleanedClip = AudioClip.Create("utterance", position, recordingClip.channels, recordingClip.frequency, false);
        cleanedClip.SetData(samples, 0);
        SetTalkingAnimation(false);

        if (conversationMode == ConversationMode.RealtimeAudioModel)
        {
            StartCoroutine(RealtimeAudioPipeline(cleanedClip));
        }
        else
        {
            StartCoroutine(ConversationPipeline(cleanedClip));
        }
    }

    private IEnumerator ConversationPipeline(AudioClip clip)
    {
        float turnStartedAt = Time.realtimeSinceStartup;
        float sttStartedAt = Time.realtimeSinceStartup;
        string base64Audio = Convert.ToBase64String(ConvertClipToWav(clip));
        string transcript = null;
        yield return StartCoroutine(TranscribeCoroutine(base64Audio, result => transcript = result));
        float sttMs = (Time.realtimeSinceStartup - sttStartedAt) * 1000f;

        if (string.IsNullOrWhiteSpace(transcript))
        {
            subtitleLabel?.SetText("❌ Échec de la transcription.");
            yield break;
        }

        float chatMs = -1f;
        float ttsMs = -1f;
        subtitleLabel?.SetText($"Vous: {transcript}");
        yield return StartCoroutine(ConversationPipeline(transcript, (chat, tts) =>
        {
            chatMs = chat;
            ttsMs = tts;
        }));

        float totalMs = (Time.realtimeSinceStartup - turnStartedAt) * 1000f;
        Debug.Log(
            $"AIScript[REST] latency ms => STT={sttMs:F0}, CHAT={chatMs:F0}, TTS={ttsMs:F0}, TOTAL={totalMs:F0}"
        );
    }

    private IEnumerator ConversationPipeline(string userMessage)
    {
        yield return StartCoroutine(ConversationPipeline(userMessage, null));
    }

    private IEnumerator ConversationPipeline(string userMessage, Action<float, float> onMetrics)
    {
        float chatStartedAt = Time.realtimeSinceStartup;
        ConversationResponse response = null;
        yield return StartCoroutine(
            PostJsonCoroutine(
                $"{backendBaseUrl}/api/conversation",
                JsonUtility.ToJson(new ConversationRequest
                {
                    message = userMessage,
                    sessionId = sessionId,
                    userId = userId,
                    locale = locale,
                    targetLanguage = targetLanguage,
                    proficiencyLevel = proficiencyLevel,
                }),
                json => response = JsonUtility.FromJson<ConversationResponse>(json)
            )
        );
        float chatMs = (Time.realtimeSinceStartup - chatStartedAt) * 1000f;

        if (response == null || string.IsNullOrWhiteSpace(response.reply))
        {
            subtitleLabel?.SetText("❌ Réponse vide du coach.");
            yield break;
        }

        subtitleLabel?.SetText(response.reply);
        float ttsStartedAt = Time.realtimeSinceStartup;
        yield return StartCoroutine(SpeakCoroutine(response.reply));
        float ttsMs = (Time.realtimeSinceStartup - ttsStartedAt) * 1000f;

        onMetrics?.Invoke(chatMs, ttsMs);
        Debug.Log($"AIScript[REST] text turn latency ms => CHAT={chatMs:F0}, TTS={ttsMs:F0}");
    }

    private IEnumerator RealtimeTextPromptPipeline(string prompt)
    {
        if (!EnsureRealtimeClient())
        {
            subtitleLabel?.SetText("❌ AIRealTime manquant dans la scène.");
            yield break;
        }

        Task<AIRealTime.RealtimeTurnResult> task = realtimeClient.SendTextTurnAsync(prompt);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            subtitleLabel?.SetText("❌ Erreur Realtime (text).");
            Debug.LogError($"AIScript: Realtime text task faulted -> {task.Exception}");
            yield break;
        }

        HandleRealtimeResult(task.Result, "text");
    }

    private IEnumerator RealtimeAudioPipeline(AudioClip clip)
    {
        if (!EnsureRealtimeClient())
        {
            subtitleLabel?.SetText("❌ AIRealTime manquant dans la scène.");
            yield break;
        }

        byte[] pcm16Mono = ConvertClipToPcm16Mono(clip);
        if (pcm16Mono.Length == 0)
        {
            subtitleLabel?.SetText("❌ Audio invalide pour Realtime.");
            yield break;
        }

        Task<AIRealTime.RealtimeTurnResult> task = realtimeClient.SendAudioTurnAsync(pcm16Mono);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            subtitleLabel?.SetText("❌ Erreur Realtime (audio).");
            Debug.LogError($"AIScript: Realtime audio task faulted -> {task.Exception}");
            yield break;
        }

        HandleRealtimeResult(task.Result, "audio");
    }

    private void HandleRealtimeResult(AIRealTime.RealtimeTurnResult result, string mode)
    {
        if (result == null || !result.success)
        {
            string error = result?.error ?? "Tour Realtime échoué.";
            subtitleLabel?.SetText($"❌ {error}");
            Debug.LogError($"AIScript[Realtime:{mode}] failed -> {error}");
            return;
        }

        string userText = string.IsNullOrWhiteSpace(result.userTranscript) ? "(transcript non reçu)" : result.userTranscript;
        string assistantText = string.IsNullOrWhiteSpace(result.assistantText) ? "(texte assistant non reçu)" : result.assistantText;
        subtitleLabel?.SetText($"Vous: {userText}\nIA: {assistantText}");

        Debug.Log(
            $"AIScript[Realtime:{mode}] latency ms => FIRST={result.firstResponseMs:F0}, TOTAL={result.totalResponseMs:F0}"
        );
    }

    private bool EnsureRealtimeClient()
    {
        if (realtimeClient == null)
        {
            realtimeClient = GetComponent<AIRealTime>();
        }
        if (realtimeClient == null)
        {
            realtimeClient = FindObjectOfType<AIRealTime>();
        }
        if (realtimeClient == null)
        {
            return false;
        }

        realtimeClient.SetLanguage(targetLanguage);
        return true;
    }

    private IEnumerator TranscribeCoroutine(string base64Audio, Action<string> onComplete)
    {
        string transcription = null;
        yield return StartCoroutine(
            PostJsonCoroutine(
                $"{backendBaseUrl}/api/transcribe",
                JsonUtility.ToJson(new TranscribeRequest
                {
                    audio = base64Audio,
                    mimeType = "audio/wav",
                }),
                json =>
                {
                    var payload = JsonUtility.FromJson<TranscribeResponse>(json);
                    transcription = payload?.text ?? string.Empty;
                }
            )
        );
        onComplete?.Invoke(transcription);
    }

    private IEnumerator SpeakCoroutine(string text)
    {
        AudioClip spokenClip = null;
        yield return StartCoroutine(
            PostBinaryCoroutine(
                $"{backendBaseUrl}/api/tts",
                JsonUtility.ToJson(new TtsRequest
                {
                    text = text,
                    voice = ttsVoice,
                    format = "wav",
                }),
                data => spokenClip = CreateClipFromWav(data)
            )
        );

        if (spokenClip == null)
        {
            Debug.LogWarning("AIScript: Failed to synthesize speech.");
            yield break;
        }

        if (avatarAudioSource != null)
        {
            avatarAudioSource.Stop();
            avatarAudioSource.clip = spokenClip;
            avatarAudioSource.Play();
        }
    }

    private IEnumerator PostJsonCoroutine(string url, string payload, Action<string> onSuccess)
    {
        using var request = new UnityWebRequest(url, "POST");
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(payloadBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrWhiteSpace(AuthState.AccessToken))
        {
            request.SetRequestHeader("Authorization", $"Bearer {AuthState.AccessToken}");
        }

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"AIScript: Request to {url} failed => {request.error}\n{request.downloadHandler.text}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private IEnumerator PostBinaryCoroutine(string url, string payload, Action<byte[]> onSuccess)
    {
        using var request = new UnityWebRequest(url, "POST");
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(payloadBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrWhiteSpace(AuthState.AccessToken))
        {
            request.SetRequestHeader("Authorization", $"Bearer {AuthState.AccessToken}");
        }

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"AIScript: Binary request to {url} failed => {request.error}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.data);
    }

    private byte[] ConvertClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        byte[] pcmData = new byte[samples.Length * 2];
        const float rescaleFactor = 32767f;

        for (int i = 0; i < samples.Length; ++i)
        {
            short value = (short)Mathf.Clamp(samples[i] * rescaleFactor, short.MinValue, short.MaxValue);
            byte[] bytes = BitConverter.GetBytes(value);
            pcmData[i * 2] = bytes[0];
            pcmData[i * 2 + 1] = bytes[1];
        }

        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream, Encoding.ASCII, true);

        int subChunk2Size = pcmData.Length;
        int chunkSize = 36 + subChunk2Size;
        int byteRate = clip.frequency * clip.channels * 2;
        short blockAlign = (short)(clip.channels * 2);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(chunkSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)clip.channels);
        writer.Write(clip.frequency);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(subChunk2Size);
        writer.Write(pcmData);
        writer.Flush();
        return memoryStream.ToArray();
    }

    private byte[] ConvertClipToPcm16Mono(AudioClip clip)
    {
        if (clip == null || clip.samples == 0)
        {
            return Array.Empty<byte>();
        }

        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        byte[] pcmData = new byte[clip.samples * 2];
        const float rescaleFactor = 32767f;

        for (int i = 0; i < clip.samples; i++)
        {
            float monoSample = samples[i * clip.channels];
            short value = (short)Mathf.Clamp(monoSample * rescaleFactor, short.MinValue, short.MaxValue);
            byte[] bytes = BitConverter.GetBytes(value);
            pcmData[i * 2] = bytes[0];
            pcmData[(i * 2) + 1] = bytes[1];
        }

        return pcmData;
    }

    private AudioClip CreateClipFromWav(byte[] wavBytes)
    {
        using var stream = new MemoryStream(wavBytes);
        using var reader = new BinaryReader(stream, Encoding.ASCII);

        string riff = new string(reader.ReadChars(4));
        if (riff != "RIFF")
        {
            Debug.LogError("AIScript: Invalid WAV header.");
            return null;
        }

        reader.ReadInt32();
        reader.ReadChars(4);
        string fmt = new string(reader.ReadChars(4));
        while (fmt != "fmt ")
        {
            int chunkSize = reader.ReadInt32();
            reader.ReadBytes(chunkSize);
            fmt = new string(reader.ReadChars(4));
        }

        int subChunk1Size = reader.ReadInt32();
        short audioFormat = reader.ReadInt16();
        short channels = reader.ReadInt16();
        int sampleRate = reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt16();
        short bitsPerSample = reader.ReadInt16();

        if (subChunk1Size > 16)
        {
            reader.ReadBytes(subChunk1Size - 16);
        }

        string dataHeader = new string(reader.ReadChars(4));
        while (dataHeader != "data")
        {
            int chunkSize = reader.ReadInt32();
            reader.ReadBytes(chunkSize);
            dataHeader = new string(reader.ReadChars(4));
        }

        int dataSize = reader.ReadInt32();
        byte[] pcm = reader.ReadBytes(dataSize);
        int totalSamples = dataSize / (bitsPerSample / 8);
        float[] samples = new float[totalSamples];

        if (bitsPerSample == 16)
        {
            for (int i = 0; i < totalSamples; i++)
            {
                short sample = BitConverter.ToInt16(pcm, i * 2);
                samples[i] = sample / 32768f;
            }
        }
        else
        {
            Debug.LogError($"AIScript: Unsupported WAV bit depth {bitsPerSample}");
            return null;
        }

        AudioClip clip = AudioClip.Create("assistant", totalSamples / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void SetTalkingAnimation(bool talking)
    {
        if (avatarAnimator != null && !string.IsNullOrEmpty(talkingBoolParameter))
        {
            avatarAnimator.SetBool(talkingBoolParameter, talking);
        }
    }

    [Serializable]
    private class ConversationRequest
    {
        public string message;
        public string sessionId;
        public string userId;
        public string locale;
        public string targetLanguage;
        public string proficiencyLevel;
    }

    [Serializable]
    private class ConversationResponse
    {
        public string reply;
        public string sessionId;
    }

    [Serializable]
    private class TranscribeRequest
    {
        public string audio;
        public string mimeType;
    }

    [Serializable]
    private class TranscribeResponse
    {
        public string text;
    }

    [Serializable]
    private class TtsRequest
    {
        public string text;
        public string voice;
        public string format;
    }
}
