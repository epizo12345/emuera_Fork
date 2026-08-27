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

public readonly record struct CompilerCompatibilityOptions(bool IgnoreCase, bool UseScopedVariableInstruction, bool SystemAllowFullSpace, bool DebugMode)
{
    // [Emuera改修:NEXT-1B-R6 2026-08-27]
    // Legacy通常起動はconfig/JSON未指定ならIgnoreCase=true、Scoped=false、全角space=true、Debug=false。
    // fixture固有設定はAuditが明示的に解決して渡すため、compiler既定へ持ち込まない。
    public static CompilerCompatibilityOptions LegacyDefaults => new(true, false, true, false);
    public StringComparer NameComparer => IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public StringComparison NameComparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public readonly record struct LegacyIdentifierScan(string Identifier, int StartPosition, int StopPosition);

public static class LegacyIdentifierScanner
{
    // [Emuera改修:NEXT-1B-R5 2026-08-27]
    // Legacy ReadSingleIdentifierROSのdelimiter集合をそのまま小さいscannerへ移す。
    // char.IsWhiteSpaceはVT/FFまで区切るため使わず、SystemAllowFullSpaceだけを先頭/命令separatorへ反映する。
    private const string Delimiters = " 　.+-*/%=!<>|&^~?#)}],:({[$\\'\"@;\t";

    public static LegacyIdentifierScan ReadFirstIdentifier(string text, CompilerCompatibilityOptions options)
    {
        var start = SkipLeadingWhitespace(text, options);
        var length = ReadIdentifierLength(text.AsSpan(start));
        return new(text.Substring(start, length), start, start + length);
    }

    public static bool IsCommandSeparator(char value, CompilerCompatibilityOptions options) => value == '\0' || value == ';' || value == ' ' || value == '\t' || options.SystemAllowFullSpace && value == '　';

    public static int SkipCommandSeparators(ReadOnlySpan<char> text, int index, CompilerCompatibilityOptions options)
    {
        while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || options.SystemAllowFullSpace && text[index] == '　')) index++;
        return index;
    }

    private static int SkipLeadingWhitespace(string text, CompilerCompatibilityOptions options)
    {
        var index = 0;
        while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || options.SystemAllowFullSpace && text[index] == '　')) index++;
        return index;
    }

    public static int ReadIdentifierLength(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length && Delimiters.IndexOf(text[index]) < 0) index++;
        return index;
    }
}

public static class LegacyOpcodeMap
{
    // [Emuera改修:NEXT-1B-R5 2026-08-27]
    // SETはsemantic opcodeでありLegacy line-head keywordではないため、statement mapから分離する。
    // enum名の自動列挙はLegacy登録集合を表さないので、対応済みopcodeだけを明示する。
    private static readonly (string Name, PrototypeOpcode Opcode)[] MapEntries =
    [
        ("SET", PrototypeOpcode.SET), ("PRINT", PrototypeOpcode.PRINT), ("PRINTC", PrototypeOpcode.PRINTC), ("PRINTLC", PrototypeOpcode.PRINTLC), ("PRINTL", PrototypeOpcode.PRINTL), ("PRINTW", PrototypeOpcode.PRINTW), ("PRINTN", PrototypeOpcode.PRINTN), ("PRINTV", PrototypeOpcode.PRINTV), ("PRINTVL", PrototypeOpcode.PRINTVL), ("PRINTVW", PrototypeOpcode.PRINTVW), ("PRINTVN", PrototypeOpcode.PRINTVN), ("PRINTS", PrototypeOpcode.PRINTS), ("PRINTSL", PrototypeOpcode.PRINTSL), ("PRINTSW", PrototypeOpcode.PRINTSW), ("PRINTSN", PrototypeOpcode.PRINTSN),
        ("PRINTFORM", PrototypeOpcode.PRINTFORM), ("PRINTFORML", PrototypeOpcode.PRINTFORML), ("PRINTFORMW", PrototypeOpcode.PRINTFORMW), ("PRINTFORMN", PrototypeOpcode.PRINTFORMN), ("PRINTFORMS", PrototypeOpcode.PRINTFORMS), ("PRINTFORMSL", PrototypeOpcode.PRINTFORMSL), ("PRINTFORMSW", PrototypeOpcode.PRINTFORMSW), ("PRINTFORMSN", PrototypeOpcode.PRINTFORMSN), ("PRINTFORMC", PrototypeOpcode.PRINTFORMC), ("PRINTFORMLC", PrototypeOpcode.PRINTFORMLC),
        ("INPUT", PrototypeOpcode.INPUT), ("INPUTS", PrototypeOpcode.INPUTS), ("TINPUT", PrototypeOpcode.TINPUT), ("TINPUTS", PrototypeOpcode.TINPUTS), ("ONEINPUT", PrototypeOpcode.ONEINPUT), ("ONEINPUTS", PrototypeOpcode.ONEINPUTS), ("TONEINPUT", PrototypeOpcode.TONEINPUT), ("TONEINPUTS", PrototypeOpcode.TONEINPUTS), ("WAIT", PrototypeOpcode.WAIT), ("TWAIT", PrototypeOpcode.TWAIT), ("WAITANYKEY", PrototypeOpcode.WAITANYKEY), ("FORCEWAIT", PrototypeOpcode.FORCEWAIT), ("AWAIT", PrototypeOpcode.AWAIT), ("DRAWLINE", PrototypeOpcode.DRAWLINE), ("DRAWLINEFORM", PrototypeOpcode.DRAWLINEFORM), ("BAR", PrototypeOpcode.BAR), ("BARL", PrototypeOpcode.BARL),
        ("ADDCHARA", PrototypeOpcode.ADDCHARA), ("ADDSPCHARA", PrototypeOpcode.ADDSPCHARA), ("ADDDEFCHARA", PrototypeOpcode.ADDDEFCHARA), ("ADDVOIDCHARA", PrototypeOpcode.ADDVOIDCHARA), ("DELCHARA", PrototypeOpcode.DELCHARA), ("RESETCOLOR", PrototypeOpcode.RESETCOLOR), ("CUSTOMDRAWLINE", PrototypeOpcode.CUSTOMDRAWLINE), ("SETCOLOR", PrototypeOpcode.SETCOLOR), ("SETFONT", PrototypeOpcode.SETFONT),
        ("CALL", PrototypeOpcode.CALL), ("TRYCALL", PrototypeOpcode.TRYCALL), ("CALLEVENT", PrototypeOpcode.CALLEVENT), ("CALLTRAIN", PrototypeOpcode.CALLTRAIN), ("CALLF", PrototypeOpcode.CALLF), ("RETURN", PrototypeOpcode.RETURN), ("RETURNFORM", PrototypeOpcode.RETURNFORM), ("RETURNF", PrototypeOpcode.RETURNF), ("IF", PrototypeOpcode.IF), ("SIF", PrototypeOpcode.SIF), ("ELSE", PrototypeOpcode.ELSE), ("ELSEIF", PrototypeOpcode.ELSEIF), ("ENDIF", PrototypeOpcode.ENDIF), ("SELECTCASE", PrototypeOpcode.SELECTCASE), ("CASE", PrototypeOpcode.CASE), ("CASEELSE", PrototypeOpcode.CASEELSE), ("ENDSELECT", PrototypeOpcode.ENDSELECT),
        ("REPEAT", PrototypeOpcode.REPEAT), ("REND", PrototypeOpcode.REND), ("CONTINUE", PrototypeOpcode.CONTINUE), ("BREAK", PrototypeOpcode.BREAK), ("FOR", PrototypeOpcode.FOR), ("NEXT", PrototypeOpcode.NEXT), ("WHILE", PrototypeOpcode.WHILE), ("WEND", PrototypeOpcode.WEND), ("DO", PrototypeOpcode.DO), ("LOOP", PrototypeOpcode.LOOP), ("GOTO", PrototypeOpcode.GOTO), ("JUMP", PrototypeOpcode.JUMP), ("TRYJUMP", PrototypeOpcode.TRYJUMP), ("TRYGOTO", PrototypeOpcode.TRYGOTO), ("TRYGOTOFORM", PrototypeOpcode.TRYGOTOFORM),
        ("PRINTDATA", PrototypeOpcode.PRINTDATA), ("PRINTDATAL", PrototypeOpcode.PRINTDATAL), ("PRINTDATAW", PrototypeOpcode.PRINTDATAW), ("DATA", PrototypeOpcode.DATA), ("DATAFORM", PrototypeOpcode.DATAFORM), ("ENDDATA", PrototypeOpcode.ENDDATA), ("SETBIT", PrototypeOpcode.SETBIT), ("CLEARBIT", PrototypeOpcode.CLEARBIT), ("INVERTBIT", PrototypeOpcode.INVERTBIT), ("SWAP", PrototypeOpcode.SWAP), ("POWER", PrototypeOpcode.POWER), ("TIMES", PrototypeOpcode.TIMES), ("UPCHECK", PrototypeOpcode.UPCHECK), ("CUPCHECK", PrototypeOpcode.CUPCHECK), ("CLEARLINE", PrototypeOpcode.CLEARLINE), ("REUSELASTLINE", PrototypeOpcode.REUSELASTLINE), ("OUTPUTLOG", PrototypeOpcode.OUTPUTLOG), ("QUIT", PrototypeOpcode.QUIT), ("REDRAW", PrototypeOpcode.REDRAW)
    ];
    private static readonly IReadOnlyDictionary<string, PrototypeOpcode> MapIgnoreCase = BuildMap(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, PrototypeOpcode> MapCaseSensitive = BuildMap(StringComparer.Ordinal);

    private static Dictionary<string, PrototypeOpcode> BuildMap(StringComparer comparer) => MapEntries.ToDictionary(static entry => entry.Name, static entry => entry.Opcode, comparer);
    // [Emuera改修:NEXT-1B-R4 2026-08-27]
    // Legacyの実instruction dictionaryをB(statement)とC(method-backed line-head)へ分類して保持する。
    // production pathでLegacy parserを呼ばず、A=B∪Cの全行頭識別子をassignmentより先にguardする。
    private static readonly HashSet<string> LegacyStatementIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRINT", "PRINTL", "PRINTW", "PRINTN", "PRINTV", "PRINTVL", "PRINTVW", "PRINTVN",
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
        "TOOLTIP_SETDURATION", "PRINT_IMG", "PRINT_RECT", "PRINT_SPACE", "INPUTMOUSEKEY", "HTML_PRINT_ISLAND",
        "HTML_PRINT_ISLAND_CLEAR",
    };
    private static readonly HashSet<string> LegacyStatementIdentifiersCaseSensitive = LegacyStatementIdentifiers.ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> LegacyStatementIdentifiersScoped = new(LegacyStatementIdentifiers, StringComparer.OrdinalIgnoreCase) { "VARI", "VARS" };
    private static readonly HashSet<string> LegacyStatementIdentifiersScopedCaseSensitive = LegacyStatementIdentifiersScoped.ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> LegacyMethodBackedLineHeads = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "ALLSAMES", "ARRAYMSORT", "BARSTR", "CBGCLEAR", "CBGCLEARBUTTON", "CBGREMOVEBMAP", "CBGREMOVERANGE",
        "CBGSETBMAPG", "CBGSETBUTTONSPRITE", "CBGSETG", "CBGSETSPRITE", "CBRT", "CHARATU", "CHKCHARADATA", "CHKDATA",
        "CHKFONT", "CLIENTHEIGHT", "CLIENTWIDTH", "CMATCH", "COLOR_FROMNAME", "COLOR_FROMRGB", "CONVERT", "CSVABL",
        "CSVBASE", "CSVCALLNAME", "CSVCFLAG", "CSVCSTR", "CSVEQUIP", "CSVEXP", "CSVJUEL", "CSVMARK", "CSVMASTERNAME",
        "CSVNAME", "CSVNICKNAME", "CSVRELATION", "CSVTALENT", "CURRENTALIGN", "CURRENTREDRAW", "DICT_CONTAINS_KEY",
        "DICT_CREATE", "DICT_EXIST", "DICT_GET_VALUE_LONG", "DICT_GET_VALUE_STRING", "DICT_SET_VALUE", "ESCAPE", "EXISTCSV",
        "EXISTFUNCTION", "EXPONENT", "FINDCHARA", "FINDELEMENT", "FINDLASTCHARA", "FINDLASTELEMENT", "FIND_CHARADATA", "GCLEAR",
        "GCREATE", "GCREATED", "GCREATEFROMFILE", "GDISPOSE", "GDRAWG", "GDRAWGWITHMASK", "GDRAWSPRITE", "GDRAWTEXT",
        "GETBGCOLOR", "GETBIT", "GETCHARA", "GETCOLOR", "GETCONFIG", "GETCONFIGS", "GETCSVNOBYCALLNAME", "GETCSVNOBYMASTERNAME",
        "GETCSVNOBYNAME", "GETCSVNOBYNICKNAME", "GETDEFBGCOLOR", "GETDEFCOLOR", "GETEXPLV", "GETFOCUSCOLOR", "GETFONT", "GETKEY",
        "GETKEYTRIGGERED", "GETLINESTR", "GETMILLISECOND", "GETNUM", "GETNUMB", "GETPALAMLV", "GETSECOND", "GETSPCHARA", "GETSTYLE",
        "GETTIMES", "GFILLRECTANGLE", "GGETCOLOR", "GHEIGHT", "GLOAD", "GROUPMATCH", "GSAVE", "GSETBRUSH", "GSETCOLOR",
        "GSETFONT", "GSETPEN", "GWIDTH", "G_POLYGON_DRAW", "G_POLYGON_FILL", "G_POLYGON_POINT_ADD", "G_POLYGON_POINT_CLEAR",
        "HASH_XXH3", "HASH_XXH32", "HTML_ESCAPE", "HTML_GETPRINTEDSTR", "HTML_POPPRINTINGSTR", "HTML_TOPLAINTEXT", "INRANGE",
        "INRANGEARRAY", "INRANGECARRAY", "ISACTIVE", "ISNUMERIC", "ISSKIP", "LIMIT", "LINEISEMPTY", "LOADTEXT", "LOG", "LOG10",
        "MATCH", "MAX", "MAXARRAY", "MAXCARRAY", "MESSKIP", "MIN", "MINARRAY", "MINCARRAY", "MONEYSTR", "MOUSESKIP",
        "MOUSEX", "MOUSEY", "NOSAMES", "PRINTCLENGTH", "RAND", "REPLACE", "SAVETEXT", "SETANIMETIMER", "SIGN",
        "SPRITEANIMEADDFRAME", "SPRITEANIMECREATE", "SPRITECREATE", "SPRITECREATED", "SPRITEDISPOSE", "SPRITEGETCOLOR",
        "SPRITEHEIGHT", "SPRITEMOVE", "SPRITEPOSX", "SPRITEPOSY", "SPRITESETPOS", "SPRITEWIDTH", "SQL_CONNECTION_OPEN",
        "SQL_EXECUTE_NONQUERY", "SQL_EXECUTE_READER", "SQL_EXECUTE_SCALER_LONG", "SQL_EXECUTE_SCALER_STRING", "SQL_READER_GET_LONG",
        "SQL_READER_GET_STRING", "SQL_READER_IS_NULL", "SQL_READER_READ", "SQRT", "STRCOUNT", "STRFIND", "STRFINDU", "STRFORM",
        "STRJOIN", "STRLENS", "STRLENSU", "SUBSTRING", "SUBSTRINGU", "SUMARRAY", "SUMCARRAY", "TOFULL", "TOHALF", "TOINT",
        "TOLOWER", "TOSTR", "TOUPPER", "UNICODE", "UNICODEBYTE",
    };
    private static readonly HashSet<string> LegacyMethodBackedLineHeadsCaseSensitive = LegacyMethodBackedLineHeads.ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> StatementSet(CompilerCompatibilityOptions options) => options.IgnoreCase
        ? (options.UseScopedVariableInstruction ? LegacyStatementIdentifiersScoped : LegacyStatementIdentifiers)
        : (options.UseScopedVariableInstruction ? LegacyStatementIdentifiersScopedCaseSensitive : LegacyStatementIdentifiersCaseSensitive);

    private static HashSet<string> MethodSet(CompilerCompatibilityOptions options) => options.IgnoreCase ? LegacyMethodBackedLineHeads : LegacyMethodBackedLineHeadsCaseSensitive;

    public static bool TryMap(string token, out PrototypeOpcode opcode) => MapIgnoreCase.TryGetValue(token, out opcode);
    public static bool TryMap(string token, CompilerCompatibilityOptions options, out PrototypeOpcode opcode) => (options.IgnoreCase ? MapIgnoreCase : MapCaseSensitive).TryGetValue(token, out opcode);
    public static bool TryMapStatementIdentifier(string token, CompilerCompatibilityOptions options, out PrototypeOpcode opcode)
    {
        opcode = PrototypeOpcode.Unsupported;
        return IsStatementIdentifier(token, options) && TryMap(token, options, out opcode) && opcode != PrototypeOpcode.SET;
    }
    public static bool IsLegacyLineHeadIdentifier(string token, CompilerCompatibilityOptions options) => IsStatementIdentifier(token, options) || IsMethodBackedLineHead(token, options);
    public static bool IsAssignmentGuardIdentifier(string token) => IsLegacyLineHeadIdentifier(token, CompilerCompatibilityOptions.LegacyDefaults);
    public static bool IsAssignmentGuardIdentifier(string token, CompilerCompatibilityOptions options) => IsLegacyLineHeadIdentifier(token, options);
    public static bool IsStatementIdentifier(string token) => IsStatementIdentifier(token, CompilerCompatibilityOptions.LegacyDefaults);
    public static bool IsStatementIdentifier(string token, CompilerCompatibilityOptions options) => StatementSet(options).Contains(token);
    public static bool IsMethodBackedLineHead(string token) => IsMethodBackedLineHead(token, CompilerCompatibilityOptions.LegacyDefaults);
    public static bool IsMethodBackedLineHead(string token, CompilerCompatibilityOptions options) => MethodSet(options).Contains(token);
    public static IReadOnlyCollection<string> AssignmentGuardIdentifierNames => GetAssignmentGuardIdentifierNames(CompilerCompatibilityOptions.LegacyDefaults);
    public static IReadOnlyCollection<string> StatementIdentifierNames => StatementSet(CompilerCompatibilityOptions.LegacyDefaults);
    public static IReadOnlyCollection<string> MethodBackedLineHeadNames => MethodSet(CompilerCompatibilityOptions.LegacyDefaults);
    public static IReadOnlyCollection<string> SupportedStatementIdentifierNames => GetSupportedStatementIdentifierNames(CompilerCompatibilityOptions.LegacyDefaults);
    public static string[] GetAssignmentGuardIdentifierNames(CompilerCompatibilityOptions options) => StatementSet(options).Concat(MethodSet(options)).Distinct(options.NameComparer).Order(options.NameComparer).ToArray();
    public static string[] StatementIdentifierNamesFor(CompilerCompatibilityOptions options) => StatementSet(options).Order(options.NameComparer).ToArray();
    public static string[] MethodBackedLineHeadNamesFor(CompilerCompatibilityOptions options) => MethodSet(options).Order(options.NameComparer).ToArray();
    public static string[] GetSupportedStatementIdentifierNames(CompilerCompatibilityOptions options) => MapEntries.Where(entry => entry.Opcode != PrototypeOpcode.SET && IsStatementIdentifier(entry.Name, options)).Select(static entry => entry.Name).Order(options.NameComparer).ToArray();
    public static string[] SupportedLegacyNames => MapIgnoreCase.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
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
    private readonly CompilerCompatibilityOptions options;

    // [Emuera改修:NEXT-1B-R6 2026-08-27]
    // 無指定compilerはLegacy通常起動の意味だけを委譲し、現在fixtureの設定を暗黙には読まない。
    public FunctionCompiler(CompilerCompatibilityOptions? options = null) => this.options = options ?? CompilerCompatibilityOptions.LegacyDefaults;

    public CompilerCompatibilityOptions Options => options;

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
            var instructions = Scan(source.Bytes, source.Function.Span.StartLine, options, out var reason, out var detail);
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

    private static ImmutableArray<PrototypeInstruction> Scan(byte[] bytes, int startLine, CompilerCompatibilityOptions options, out UnsupportedReason reason, out string? detail)
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
            var scan = LegacyIdentifierScanner.ReadFirstIdentifier(text, options);
            var trimStart = scan.StartPosition;
            var trimmed = text[trimStart..];
            if (trimmed.Length == 0) { line++; continue; }
            // [Emuera改修:NEXT-1B-R6 2026-08-27]
            // DebugModeの;#;は命令を消してCompiled扱いにせず、未実装の最小fallbackへ送る。
            if (trimmed.StartsWith(";#;", StringComparison.Ordinal))
            {
                if (options.DebugMode) return Fail(UnsupportedReason.UnknownSyntax, "DebugMode ;#; prefix requires Legacy fallback", out reason, out detail);
                line++; continue;
            }
            if (trimmed[0] == ';') { line++; continue; }
            if (trimmed.EndsWith('\\')) return Fail(UnsupportedReason.Multiline, "line continuation", out reason, out detail);
            if (trimmed[0] is '[' or '#' or '$' or '}' or '{' or '@')
                return Fail(trimmed[0] == '$' ? UnsupportedReason.LocalLabelOrGoto : UnsupportedReason.UnknownSyntax, "unsupported structural line", out reason, out detail);
            var tokenLength = scan.StopPosition - scan.StartPosition;
            if (tokenLength == 0) return Fail(UnsupportedReason.UnknownSyntax, "empty instruction", out reason, out detail);
            var token = trimmed[..tokenLength];
            var isMappedCommand = LegacyOpcodeMap.TryMapStatementIdentifier(token, options, out var opcode);
            var isKnownLineHead = LegacyOpcodeMap.IsLegacyLineHeadIdentifier(token, options);
            var separator = tokenLength < trimmed.Length ? trimmed[tokenLength] : '\0';
            if (isKnownLineHead)
            {
                if (!LegacyIdentifierScanner.IsCommandSeparator(separator, options))
                    return Fail(UnsupportedReason.UnknownSyntax, $"invalid command separator after: {token}", out reason, out detail);
                if (!isMappedCommand)
                    return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            }
            else if (!TryFindAssignment(trimmed, out _))
            {
                return Fail(UnsupportedReason.UnsupportedInstruction, $"unsupported instruction: {token}", out reason, out detail);
            }
            var isAssignment = !isKnownLineHead;
            if (isAssignment) opcode = PrototypeOpcode.SET;
            var operandStart = isAssignment ? 0 : LegacyIdentifierScanner.SkipCommandSeparators(trimmed, tokenLength, options);
            var operand = operandStart < trimmed.Length && trimmed[operandStart] != ';' ? trimmed[operandStart..].TrimEnd() : string.Empty;
            var operandOffset = lineStart + Encoding.UTF8.GetByteCount(text[..(trimStart + operandStart)]);
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

        static bool TryFindAssignment(ReadOnlySpan<char> text, out int operatorStart)
        {
            // [Emuera改修:NEXT-1B-R5 2026-08-27]
            // 複雑なlvalueを一律に拒否せず、Legacyのassignment候補だけを構造的に拾い、未知行をSETへ誤分類しない。
            operatorStart = -1;
            var depth = 0;
            var quote = '\0';
            for (var index = 0; index < text.Length; index++)
            {
                var current = text[index];
                if (quote != '\0')
                {
                    if (current == '\\') index++;
                    else if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '\'' && index + 1 < text.Length && text[index + 1] == '=') { operatorStart = index; break; }
                if (current is '"' or '\'') { quote = current; continue; }
                if (current is '(' or '[' or '{') { depth++; continue; }
                if (current is ')' or ']' or '}') { if (depth == 0) return false; depth--; continue; }
                if (depth != 0) continue;
                if (index + 1 < text.Length && (current is '+' or '-' or '*' or '/' or '%' or '|' or '&' or '^') && text[index + 1] == '=') { operatorStart = index; break; }
                if (current == '=' && (index + 1 == text.Length || text[index + 1] != '=') && (index == 0 || text[index - 1] is not ('=' or '!' or '<' or '>'))) { operatorStart = index; break; }
            }
            return operatorStart >= 0 && IsStructuralLValue(text[..operatorStart]);
        }

        static bool IsStructuralLValue(ReadOnlySpan<char> text)
        {
            text = text.Trim();
            if (text.IsEmpty) return false;
            var index = 0;
            var sawIdentifier = false;
            var afterColon = false;
            while (index < text.Length)
            {
                while (index < text.Length && text[index] is ' ' or '\t' or '　') index++;
                if (index == text.Length) break;
                if (text[index] == ':') { if (!sawIdentifier) return false; afterColon = true; index++; continue; }
                if (text[index] is '"' or '\'') { if (!sawIdentifier) return false; if (!SkipQuoted(text, ref index)) return false; afterColon = false; continue; }
                if (text[index] == '@' && index + 1 < text.Length && (text[index + 1] is '"' or '\'')) { index++; if (!SkipQuoted(text, ref index)) return false; afterColon = false; continue; }
                if (text[index] is '(' or '[' or '{') { if (!SkipBalanced(text, ref index)) return false; afterColon = false; continue; }
                var length = LegacyIdentifierScanner.ReadIdentifierLength(text[index..]);
                if (length == 0 || sawIdentifier && !afterColon) return false;
                sawIdentifier = true;
                afterColon = false;
                index += length;
            }
            return sawIdentifier;
        }

        static bool SkipQuoted(ReadOnlySpan<char> text, ref int index)
        {
            var quote = text[index++];
            while (index < text.Length)
            {
                if (text[index] == '\\') { index += Math.Min(2, text.Length - index); continue; }
                if (text[index++] == quote) return true;
            }
            return false;
        }

        static bool SkipBalanced(ReadOnlySpan<char> text, ref int index)
        {
            var opening = text[index++];
            var closing = opening switch { '(' => ')', '[' => ']', _ => '}' };
            var depth = 1;
            while (index < text.Length)
            {
                if (text[index] is '"' or '\'') { if (!SkipQuoted(text, ref index)) return false; continue; }
                if (text[index] == opening) depth++;
                else if (text[index] == closing && --depth == 0) { index++; return true; }
                index++;
            }
            return false;
        }

    }

    private static int MetadataBytesEstimate(string name) => 32 + Encoding.UTF8.GetByteCount(name);
}
