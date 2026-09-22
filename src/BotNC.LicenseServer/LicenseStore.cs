using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BotNC.LicenseServer;

internal sealed record LicenseUser(long Id, string Username, string DisplayName, string PasswordHash,
    string? DeviceId, string? DeviceFingerprint, string? MachineName, bool Disabled);

internal sealed record LicenseAlert(long Id, string Username, string AttemptedMachine,
    string ExistingMachine, string OccurredUtc);
internal enum DeviceBindingResult { Allowed, Denied, DeniedWithNewAlert }

internal sealed class LicenseStore(string databasePath)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    display_name TEXT NOT NULL,
                    password_hash TEXT NOT NULL,
                    device_id TEXT,
                    device_fingerprint TEXT,
                    machine_name TEXT,
                    disabled INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS alerts (
                    id INTEGER PRIMARY KEY,
                    username TEXT NOT NULL,
                    attempted_machine TEXT NOT NULL,
                    existing_machine TEXT NOT NULL,
                    occurred_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key=$key";
            command.Parameters.AddWithValue("$key", key);
            return (string?)await command.ExecuteScalarAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CreateUserAsync(string username, string displayName, string password)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO users(username,display_name,password_hash) VALUES($user,$name,$hash)";
            command.Parameters.AddWithValue("$user", username);
            command.Parameters.AddWithValue("$name", displayName);
            command.Parameters.AddWithValue("$hash", HashPassword(password));
            return await command.ExecuteNonQueryAsync() == 1;
        }
        finally { _gate.Release(); }
    }

    public async Task<LicenseUser?> GetUserAsync(string username)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,username,display_name,password_hash,device_id,device_fingerprint,machine_name,disabled FROM users WHERE username=$user";
            command.Parameters.AddWithValue("$user", username);
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? ReadUser(reader) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<LicenseUser>> ListUsersAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var users = new List<LicenseUser>();
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,username,display_name,password_hash,device_id,device_fingerprint,machine_name,disabled FROM users ORDER BY username";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) users.Add(ReadUser(reader));
            return users;
        }
        finally { _gate.Release(); }
    }

    public async Task<DeviceBindingResult> BindOrRejectAsync(LicenseUser user, string deviceId, string fingerprint, string machineName)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            if (user.DeviceId is null)
            {
                await using var bind = connection.CreateCommand();
                bind.CommandText = "UPDATE users SET device_id=$id,device_fingerprint=$fp,machine_name=$name WHERE id=$user AND device_id IS NULL AND disabled=0";
                bind.Parameters.AddWithValue("$id", deviceId);
                bind.Parameters.AddWithValue("$fp", fingerprint);
                bind.Parameters.AddWithValue("$name", machineName);
                bind.Parameters.AddWithValue("$user", user.Id);
                if (await bind.ExecuteNonQueryAsync() == 1) return DeviceBindingResult.Allowed;
            }
            else if (string.Equals(user.DeviceId, deviceId, StringComparison.Ordinal) &&
                     string.Equals(user.DeviceFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return DeviceBindingResult.Allowed;
            }

            await using var alert = connection.CreateCommand();
            alert.CommandText = """
                INSERT INTO alerts(username,attempted_machine,existing_machine,occurred_utc)
                SELECT $user,$attempt,COALESCE(machine_name,'(desconhecido)'),$time FROM users
                WHERE id=$id AND NOT EXISTS (
                    SELECT 1 FROM alerts WHERE username=$user AND attempted_machine=$attempt
                    AND occurred_utc >= $cutoff
                )
                """;
            alert.Parameters.AddWithValue("$user", user.Username);
            alert.Parameters.AddWithValue("$attempt", machineName);
            alert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            alert.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddMinutes(-15).ToString("O"));
            alert.Parameters.AddWithValue("$id", user.Id);
            return await alert.ExecuteNonQueryAsync() == 1
                ? DeviceBindingResult.DeniedWithNewAlert : DeviceBindingResult.Denied;
        }
        finally { _gate.Release(); }
    }

    public async Task SetUserStateAsync(long userId, bool? disabled, bool resetDevice)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = resetDevice
                ? "UPDATE users SET device_id=NULL,device_fingerprint=NULL,machine_name=NULL WHERE id=$id"
                : "UPDATE users SET disabled=$disabled WHERE id=$id";
            command.Parameters.AddWithValue("$id", userId);
            if (!resetDevice) command.Parameters.AddWithValue("$disabled", disabled == true ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task SetUserPasswordAsync(long userId, string password)
    {
        await _gate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE users SET password_hash=$hash WHERE id=$id";
            command.Parameters.AddWithValue("$hash", HashPassword(password));
            command.Parameters.AddWithValue("$id", userId);
            await command.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<LicenseAlert>> ListAlertsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var alerts = new List<LicenseAlert>();
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,username,attempted_machine,existing_machine,occurred_utc FROM alerts ORDER BY id DESC LIMIT 100";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                alerts.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            return alerts;
        }
        finally { _gate.Release(); }
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static LicenseUser ReadUser(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt64(7) != 0);

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256:310000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split(':');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) ||
            iterations is < 100_000 or > 1_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
