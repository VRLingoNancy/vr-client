using NativeWebSocket;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class WebSocketClient
{
    private WebSocket ws;

    public Action<WsMessage> OnMessage;

    public async Task Connect(string url, string ticket)
    {
        var separator = url.Contains("?") ? "&" : "?";
        var urlWithTicket = $"{url}{separator}ticket={ticket}";
        ws = new WebSocket(urlWithTicket);

        ws.OnOpen += () =>
        {
            Debug.Log("✅ WS connecté");
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
            Debug.Log("WS Closed");
        };

        _ = ws.Connect();

        while (ws.State != WebSocketState.Open)
            await Task.Delay(50);
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
}
