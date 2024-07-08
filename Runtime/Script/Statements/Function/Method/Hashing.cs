#nullable enable

using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using System.IO.Hashing;
using System.Text;

namespace MinorShift.Emuera.GameData.Function;

internal static partial class FunctionMethodCreator
{
    public sealed class XHH3 : FunctionMethod
    {
        public XHH3()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }


        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var source = arguments[0].GetStrValue(exm);
            return (long)XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(source));
        }
    }
    public sealed class XHH32 : FunctionMethod
    {
        public XHH32()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }


        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var source = arguments[0].GetStrValue(exm);
            return XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(source));
        }
    }
}