using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

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
    [SerializeField] private ConversationMode conversationMode =
        ConversationMode.RestSttChatTts;
    [SerializeField] private AIRealTime realtimeClient;
    [SerializeField] private bool verbosePipelineLogs = true;

    [Header("Input Settings")]
    [SerializeField] private InputActionProperty pushToTalkKey;
    [SerializeField] private float maxRecordSeconds = 10f;
    [SerializeField] private int microphoneSampleRate = 24000;

    private string microphoneDevice;
    private AudioClip recordingClip;
    private bool isRecording;
    private string activeRecordingTraceId = string.Empty;

    private void Awake()
    {
        if (avatarAudioSource == null)
        {
            avatarAudioSource = GetComponent<AudioSource>();
        }

        if (
            string.IsNullOrWhiteSpace(backendBaseUrl) &&
            !string.IsNullOrWhiteSpace(AuthState.httpUrl)
        )
        {
            backendBaseUrl = AuthState.httpUrl;
        }
        else if (
            !string.IsNullOrWhiteSpace(AuthState.httpUrl) &&
            backendBaseUrl.Contains("localhost")
        )
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
            Debug.Log($"AIScript: microphone selected -> {microphoneDevice}");
        }
        else
        {
            Debug.LogWarning("AIScript: No microphone detected.");
        }
    }

    private void Update()
    {
        if (microphoneDevice == null)
        {
            return;
        }

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
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        string trimmed = prompt.Trim();
        string traceId = GenerateTraceId(
            conversationMode == ConversationMode.RealtimeAudioModel
                ? "rt-text"
                : "rest-text"
        );

        subtitleLabel?.SetText($"AIScript: sending text prompt ({conversationMode})");
        LogInfo(traceId, $"SubmitTextPrompt => \"{trimmed}\"");

        if (conversationMode == ConversationMode.RealtimeAudioModel)
        {
            StartCoroutine(RealtimeTextPromptPipeline(trimmed, traceId));
        }
        else
        {
            StartCoroutine(ConversationPipeline(trimmed, traceId));
        }
    }

    private void StartRecording()
    {
        activeRecordingTraceId = GenerateTraceId(
            conversationMode == ConversationMode.RealtimeAudioModel
                ? "rt-voice"
                : "rest-voice"
        );

        recordingClip = Microphone.Start(
            microphoneDevice,
            false,
            Mathf.CeilToInt(maxRecordSeconds),
            microphoneSampleRate
        );
        isRecording = true;
        SetTalkingAnimation(true);
        subtitleLabel?.SetText("🎙️ Parlez...");
        LogInfo(
            activeRecordingTraceId,
            $"Recording started. device={microphoneDevice}, sampleRate={microphoneSampleRate}, maxSeconds={maxRecordSeconds}"
        );
    }

    private void StopRecordingAndProcess()
    {
        string traceId = string.IsNullOrWhiteSpace(activeRecordingTraceId)
            ? GenerateTraceId("voice")
            : activeRecordingTraceId;
        activeRecordingTraceId = string.Empty;

        int position = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        isRecording = false;

        float durationMs =
            recordingClip != null && recordingClip.frequency > 0
                ? (position * 1000f) / recordingClip.frequency
                : -1f;

        subtitleLabel?.SetText($"AIScript: captured {position} samples");
        LogInfo(
            traceId,
            $"Recording stopped. samples={position}, durationMs={durationMs:F0}"
        );

        if (position <= 0 || recordingClip == null)
        {
            subtitleLabel?.SetText("Aucune capture détectée.");
            SetTalkingAnimation(false);
            LogWarn(traceId, "No audio captured.");
            return;
        }

        float[] samples = new float[position * recordingClip.channels];
        recordingClip.GetData(samples, 0);

        AudioClip cleanedClip = AudioClip.Create(
            "utterance",
            position,
            recordingClip.channels,
            recordingClip.frequency,
            false
        );
        cleanedClip.SetData(samples, 0);
        SetTalkingAnimation(false);

        if (conversationMode == ConversationMode.RealtimeAudioModel)
        {
            StartCoroutine(RealtimeAudioPipeline(cleanedClip, traceId));
        }
        else
        {
            StartCoroutine(ConversationPipeline(cleanedClip, traceId));
        }
    }

    private IEnumerator ConversationPipeline(AudioClip clip, string traceId)
    {
        float turnStartedAt = Time.realtimeSinceStartup;

        byte[] wavBytes = ConvertClipToWav(clip);
        string base64Audio = Convert.ToBase64String(wavBytes);
        LogInfo(
            traceId,
            $"REST turn start. wavBytes={wavBytes.Length}, base64Chars={base64Audio.Length}"
        );

        float sttStartedAt = Time.realtimeSinceStartup;
        string transcript = null;
        yield return StartCoroutine(
            TranscribeCoroutine(base64Audio, result => transcript = result, traceId)
        );
        float sttMs = (Time.realtimeSinceStartup - sttStartedAt) * 1000f;

        if (string.IsNullOrWhiteSpace(transcript))
        {
            subtitleLabel?.SetText("❌ Échec de la transcription.");
            LogWarn(traceId, $"STT failed after {sttMs:F0} ms.");
            yield break;
        }

        float chatMs = -1f;
        float ttsMs = -1f;
        subtitleLabel?.SetText($"Vous: {transcript}");
        LogInfo(traceId, $"STT transcript received ({transcript.Length} chars)");

        yield return StartCoroutine(
            ConversationPipeline(
                transcript,
                (chat, tts) =>
                {
                    chatMs = chat;
                    ttsMs = tts;
                },
                traceId
            )
        );

        float totalMs = (Time.realtimeSinceStartup - turnStartedAt) * 1000f;
        LogInfo(
            traceId,
            $"REST latency ms => STT={sttMs:F0}, CHAT={chatMs:F0}, TTS={ttsMs:F0}, TOTAL={totalMs:F0}"
        );
    }

    private IEnumerator ConversationPipeline(string userMessage, string traceId)
    {
        yield return StartCoroutine(ConversationPipeline(userMessage, null, traceId));
    }

    private IEnumerator ConversationPipeline(
        string userMessage,
        Action<float, float> onMetrics,
        string traceId
    )
    {
        float chatStartedAt = Time.realtimeSinceStartup;
        ConversationResponse response = null;
        yield return StartCoroutine(
            PostJsonCoroutine(
                $"{backendBaseUrl}/api/conversation",
                JsonUtility.ToJson(
                    new ConversationRequest
                    {
                        message = userMessage,
                        sessionId = sessionId,
                        userId = userId,
                        locale = locale,
                        targetLanguage = targetLanguage,
                        proficiencyLevel = proficiencyLevel,
                    }
                ),
                json => response = JsonUtility.FromJson<ConversationResponse>(json),
                traceId,
                "conversation"
            )
        );
        float chatMs = (Time.realtimeSinceStartup - chatStartedAt) * 1000f;

        if (response == null || string.IsNullOrWhiteSpace(response.reply))
        {
            subtitleLabel?.SetText("❌ Réponse vide du coach.");
            LogWarn(traceId, $"Conversation response empty after {chatMs:F0} ms.");
            yield break;
        }

        subtitleLabel?.SetText(response.reply);
        float ttsStartedAt = Time.realtimeSinceStartup;
        yield return StartCoroutine(SpeakCoroutine(response.reply, traceId));
        float ttsMs = (Time.realtimeSinceStartup - ttsStartedAt) * 1000f;

        onMetrics?.Invoke(chatMs, ttsMs);
        LogInfo(traceId, $"REST text turn latency ms => CHAT={chatMs:F0}, TTS={ttsMs:F0}");
    }

    private IEnumerator RealtimeTextPromptPipeline(string prompt, string traceId)
    {
        if (!EnsureRealtimeClient())
        {
            subtitleLabel?.SetText("❌ AIRealTime manquant dans la scène.");
            LogWarn(traceId, "AIRealTime component missing.");
            yield break;
        }

        Task<AIRealTime.RealtimeTurnResult> task = realtimeClient.SendTextTurnAsync(
            prompt,
            traceId
        );
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            subtitleLabel?.SetText("❌ Erreur Realtime (text).");
            Debug.LogError(
                $"AIScript[{traceId}] Realtime text task faulted -> {task.Exception}"
            );
            yield break;
        }

        HandleRealtimeResult(task.Result, "text");
    }

    private IEnumerator RealtimeAudioPipeline(AudioClip clip, string traceId)
    {
        if (!EnsureRealtimeClient())
        {
            subtitleLabel?.SetText("❌ AIRealTime manquant dans la scène.");
            LogWarn(traceId, "AIRealTime component missing.");
            yield break;
        }

        byte[] pcm16Mono = ConvertClipToPcm16Mono(clip);
        if (pcm16Mono.Length == 0)
        {
            subtitleLabel?.SetText("❌ Audio invalide pour Realtime.");
            LogWarn(traceId, "Realtime audio invalid (0 byte PCM).");
            yield break;
        }

        LogInfo(traceId, $"Realtime audio send requested ({pcm16Mono.Length} bytes PCM16)");
        Task<AIRealTime.RealtimeTurnResult> task = realtimeClient.SendAudioTurnAsync(
            pcm16Mono,
            traceId
        );
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            subtitleLabel?.SetText("❌ Erreur Realtime (audio).");
            Debug.LogError(
                $"AIScript[{traceId}] Realtime audio task faulted -> {task.Exception}"
            );
            yield break;
        }

        HandleRealtimeResult(task.Result, "audio");
    }

    private void HandleRealtimeResult(AIRealTime.RealtimeTurnResult result, string mode)
    {
        if (result == null || !result.success)
        {
            string traceId = result?.traceId ?? "unknown";
            string error = result?.error ?? "Tour Realtime échoué.";
            subtitleLabel?.SetText($"❌ {error}");
            Debug.LogError($"AIScript[{traceId}] Realtime:{mode} failed -> {error}");
            return;
        }

        string userText = string.IsNullOrWhiteSpace(result.userTranscript)
            ? "(transcript non reçu)"
            : result.userTranscript;
        string assistantText = string.IsNullOrWhiteSpace(result.assistantText)
            ? "(texte assistant non reçu)"
            : result.assistantText;

        subtitleLabel?.SetText($"Vous: {userText}\nIA: {assistantText}");
        LogInfo(
            result.traceId,
            $"Realtime:{mode} latency ms => FIRST={result.firstResponseMs:F0}, TOTAL={result.totalResponseMs:F0}"
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

    private IEnumerator TranscribeCoroutine(
        string base64Audio,
        Action<string> onComplete,
        string traceId
    )
    {
        string transcription = null;
        yield return StartCoroutine(
            PostJsonCoroutine(
                $"{backendBaseUrl}/api/transcribe",
                JsonUtility.ToJson(
                    new TranscribeRequest
                    {
                        audio = base64Audio,
                        mimeType = "audio/wav",
                    }
                ),
                json =>
                {
                    var payload = JsonUtility.FromJson<TranscribeResponse>(json);
                    transcription = payload?.text ?? string.Empty;
                },
                traceId,
                "transcribe"
            )
        );
        onComplete?.Invoke(transcription);
    }

    private IEnumerator SpeakCoroutine(string text, string traceId)
    {
        AudioClip spokenClip = null;
        yield return StartCoroutine(
            PostBinaryCoroutine(
                $"{backendBaseUrl}/api/tts",
                JsonUtility.ToJson(
                    new TtsRequest
                    {
                        text = text,
                        voice = ttsVoice,
                        format = "wav",
                    }
                ),
                data => spokenClip = CreateClipFromWav(data),
                traceId,
                "tts"
            )
        );

        // if (spokenClip == null)
        // {
        //     LogWarn(traceId, "TTS returned no playable clip.");
        //     return;
        // }

        if (avatarAudioSource != null)
        {
            avatarAudioSource.Stop();
            avatarAudioSource.clip = spokenClip;
            avatarAudioSource.Play();
            LogInfo(
                traceId,
                $"Avatar playback started. clipSamples={spokenClip.samples}, frequency={spokenClip.frequency}"
            );
        }
    }

    private IEnumerator PostJsonCoroutine(
        string url,
        string payload,
        Action<string> onSuccess,
        string traceId = null,
        string stage = "json"
    )
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
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            request.SetRequestHeader("X-Trace-Id", traceId);
        }
        request.SetRequestHeader("X-Client-Mode", conversationMode.ToString());
        request.SetRequestHeader("X-Client-Stage", stage);

        float startedAt = Time.realtimeSinceStartup;
        LogInfo(traceId, $"HTTP {stage} -> {url} (payloadBytes={payloadBytes.Length})");
        yield return request.SendWebRequest();
        float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                $"AIScript[{traceId}] HTTP {stage} failed in {elapsedMs:F0}ms => {request.error}, status={request.responseCode}, body={request.downloadHandler.text}"
            );
            yield break;
        }

        LogInfo(
            traceId,
            $"HTTP {stage} OK in {elapsedMs:F0}ms, status={request.responseCode}, responseChars={request.downloadHandler.text?.Length ?? 0}"
        );
        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private IEnumerator PostBinaryCoroutine(
        string url,
        string payload,
        Action<byte[]> onSuccess,
        string traceId = null,
        string stage = "binary"
    )
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
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            request.SetRequestHeader("X-Trace-Id", traceId);
        }
        request.SetRequestHeader("X-Client-Mode", conversationMode.ToString());
        request.SetRequestHeader("X-Client-Stage", stage);

        float startedAt = Time.realtimeSinceStartup;
        LogInfo(traceId, $"HTTP {stage} -> {url} (payloadBytes={payloadBytes.Length})");
        yield return request.SendWebRequest();
        float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                $"AIScript[{traceId}] HTTP {stage} failed in {elapsedMs:F0}ms => {request.error}, status={request.responseCode}"
            );
            yield break;
        }

        int responseBytes = request.downloadHandler.data?.Length ?? 0;
        LogInfo(
            traceId,
            $"HTTP {stage} OK in {elapsedMs:F0}ms, status={request.responseCode}, responseBytes={responseBytes}"
        );
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
            short value = (short)
                Mathf.Clamp(samples[i] * rescaleFactor, short.MinValue, short.MaxValue);
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
            short value = (short)
                Mathf.Clamp(monoSample * rescaleFactor, short.MinValue, short.MaxValue);
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
        reader.ReadInt16();
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

        AudioClip clip = AudioClip.Create(
            "assistant",
            totalSamples / channels,
            channels,
            sampleRate,
            false
        );
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

    private void LogInfo(string traceId, string message)
    {
        if (!verbosePipelineLogs)
        {
            return;
        }

        string safeTrace = string.IsNullOrWhiteSpace(traceId) ? "no-trace" : traceId;
        Debug.Log($"AIScript[{safeTrace}] {message}");
    }

    private void LogWarn(string traceId, string message)
    {
        string safeTrace = string.IsNullOrWhiteSpace(traceId) ? "no-trace" : traceId;
        Debug.LogWarning($"AIScript[{safeTrace}] {message}");
    }

    private static string GenerateTraceId(string prefix)
    {
        string compact = Guid.NewGuid().ToString("N");
        return $"{prefix}-{compact.Substring(0, 8)}";
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
