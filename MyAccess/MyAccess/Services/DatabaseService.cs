using System.Text;
using Npgsql;

namespace MyAccess.Services;

public class DatabaseService
{
    private string _baseConnectionString;
    private AuthService _authService;

    public DatabaseService(string baseConnectionString, AuthService authService)
    {
        _baseConnectionString = baseConnectionString;
        _authService = authService;
    }
    
    public void UpdateAuthService(AuthService authService)
    {
        _authService = authService;
    }
    
    public static bool IsNumeric(string pgType)
    {
        return pgType.ToLower() switch
        {
            "int4" or "integer" or "numeric" or "float8" or "double precision" => true,
            _ => false
        };
    }
    
    private void CheckAdminAccess()
    {
        if (_authService?.CurrentUser?.Role != "admin")
        {
            throw new UnauthorizedAccessException("Требуются права администратора");
        }
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
        CheckAdminAccess();
        
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
        CheckAdminAccess();
        
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

    public async Task<List<Dictionary<string, object>>> GetTableDataAsync(
        string databaseName, 
        string tableName, 
        int limit = 100) // Добавляем параметр лимита
    {
        var rows = new List<Dictionary<string, object>>();
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        // Используем параметризованный запрос для безопасности
        var query = $"SELECT * FROM \"{tableName}\" LIMIT @limit";
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("limit", limit); // Добавляем параметр

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
        CheckAdminAccess();
        
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var columnDefs = new List<string>();
        var foreignKeys = new List<string>();

        foreach (var c in columns)
        {
            columnDefs.Add($"\"{c.Name}\" {c.Type} {(c.IsPrimaryKey ? "PRIMARY KEY" : "")}".Trim());

            if (c.IsForeignKey && !string.IsNullOrWhiteSpace(c.ForeignTable) && !string.IsNullOrWhiteSpace(c.ForeignColumn))
            {
                foreignKeys.Add($"FOREIGN KEY (\"{c.Name}\") REFERENCES \"{c.ForeignTable}\"(\"{c.ForeignColumn}\")");
            }
        }

        var allDefs = columnDefs.Concat(foreignKeys);
        var query = $@"
        CREATE TABLE IF NOT EXISTS ""{tableName}"" (
            {string.Join(", ", allDefs)}
        )";

        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
    }



    public async Task DeleteTableAsync(string databaseName, string tableName)
    {
        CheckAdminAccess();
        
        await using var conn = new NpgsqlConnection(GetConnectionString(databaseName));
        await conn.OpenAsync();

        var query = $@"DROP TABLE IF EXISTS ""{tableName}"" CASCADE";
        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task UpdateRecordAsync(string dbName, string tableName, 
    Dictionary<string, object> primaryKeys, 
    Dictionary<string, object> updatedValues)
    {
        CheckAdminAccess();
        
        if (primaryKeys.Count == 0) 
            throw new ArgumentException("Primary keys required for update");
        if (updatedValues.Count == 0) 
            throw new ArgumentException("Updated values cannot be empty");

        await using var conn = new NpgsqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();

        // Генерация SET части с параметрами
        var setParams = updatedValues.Keys
            .Select((k, i) => $@"""{k}"" = @set_{i}")
            .ToList();
        
        // Генерация WHERE части с параметрами
        var whereParams = primaryKeys.Keys
            .Select((k, i) => $@"""{k}"" = @where_{i}")
            .ToList();

        var query = $@"UPDATE ""{tableName}"" 
            SET {string.Join(", ", setParams)} 
            WHERE {string.Join(" AND ", whereParams)}";

        await using var cmd = new NpgsqlCommand(query, conn);
        
        // Добавление параметров SET
        var setIndex = 0;
        foreach (var kvp in updatedValues)
        {
            cmd.Parameters.AddWithValue($"set_{setIndex}", kvp.Value ?? DBNull.Value);
            setIndex++;
        }
        
        // Добавление параметров WHERE
        var whereIndex = 0;
        foreach (var kvp in primaryKeys)
        {
            cmd.Parameters.AddWithValue($"where_{whereIndex}", kvp.Value ?? DBNull.Value);
            whereIndex++;
        }

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRecordAsync(string dbName, string tableName, 
        Dictionary<string, object> primaryKeys)
    {
        CheckAdminAccess();
        
        if (primaryKeys.Count == 0) 
            throw new ArgumentException("Primary keys required for deletion");

        await using var conn = new NpgsqlConnection(GetConnectionString(dbName));
        await conn.OpenAsync();

        // Генерация параметров с индексами
        var whereParams = primaryKeys.Keys
            .Select((k, i) => $@"""{k}"" = @where_{i}")
            .ToList();

        var query = $@"DELETE FROM ""{tableName}"" 
            WHERE {string.Join(" AND ", whereParams)}";

        await using var cmd = new NpgsqlCommand(query, conn);
        
        // Добавление параметров
        var whereIndex = 0;
        foreach (var kvp in primaryKeys)
        {
            cmd.Parameters.AddWithValue($"where_{whereIndex}", kvp.Value ?? DBNull.Value);
            whereIndex++;
        }

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
        CheckAdminAccess();
        
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
        CheckAdminAccess();
        
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
    
    public class ForeignKeyInfo
    {
        public string ColumnName { get; set; }
        public string ReferencedTable { get; set; }
        public string ReferencedColumn { get; set; }
    }

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string dbName, string tableName)
    {
        
        await using var connection = new NpgsqlConnection(GetConnectionString(dbName));
        await connection.OpenAsync();

        var sql = @"
            SELECT 
                kcu.column_name AS fk_column,
                ccu.table_name AS ref_table,
                ccu.column_name AS ref_column
            FROM 
                information_schema.table_constraints AS tc 
                JOIN information_schema.key_column_usage AS kcu
                    ON tc.constraint_name = kcu.constraint_name
                    AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage AS ccu
                    ON ccu.constraint_name = tc.constraint_name
                    AND ccu.table_schema = tc.table_schema
            WHERE 
                tc.constraint_type = 'FOREIGN KEY' 
                AND tc.table_name = @tableName;
        ";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("tableName", tableName);
        
        var foreignKeys = new List<ForeignKeyInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new ForeignKeyInfo
            {
                ColumnName = reader.GetString(0),
                ReferencedTable = reader.GetString(1),
                ReferencedColumn = reader.GetString(2)
            });
        }
        
        return foreignKeys;
    }

    public async Task<Dictionary<string, List<Dictionary<string, object>>>> GetReferenceDataAsync(
        string dbName, 
        List<ForeignKeyInfo> foreignKeys)
    {
        var result = new Dictionary<string, List<Dictionary<string, object>>>();
        
        foreach (var fk in foreignKeys)
        {
            // Получаем данные для каждой связанной таблицы
            var data = await GetTableDataAsync(dbName, fk.ReferencedTable);
            result[fk.ColumnName] = data;
        }
        
        return result;
    }

    public class ColumnDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsPrimaryKey { get; set; }
        
        public bool IsNumeric => IsNumeric(Type);

        public bool IsForeignKey { get; set; }  // ⬅ добавлено
        public string ForeignTable { get; set; }  // ⬅ добавлено
        public string ForeignColumn { get; set; }  // ⬅ добавлено
    }
}