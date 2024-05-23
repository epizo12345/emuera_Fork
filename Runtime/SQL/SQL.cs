using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using MinorShift.Emuera.Runtime.Config;
#nullable enable
namespace Runtime.SQL;

static class SQL
{
    static SqliteConnection? _connection;
    static Dictionary<long, SqliteDataReader> _readers = [];
    static string tempDir { get => Config.SavDir + "temp_db/"; }

    static public void ConnectionOpen(string name)
    {
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }
        _connection = new SqliteConnection("Data Source=" + tempDir + name + ".db");
        _connection.Open();
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
                dbFile.CopyTo(destPath);
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
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        _readers[readerID] = command.ExecuteReader();
    }

    static public T? ExecuteScaler<T>(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return (T?)command.ExecuteScalar();
    }
    static public void ExecuteNonQuery(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static public void ReaderRead(long readerID)
    {
        _readers[readerID].Read();
    }

    static public long ReaderGetLong(long readerID, int index)
    {
        return _readers[readerID].GetInt64(index);
    }

    static public string ReaderGetString(long readerID, int index)
    {
        return _readers[readerID].GetString(index);
    }
}