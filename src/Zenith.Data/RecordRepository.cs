using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;

namespace Zenith.Data;

public class TimeSpanHandler : SqlMapper.TypeHandler<TimeSpan>
{
    public override void SetValue(IDbDataParameter parameter, TimeSpan value)
    {
        parameter.Value = value.Ticks;
    }

    public override TimeSpan Parse(object value)
    {
        if (value is string s && TimeSpan.TryParse(s, out var ts)) return ts;
        return new TimeSpan(Convert.ToInt64(value));
    }
}

public class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.Value = value.ToString("o");
    }

    public override DateTime Parse(object value)
    {
        if (value == null) return DateTime.MinValue;
        return DateTime.Parse(value.ToString()!);
    }
}

public class RecordRepository
{
    private readonly string _connectionString;
    private static bool _handlersRegistered;

    public RecordRepository(string databasePath)
    {
        _connectionString = $"Data Source={databasePath};";

        if (!_handlersRegistered)
        {
            SqlMapper.AddTypeHandler(new TimeSpanHandler());
            SqlMapper.AddTypeHandler(new DateTimeHandler());
            _handlersRegistered = true;
        }
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var version = await connection.ExecuteScalarAsync<long>("PRAGMA user_version;");
        if (version == 0)
        {
            var createTableSql = @"
                CREATE TABLE Records (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    Duration INTEGER NOT NULL,
                    FileSize INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    Resolution TEXT NOT NULL,
                    Codec TEXT NOT NULL,
                    ThumbnailPath TEXT NOT NULL
                );
            ";
            await connection.ExecuteAsync(createTableSql);
            await connection.ExecuteAsync("PRAGMA user_version = 1;");
        }
    }

    public async Task<int> InsertAsync(Record record)
    {
        using var connection = new SqliteConnection(_connectionString);
        var sql = @"
            INSERT INTO Records (FileName, FilePath, Duration, FileSize, CreatedAt, Resolution, Codec, ThumbnailPath)
            VALUES (@FileName, @FilePath, @Duration, @FileSize, @CreatedAt, @Resolution, @Codec, @ThumbnailPath);
            SELECT last_insert_rowid();
        ";

        return await connection.ExecuteScalarAsync<int>(sql, record);
    }

    public async Task<IEnumerable<Record>> GetAllAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        var sql = "SELECT * FROM Records ORDER BY CreatedAt DESC;";
        return await connection.QueryAsync<Record>(sql);
    }

    public async Task ClearAllAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        var sql = "DELETE FROM Records;";
        await connection.ExecuteAsync(sql);
    }
}
