using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Lantern.Configuration;

namespace Lantern.Devices;

internal sealed class DeviceRepository(IOptions<DatabaseOptions> options, ILogger<DeviceRepository> logger)
{
    private const string TimestampFormat = "O";
    private const string InitialScanCompletedKey = "initial_scan_completed";
    private const string NotificationPendingKeyPrefix = "notification_pending:";
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

    public async Task<RepositoryResult> GetAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(macAddress, out var normalized))
        {
            return new InvalidMacAddressRepositoryErrorResult();
        }

        try
        {
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
            var device = await reader.ReadAsync(cancellationToken) ? ReadDevice(reader) : null;
            return new RepositoryResult<Device?>(device);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to read device {MacAddress}", normalized);
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> UpsertObservationAsync(
        DeviceObservation observation,
        CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(observation.MacAddress, out var normalized))
        {
            return new InvalidMacAddressRepositoryErrorResult();
        }

        try
        {
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
            return new SuccessRepositoryResult();
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to upsert observation for device {MacAddress}", normalized);
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> IsInitialScanCompletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", InitialScanCompletedKey);
            var value = await command.ExecuteScalarAsync(cancellationToken) as string;
            return new RepositoryResult<bool>(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to read initial scan state");
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> MarkInitialScanCompletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_state (key, value) VALUES ($key, 'true')
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$key", InitialScanCompletedKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new SuccessRepositoryResult();
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to mark initial scan completed");
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> MarkNotificationDeliveredAsync(
        string macAddress,
        DateTimeOffset deliveredAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(macAddress, out var normalized))
        {
            return new InvalidMacAddressRepositoryErrorResult();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                UPDATE devices
                SET last_notification_utc = $deliveredAtUtc
                WHERE mac_address = $macAddress;

                DELETE FROM app_state WHERE key = $pendingKey;
                """;
            command.Parameters.AddWithValue("$macAddress", normalized);
            command.Parameters.AddWithValue("$deliveredAtUtc", deliveredAtUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$pendingKey", NotificationPendingKeyPrefix + normalized);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SuccessRepositoryResult();
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to record notification delivery for device {MacAddress}", normalized);
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> IsNotificationPendingAsync(
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(macAddress, out var normalized))
        {
            return new InvalidMacAddressRepositoryErrorResult();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM app_state WHERE key = $key;";
            command.Parameters.AddWithValue("$key", NotificationPendingKeyPrefix + normalized);
            return new RepositoryResult<bool>(await command.ExecuteScalarAsync(cancellationToken) is not null);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to read notification state for device {MacAddress}", normalized);
            return new ErrorRepositoryResult();
        }
    }

    public async Task<RepositoryResult> MarkNotificationPendingAsync(
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        if (!MacAddress.TryNormalize(macAddress, out var normalized))
        {
            return new InvalidMacAddressRepositoryErrorResult();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO app_state (key, value) VALUES ($key, 'true');";
            command.Parameters.AddWithValue("$key", NotificationPendingKeyPrefix + normalized);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new SuccessRepositoryResult();
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Failed to persist notification state for device {MacAddress}", normalized);
            return new ErrorRepositoryResult();
        }
    }

    public async Task<bool> IsAccessibleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database accessibility check failed");
            return false;
        }
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
