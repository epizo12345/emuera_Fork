using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;

namespace MinorShift.Emuera.GameData.Function;
internal static partial class FunctionMethodCreator
{
    public sealed class EXISTFUNCTION : FunctionMethod
    {
        public EXISTFUNCTION()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }


        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var functionName = arguments[0].GetStrValue(exm);
            return GlobalStatic.IdentifierDictionary.ExistFunction(functionName);
        }
    }

}