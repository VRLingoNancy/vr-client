public static class AuthState
{
    public static string AccessToken;
    public static readonly string apiUri = "10.115.174.230:3000"; // Update with local ip (ipconfig / ifconfig)
    public static readonly string httpUrl = $"http://{apiUri}";
    public static readonly string wsUrl = $"ws://{apiUri}";
    public static readonly string EMAIL = "test@vrlingo.local"; // TODO : remove this and use Meta token account
    public static readonly string PASSWORD = "test1234"; // TODO : remove this and use Meta token account
}
