using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace GTS.Billing.Infrastructure;

// Preservation evidence only: persisted values are hashed, never logged or returned to the UI.
internal sealed record DatabaseTableFingerprint(string Name,string[] Columns,string[] Ordering,long Count,string Hash);
internal static class DatabasePreservation
{
    private static string Q(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    internal static async Task<List<DatabaseTableFingerprint>> CaptureAsync(SqliteConnection connection,
        IReadOnlyList<DatabaseTableFingerprint>? expected,CancellationToken ct,
        IReadOnlyList<string>? addedModules=null)
    {
        var specs=expected?.ToList() ?? [];
        if(expected is null)
        {
            var names=new List<string>();
            await using(var command=connection.CreateCommand())
            {
                command.CommandText="SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                await using var rows=await command.ExecuteReaderAsync(ct);
                while(await rows.ReadAsync(ct))
                {
                    var name=rows.GetString(0);
                    if(!name.StartsWith("sqlite_",StringComparison.Ordinal) && !name.StartsWith("__EF",StringComparison.Ordinal)) names.Add(name);
                }
            }
            foreach(var name in names)
            {
                var columns=new List<string>();var keys=new SortedDictionary<int,string>();
                await using var command=connection.CreateCommand();command.CommandText="PRAGMA table_info("+Q(name)+")";
                await using var rows=await command.ExecuteReaderAsync(ct);
                while(await rows.ReadAsync(ct))
                {
                    var column=rows.GetString(1);columns.Add(column);
                    var key=rows.GetInt32(5);if(key>0) keys.Add(key,column);
                }
                if(columns.Count==0) throw new InvalidOperationException("Cannot verify preservation.");
                specs.Add(new(name,columns.ToArray(),keys.Count>0?keys.Values.ToArray():columns.ToArray(),0,""));
            }
        }
        var result=new List<DatabaseTableFingerprint>();
        foreach(var spec in specs)
        {
            using var hash=SHA256.Create();using var stream=new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write);
            using var writer=new BinaryWriter(stream,Encoding.UTF8,leaveOpen:true);
            writer.Write(spec.Name);foreach(var column in spec.Columns) writer.Write(column);
            await using var command=connection.CreateCommand();
            var filter="";
            if(spec.Name=="Modules" && addedModules is {Count:>0})
            {
                var parameters=addedModules.Select((code,index)=>"$module"+index).ToArray();
                for(var i=0;i<parameters.Length;i++) command.Parameters.AddWithValue(parameters[i],addedModules[i]);
                filter=" WHERE Code NOT IN ("+string.Join(",",parameters)+")";
            }
            command.CommandText="SELECT "+string.Join(",",spec.Columns.Select(Q))+" FROM "+Q(spec.Name)+filter+" ORDER BY "+string.Join(",",spec.Ordering.Select(Q));
            await using var rows=await command.ExecuteReaderAsync(ct);long count=0;
            while(await rows.ReadAsync(ct))
            {
                count++;writer.Write((byte)127);
                for(var i=0;i<spec.Columns.Length;i++)
                    switch(rows.GetValue(i))
                    {
                        case DBNull:writer.Write((byte)0);break;
                        case long number:writer.Write((byte)1);writer.Write(number);break;
                        case double number:writer.Write((byte)2);writer.Write(number);break;
                        case string text:writer.Write((byte)3);writer.Write(text);break;
                        case byte[] bytes:writer.Write((byte)4);writer.Write(bytes.Length);writer.Write(bytes);break;
                        default:throw new InvalidOperationException("Unsupported persisted value.");
                    }
            }
            writer.Flush();stream.FlushFinalBlock();
            result.Add(spec with {Count=count,Hash=Convert.ToHexString(hash.Hash!)});
        }
        return result;
    }
    internal static void Verify(IReadOnlyList<DatabaseTableFingerprint> before,IReadOnlyList<DatabaseTableFingerprint> after)
    {
        if(before.Count!=after.Count || before.Zip(after).Any(x=>x.First.Name!=x.Second.Name || x.First.Count!=x.Second.Count || x.First.Hash!=x.Second.Hash))
            throw new InvalidOperationException("Preservation check failed.");
    }
    internal static async Task VerifyIntegrityAsync(SqliteConnection connection,CancellationToken ct)
    {
        await using var command=connection.CreateCommand();command.CommandText="PRAGMA integrity_check";
        await using(var rows=await command.ExecuteReaderAsync(ct))
            if(!await rows.ReadAsync(ct) || rows.GetString(0)!="ok" || await rows.ReadAsync(ct))
                throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
        command.CommandText="PRAGMA foreign_key_check";
        await using(var rows=await command.ExecuteReaderAsync(ct))
            if(await rows.ReadAsync(ct)) throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
        command.CommandText="PRAGMA foreign_keys";
        if(Convert.ToInt64(await command.ExecuteScalarAsync(ct))!=1) throw new DatabaseStartupException(DatabaseStartupProblem.IntegrityFailure);
    }
}

