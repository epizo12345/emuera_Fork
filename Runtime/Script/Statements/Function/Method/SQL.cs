using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using Runtime.SQL;

#nullable enable

namespace MinorShift.Emuera.GameData.Function;
internal static partial class FunctionMethodCreator
{
    public sealed class SQLConnectionOpen : FunctionMethod
    {
        public SQLConnectionOpen()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            SQL.ConnectionOpen(name);
            return 1;
        }
    }
    public sealed class SQLExecuteReader : FunctionMethod
    {
        public SQLExecuteReader()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(long), typeof(string)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var id = arguments[0].GetIntValue(exm);
            var sql = arguments[1].GetStrValue(exm);
            SQL.ExecuteReader(id, sql);
            return 1;
        }
    }

    public sealed class SQLReaderRead : FunctionMethod
    {
        public SQLReaderRead()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(long)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var id = arguments[0].GetIntValue(exm);
            SQL.ReaderRead(id);
            return 1;
        }
    }
    public sealed class SQLReaderGetLong : FunctionMethod
    {
        public SQLReaderGetLong()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(long), typeof(long)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var id = arguments[0].GetIntValue(exm);
            var index = arguments[1].GetIntValue(exm);
            return SQL.ReaderGetLong(id, (int)index);
        }
    }

    public sealed class SQLReaderGetString : FunctionMethod
    {
        public SQLReaderGetString()
        {
            ReturnType = typeof(string);
            argumentTypeArray = [typeof(long), typeof(long)];
            CanRestructure = false;
        }
        public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var id = arguments[0].GetIntValue(exm);
            var index = arguments[1].GetIntValue(exm);
            return SQL.ReaderGetString(id, (int)index);
        }
    }
}