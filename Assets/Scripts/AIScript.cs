using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal Unity behaviour that connects the avatar to the Fastify backend (conversation + STT + TTS).
/// Drop this on the avatar GameObject, wire an AudioSource and optional subtitle label, then hold the
/// push-to-talk key (default T) to record. On release, the snippet is transcribed, answered, and voiced.
/// </summary>
public class AIScript : MonoBehaviour
{
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

    [Header("Input Settings")]
    [SerializeField] private InputActionProperty pushToTalkKey;
    [SerializeField] private float maxRecordSeconds = 10f;
    [SerializeField] private int microphoneSampleRate = 44100;

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
        subtitleLabel?.SetText($"AIScript: sending text prompt \"{trimmed}\"");
        StartCoroutine(ConversationPipeline(prompt.Trim()));
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

        StartCoroutine(ConversationPipeline(cleanedClip));
    }

    private IEnumerator ConversationPipeline(AudioClip clip)
    {
        string base64Audio = Convert.ToBase64String(ConvertClipToWav(clip));
        string transcript = null;
        yield return StartCoroutine(TranscribeCoroutine(base64Audio, result => transcript = result));

        if (string.IsNullOrWhiteSpace(transcript))
        {
            subtitleLabel?.SetText("❌ Échec de la transcription.");
            yield break;
        }

        subtitleLabel?.SetText($"Vous: {transcript}");
        yield return StartCoroutine(ConversationPipeline(transcript));
    }

    private IEnumerator ConversationPipeline(string userMessage)
    {
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

        if (response == null || string.IsNullOrWhiteSpace(response.reply))
        {
            subtitleLabel?.SetText("❌ Réponse vide du coach.");
            yield break;
        }

        subtitleLabel?.SetText(response.reply);
        yield return StartCoroutine(SpeakCoroutine(response.reply));
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
