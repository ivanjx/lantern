namespace Lantern.Configuration;

internal sealed class DatabaseOptions
{
    public const string PathEnvironmentVariable = "DATABASE_PATH";

    public string Path { get; set; } = "data/lantern.db";
}
