namespace Lantern.Configuration;

internal sealed class MikroTikOptions
{
    public const string BaseUrlEnvironmentVariable = "MIKROTIK_BASE_URL";
    public const string UsernameEnvironmentVariable = "MIKROTIK_USERNAME";
    public const string PasswordEnvironmentVariable = "MIKROTIK_PASSWORD";
    public const string AllowInvalidCertificateEnvironmentVariable = "MIKROTIK_ALLOW_INVALID_CERTIFICATE";

    public string BaseUrl { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public bool AllowInvalidCertificate { get; set; }
}
