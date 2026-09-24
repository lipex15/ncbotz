using Microsoft.Data.Sqlite;

namespace BotNC.App.Services;

internal sealed record StatisticEvent(string Id, string Profile, int Client, string Session,
    DateTimeOffset At, string Kind, string Description, long? Gold = null, double Quantity = 1,
    string Evidence = "")
{
    public string DisplayTime => At.ToLocalTime().ToString("dd/MM HH:mm:ss");
    public string DisplayClient => $"Cliente {Client}";
    public string DisplayValue => Kind == "pending" ? Gold is { } candidate ? $"{candidate:N0} ouro · pendente" : "Não confirmado" : Kind == "expense"
        ? Gold is { } value ? $"{value:N0} ouro" : "Valor não apurado"
        : Kind == "farm" ? $"{Quantity / 60:N1} min" : "Registrado";
}

internal sealed class StatisticsStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;
    private SqliteConnection Connect() => new(new SqliteConnectionStringBuilder
    {
        DataSource = System.IO.Path.Combine(directory, "statistics.db"),
        DefaultTimeout = 2
    }.ToString());

    internal async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            System.IO.Directory.CreateDirectory(directory);
            await using var connection = Connect();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS events (
                    profile TEXT NOT NULL, client INTEGER NOT NULL, id TEXT NOT NULL,
                    session TEXT NOT NULL, at INTEGER NOT NULL, kind TEXT NOT NULL,
                    description TEXT NOT NULL, gold INTEGER NULL, quantity REAL NOT NULL,
                    evidence TEXT NOT NULL, PRIMARY KEY(profile, client, id));
                CREATE INDEX IF NOT EXISTS events_period ON events(profile,at);
                """;
            await command.ExecuteNonQueryAsync();
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    internal async Task RecordAsync(StatisticEvent item)
    {
        if (item.Client is < 1 or > 2 || item.Gold < 0 || !double.IsFinite(item.Quantity) || item.Quantity < 0)
            throw new ArgumentException("Evento estatístico inválido.");
        await InitializeAsync();
        await _gate.WaitAsync();
        try
        {
            await using var connection = Connect();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO events VALUES($profile,$client,$id,$session,$at,$kind,$description,$gold,$quantity,$evidence)
                ON CONFLICT(profile,client,id) DO UPDATE SET kind=excluded.kind,gold=excluded.gold,evidence=excluded.evidence
                WHERE events.kind='pending' AND excluded.kind='expense'
                """;
            command.Parameters.AddWithValue("$profile", item.Profile);
            command.Parameters.AddWithValue("$client", item.Client);
            command.Parameters.AddWithValue("$id", item.Id);
            command.Parameters.AddWithValue("$session", item.Session);
            command.Parameters.AddWithValue("$at", item.At.UtcTicks);
            command.Parameters.AddWithValue("$kind", item.Kind);
            command.Parameters.AddWithValue("$description", item.Description);
            command.Parameters.AddWithValue("$gold", (object?)item.Gold ?? DBNull.Value);
            command.Parameters.AddWithValue("$quantity", item.Quantity);
            command.Parameters.AddWithValue("$evidence", item.Evidence);
            await command.ExecuteNonQueryAsync();
        }
        finally { _gate.Release(); }
    }

    internal async Task<IReadOnlyList<StatisticEvent>> ReadAsync(string profile, DateTimeOffset since,
        DateTimeOffset until, int? client = null, string? session = null)
    {
        await InitializeAsync();
        await using var connection = Connect();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,client,session,at,kind,description,gold,quantity,evidence FROM events
            WHERE profile=$profile AND at >= $since AND at < $until
            AND ($client IS NULL OR client=$client) AND ($session IS NULL OR session=$session)
            ORDER BY at DESC
            """;
        command.Parameters.AddWithValue("$profile", profile);
        command.Parameters.AddWithValue("$since", since.UtcTicks);
        command.Parameters.AddWithValue("$until", until.UtcTicks);
        command.Parameters.AddWithValue("$client", (object?)client ?? DBNull.Value);
        command.Parameters.AddWithValue("$session", (object?)session ?? DBNull.Value);
        var result = new List<StatisticEvent>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new(reader.GetString(0), profile, reader.GetInt32(1), reader.GetString(2),
                new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.GetDouble(7), reader.GetString(8)));
        return result;
    }
}
