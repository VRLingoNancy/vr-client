using Newtonsoft.Json;

public class WsMessage
{
    public string type;
    public string delta;

    [JsonProperty("conversation_id")]
    public string conversationId;

    [JsonProperty("user_transcript")]
    public string userTranscript;
}
