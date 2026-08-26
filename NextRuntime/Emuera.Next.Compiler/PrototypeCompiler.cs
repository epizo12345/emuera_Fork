using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum CompileStatus
{
    Compiled,
    Unsupported,
    SourceChanged,
    InvalidSource,
    CompilerError,
}

public enum PrototypeOpcode : byte
{
    Set,
    Print,
    PrintLine,
    PrintWait,
    PrintNoWait,
    PrintVariable,
    PrintForm,
    Input,
    Wait,
    Draw,
    Character,
    Call,
    Return,
    ControlFlow,
    Loop,
    Goto,
    Data,
    State,
    Output,
    Unsupported,
}

[Flags]
public enum PrototypeInstructionFlags : byte
{
    None = 0,
    HasOperand = 1,
    ControlFlow = 2,
    Call = 4,
}

public readonly record struct PrototypeInstruction(
    PrototypeOpcode Opcode,
    PrototypeInstructionFlags Flags,
    int SourceLine,
    int OperandOffset,
    int OperandLength);

public enum UnsupportedReason
{
    None,
    UnsupportedInstruction,
    ExpressionSensitiveSyntax,
    LocalLabelOrGoto,
    MacroSensitiveSyntax,
    Multiline,
    UnknownSyntax,
    IndexFallback,
    SourceChanged,
    InvalidSource,
    ReadError,
}

public sealed record CompileResult(
    CompileStatus Status,
    CompiledFunction? Function,
    UnsupportedReason Reason,
    string? Detail)
{
    public static CompileResult Unsupported(UnsupportedReason reason, string detail) => new(CompileStatus.Unsupported, null, reason, detail);
}

public sealed record CompiledFunction(
    string FileIdentity,
    string Name,
    SourceSpan Span,
    ImmutableArray<PrototypeInstruction> Instructions,
    string Fingerprint,
    int MetadataBytesEstimate)
{
    public int InstructionStorageBytes => Instructions.Length * FunctionCompiler.InstructionPayloadBytes;
}

public static class LegacyOpcodeMap
{
    private static readonly IReadOnlyDictionary<string, PrototypeOpcode> Map =
        new Dictionary<string, PrototypeOpcode>(StringComparer.OrdinalIgnoreCase)
        {
            ["SET"] = PrototypeOpcode.Set,
            ["PRINT"] = PrototypeOpcode.Print, ["PRINTC"] = PrototypeOpcode.Print, ["PRINTLC"] = PrototypeOpcode.PrintLine,
            ["PRINTL"] = PrototypeOpcode.PrintLine, ["PRINTW"] = PrototypeOpcode.PrintWait, ["PRINTN"] = PrototypeOpcode.PrintNoWait,
            ["PRINTV"] = PrototypeOpcode.PrintVariable, ["PRINTVL"] = PrototypeOpcode.PrintVariable,
            ["PRINTVW"] = PrototypeOpcode.PrintVariable, ["PRINTVN"] = PrototypeOpcode.PrintVariable,
            ["PRINTS"] = PrototypeOpcode.PrintVariable, ["PRINTSL"] = PrototypeOpcode.PrintVariable,
            ["PRINTSW"] = PrototypeOpcode.PrintVariable, ["PRINTSN"] = PrototypeOpcode.PrintVariable,
            ["PRINTFORM"] = PrototypeOpcode.PrintForm, ["PRINTFORML"] = PrototypeOpcode.PrintForm,
            ["PRINTFORMW"] = PrototypeOpcode.PrintForm, ["PRINTFORMN"] = PrototypeOpcode.PrintForm,
            ["PRINTFORMS"] = PrototypeOpcode.PrintForm, ["PRINTFORMSL"] = PrototypeOpcode.PrintForm,
            ["PRINTFORMSW"] = PrototypeOpcode.PrintForm, ["PRINTFORMSN"] = PrototypeOpcode.PrintForm,
            ["PRINTFORMC"] = PrototypeOpcode.PrintForm, ["PRINTFORMLC"] = PrototypeOpcode.PrintForm,
            ["INPUT"] = PrototypeOpcode.Input, ["INPUTS"] = PrototypeOpcode.Input, ["TINPUT"] = PrototypeOpcode.Input,
            ["TINPUTS"] = PrototypeOpcode.Input, ["ONEINPUT"] = PrototypeOpcode.Input, ["ONEINPUTS"] = PrototypeOpcode.Input,
            ["TONEINPUT"] = PrototypeOpcode.Input, ["TONEINPUTS"] = PrototypeOpcode.Input,
            ["WAIT"] = PrototypeOpcode.Wait, ["TWAIT"] = PrototypeOpcode.Wait, ["WAITANYKEY"] = PrototypeOpcode.Wait,
            ["FORCEWAIT"] = PrototypeOpcode.Wait, ["AWAIT"] = PrototypeOpcode.Wait,
            ["DRAWLINE"] = PrototypeOpcode.Draw, ["DRAWLINEFORM"] = PrototypeOpcode.Draw, ["BAR"] = PrototypeOpcode.Draw, ["BARL"] = PrototypeOpcode.Draw,
            ["ADDCHARA"] = PrototypeOpcode.Character, ["ADDSPCHARA"] = PrototypeOpcode.Character, ["ADDDEFCHARA"] = PrototypeOpcode.Character,
            ["ADDVOIDCHARA"] = PrototypeOpcode.Character, ["DELCHARA"] = PrototypeOpcode.Character,
            ["CALL"] = PrototypeOpcode.Call, ["TRYCALL"] = PrototypeOpcode.Call, ["CALLEVENT"] = PrototypeOpcode.Call,
            ["CALLTRAIN"] = PrototypeOpcode.Call, ["CALLF"] = PrototypeOpcode.Call,
            ["RETURN"] = PrototypeOpcode.Return, ["RETURNFORM"] = PrototypeOpcode.Return, ["RETURNF"] = PrototypeOpcode.Return,
            ["IF"] = PrototypeOpcode.ControlFlow, ["SIF"] = PrototypeOpcode.ControlFlow, ["ELSE"] = PrototypeOpcode.ControlFlow,
            ["ELSEIF"] = PrototypeOpcode.ControlFlow, ["ENDIF"] = PrototypeOpcode.ControlFlow,
            ["SELECTCASE"] = PrototypeOpcode.ControlFlow, ["CASE"] = PrototypeOpcode.ControlFlow, ["CASEELSE"] = PrototypeOpcode.ControlFlow,
            ["ENDSELECT"] = PrototypeOpcode.ControlFlow,
            ["REPEAT"] = PrototypeOpcode.Loop, ["REND"] = PrototypeOpcode.Loop, ["CONTINUE"] = PrototypeOpcode.Loop,
            ["BREAK"] = PrototypeOpcode.Loop, ["FOR"] = PrototypeOpcode.Loop, ["NEXT"] = PrototypeOpcode.Loop,
            ["WHILE"] = PrototypeOpcode.Loop, ["WEND"] = PrototypeOpcode.Loop, ["DO"] = PrototypeOpcode.Loop, ["LOOP"] = PrototypeOpcode.Loop,
            ["GOTO"] = PrototypeOpcode.Goto, ["JUMP"] = PrototypeOpcode.Goto, ["TRYJUMP"] = PrototypeOpcode.Goto,
            ["TRYGOTO"] = PrototypeOpcode.Goto, ["TRYGOTOFORM"] = PrototypeOpcode.Goto,
            ["PRINTDATA"] = PrototypeOpcode.Data, ["PRINTDATAL"] = PrototypeOpcode.Data, ["PRINTDATAW"] = PrototypeOpcode.Data,
            ["DATA"] = PrototypeOpcode.Data, ["DATAFORM"] = PrototypeOpcode.Data, ["ENDDATA"] = PrototypeOpcode.Data,
            ["SETBIT"] = PrototypeOpcode.State, ["CLEARBIT"] = PrototypeOpcode.State, ["INVERTBIT"] = PrototypeOpcode.State,
            ["SWAP"] = PrototypeOpcode.State, ["POWER"] = PrototypeOpcode.State, ["TIMES"] = PrototypeOpcode.State,
            ["UPCHECK"] = PrototypeOpcode.State, ["CUPCHECK"] = PrototypeOpcode.State,
            ["CLEARLINE"] = PrototypeOpcode.Output, ["REUSELASTLINE"] = PrototypeOpcode.Output, ["OUTPUTLOG"] = PrototypeOpcode.Output,
            ["QUIT"] = PrototypeOpcode.Output, ["REDRAW"] = PrototypeOpcode.Output,
        };

    public static bool TryMap(string token, out PrototypeOpcode opcode) => Map.TryGetValue(token, out opcode);
    public static string[] SupportedLegacyNames => Map.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed class FunctionCompiler
{
    public const int InstructionPayloadBytes = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public CompileResult TryCompile(SourceFileIndex file, FunctionIndex function)
    {
        if (file.HasFallback || function.Flags != SourceIndexFlags.None)
            return CompileResult.Unsupported(UnsupportedReason.IndexFallback, "Source Index fallback flags");

        var read = FunctionSourceReader.Read(file, function);
        if (read.Status != SourceReadStatus.Read)
            return read.Status switch
            {
                SourceReadStatus.SourceChanged => new(CompileStatus.SourceChanged, null, UnsupportedReason.SourceChanged, read.Reason),
                SourceReadStatus.InvalidSource => new(CompileStatus.InvalidSource, null, UnsupportedReason.InvalidSource, read.Reason),
                _ => new(CompileStatus.CompilerError, null, UnsupportedReason.ReadError, read.Reason),
            };

        try
        {
            var source = read.Source!.Value;
            var instructions = Scan(source.Bytes, source.Function.Span.StartLine, out var reason, out var detail);
            if (reason != UnsupportedReason.None)
                return CompileResult.Unsupported(reason, detail!);
            var fingerprint = Convert.ToHexString(SHA256.HashData(source.Bytes));
            return new(CompileStatus.Compiled,
                new(source.File.FileIdentity, source.Function.Name, source.Function.Span, instructions, fingerprint,
                    MetadataBytesEstimate(source.Function.Name)), UnsupportedReason.None, null);
        }
        catch (DecoderFallbackException ex)
        {
            return new(CompileStatus.InvalidSource, null, UnsupportedReason.InvalidSource, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            return new(CompileStatus.CompilerError, null, UnsupportedReason.UnknownSyntax, ex.Message);
        }
    }

    private static ImmutableArray<PrototypeInstruction> Scan(byte[] bytes, int startLine, out UnsupportedReason reason, out string? detail)
    {
        var list = ImmutableArray.CreateBuilder<PrototypeInstruction>();
        reason = UnsupportedReason.None;
        detail = null;
        var offset = 0;
        var line = startLine;
        var first = true;
        while (offset < bytes.Length)
        {
            var lineStart = offset;
            while (offset < bytes.Length && bytes[offset] != (byte)'\n') offset++;
            var lineEnd = offset;
            if (lineEnd > lineStart && bytes[lineEnd - 1] == (byte)'\r') lineEnd--;
            var lineBytes = bytes.AsSpan(lineStart, lineEnd - lineStart);
            offset = offset < bytes.Length ? offset + 1 : offset;
            var text = StrictUtf8.GetString(lineBytes);
            if (first) { first = false; line++; continue; }
            var trimmed = text.TrimStart(' ', '\t');
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith("//", StringComparison.Ordinal)) { line++; continue; }
            if (trimmed.EndsWith('\\')) return Fail(UnsupportedReason.Multiline, "line continuation", out reason, out detail);
            if (trimmed[0] is '[' or '#' or '$' or '}' or '{' or '@')
                return Fail(trimmed[0] == '$' ? UnsupportedReason.LocalLabelOrGoto : UnsupportedReason.UnknownSyntax, "unsupported structural line", out reason, out detail);

            var tokenLength = 0;
            while (tokenLength < trimmed.Length && !char.IsWhiteSpace(trimmed[tokenLength]) && trimmed[tokenLength] is not (',' or '(')) tokenLength++;
            if (tokenLength == 0)
                return Fail(UnsupportedReason.UnknownSyntax, "empty instruction", out reason, out detail);
            var token = trimmed[..tokenLength];
            if (!LegacyOpcodeMap.TryMap(token, out var opcode))
                return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            var operandStart = tokenLength;
            while (operandStart < trimmed.Length && (trimmed[operandStart] == ' ' || trimmed[operandStart] == '\t' || trimmed[operandStart] == ',')) operandStart++;
            var operand = trimmed[operandStart..].TrimEnd();
            var operandOffset = lineStart + Encoding.UTF8.GetByteCount(text[..(text.Length - trimmed.Length + operandStart)]);
            var operandLength = Encoding.UTF8.GetByteCount(operand);
            var flags = operandLength > 0 ? PrototypeInstructionFlags.HasOperand : PrototypeInstructionFlags.None;
            if (opcode == PrototypeOpcode.ControlFlow || opcode == PrototypeOpcode.Loop || opcode == PrototypeOpcode.Goto) flags |= PrototypeInstructionFlags.ControlFlow;
            if (opcode == PrototypeOpcode.Call) flags |= PrototypeInstructionFlags.Call;
            list.Add(new(opcode, flags, line, operandOffset, operandLength));
            line++;
        }
        return list.ToImmutable();

        static ImmutableArray<PrototypeInstruction> Fail(UnsupportedReason value, string message, out UnsupportedReason result, out string? detail)
        {
            result = value;
            detail = message;
            return ImmutableArray<PrototypeInstruction>.Empty;
        }
    }

    private static int MetadataBytesEstimate(string name) => 32 + Encoding.UTF8.GetByteCount(name);
}
