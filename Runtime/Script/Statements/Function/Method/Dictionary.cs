using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using Runtime.Dictionary;

namespace MinorShift.Emuera.GameData.Function;
internal static partial class FunctionMethodCreator
{
    public sealed class DictCreate : FunctionMethod
    {
        public DictCreate()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            ERBDictionary.CreateDictionary(name);
            return 1;
        }
    }
    public sealed class DictExist : FunctionMethod
    {
        public DictExist()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);

            return ERBDictionary.ExitsDictionary(name) ? 1 : 0;
        }
    }
    public sealed class DictContainsKey : FunctionMethod
    {
        public DictContainsKey()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            long key;
            if (arguments[1].IsString)
            {
                key = arguments[1].GetStrValue(exm).GetHashCode();
            }
            else if (arguments[1].IsInteger)
            {
                key = arguments[1].GetIntValue(exm);
            }
            else
            {
                throw new System.Exception("謎の型");
            }

            return ERBDictionary.ContainsKey(name, key) ? 1 : 0;
        }
    }

    public sealed class DictGetValueInt : FunctionMethod
    {
        public DictGetValueInt()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            long key;
            if (arguments[1].IsString)
            {
                key = arguments[1].GetStrValue(exm).GetHashCode();
            }
            else if (arguments[1].IsInteger)
            {
                key = arguments[1].GetIntValue(exm);
            }
            else
            {
                throw new System.Exception("謎の型");
            }

            return ERBDictionary.Get<long>(name, key);
        }
    }
    public sealed class DictGetValueString : FunctionMethod
    {
        public DictGetValueString()
        {
            ReturnType = typeof(string);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return null;
        }
        public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            long key;
            if (arguments[1].IsString)
            {
                key = arguments[1].GetStrValue(exm).GetHashCode();
            }
            else if (arguments[1].IsInteger)
            {
                key = arguments[1].GetIntValue(exm);
            }
            else
            {
                throw new System.Exception("謎の型");
            }

            return ERBDictionary.Get<string>(name, key);
        }
    }
    public sealed class DictSetValue : FunctionMethod
    {
        public DictSetValue()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            long key;
            if (arguments[1].IsString)
            {
                key = arguments[1].GetStrValue(exm).GetHashCode();
            }
            else if (arguments[1].IsInteger)
            {
                key = arguments[1].GetIntValue(exm);
            }
            else
            {
                throw new System.Exception("謎の型");
            }

            if (arguments[2].IsString)
            {
                ERBDictionary.Set(name, key, arguments[2].GetStrValue(exm));
            }
            else if (arguments[2].IsInteger)
            {
                ERBDictionary.Set(name, key, arguments[2].GetIntValue(exm));
            }
            else
            {
                throw new System.Exception("謎の型");
            }

            return 1;
        }
    }
}