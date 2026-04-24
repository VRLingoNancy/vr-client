public static class AuthState
{
    public static string AccessToken = "";
    public static string learningLanguage = "fr"; // one of: fr, en, it, de
    public static float micNoiseGate = 0.05f;     // RMS threshold; 0 = no gate
    public static string ip = "10.18.207.11"; // Update with local ip (ipconfig / ifconfig)
    public static readonly string port = "3000";
    public static string apiUri => $"{ip}:{port}";
    public static string httpUrl => $"http://{apiUri}";
    public static string wsUrl => $"ws://{apiUri}";
    public static readonly string EMAIL = "test@vrlingo.local"; // TODO : remove this and use Meta token account
    public static readonly string PASSWORD = "test1234"; // TODO : remove this and use Meta token account
}
