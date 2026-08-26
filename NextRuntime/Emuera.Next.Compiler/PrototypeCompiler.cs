using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum CompileStatus { Compiled, Unsupported, SourceChanged, InvalidSource, CompilerError }

public enum PrototypeOpcode : ushort
{
    Unsupported,
    SET, PRINT, PRINTC, PRINTLC, PRINTL, PRINTW, PRINTN, PRINTV, PRINTVL, PRINTVW, PRINTVN,
    PRINTS, PRINTSL, PRINTSW, PRINTSN, PRINTFORM, PRINTFORML, PRINTFORMW, PRINTFORMN,
    PRINTFORMS, PRINTFORMSL, PRINTFORMSW, PRINTFORMSN, PRINTFORMC, PRINTFORMLC,
    INPUT, INPUTS, TINPUT, TINPUTS, ONEINPUT, ONEINPUTS, TONEINPUT, TONEINPUTS,
    WAIT, TWAIT, WAITANYKEY, FORCEWAIT, AWAIT,
    DRAWLINE, DRAWLINEFORM, BAR, BARL,
    ADDCHARA, ADDSPCHARA, ADDDEFCHARA, ADDVOIDCHARA, DELCHARA,
    CALL, TRYCALL, CALLEVENT, CALLTRAIN, CALLF,
    RETURN, RETURNFORM, RETURNF,
    IF, SIF, ELSE, ELSEIF, ENDIF, SELECTCASE, CASE, CASEELSE, ENDSELECT,
    REPEAT, REND, CONTINUE, BREAK, FOR, NEXT, WHILE, WEND, DO, LOOP,
    GOTO, JUMP, TRYJUMP, TRYGOTO, TRYGOTOFORM,
    PRINTDATA, PRINTDATAL, PRINTDATAW, DATA, DATAFORM, ENDDATA,
    SETBIT, CLEARBIT, INVERTBIT, SWAP, POWER, TIMES, UPCHECK, CUPCHECK,
    CLEARLINE, REUSELASTLINE, OUTPUTLOG, QUIT, REDRAW,
}

[Flags]
public enum PrototypeInstructionFlags : ushort { None = 0, HasOperand = 1, ControlFlow = 2, Call = 4 }

public readonly record struct PrototypeInstruction(PrototypeOpcode Opcode, PrototypeInstructionFlags Flags,
    int SourceLine, int OperandOffset, int OperandLength);

public readonly record struct SourceFingerprint(ulong A, ulong B, ulong C, ulong D)
{
    public static SourceFingerprint FromBytes(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(24, 8)));
    }
    public string ToHexString() => $"{A:X16}{B:X16}{C:X16}{D:X16}";
}

public enum UnsupportedReason
{
    None, UnsupportedInstruction, ExpressionSensitiveSyntax, LocalLabelOrGoto, MacroSensitiveSyntax,
    Multiline, UnknownSyntax, IndexFallback, SourceChanged, InvalidSource, ReadError,
}

public sealed record CompileResult(CompileStatus Status, CompiledFunction? Function, UnsupportedReason Reason,
    string? Detail, SourceFingerprint Fingerprint)
{
    public static CompileResult Unsupported(UnsupportedReason reason, string detail) =>
        new(CompileStatus.Unsupported, null, reason, detail, default);
}

public sealed record CompiledFunction(string FileIdentity, string Name, SourceSpan Span,
    ImmutableArray<PrototypeInstruction> Instructions, int MetadataBytesEstimate)
{
    public int InstructionStorageBytes => Instructions.Length * FunctionCompiler.InstructionPayloadBytes;
}

public static class LegacyOpcodeMap
{
    private static readonly IReadOnlyDictionary<string, PrototypeOpcode> Map =
        Enum.GetValues<PrototypeOpcode>().Where(static opcode => opcode != PrototypeOpcode.Unsupported)
            .ToDictionary(static opcode => opcode.ToString(), static opcode => opcode, StringComparer.OrdinalIgnoreCase);

    public static bool TryMap(string token, out PrototypeOpcode opcode) => Map.TryGetValue(token, out opcode);
    public static string[] SupportedLegacyNames => Map.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool IsControlFlow(PrototypeOpcode opcode) => opcode is PrototypeOpcode.IF or PrototypeOpcode.SIF or
        PrototypeOpcode.ELSE or PrototypeOpcode.ELSEIF or PrototypeOpcode.ENDIF or PrototypeOpcode.SELECTCASE or
        PrototypeOpcode.CASE or PrototypeOpcode.CASEELSE or PrototypeOpcode.ENDSELECT or PrototypeOpcode.GOTO or
        PrototypeOpcode.JUMP or PrototypeOpcode.TRYJUMP or PrototypeOpcode.TRYGOTO or PrototypeOpcode.TRYGOTOFORM or
        PrototypeOpcode.REPEAT or PrototypeOpcode.REND or PrototypeOpcode.CONTINUE or PrototypeOpcode.BREAK or
        PrototypeOpcode.FOR or PrototypeOpcode.NEXT or PrototypeOpcode.WHILE or PrototypeOpcode.WEND or
        PrototypeOpcode.DO or PrototypeOpcode.LOOP;
    public static bool IsCall(PrototypeOpcode opcode) => opcode is PrototypeOpcode.CALL or PrototypeOpcode.TRYCALL or
        PrototypeOpcode.CALLEVENT or PrototypeOpcode.CALLTRAIN or PrototypeOpcode.CALLF;
}

public sealed class FunctionCompiler
{
    public const int InstructionPayloadBytes = 16;
    public const int FunctionMetadataPayloadBytes = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public CompileResult TryCompile(SourceFileIndex file, FunctionIndex function)
    {
        if (file.HasFallback || function.Flags != SourceIndexFlags.None)
            return CompileResult.Unsupported(UnsupportedReason.IndexFallback, "Source Index fallback flags");
        var read = FunctionSourceReader.Read(file, function);
        return read.Status switch
        {
            SourceReadStatus.Read => TryCompile(read.Source!.Value),
            SourceReadStatus.SourceChanged => new(CompileStatus.SourceChanged, null, UnsupportedReason.SourceChanged, read.Reason, default),
            SourceReadStatus.InvalidSource => new(CompileStatus.InvalidSource, null, UnsupportedReason.InvalidSource, read.Reason, default),
            _ => new(CompileStatus.CompilerError, null, UnsupportedReason.ReadError, read.Reason, default),
        };
    }

    public CompileResult TryCompile(FunctionSource source)
    {
        try
        {
            var instructions = Scan(source.Bytes, source.Function.Span.StartLine, out var reason, out var detail);
            if (reason != UnsupportedReason.None)
                return CompileResult.Unsupported(reason, detail!);
            var fingerprint = SourceFingerprint.FromBytes(source.Bytes);
            return new(CompileStatus.Compiled,
                new(source.File.FileIdentity, source.Function.Name, source.Function.Span, instructions,
                    MetadataBytesEstimate(source.Function.Name)), UnsupportedReason.None, null, fingerprint);
        }
        catch (DecoderFallbackException ex)
        {
            return new(CompileStatus.InvalidSource, null, UnsupportedReason.InvalidSource, ex.Message, default);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            return new(CompileStatus.CompilerError, null, UnsupportedReason.UnknownSyntax, ex.Message, default);
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
            if (tokenLength == 0) return Fail(UnsupportedReason.UnknownSyntax, "empty instruction", out reason, out detail);
            var token = trimmed[..tokenLength];
            if (!LegacyOpcodeMap.TryMap(token, out var opcode))
                return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            var operandStart = tokenLength;
            while (operandStart < trimmed.Length && (trimmed[operandStart] == ' ' || trimmed[operandStart] == '\t' || trimmed[operandStart] == ',')) operandStart++;
            var operand = trimmed[operandStart..].TrimEnd();
            var operandOffset = lineStart + Encoding.UTF8.GetByteCount(text[..(text.Length - trimmed.Length + operandStart)]);
            var operandLength = Encoding.UTF8.GetByteCount(operand);
            var flags = operandLength > 0 ? PrototypeInstructionFlags.HasOperand : PrototypeInstructionFlags.None;
            if (LegacyOpcodeMap.IsControlFlow(opcode)) flags |= PrototypeInstructionFlags.ControlFlow;
            if (LegacyOpcodeMap.IsCall(opcode)) flags |= PrototypeInstructionFlags.Call;
            list.Add(new(opcode, flags, line, operandOffset, operandLength));
            line++;
        }
        return list.ToImmutable();

        static ImmutableArray<PrototypeInstruction> Fail(UnsupportedReason value, string message, out UnsupportedReason result, out string? detail)
        { result = value; detail = message; return ImmutableArray<PrototypeInstruction>.Empty; }
    }

    private static int MetadataBytesEstimate(string name) => 32 + Encoding.UTF8.GetByteCount(name);
}
