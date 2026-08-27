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
    // [Emuera改修:NEXT-1B 2026-08-27] Legacy FunctionCodeごとにenumを分離し、実行意味の統合を先送りする。
    RESETCOLOR, CUSTOMDRAWLINE, SETCOLOR, SETFONT,
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
    // [Emuera改修:NEXT-1A-R3 2026-08-27]
    // Legacyのopcode名を1対1で保持する。CALL/TRYCALL/PRINT系を意味統合すると差分検証と将来の評価順を壊すため、未対応はUnsupportedへ送る。
    private static readonly IReadOnlyDictionary<string, PrototypeOpcode> Map =
        Enum.GetValues<PrototypeOpcode>().Where(static opcode => opcode != PrototypeOpcode.Unsupported)
            .ToDictionary(static opcode => opcode.ToString(), static opcode => opcode, StringComparer.OrdinalIgnoreCase);
    // [Emuera改修:NEXT-1B-R2 2026-08-27]
    // Legacy FunctionCode/FunctionMethodの実在名を1か所へ固定する。production pathでLegacy parserを呼ばず、
    // 未対応Legacy命令のoperand中の「=」をSETへ誤認しないための予約メタデータとして保持する。
    private static readonly HashSet<string> ReservedLegacyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "SET", "PRINT", "PRINTL", "PRINTW", "PRINTN", "PRINTV", "PRINTVL", "PRINTVW", "PRINTVN",
        "PRINTS", "PRINTSL", "PRINTSW", "PRINTSN", "PRINTFORM", "PRINTFORML", "PRINTFORMW", "PRINTFORMN",
        "PRINTFORMS", "PRINTFORMSL", "PRINTFORMSW", "PRINTFORMSN", "PRINTC", "CLEARLINE", "REUSELASTLINE",
        "WAIT", "INPUT", "INPUTS", "TINPUT", "TINPUTS", "TWAIT", "WAITANYKEY", "FORCEWAIT", "ONEINPUT",
        "ONEINPUTS", "TONEINPUT", "TONEINPUTS", "AWAIT", "DRAWLINE", "BAR", "BARL", "TIMES", "PRINT_ABL",
        "PRINT_TALENT", "PRINT_MARK", "PRINT_EXP", "PRINT_PALAM", "PRINT_ITEM", "PRINT_SHOPITEM", "UPCHECK",
        "CUPCHECK", "ADDCHARA", "ADDSPCHARA", "ADDDEFCHARA", "ADDVOIDCHARA", "DELCHARA", "PUTFORM", "QUIT",
        "OUTPUTLOG", "BEGIN", "SAVEGAME", "LOADGAME", "SIF", "IF", "ELSE", "ELSEIF", "ENDIF", "REPEAT",
        "REND", "CONTINUE", "BREAK", "GOTO", "JUMP", "CALL", "CALLEVENT", "RETURN", "RETURNFORM", "RETURNF",
        "RESTART", "STRLEN", "STRLENFORM", "STRLENU", "STRLENFORMU", "PRINTLC", "PRINTFORMC", "PRINTFORMLC",
        "SWAPCHARA", "COPYCHARA", "ADDCOPYCHARA", "VARSIZE", "SPLIT", "PRINTSINGLE", "PRINTSINGLEV", "PRINTSINGLES",
        "PRINTSINGLEFORM", "PRINTSINGLEFORMS", "PRINTBUTTON", "PRINTBUTTONC", "PRINTBUTTONLC", "PRINTPLAIN", "PRINTPLAINFORM",
        "SAVEDATA", "LOADDATA", "DELDATA", "GETTIME", "TRYJUMP", "TRYCALL", "TRYGOTO", "JUMPFORM", "CALLFORM",
        "GOTOFORM", "TRYJUMPFORM", "TRYCALLFORM", "TRYGOTOFORM", "CALLTRAIN", "STOPCALLTRAIN", "CATCH", "ENDCATCH",
        "TRYCJUMP", "TRYCCALL", "TRYCGOTO", "TRYCJUMPFORM", "TRYCCALLFORM", "TRYCGOTOFORM", "TRYCALLLIST", "TRYJUMPLIST",
        "TRYGOTOLIST", "FUNC", "ENDFUNC", "CALLF", "CALLFORMF", "SETCOLOR", "SETCOLORBYNAME", "RESETCOLOR",
        "SETBGCOLOR", "SETBGCOLORBYNAME", "RESETBGCOLOR", "FONTBOLD", "FONTITALIC", "FONTREGULAR", "SORTCHARA",
        "FONTSTYLE", "ALIGNMENT", "CUSTOMDRAWLINE", "DRAWLINEFORM", "CLEARTEXTBOX", "SETFONT", "FOR", "NEXT", "WHILE",
        "WEND", "POWER", "SAVEGLOBAL", "LOADGLOBAL", "SWAP", "RESETDATA", "RESETGLOBAL", "RANDOMIZE", "DUMPRAND",
        "INITRAND", "REDRAW", "DOTRAIN", "SELECTCASE", "CASE", "CASEELSE", "ENDSELECT", "DO", "LOOP", "PRINTDATA",
        "PRINTDATAL", "PRINTDATAW", "DATA", "DATAFORM", "ENDDATA", "DATALIST", "ENDLIST", "STRDATA", "PRINTCPERLINE",
        "SETBIT", "CLEARBIT", "INVERTBIT", "DELALLCHARA", "PICKUPCHARA", "VARSET", "CVARSET", "MATCHALL", "RESET_STAIN",
        "SAVENOS", "FORCEKANA", "SKIPDISP", "NOSKIP", "ENDNOSKIP", "ARRAYSHIFT", "ARRAYREMOVE", "ARRAYSORT", "ARRAYCOPY",
        "ENCODETOUNI", "DEBUGPRINT", "DEBUGPRINTL", "DEBUGPRINTFORM", "DEBUGPRINTFORML", "DEBUGCLEAR", "ASSERT", "THROW",
        "SAVEVAR", "LOADVAR", "SAVECHARA", "LOADCHARA", "REF", "REFBYNAME", "PRINTK", "PRINTKL", "PRINTKW", "PRINTVK",
        "PRINTVKL", "PRINTVKW", "PRINTSK", "PRINTSKL", "PRINTSKW", "PRINTFORMK", "PRINTFORMKL", "PRINTFORMKW",
        "PRINTFORMSK", "PRINTFORMSKL", "PRINTFORMSKW", "PRINTCK", "PRINTLCK", "PRINTFORMCK", "PRINTFORMLCK", "PRINTSINGLEK",
        "PRINTSINGLEVK", "PRINTSINGLESK", "PRINTSINGLEFORMK", "PRINTSINGLEFORMSK", "PRINTDATAK", "PRINTDATAKL", "PRINTDATAKW",
        "PRINTD", "PRINTDL", "PRINTDW", "PRINTVD", "PRINTVDL", "PRINTVDW", "PRINTSD", "PRINTSDL", "PRINTSDW",
        "PRINTFORMD", "PRINTFORMDL", "PRINTFORMDW", "PRINTFORMSD", "PRINTFORMSDL", "PRINTFORMSDW", "PRINTCD", "PRINTLCD",
        "PRINTFORMCD", "PRINTFORMLCD", "PRINTSINGLED", "PRINTSINGLEVD", "PRINTSINGLESD", "PRINTSINGLEFORMD", "PRINTSINGLEFORMSD",
        "PRINTDATAD", "PRINTDATADL", "PRINTDATADW", "HTML_PRINT", "HTML_TAGSPLIT", "TOOLTIP_SETCOLOR", "TOOLTIP_SETDELAY",
        "TOOLTIP_SETDURATION", "PRINT_IMG", "PRINT_RECT", "PRINT_SPACE", "INPUTMOUSEKEY", "VARI", "VARS", "HTML_PRINT_ISLAND",
        "HTML_PRINT_ISLAND_CLEAR",
    };
    static LegacyOpcodeMap() => ReservedLegacyCommands.UnionWith(new[]
    {
        "GETCHARA", "GETSPCHARA", "CSVNAME", "CSVCALLNAME", "CSVNICKNAME", "CSVMASTERNAME", "CSVCSTR", "CSVBASE", "CSVABL",
        "CSVMARK", "CSVEXP", "CSVRELATION", "CSVTALENT", "CSVCFLAG", "CSVEQUIP", "CSVJUEL", "GETCSVNOBYNAME", "GETCSVNOBYNICKNAME",
        "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME", "FINDCHARA", "FINDLASTCHARA", "EXISTCSV", "CHKFONT", "CHKDATA", "ISSKIP",
        "MOUSESKIP", "MESSKIP", "GETCOLOR", "GETDEFCOLOR", "GETFOCUSCOLOR", "GETBGCOLOR", "GETDEFBGCOLOR", "GETSTYLE", "GETFONT",
        "BARSTR", "CURRENTALIGN", "CURRENTREDRAW", "COLOR_FROMNAME", "COLOR_FROMRGB", "CHKCHARADATA", "FIND_CHARADATA", "MONEYSTR",
        "PRINTCLENGTH", "GETTIMES", "GETMILLISECOND", "GETSECOND", "RAND", "MIN", "MAX", "ABS", "SQRT", "CBRT", "LOG", "LOG10",
        "EXPONENT", "SIGN", "LIMIT", "SUMARRAY", "SUMCARRAY", "MATCH", "CMATCH", "GROUPMATCH", "NOSAMES", "ALLSAMES", "MAXARRAY",
        "MAXCARRAY", "MINARRAY", "MINCARRAY", "GETBIT", "GETNUM", "GETPALAMLV", "GETEXPLV", "FINDELEMENT", "FINDLASTELEMENT", "INRANGE",
        "INRANGEARRAY", "INRANGECARRAY", "GETNUMB", "ARRAYMSORT", "STRLENS", "STRLENSU", "SUBSTRING", "SUBSTRINGU", "STRFIND", "STRFINDU",
        "STRCOUNT", "TOSTR", "TOINT", "TOUPPER", "TOLOWER", "TOHALF", "TOFULL", "LINEISEMPTY", "REPLACE", "UNICODE", "UNICODEBYTE",
        "CONVERT", "ISNUMERIC", "ESCAPE", "CHARATU", "GETLINESTR", "STRFORM", "STRJOIN", "GETCONFIG", "GETCONFIGS", "HTML_GETPRINTEDSTR",
        "HTML_POPPRINTINGSTR", "HTML_TOPLAINTEXT", "HTML_ESCAPE", "SPRITECREATED", "SPRITEWIDTH", "SPRITEHEIGHT", "SPRITEMOVE", "SPRITESETPOS",
        "SPRITEPOSX", "SPRITEPOSY", "CLIENTWIDTH", "CLIENTHEIGHT", "GETKEY", "GETKEYTRIGGERED", "MOUSEX", "MOUSEY", "ISACTIVE", "SAVETEXT",
        "LOADTEXT", "GCREATED", "GWIDTH", "GHEIGHT", "GGETCOLOR", "SPRITEGETCOLOR", "GCREATE", "GCREATEFROMFILE", "GDISPOSE", "GCLEAR",
        "GFILLRECTANGLE", "G_POLYGON_DRAW", "G_POLYGON_FILL", "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR", "GDRAWTEXT", "GDRAWSPRITE",
        "GSETCOLOR", "GDRAWG", "GDRAWGWITHMASK", "GSETBRUSH", "GSETFONT", "GSETPEN", "SPRITECREATE", "SPRITEDISPOSE", "CBGSETG",
        "CBGSETSPRITE", "CBGCLEAR", "CBGCLEARBUTTON", "CBGREMOVERANGE", "CBGREMOVEBMAP", "CBGSETBMAPG", "CBGSETBUTTONSPRITE", "GSAVE",
        "GLOAD", "SPRITEANIMECREATE", "SPRITEANIMEADDFRAME", "SETANIMETIMER", "SQL_CONNECTION_OPEN", "SQL_EXECUTE_READER", "SQL_EXECUTE_SCALER_LONG",
        "SQL_EXECUTE_SCALER_STRING", "SQL_EXECUTE_NONQUERY", "SQL_READER_READ", "SQL_READER_GET_LONG", "SQL_READER_GET_STRING", "SQL_READER_IS_NULL",
        "EXISTFUNCTION", "DICT_CREATE", "DICT_EXIST", "DICT_CONTAINS_KEY", "DICT_SET_VALUE", "DICT_GET_VALUE_STRING", "DICT_GET_VALUE_LONG",
        "HASH_XXH3", "HASH_XXH32",
    });

    public static bool TryMap(string token, out PrototypeOpcode opcode) => Map.TryGetValue(token, out opcode);
    public static bool IsReservedLegacyCommand(string token) => ReservedLegacyCommands.Contains(token);
    public static IReadOnlyCollection<string> ReservedLegacyCommandNames => ReservedLegacyCommands;
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
    public const int FunctionDescriptorFieldPayloadBytes = 64;
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
            var isMappedCommand = LegacyOpcodeMap.TryMap(token, out var opcode);
            var isKnownCommand = isMappedCommand || LegacyOpcodeMap.IsReservedLegacyCommand(token);
            if (!isMappedCommand && LegacyOpcodeMap.IsReservedLegacyCommand(token))
                return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            var isAssignment = !isKnownCommand && IsAssignment(trimmed);
            if (isAssignment) opcode = PrototypeOpcode.SET;
            else if (opcode == PrototypeOpcode.Unsupported)
                return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            var operandStart = isAssignment ? 0 : tokenLength;
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

        static bool IsAssignment(string text)
        {
            // [Emuera改修:NEXT-1B-R2 2026-08-27]
            // Legacyの変数代入をSETとして識別するが、比較演算子は式意味論を含むため取り込まない。
            if (text.Length == 0 || text[0] is '"' or '\'') return false;
            var equal = text.IndexOf('=');
            if (equal < 0) return false;
            if (equal > 0 && text[equal - 1] is '=' or '!' or '<' or '>') return false;
            return equal + 1 >= text.Length || text[equal + 1] != '=';
        }

    }

    private static int MetadataBytesEstimate(string name) => 32 + Encoding.UTF8.GetByteCount(name);
}
