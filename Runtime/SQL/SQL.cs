using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
#nullable enable
namespace Runtime.SQL;

static class SQL
{
    static SqliteConnection? _connection;
    static Dictionary<long, SqliteDataReader> _readers = [];
    static string tempDir { get => Config.SavDir + "temp_db" + Path.DirectorySeparatorChar; }

    static public void ConnectionOpen(string name)
    {
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }

        _connection?.Close();

        var connection = new SqliteConnectionStringBuilder()
        {
            DataSource = $"{tempDir}{name}.db",
        };
        _connection = new SqliteConnection(connection.ConnectionString);
        _connection.Open();

        var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = OFF;PRAGMA synchronous  = OFF;";
        command.ExecuteNonQuery();
    }

    static public void SetUpTempDB()
    {
        if (Directory.Exists(tempDir))
        {
            _connection?.Close();
            SqliteConnection.ClearAllPools();

            Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
        }
    }
    static public void Save(string destDirPath)
    {

        if (Directory.Exists(tempDir))
        {
            if (!Directory.Exists(destDirPath)) Directory.CreateDirectory(destDirPath);

            var dbFiles = new DirectoryInfo(tempDir).EnumerateFiles();
            foreach (var dbFile in dbFiles)
            {
                var destPath = Path.Combine(destDirPath, dbFile.Name);
                dbFile.CopyTo(destPath, true);
            }
        }

    }

    static public void Load(string srcDirPath)
    {
        SetUpTempDB();

        if (Directory.Exists(srcDirPath))
        {
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var dbFiles = new DirectoryInfo(srcDirPath).EnumerateFiles();
            foreach (var dbFile in dbFiles)
            {
                var destPath = Path.Combine(tempDir, dbFile.Name);
                dbFile.CopyTo(destPath);
            }
        }


    }

    static public void ExecuteReader(long readerID, string sql)
    {
        if (_connection == null)
        {
            throw new CodeEE("SQLコネクションが開いていません");
        }
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        _readers[readerID] = command.ExecuteReader();
    }

    static public T? ExecuteScaler<T>(string sql)
    {
        if (_connection == null)
        {
            throw new CodeEE("SQLコネクションが開いていません");
        }
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return (T?)command.ExecuteScalar();
    }
    static public void ExecuteNonQuery(string sql)
    {
        if (_connection == null)
        {
            throw new CodeEE("SQLコネクションが開いていません");
        }
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static public void ReaderRead(long readerID)
    {
        if (!_readers.TryGetValue(readerID, out var reader))
        {
            throw new CodeEE($"IDが {readerID} のREADERは存在しません");
        }

        reader.Read();
    }

    static public long ReaderGetLong(long readerID, int index)
    {
        if (!_readers.TryGetValue(readerID, out var reader))
        {
            throw new CodeEE($"IDが {readerID} のREADERは存在しません");
        }
        return reader.GetInt64(index);
    }

    static public string ReaderGetString(long readerID, int index)
    {
        if (!_readers.TryGetValue(readerID, out var reader))
        {
            throw new CodeEE($"IDが {readerID} のREADERは存在しません");
        }
        return reader.GetString(index);
    }
}