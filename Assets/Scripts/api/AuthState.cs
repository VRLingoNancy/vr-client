public static class AuthState
{
    public static string AccessToken;
    public static string apiUri = "127.0.0.1:3000";
    public static string httpUrl => $"http://{apiUri}";
    public static string wsUrl => $"ws://{apiUri}";
    public static readonly string EMAIL = "test@vrlingo.local"; // TODO : remove this and use Meta token account
    public static readonly string PASSWORD = "test1234"; // TODO : remove this and use Meta token account

    public static void SetApiUri(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            apiUri = value.Trim();
    }
}
