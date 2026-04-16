using NativeWebSocket;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;

public class WebSocketClient
{
    private WebSocket ws;
    private const float ConnectTimeoutSeconds = 10f;

    public Action<WsMessage> OnMessage;

    public async Task Connect(string url, string token, string ticket)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Missing JWT token for WebSocket connection.");
        if (string.IsNullOrWhiteSpace(ticket))
            throw new InvalidOperationException("Missing realtime ticket for WebSocket connection.");

        string websocketUrl = BuildUrlWithTicket(url, ticket);
        var headers = new Dictionary<string, string>
        {
            { "Authorization", $"Bearer {token}" }
        };

        ws = new WebSocket(websocketUrl, headers);

        ws.OnOpen += () =>
        {
            Debug.Log($"WS Connected -> {SanitizeUrl(websocketUrl)}");
        };

        ws.OnMessage += (bytes) =>
        {
            var msg = System.Text.Encoding.UTF8.GetString(bytes);

            try
            {
                var evt = JsonConvert.DeserializeObject<WsMessage>(msg);
                OnMessage?.Invoke(evt);
            }
            catch (Exception e)
            {
                Debug.LogError("WS parse error: " + e + "\nRAW: " + msg);
            }
        };

        ws.OnError += (err) =>
        {
            Debug.LogError("WS Error: " + err);
        };

        ws.OnClose += (code) =>
        {
            Debug.Log("WS Closed: " + code);
        };

        _ = ws.Connect();
        float startedAt = Time.realtimeSinceStartup;

        while (ws.State != WebSocketState.Open)
        {
            if (ws.State == WebSocketState.Closed)
                throw new InvalidOperationException("WebSocket closed before opening.");

            if (Time.realtimeSinceStartup - startedAt > ConnectTimeoutSeconds)
                throw new TimeoutException($"WebSocket connect timeout after {ConnectTimeoutSeconds:0}s.");

            await Task.Delay(50);
        }
    }

    public async Task Send(object obj)
    {
        if (ws.State != WebSocketState.Open) return;

        string json = JsonConvert.SerializeObject(obj);
        await ws.SendText(json);
    }

    public void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }

    private static string BuildUrlWithTicket(string url, string ticket)
    {
        string separator = url.Contains("?") ? "&" : "?";
        return $"{url}{separator}ticket={Uri.EscapeDataString(ticket)}";
    }

    private static string SanitizeUrl(string url)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            url,
            @"ticket=[^&]+",
            "ticket=<redacted>"
        );
    }
}
