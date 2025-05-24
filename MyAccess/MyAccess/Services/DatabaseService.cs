using System.Text;
using Npgsql;

namespace MyAccess.Services;

public class DatabaseService
{
    private string _baseConnectionString; // Без Database=

    public DatabaseService(string baseConnectionString)
    {
        _baseConnectionString = baseConnectionString;
    }

    private string GetConnectionString(string? dbName = null)
    {
        if (string.IsNullOrWhiteSpace(dbName))
            return _baseConnectionString;

        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = dbName
        };
        return builder.ConnectionString;
    }

    public async Task CreateDatabaseAsync(string dbName)
    {
        await using var connection = new NpgsqlConnection(GetConnectionString("postgres"));
        await connection.OpenAsync();
        
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {dbName}", connection);
        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task<bool> DatabaseExistsAsync(string dbName)
    {
        await using var connection = new NpgsqlConnection(GetConnectionString("postgres"));
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @dbName)", 
            connection);
    
        cmd.Parameters.AddWithValue("dbName", dbName);
        return (bool?)await cmd.ExecuteScalarAsync() ?? false;
    }
    
    public async Task DropDatabaseAsync(string dbName)
    {
        await using var connection = new NpgsqlConnection(GetConnectionString("postgres"));
        await connection.OpenAsync();

        // Проверка существования БД
        var exists = await DatabaseExistsAsync(dbName);
        if (!exists) return;

        // Завершение активных подключений
        await using var terminateCmd = new NpgsqlCommand(
            @"SELECT pg_terminate_backend(pg_stat_activity.pid)
        FROM pg_stat_activity
        WHERE pg_stat_activity.datname = @dbName
          AND pid <> pg_backend_pid()", 
            connection);
    
        terminateCmd.Parameters.AddWithValue("dbName", dbName);
        await terminateCmd.ExecuteNonQueryAsync();

        // Удаление БД
        await using var dropCmd = new NpgsqlCommand($"DROP DATABASE \"{dbName}\"", connection);
        await dropCmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetDatabasesAsync()
    {
        var databases = new List<string>();
        await using var conn = new NpgsqlConnection(GetConnectionString("postgres")); // подключаемся к системной базе
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datistemplate = false", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    public async Task<List<string>> GetTablesAsync(string databaseName)
    {
        var tables = new List<string>();
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();
        var query = @"SELECT table_name FROM information_schema.tables 
                      WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    public async Task<List<Dictionary<string, object>>> GetTableDataAsync(string databaseName, string tableName)
    {
        var rows = new List<Dictionary<string, object>>();
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var query = $"SELECT * FROM \"{tableName}\"";
        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.GetValue(i);
            }
            rows.Add(row);
        }

        return rows;
    }
    
    public async Task CreateTableAsync(string databaseName, string tableName, List<ColumnDefinition> columns)
    {
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var columnDefinitions = string.Join(", ", 
            columns.Select(c => $"\"{c.Name}\" {c.Type} {(c.IsPrimaryKey ? "PRIMARY KEY" : "")}"));

        var query = $@"
            CREATE TABLE IF NOT EXISTS ""{tableName}"" (
                {columnDefinitions}
            )";

        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteTableAsync(string databaseName, string tableName)
    {
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var query = $@"DROP TABLE IF EXISTS ""{tableName}"" CASCADE";
        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task<List<ColumnDefinition>> GetTableColumnsAsync(string databaseName, string tableName)
    {
        var columns = new List<ColumnDefinition>();
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var query = @"
            SELECT 
                column_name, 
                data_type,
                is_identity = 'YES' as is_primary
            FROM information_schema.columns
            WHERE table_name = @tableName";

        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("tableName", tableName);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnDefinition
            {
                Name = reader.GetString(0),
                Type = reader.GetString(1),
                IsPrimaryKey = reader.GetBoolean(2)
            });
        }
        return columns;
    }

    public async Task UpdateTableAsync(string databaseName, string originalTableName, string newTableName, List<ColumnDefinition> columns)
    {
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();
        
        using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // Переименование таблицы
            if (originalTableName != newTableName)
            {
                await using var renameCmd = new NpgsqlCommand(
                    $"ALTER TABLE \"{originalTableName}\" RENAME TO \"{newTableName}\"", 
                    conn, transaction);
                await renameCmd.ExecuteNonQueryAsync();
            }

            // Обновление структуры
            var existingColumns = await GetTableColumnsAsync(databaseName, newTableName);
            
            // Удаление отсутствующих колонок
            foreach (var existing in existingColumns)
            {
                if (!columns.Any(c => c.Name == existing.Name))
                {
                    await using var dropCmd = new NpgsqlCommand(
                        $"ALTER TABLE \"{newTableName}\" DROP COLUMN \"{existing.Name}\"", 
                        conn, transaction);
                    await dropCmd.ExecuteNonQueryAsync();
                }
            }

            // Добавление/изменение колонок
            foreach (var column in columns)
            {
                if (!existingColumns.Any(c => c.Name == column.Name))
                {
                    await using var addCmd = new NpgsqlCommand(
                        $"ALTER TABLE \"{newTableName}\" ADD COLUMN \"{column.Name}\" {column.Type}", 
                        conn, transaction);
                    await addCmd.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    
    public async Task InsertRecordAsync(string dbName, string tableName, Dictionary<string, object> record)
    {
        using var connection = new NpgsqlConnection(GetConnectionString(dbName));
        await connection.OpenAsync();

        var columns = string.Join(", ", record.Keys);
        var parameters = string.Join(", ", record.Keys.Select((k, i) => $"@p{i}"));
        
        var query = $"INSERT INTO {tableName} ({columns}) VALUES ({parameters})";
        
        using var command = new NpgsqlCommand(query, connection);
        
        int i = 0;
        foreach (var (key, value) in record)
        {
            command.Parameters.AddWithValue($"p{i}", value ?? DBNull.Value);
            i++;
        }
        
        await command.ExecuteNonQueryAsync();
    }

    public class ColumnDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    public class TableAlteration
    {
        public AlterationType Type { get; set; }
        public ColumnDefinition Column { get; set; }
    }

    public enum AlterationType
    {
        AddColumn,
        DropColumn,
        AlterColumnType
    }
}