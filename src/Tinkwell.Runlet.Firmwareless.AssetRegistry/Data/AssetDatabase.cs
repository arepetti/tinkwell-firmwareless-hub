using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;

/// <summary>
/// SQLite-backed storage for assets, permissions, and command queues.
/// Uses WAL mode and serialized writes (same pattern as the legacy hub database).
/// </summary>
public sealed class AssetDatabase : IAsyncDisposable
{
    private const string TimestampFormat = "O";

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AssetDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS assets (
                id                    TEXT PRIMARY KEY,
                display_name          TEXT,
                vendor_id             INTEGER,
                product_id            INTEGER,
                variant               BLOB,
                firmware_version      TEXT,
                communication_mode    TEXT NOT NULL DEFAULT 'service-only',
                firmlet_name          TEXT,
                firmlet_version       TEXT,
                firmlet_initialized   INTEGER NOT NULL DEFAULT 0,
                state                 TEXT NOT NULL DEFAULT 'created',
                created_at            TEXT NOT NULL,
                last_heartbeat        TEXT,
                last_error            TEXT
            );

            CREATE TABLE IF NOT EXISTS commands (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id   TEXT NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                cmd_type   TEXT NOT NULL,
                payload    BLOB NOT NULL DEFAULT X'',
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_commands_asset
                ON commands (asset_id, id);

            CREATE TABLE IF NOT EXISTS permissions (
                asset_id         TEXT NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                permission_type  TEXT NOT NULL,
                target           TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (asset_id, permission_type, target)
            );
            """;
        cmd.ExecuteNonQuery();

        MigrateSchema();
    }

    private void MigrateSchema()
    {
        if (!ColumnExists("assets", "firmlet_initialized"))
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE assets ADD COLUMN firmlet_initialized INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ── Assets ──────────────────────────────────────────────────────────

    public async Task UpsertAssetAsync(Asset asset)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO assets (id, display_name, vendor_id, product_id, variant,
                                    firmware_version, communication_mode, firmlet_name,
                                    firmlet_version, firmlet_initialized, state, created_at,
                                    last_heartbeat, last_error)
                VALUES (@id, @name, @vid, @pid, @var, @fw, @comm, @fn, @fv, @fi, @st, @ca, @lh, @le)
                ON CONFLICT (id) DO UPDATE SET
                    display_name = @name, vendor_id = @vid, product_id = @pid, variant = @var,
                    firmware_version = @fw, communication_mode = @comm, firmlet_name = @fn,
                    firmlet_version = @fv, firmlet_initialized = @fi, state = @st,
                    last_heartbeat = @lh, last_error = @le
                """;
            BindAssetParams(cmd, asset);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Asset?> GetAssetAsync(Guid id)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM assets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadAsset(reader) : null;
    }

    public async Task<IReadOnlyList<Asset>> ListAssetsAsync()
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM assets ORDER BY created_at";
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<Asset>();
        while (await reader.ReadAsync())
            list.Add(ReadAsset(reader));
        return list;
    }

    public async Task UpdateAssetStateAsync(Guid id, AssetState state, string? error = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE assets SET state = @st, last_error = @le WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", id.ToString());
            cmd.Parameters.AddWithValue("@st", state.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@le", (object?)error ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RecordHeartbeatAsync(Guid id)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE assets SET last_heartbeat = @ts, state = 'online' WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", id.ToString());
            cmd.Parameters.AddWithValue("@ts", FormatTimestamp(DateTimeOffset.UtcNow));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> GetFirmletInitializedAsync(Guid id)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT firmlet_initialized FROM assets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && Convert.ToInt32(result) != 0;
    }

    public async Task SetFirmletInitializedAsync(Guid id, bool initialized)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE assets SET firmlet_initialized = @fi WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id.ToString());
            cmd.Parameters.AddWithValue("@fi", initialized ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAssetAsync(Guid id)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM assets WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id.ToString());
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── Permissions ─────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all permission rows for <paramref name="assetId"/> with <paramref name="permissions"/>.
    /// </summary>
    public async Task SetPermissionsAsync(Guid assetId, IReadOnlyList<AssetPermission> permissions)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var tx = _connection.BeginTransaction();

            await using (var del = _connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM permissions WHERE asset_id = @aid";
                del.Parameters.AddWithValue("@aid", assetId.ToString());
                await del.ExecuteNonQueryAsync();
            }

            foreach (var p in permissions)
            {
                await using var ins = _connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO permissions (asset_id, permission_type, target)
                    VALUES (@aid, @pt, @tg)
                    """;
                ins.Parameters.AddWithValue("@aid", assetId.ToString());
                ins.Parameters.AddWithValue("@pt", p.PermissionType);
                ins.Parameters.AddWithValue("@tg", p.Target ?? "");
                await ins.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<AssetPermission>> GetPermissionsAsync(Guid assetId)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT permission_type, target FROM permissions WHERE asset_id = @aid
            ORDER BY permission_type, target
            """;
        cmd.Parameters.AddWithValue("@aid", assetId.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<AssetPermission>();
        while (await reader.ReadAsync())
        {
            list.Add(new AssetPermission(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return list;
    }

    // ── Command queue ───────────────────────────────────────────────────

    public async Task EnqueueCommandAsync(Guid assetId, string cmdType, byte[] payload)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO commands (asset_id, cmd_type, payload, created_at)
                VALUES (@aid, @ct, @p, @ca)
                """;
            cmd.Parameters.AddWithValue("@aid", assetId.ToString());
            cmd.Parameters.AddWithValue("@ct", cmdType);
            cmd.Parameters.AddWithValue("@p", payload);
            cmd.Parameters.AddWithValue("@ca", FormatTimestamp(DateTimeOffset.UtcNow));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<int> GetPendingCommandCountAsync(Guid assetId)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM commands WHERE asset_id = @aid";
        cmd.Parameters.AddWithValue("@aid", assetId.ToString());
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<CommandEntry>> DequeueCommandsAsync(Guid assetId, int maxCount = 10)
    {
        await _writeLock.WaitAsync();
        try
        {
            await using var tx = _connection.BeginTransaction();

            await using var select = _connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = """
                SELECT id, asset_id, cmd_type, payload, created_at
                FROM commands
                WHERE asset_id = @aid
                ORDER BY id
                LIMIT @lim
                """;
            select.Parameters.AddWithValue("@aid", assetId.ToString());
            select.Parameters.AddWithValue("@lim", maxCount);

            var entries = new List<CommandEntry>();
            await using (var reader = await select.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    entries.Add(new CommandEntry
                    {
                        Id = reader.GetInt64(0),
                        AssetId = Guid.Parse(reader.GetString(1)),
                        CommandType = reader.GetString(2),
                        Payload = (byte[])reader.GetValue(3),
                        CreatedAt = ParseTimestamp(reader.GetString(4)),
                    });
                }
            }

            if (entries.Count > 0)
            {
                var maxId = entries[^1].Id;
                await using var delete = _connection.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM commands WHERE asset_id = @aid AND id <= @mid";
                delete.Parameters.AddWithValue("@aid", assetId.ToString());
                delete.Parameters.AddWithValue("@mid", maxId);
                await delete.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return entries;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void BindAssetParams(SqliteCommand cmd, Asset asset)
    {
        cmd.Parameters.AddWithValue("@id", asset.Id.ToString());
        cmd.Parameters.AddWithValue("@name", (object?)asset.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vid", (object?)asset.VendorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)asset.ProductId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@var", (object?)asset.Variant ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fw", (object?)asset.FirmwareVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@comm", asset.CommunicationMode switch
        {
            CommunicationMode.AlwaysOn => "always-on",
            CommunicationMode.Mailbox => "mailbox",
            _ => "service-only",
        });
        cmd.Parameters.AddWithValue("@fn", (object?)asset.FirmletName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fv", (object?)asset.FirmletVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fi", asset.FirmletInitialized ? 1 : 0);
        cmd.Parameters.AddWithValue("@st", asset.State.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@ca", FormatTimestamp(asset.CreatedAt));
        cmd.Parameters.AddWithValue("@lh", asset.LastHeartbeat.HasValue
            ? FormatTimestamp(asset.LastHeartbeat.Value)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@le", (object?)asset.LastError ?? DBNull.Value);
    }

    private static Asset ReadAsset(SqliteDataReader reader)
    {
        var commMode = reader.GetString(reader.GetOrdinal("communication_mode")) switch
        {
            "always-on" => CommunicationMode.AlwaysOn,
            "mailbox" => CommunicationMode.Mailbox,
            _ => CommunicationMode.ServiceOnly,
        };

        var stateStr = reader.GetString(reader.GetOrdinal("state"));
        var state = Enum.TryParse<AssetState>(stateStr, ignoreCase: true, out var s)
            ? s : AssetState.Created;

        return new Asset
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            DisplayName = reader.IsDBNull(reader.GetOrdinal("display_name"))
                ? null : reader.GetString(reader.GetOrdinal("display_name")),
            VendorId = reader.IsDBNull(reader.GetOrdinal("vendor_id"))
                ? null : reader.GetInt32(reader.GetOrdinal("vendor_id")),
            ProductId = reader.IsDBNull(reader.GetOrdinal("product_id"))
                ? null : reader.GetInt32(reader.GetOrdinal("product_id")),
            Variant = reader.IsDBNull(reader.GetOrdinal("variant"))
                ? null : (byte[])reader.GetValue(reader.GetOrdinal("variant")),
            FirmwareVersion = reader.IsDBNull(reader.GetOrdinal("firmware_version"))
                ? null : reader.GetString(reader.GetOrdinal("firmware_version")),
            CommunicationMode = commMode,
            FirmletName = reader.IsDBNull(reader.GetOrdinal("firmlet_name"))
                ? null : reader.GetString(reader.GetOrdinal("firmlet_name")),
            FirmletVersion = reader.IsDBNull(reader.GetOrdinal("firmlet_version"))
                ? null : reader.GetString(reader.GetOrdinal("firmlet_version")),
            FirmletInitialized = !reader.IsDBNull(reader.GetOrdinal("firmlet_initialized"))
                && reader.GetInt32(reader.GetOrdinal("firmlet_initialized")) != 0,
            State = state,
            CreatedAt = ParseTimestamp(reader.GetString(reader.GetOrdinal("created_at"))),
            LastHeartbeat = reader.IsDBNull(reader.GetOrdinal("last_heartbeat"))
                ? null : ParseTimestamp(reader.GetString(reader.GetOrdinal("last_heartbeat"))),
            LastError = reader.IsDBNull(reader.GetOrdinal("last_error"))
                ? null : reader.GetString(reader.GetOrdinal("last_error")),
        };
    }

    private static string FormatTimestamp(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _writeLock.Dispose();
    }
}
