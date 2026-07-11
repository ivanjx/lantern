using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Lantern.Configuration;

namespace Lantern.Devices;

internal sealed class DeviceRepository(IOptions<DatabaseOptions> options, ILogger<DeviceRepository> logger)
{
    private const string TimestampFormat = "O";
    private readonly string _connectionString = CreateConnectionString(options.Value.Path);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS devices (
                mac_address TEXT PRIMARY KEY,
                friendly_name TEXT NULL,
                status INTEGER NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                last_ip_address TEXT NULL,
                last_hostname TEXT NULL,
                last_notification_utc TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_devices_status ON devices(status);
            CREATE INDEX IF NOT EXISTS idx_devices_last_seen ON devices(last_seen_utc);

            CREATE TABLE IF NOT EXISTS app_state (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Database initialized");
    }

    public async Task<Device?> GetAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(macAddress, out var normalized))
        {
            throw new ArgumentException("Invalid MAC address.", nameof(macAddress));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mac_address, friendly_name, status, first_seen_utc, last_seen_utc,
                   last_ip_address, last_hostname, last_notification_utc
            FROM devices
            WHERE mac_address = $macAddress;
            """;
        command.Parameters.AddWithValue("$macAddress", normalized);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDevice(reader) : null;
    }

    public async Task UpsertObservationAsync(DeviceObservation observation, CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(observation.MacAddress, out var normalized))
        {
            throw new ArgumentException("Invalid MAC address.", nameof(observation));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices (
                mac_address, friendly_name, status, first_seen_utc, last_seen_utc,
                last_ip_address, last_hostname, last_notification_utc)
            VALUES ($macAddress, NULL, $status, $observedAtUtc, $observedAtUtc, $ipAddress, $hostname, NULL)
            ON CONFLICT(mac_address) DO UPDATE SET
                last_seen_utc = excluded.last_seen_utc,
                last_ip_address = excluded.last_ip_address,
                last_hostname = excluded.last_hostname;
            """;
        command.Parameters.AddWithValue("$macAddress", normalized);
        command.Parameters.AddWithValue("$status", (int)DeviceStatus.Unknown);
        command.Parameters.AddWithValue("$observedAtUtc", observation.ObservedAtUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ipAddress", (object?)observation.IpAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$hostname", (object?)observation.Hostname ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string CreateConnectionString(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    private static Device ReadDevice(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        (DeviceStatus)reader.GetInt32(2),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
