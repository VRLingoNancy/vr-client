using UnityEngine;
using System;
using System.Text;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class WebSocketClient
{
    private ClientWebSocket ws;
    private CancellationTokenSource cancel = new CancellationTokenSource();

    public Action<WsMessage> OnMessage;

    public async Task Connect(string url, string token)
    {
        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");

        await ws.ConnectAsync(new Uri(url), cancel.Token);

        Debug.Log("✅ WS connecté");

        _ = ReceiveLoop();
    }

    async Task ReceiveLoop()
    {
        byte[] buffer = new byte[8192];

        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);

            string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

            try
            {
                var evt = JsonConvert.DeserializeObject<WsMessage>(msg);

                if (evt != null)
                    OnMessage?.Invoke(evt);
                else
                    Debug.LogWarning("Message WS ignoré: " + msg);
            }
            catch (Exception e)
            {
                Debug.LogError("WS parse error: " + e + "\nRAW: " + msg);
            }
        }
    }

    public async Task Send(object obj)
    {
        if (ws.State != WebSocketState.Open) return;

        string json = JsonConvert.SerializeObject(obj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancel.Token
        );
    }
}
