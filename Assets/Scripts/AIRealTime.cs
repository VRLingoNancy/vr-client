using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

public class AIRealTime : MonoBehaviour
{
    [Serializable]
    public class RealtimeTurnResult
    {
        public bool success;
        public string error;
        public string traceId;
        public string userTranscript;
        public string assistantText;
        public float firstResponseMs;
        public float totalResponseMs;
    }

    [Header("Realtime")]
    [SerializeField] private string languageCode = "fr-FR";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private float turnTimeoutSeconds = 25f;
    [SerializeField] private bool verboseLogs = true;

    private WebSocket ws;
    private bool isConnecting;
    private Task connectTask;
    private string sessionTraceId = string.Empty;
    private string activeTurnTraceId = string.Empty;

    private TaskCompletionSource<RealtimeTurnResult> activeTurnCompletion;
    private float turnStartedAt;
    private float firstResponseAt = -1f;
    private string latestUserTranscript = string.Empty;
    private string latestAssistantText = string.Empty;

    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    private async void Start()
    {
        if (connectOnStart)
        {
            await EnsureConnectedAsync();
        }
    }

    public void SetLanguage(string langCode)
    {
        if (!string.IsNullOrWhiteSpace(langCode))
        {
            languageCode = langCode.Trim();
        }
    }

    public async Task<bool> EnsureConnectedAsync(string traceId = null)
    {
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            sessionTraceId = traceId;
        }

        if (IsConnected)
        {
            return true;
        }

        if (isConnecting && connectTask != null)
        {
            await connectTask;
            return IsConnected;
        }

        connectTask = ConnectInternalAsync();
        await connectTask;
        return IsConnected;
    }

    public async Task<RealtimeTurnResult> SendTextTurnAsync(
        string text,
        string traceId = null
    )
    {
        string turnTraceId = string.IsNullOrWhiteSpace(traceId)
            ? GenerateTraceId("rt-text")
            : traceId.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return FailResult("Prompt texte vide.", turnTraceId);
        }

        if (!await EnsureConnectedAsync(turnTraceId))
        {
            return FailResult("WebSocket non connecté.", turnTraceId);
        }

        BeginTurn(turnTraceId);
        if (verboseLogs)
        {
            Debug.Log(
                $"AIRealTime[{turnTraceId}] send text turn ({text.Length} chars)"
            );
        }

        try
        {
            string escapedText = EscapeJson(text.Trim());
            await ws.SendText(
                $"{{\"event_id\":\"{EscapeJson(turnTraceId)}-input\",\"type\":\"conversation.item.create\",\"item\":{{\"type\":\"message\",\"role\":\"user\",\"content\":[{{\"type\":\"input_text\",\"text\":\"{escapedText}\"}}]}}}}"
            );
            await ws.SendText(
                $"{{\"event_id\":\"{EscapeJson(turnTraceId)}-response\",\"type\":\"response.create\"}}"
            );
        }
        catch (Exception ex)
        {
            CompleteTurn(FailResult($"Erreur envoi WS: {ex.Message}", turnTraceId));
        }

        return await WaitForTurnResultAsync();
    }

    public async Task<RealtimeTurnResult> SendAudioTurnAsync(
        byte[] pcm16MonoAudio,
        string traceId = null
    )
    {
        string turnTraceId = string.IsNullOrWhiteSpace(traceId)
            ? GenerateTraceId("rt-audio")
            : traceId.Trim();

        if (pcm16MonoAudio == null || pcm16MonoAudio.Length == 0)
        {
            return FailResult("Audio vide.", turnTraceId);
        }

        if (!await EnsureConnectedAsync(turnTraceId))
        {
            return FailResult("WebSocket non connecté.", turnTraceId);
        }

        BeginTurn(turnTraceId);
        if (verboseLogs)
        {
            Debug.Log(
                $"AIRealTime[{turnTraceId}] send audio turn ({pcm16MonoAudio.Length} bytes PCM16)"
            );
        }

        try
        {
            const int chunkSize = 12000;
            int chunkIndex = 0;
            for (
                int offset = 0;
                offset < pcm16MonoAudio.Length;
                offset += chunkSize
            )
            {
                int length = Mathf.Min(chunkSize, pcm16MonoAudio.Length - offset);
                string chunkBase64 = Convert.ToBase64String(
                    pcm16MonoAudio,
                    offset,
                    length
                );
                await ws.SendText(
                    $"{{\"event_id\":\"{EscapeJson(turnTraceId)}-chunk-{chunkIndex}\",\"type\":\"input_audio_buffer.append\",\"audio\":\"{chunkBase64}\"}}"
                );
                chunkIndex++;
            }

            await ws.SendText(
                $"{{\"event_id\":\"{EscapeJson(turnTraceId)}-commit\",\"type\":\"input_audio_buffer.commit\"}}"
            );
            await ws.SendText(
                $"{{\"event_id\":\"{EscapeJson(turnTraceId)}-response\",\"type\":\"response.create\"}}"
            );
        }
        catch (Exception ex)
        {
            CompleteTurn(
                FailResult($"Erreur envoi audio WS: {ex.Message}", turnTraceId)
            );
        }

        return await WaitForTurnResultAsync();
    }

    private async Task ConnectInternalAsync()
    {
        isConnecting = true;

        try
        {
            for (
                int i = 0;
                i < 30 && string.IsNullOrWhiteSpace(AuthState.AccessToken);
                i++
            )
            {
                await Task.Delay(100);
            }

            if (string.IsNullOrWhiteSpace(sessionTraceId))
            {
                sessionTraceId = GenerateTraceId("rt-session");
            }

            string url =
                $"{AuthState.wsUrl}/api/realtime/session?lang={Uri.EscapeDataString(languageCode)}&traceId={Uri.EscapeDataString(sessionTraceId)}";

            if (!string.IsNullOrWhiteSpace(AuthState.AccessToken))
            {
                var headers = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {AuthState.AccessToken}" },
                    { "X-Trace-Id", sessionTraceId },
                };
                ws = new WebSocket(url, headers);
            }
            else
            {
                ws = new WebSocket(url);
            }

            ws.OnOpen += () =>
            {
                Debug.Log($"AIRealTime[{sessionTraceId}] WS connected -> {url}");
            };

            ws.OnMessage += OnWebSocketMessage;
            await ws.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"AIRealTime[{sessionTraceId}] connect failed -> {ex.Message}");
        }
        finally
        {
            isConnecting = false;
        }
    }

    private async Task<RealtimeTurnResult> WaitForTurnResultAsync()
    {
        if (activeTurnCompletion == null)
        {
            return FailResult("Aucun tour actif.", activeTurnTraceId);
        }

        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(turnTimeoutSeconds));
        Task completed = await Task.WhenAny(activeTurnCompletion.Task, timeoutTask);

        if (completed == timeoutTask)
        {
            CompleteTurn(FailResult("Timeout Realtime.", activeTurnTraceId));
        }

        return await activeTurnCompletion.Task;
    }

    private void BeginTurn(string turnTraceId)
    {
        activeTurnCompletion = new TaskCompletionSource<RealtimeTurnResult>();
        turnStartedAt = Time.realtimeSinceStartup;
        firstResponseAt = -1f;
        activeTurnTraceId = turnTraceId;
        latestUserTranscript = string.Empty;
        latestAssistantText = string.Empty;
    }

    private void CompleteTurn(RealtimeTurnResult result)
    {
        if (activeTurnCompletion == null || activeTurnCompletion.Task.IsCompleted)
        {
            return;
        }

        activeTurnCompletion.TrySetResult(result);
    }

    private void OnWebSocketMessage(byte[] bytes)
    {
        string message = Encoding.UTF8.GetString(bytes);
        if (verboseLogs)
        {
            Debug.Log($"AIRealTime[{activeTurnTraceId}] <- {message}");
        }

        if (activeTurnCompletion == null || activeTurnCompletion.Task.IsCompleted)
        {
            return;
        }

        string eventType = ExtractStringField(message, "type");
        if (string.IsNullOrEmpty(eventType))
        {
            return;
        }

        if (firstResponseAt < 0f && IsFirstResponseEvent(eventType))
        {
            firstResponseAt = Time.realtimeSinceStartup;
        }

        if (eventType == "conversation.item.input_audio_transcription.completed")
        {
            string transcript = ExtractStringField(message, "transcript");
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                latestUserTranscript = transcript;
            }
        }
        else if (
            eventType == "response.audio_transcript.delta" ||
            eventType == "response.text.delta"
        )
        {
            string delta = ExtractStringField(message, "delta");
            if (!string.IsNullOrWhiteSpace(delta))
            {
                latestAssistantText += delta;
            }
        }
        else if (eventType == "error")
        {
            string err = ExtractStringField(message, "message");
            CompleteTurn(
                FailResult(
                    string.IsNullOrWhiteSpace(err) ? "Erreur Realtime." : err,
                    activeTurnTraceId
                )
            );
        }
        else if (eventType == "response.done")
        {
            float now = Time.realtimeSinceStartup;
            float firstMs =
                firstResponseAt < 0f ? -1f : (firstResponseAt - turnStartedAt) * 1000f;
            float totalMs = (now - turnStartedAt) * 1000f;

            CompleteTurn(
                new RealtimeTurnResult
                {
                    success = true,
                    traceId = activeTurnTraceId,
                    firstResponseMs = firstMs,
                    totalResponseMs = totalMs,
                    userTranscript = latestUserTranscript,
                    assistantText = latestAssistantText,
                }
            );
        }
    }

    private static bool IsFirstResponseEvent(string eventType)
    {
        return eventType == "response.audio.delta" ||
            eventType == "response.audio_transcript.delta" ||
            eventType == "response.text.delta";
    }

    private static string EscapeJson(string raw)
    {
        return raw
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private static string ExtractStringField(string json, string fieldName)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName))
        {
            return string.Empty;
        }

        string pattern =
            $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"";
        Match match = Regex.Match(json, pattern);
        if (!match.Success)
        {
            return string.Empty;
        }

        string encoded = match.Groups["value"].Value;
        return Regex.Unescape(encoded);
    }

    private static RealtimeTurnResult FailResult(string error, string traceId)
    {
        return new RealtimeTurnResult
        {
            success = false,
            error = error,
            traceId = traceId,
            firstResponseMs = -1f,
            totalResponseMs = -1f,
            userTranscript = string.Empty,
            assistantText = string.Empty,
        };
    }

    private static string GenerateTraceId(string prefix)
    {
        string compact = Guid.NewGuid().ToString("N");
        return $"{prefix}-{compact.Substring(0, 8)}";
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    private async void OnApplicationQuit()
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            await ws.Close();
        }
    }
}
