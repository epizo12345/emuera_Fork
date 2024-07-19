using System.Globalization;
using System.Threading;

namespace MinorShift.Emuera.UI.Framework;

public static class LocalizationManager
{
    //Main window

    public static class Parameters
    {
        public static string HelpExeDir => FormLocalization.Parameters_HelpExeDir;
        public static string HelpDebug => FormLocalization.Parameters_HelpDebug;
        public static string HelpfilesArg => FormLocalization.Parameters_HelpFilesArg;
    }

    public static class MainWindow
    {
        public static string File => FormLocalization.MainWindow_File;
        public static string FileFilter => FormLocalization.MainWindow_FileFilter;
        public static string Restart => FormLocalization.MainWindow_Restart;
        public static string RestartDebugMode => FormLocalization.RestartDebugMode;
        public static string SaveLog => FormLocalization.MainWindow_SaveLog;
        public static string CopyLogToClipboard => FormLocalization.CopyLogToClipboard;
        public static string BackToTitle => FormLocalization.MainWindow_BackToTitle;
        public static string ReloadAllScripts => FormLocalization.MainWindow_ReloadAllScripts;
        public static string ReloadFolder => FormLocalization.MainWindow_ReloadFolder;
        public static string ReloadScriptFile => FormLocalization.MainWindow_ReloadScriptFile;
        public static string ContextMenu_KeyMacro => FormLocalization.ContextMenu_KeyMacro;
        public static string ContextMenu_Cut => FormLocalization.ContextMenu_Cut;
        public static string ContextMenu_Copy => FormLocalization.ContextMenu_Copy;
        public static string ContextMenu_Paste => FormLocalization.ContextMenu_Paste;
        public static string ContextMenu_Delete => FormLocalization.ContextMenu_Delete;
        public static string ContextMenu_Execute => FormLocalization.ContextMenu_Execute;
        public static string ContextMenu_KeyMacroGroup => FormLocalization.ContextMenu_KeyMacroGroup;
        public static string ContextMenu_KeyMacroGroup_Group => FormLocalization.ContextMenu_KeyMacroGroup_Group;


        public static string Debug => FormLocalization.MainWindow_Debug;
        public static string OpenDebugWindow => FormLocalization.MainWindow_OpenDebugWindow;
        public static string UpdateDebugInfo => FormLocalization.UpdateDebugInfo;


        public static string Tools => FormLocalization.MainWindow_Tools;
        public static string ToggleWidthLock => FormLocalization.MainWindow_ToggleWidthLock;
        public static string CopyToClipboard => FormLocalization.MainWindow_CopyToClipboard;


        public static string Settings => FormLocalization.MainWindow_Settings;
        public static string Exit => FormLocalization.MainWindow_Exit;


        public static string Language => FormLocalization.MainWindow_Language;
    }

    public static class ConfigDialog
    {
        public static string Title => FormLocalization.ConfigDialog_Title;

        public static string ChangeWontTakeEffectUntilRestart =>
            FormLocalization.ConfigDialog_ChangeWontTakeEffectUntilRestart;

        public static string Save => FormLocalization.ConfigDialog_Save;
        public static string SaveAndRestart => FormLocalization.ConfigDialog_SaveAndRestart;
        public static string Cancel => FormLocalization.ConfigDialog_Cancel;


        public static string Environment => FormLocalization.ConfigDialog_Environment;
        public static string Environment_UseMouse => FormLocalization.ConfigDialog_Environment_UseMouse;
        public static string Environment_UseMenu => FormLocalization.ConfigDialog_Environment_UseMenu;
        public static string Environment_UseDebugCommand => FormLocalization.ConfigDialog_Environment_UseDebugCommand;

        public static string Environment_AllowMultipleInstances =>
            FormLocalization.ConfigDialog_Environment_AllowMultipleInstances;

        public static string Environment_UseKeyMacro => FormLocalization.ConfigDialog_Environment_UseKeyMacro;
        public static string Environment_AutoSave => FormLocalization.ConfigDialog_Environment_AutoSave;
        public static string Environment_UseSaveFolder => FormLocalization.ConfigDialog_Environment_UseSaveFolder;
        public static string Environment_MaxLog => FormLocalization.ConfigDialog_Environment_MaxLog;

        public static string Environment_InfiniteLoopAlertTime =>
            FormLocalization.ConfigDialog_Environment_InfiniteLoopAlertTime;

        public static string Environment_SaveDataPerPage => FormLocalization.ConfigDialog_Environment_SaveDataPerPage;
        public static string Environment_TextEditor => FormLocalization.ConfigDialog_Environment_TextEditor;
        public static string Environment_Browse => FormLocalization.ConfigDialog_Environment_Browse;

        public static string Environment_TextEditorCommandline =>
            FormLocalization.ConfigDialog_Environment_TextEditorCommandline;

        public static string Environment_TextEditorCommandline_UserSetting =>
            FormLocalization.ConfigDialog_Environment_TextEditorCommandline_UserSetting;


        public static string Display => FormLocalization.ConfigDialog_Display;
        public static string Display_TextDrawingMode => FormLocalization.ConfigDialog_Display_TextDrawingMode;
        public static string Display_FPS => FormLocalization.ConfigDialog_Display_FPS;
        public static string Display_PrintCPerLine => FormLocalization.ConfigDialog_Display_PrintCPerLine;
        public static string Display_PrintCLength => FormLocalization.ConfigDialog_Display_PrintCLength;
        public static string Display_ButtonWrap => FormLocalization.ConfigDialog_Display_ButtonWrap;
        public static string Display_UseButtonFocusColor => FormLocalization.ConfigDialog_Display_UseButtonFocusColor;


        public static string Window => FormLocalization.ConfigDialog_Window;
        public static string Window_WindowWidth => FormLocalization.ConfigDialog_Window_WindowWidth;
        public static string Window_WindowHeight => FormLocalization.ConfigDialog_Window_WindowHeight;
        public static string Window_GetWindowSize => FormLocalization.ConfigDialog_Window_GetWindowSize;

        public static string Window_ChangeableWindowHeight =>
            FormLocalization.ConfigDialog_Window_ChangeableWindowHeight;

        public static string Window_WindowMaximixed => FormLocalization.ConfigDialog_Window_WindowMaximixed;
        public static string Window_SetWindowPos => FormLocalization.ConfigDialog_Window_SetWindowPos;
        public static string Window_WindowX => FormLocalization.ConfigDialog_Window_WindowX;
        public static string Window_WindowY => FormLocalization.ConfigDialog_Window_WindowY;
        public static string Window_GetWindowPos => FormLocalization.ConfigDialog_Window_GetWindowPos;
        public static string Window_LinesPerScroll => FormLocalization.ConfigDialog_Window_LinesPerScroll;


        public static string Font => FormLocalization.ConfigDialog_Font;
        public static string Font_BackgroundColor => FormLocalization.ConfigDialog_Font_BackgroundColor;
        public static string Font_TextColor => FormLocalization.ConfigDialog_Font_TextColor;
        public static string Font_HighlightColor => FormLocalization.ConfigDialog_Font_HighlightColor;
        public static string Font_LogHistoryColor => FormLocalization.ConfigDialog_Font_LogHistoryColor;
        public static string Font_FontName => FormLocalization.ConfigDialog_Font_FontName;
        public static string Font_FontSize => FormLocalization.ConfigDialog_Font_FontSize;
        public static string Font_LineHeight => FormLocalization.ConfigDialog_Font_LineHeight;


        public static string System => FormLocalization.ConfigDialog_System;
        public static string System_Warning => FormLocalization.ConfigDialog_System_Warning;
        public static string System_IgnoreCase => FormLocalization.ConfigDialog_System_IgnoreCase;
        public static string System_UseRename => FormLocalization.ConfigDialog_System_UseRename;
        public static string System_UseReplace => FormLocalization.ConfigDialog_System_UseReplace;
        public static string System_SearchSubfolder => FormLocalization.ConfigDialog_System_SearchSubfolder;
        public static string System_SortFileNames => FormLocalization.ConfigDialog_System_SortFileNames;
        public static string System_SystemFuncOverride => FormLocalization.ConfigDialog_System_SystemFuncOverride;

        public static string System_SystemFuncOverrideWarn =>
            FormLocalization.ConfigDialog_System_SystemFuncOverrideWarn;

        public static string System_DuplicateFuncWarn => FormLocalization.ConfigDialog_System_DuplicateFuncWarn;
        public static string System_WSIncludesFullWidth => FormLocalization.ConfigDialog_System_WSIncludesFullWidth;
        public static string System_ANSI => FormLocalization.ConfigDialog_System_ANSI;


        public static string System2 => FormLocalization.ConfigDialog_System2;
        public static string System2_IgnoreTripleSymbol => FormLocalization.ConfigDialog_System2_IgnoreTripleSymbol;
        public static string System2_SaveInBinary => FormLocalization.ConfigDialog_System2_SaveInBinary;
        public static string System2_SaveInUTF8 => FormLocalization.ConfigDialog_System2_SaveInUTF8;
        public static string System2_NoAutoCompleteCVar => FormLocalization.ConfigDialog_System2_NoAutoCompleteCVar;
        public static string System2_UseNewRandom => FormLocalization.ConfigDialog_System2_UseNewRandom;

        public static string System2_UseScopedVariableInstruction =>
            FormLocalization.ConfigDialog_System2_UseScopedVariableInstruction;


        public static string Compatibility => FormLocalization.ConfigDialog_Compatibility;
        public static string Compatibility_Warning => FormLocalization.ConfigDialog_Compatibility_Warning;

        public static string Compatibility_ExecuteErrorLine =>
            FormLocalization.ConfigDialog_Compatibility_ExecuteErrorLine;

        public static string Compatibility_NameForCallname =>
            FormLocalization.ConfigDialog_Compatibility_NameForCallname;

        public static string Compatibility_EramakerRAND => FormLocalization.ConfigDialog_Compatibility_EramakerRAND;
        public static string Compatibility_EramakerTIMES => FormLocalization.ConfigDialog_Compatibility_EramakerTIMES;
        public static string Compatibility_NoIgnoreCase => FormLocalization.ConfigDialog_Compatibility_NoIgnoreCase;
        public static string Compatibility_CallEvent => FormLocalization.ConfigDialog_Compatibility_CallEvent;

        public static string Compatibility_UseSPCharacters =>
            FormLocalization.ConfigDialog_Compatibility_UseSPCharacters;

        public static string Compatibility_ButtonWarp => FormLocalization.ConfigDialog_Compatibility_ButtonWarp;
        public static string Compatibility_OmitArgs => FormLocalization.ConfigDialog_Compatibility_OmitArgs;
        public static string Compatibility_AutoTOSTR => FormLocalization.ConfigDialog_Compatibility_AutoTOSTR;

        public static string Compatibility_EramakerStandard =>
            FormLocalization.ConfigDialog_Compatibility_EramakerStandard;

        public static string Compatibility_EmueraStandard => FormLocalization.ConfigDialog_Compatibility_EmueraStandard;


        public static string Debug => FormLocalization.ConfigDialog_Debug;
        public static string Debug_CompatibilityWarn => FormLocalization.ConfigDialog_Debug_CompatibilityWarn;
        public static string Debug_LoadingReport => FormLocalization.ConfigDialog_Debug_LoadingReport;
        public static string Debug_IgnoreUnusedFuncs => FormLocalization.ConfigDialog_Debug_IgnoreUnusedFuncs;
        public static string Debug_FuncNotFoundWarn => FormLocalization.ConfigDialog_Debug_FuncNotFoundWarn;
        public static string Debug_UnusedFuncWarn => FormLocalization.ConfigDialog_Debug_UnusedFuncWarn;
        public static string Debug_PlayerStandard => FormLocalization.ConfigDialog_Debug_PlayerStandard;
        public static string Debug_DeveloperStandard => FormLocalization.ConfigDialog_Debug_DeveloperStandard;
        public static string Debug_ReduceArgs => FormLocalization.ConfigDialog_Debug_ReduceArgs;
        public static string Debug_ReduceArgs_Never => FormLocalization.ConfigDialog_Debug_ReduceArgs_Never;
        public static string Debug_ReduceArgs_OnUpdate => FormLocalization.ConfigDialog_Debug_ReduceArgs_OnUpdate;
        public static string Debug_ReduceArgs_Always => FormLocalization.ConfigDialog_Debug_ReduceArgs_Always;
        public static string Debug_WarnLevel => FormLocalization.ConfigDialog_Debug_WarnLevel;
        public static string Debug_WarnLevel_Level0 => FormLocalization.ConfigDialog_Debug_WarnLevel_Level0;
        public static string Debug_WarnLevel_Level1 => FormLocalization.ConfigDialog_Debug_WarnLevel_Level1;
        public static string Debug_WarnLevel_Level2 => FormLocalization.ConfigDialog_Debug_WarnLevel_Level2;
        public static string Debug_WarnLevel_Level3 => FormLocalization.ConfigDialog_Debug_WarnLevel_Level3;
        public static string Debug_WarnSetting_Ignore => FormLocalization.ConfigDialog_Debug_WarnSetting_Ignore;

        public static string Debug_WarnSetting_TotalNumber =>
            FormLocalization.ConfigDialog_Debug_WarnSetting_TotalNumber;

        public static string Debug_WarnSetting_OncePerFile =>
            FormLocalization.ConfigDialog_Debug_WarnSetting_OncePerFile;

        public static string Debug_WarnSetting_Always => FormLocalization.ConfigDialog_Debug_WarnSetting_Always;
        
        public static string Clipboard => FormLocalization.ConfigDialog_Clipboard;
        public static string Clipboard_IgnoreTags => FormLocalization.ConfigDialog_Clipboard_IgnoreTags;
        public static string Clipboard_ReplaceTags => FormLocalization.ConfigDialog_Clipboard_ReplaceTags;
        public static string Clipboard_NewLineOnly => FormLocalization.ConfigDialog_Clipboard_NewLineOnly;
        public static string Clipboard_ClearClipboard => FormLocalization.ConfigDialog_Clipboard_ClearClipboard;
        public static string Clipboard_TriggerToUse => FormLocalization.ConfigDialog_Clipboard_TriggerToUse;
        public static string Clipboard_LClick => FormLocalization.ConfigDialog_Clipboard_LClick;
        public static string Clipboard_MClick => FormLocalization.ConfigDialog_Clipboard_MClick;
        public static string Clipboard_DoubleClick => FormLocalization.ConfigDialog_Clipboard_DoubleClick;
        public static string Clipboard_AnyKeyWait => FormLocalization.ConfigDialog_Clipboard_AnyKeyWait;
        public static string Clipboard_InputWait => FormLocalization.ConfigDialog_Clipboard_InputWait;
        public static string Clipboard_LinesToClipboard => FormLocalization.ConfigDialog_Clipboard_LinesToClipboard;
        public static string Clipboard_TotalBuffer => FormLocalization.ConfigDialog_Clipboard_TotalBuffer;
        public static string Clipboard_LinesToScroll => FormLocalization.ConfigDialog_Clipboard_LinesToScroll;
        public static string Clipboard_UpdateTime => FormLocalization.ConfigDialog_Clipboard_UpdateTime;
        public static string Clipboard_ScrollThrough => FormLocalization.ConfigDialog_Clipboard_ScrollThrough;
    }

    public static class DebugConfigDialog
    {
        public static string Title => FormLocalization.DebugConfigDialog_Title;
        public static string Name => FormLocalization.DebugConfigDialog_Name;
        public static string Warning => FormLocalization.DebugConfigDialog_Warning;
        public static string OpenDebugWindowOnStartup => FormLocalization.DebugConfigDialog_OpenDebugWindowOnStartup;
        public static string AlwaysOnTop => FormLocalization.DebugConfigDialog_AlwaysOnTop;
        public static string WindowWidth => FormLocalization.DebugConfigDialog_WindowWidth;
        public static string WindowHeight => FormLocalization.DebugConfigDialog_WindowHeight;
        public static string SetWindowPos => FormLocalization.DebugConfigDialog_SetWindowPos;
        public static string WindowX => FormLocalization.DebugConfigDialog_WindowX;
        public static string WindowY => FormLocalization.DebugConfigDialog_WindowY;
    }

    public static class DebugDialog
    {
        public static string Title => FormLocalization.DebugDialog_Title;
        public static string StackTrace => FormLocalization.DebugDialog_StackTrace;
        public static string Console => FormLocalization.DebugDialog_Console;
        public static string StayOnTop => FormLocalization.DebugDialog_StayOnTop;
        public static string UpdateData => FormLocalization.DebugDialog_UpdateData;
        public static string Close => FormLocalization.DebugDialog_Close;
        public static string File_SaveWatchList => FormLocalization.DebugDialog_File_SaveWatchList;
        public static string File_LoadWatchList => FormLocalization.DebugDialog_File_LoadWatchList;
        public static string Setting => FormLocalization.DebugDialog_Setting;
        public static string Setting_Config => FormLocalization.DebugDialog_Setting_Config;
        public static string VariableWatch => FormLocalization.DebugDialog_VariableWatch;
        public static string VariableWatch_Object => FormLocalization.DebugDialog_VariableWatch_Object;
        public static string VariableWatch_Value => FormLocalization.DebugDialog_VariableWatch_Value;
    }

    public static class MsgBox
    {
        public static string InstanceExists => FormLocalization.MsgBox_InstanceExists;
        public static string MultiInstanceInfo => FormLocalization.MsgBox_MultiInstanceInfo;
        public static string FolderNotFound => FormLocalization.MsgBox_FolderNotFound;
        public static string NoCsvFolder => FormLocalization.MsgBox_NoCsvFolder;
        public static string NoErbFolder => FormLocalization.MsgBox_NoErbFolder;
        public static string FailedCreateDebugFolder => FormLocalization.MsgBox_FailedCreateDebugFolder;
        public static string InvalidArg => FormLocalization.MsgBox_InvalidArg;
        public static string ConfigError => FormLocalization.MessageBox_ConfigError;
        public static string TooSmallFontSize => FormLocalization.MessageBox_TooSmallFontSize;
        public static string WINAPINotSupported => FormLocalization.MessageBox_WINAPINotSupported;
        public static string LineHeightLessThanFontSize => FormLocalization.MessageBox_LineHeightLessThanFontSize;
        public static string TooSmallDisplaySaveData => FormLocalization.MessageBox_TooSmallDisplaySaveData;
        public static string TooLargeDisplaySaveData => FormLocalization.MessageBox_TooLargeDisplaySaveData;
        public static string TooSmallLogSize => FormLocalization.MessageBox_TooSmallLogSize;
        public static string FolderCreationFailure => FormLocalization.MessageBox_FolderCreationFailure;
        public static string FailedCreateSavFolder => FormLocalization.MessageBox_FailedCreateSavFolder;
        public static string SavFolderCreated => FormLocalization.MessageBox_SavFolderCreated;
        public static string CanNotUseWhenError => FormLocalization.MessageBox_CanNotUseWhenError;
        public static string CanNotUseWhenInitialize => FormLocalization.MessageBox_CanNotUseWhenInitialize;
        public static string DataTransfer => FormLocalization.MessageBox_DataTransfer;
        public static string MissingSavFolder => FormLocalization.MessageBox_MissingSavFolder;
        public static string DataTransferFailure => FormLocalization.MessageBox_DataTransferFailure;
        public static string FailedCreateDataFolder => FormLocalization.MessageBox_FailedCreateDataFolder;
        public static string FailedMoveSavFiles => FormLocalization.MessageBox_FailedMoveSavFiles;
        public static string ConfigFileError => FormLocalization.MessageBox_ConfigFileError;
        public static string ReplaceFileError => FormLocalization.MessageBox_ReplaceFileError;
        public static string ReplaceError => FormLocalization.MessageBox_ReplaceError;
        public static string InfiniteLoop => FormLocalization.MessageBox_InfiniteLoop;
        public static string TooLongLoop => FormLocalization.MessageBox_TooLongLoop;
        public static string IllegalFontError => FormLocalization.MessageBox_IllegalFontError;
        public static string IllegalFont => FormLocalization.MessageBox_IllegalFont;
        public static string FailedOutputLogError => FormLocalization.MessageBox_FailedOutputLogError;
        public static string FailedOutputLog => FormLocalization.MessageBox_FailedOutputLog;
        public static string CanOnlyOutputToSubDirectory => FormLocalization.MessageBox_CanOnlyOutputToSubDirectory;
        public static string NotAvailableDuringScript => FormLocalization.MessageBox_NotAvailableDuringScript;
        public static string FileNotFound => FormLocalization.MessageBox_FileNotFound;
        public static string IsNotErb => FormLocalization.MessageBox_IsNotErb;
        public static string FileFormatError => FormLocalization.MessageBox_FileFormatError;
        public static string ErrorInAnalysisMode => FormLocalization.MessageBox_ErrorInAnalysisMode;
        public static string CanNotReturnToTitle => FormLocalization.MessageBox_CanNotReturnToTitle;
        public static string ReturnToTitleAsk => FormLocalization.MessageBox_ReturnToTitleAsk;
        public static string ReturnToTitle => FormLocalization.MessageBox_ReturnToTitle;
        public static string RestartAsk => FormLocalization.MessageBox_RestartAsk;
        public static string Restart => FormLocalization.MessageBox_Restart;
        public static string ExitAsk => FormLocalization.MessageBox_ExitAsk;
        public static string Exit => FormLocalization.MessageBox_Exit;
        public static string ReloadErbAsk => FormLocalization.MessageBox_ReloadErbAsk;
        public static string ReloadErb => FormLocalization.MessageBox_ReloadErb;
        public static string CanNotOpenClipboard => FormLocalization.MessageBox_CanNotOpenClipboard;
        public static string NotAllowChangeSetting => FormLocalization.MessageBox_NotAllowChangeSetting;
        public static string UnableChangeSetting => FormLocalization.MessageBox_UnableChangeSetting;
    }

    public static class KeyMacro
    {
        public static string SetMacroGroup => FormLocalization.KeyMacro_SetMacroGroup;
        public static string MacroKeyF => FormLocalization.KeyMacro_MacroKeyF;
        public static string GMacroKeyF => FormLocalization.KeyMacro_GMacroKeyF;
    }

    public static class Error
    {
        public static string WarnPrefix => ConsoleLocalization.Error_WarnPrefix;
        public static string FuncPrefix => ConsoleLocalization.Error_FuncPrefix;
        public static string NotExistColorSpecifier => ConsoleLocalization.Error_NotExistColorSpecifier;
        public static string ContainsNonNumericCharacters => ConsoleLocalization.Error_ContainsNonNumericCharacters;
        public static string InvalidSpecification => ConsoleLocalization.Error_InvalidSpecification;
        public static string DoesNotMatchCdflagElements => ConsoleLocalization.Error_DoesNotMatchCdflagElements;
        public static string TooManyCdflagElements => ConsoleLocalization.Error_TooManyCdflagElements;
        public static string DuplicateVariableDefine => ConsoleLocalization.Error_DuplicateVariableDefine;
        public static string KeywordsCannotBeEmpty => ConsoleLocalization.Error_KeywordsCannotBeEmpty;
        public static string InvalidProhibitedVar => ConsoleLocalization.Error_InvalidProhibitedVar;
        public static string CanNotSpecifiedByString => ConsoleLocalization.Error_CanNotSpecifiedByString;
        public static string NotDefinedKey => ConsoleLocalization.Error_NotDefinedKey;
        public static string CannotIndexSpecifiedByString => ConsoleLocalization.Error_CannotIndexSpecifiedByString;
        public static string UseCdflagname => ConsoleLocalization.Error_UseCdflagname;
        public static string NotExistKey => ConsoleLocalization.Error_NotExistKey;
        public static string UsedAtForPrivVar => ConsoleLocalization.Error_UsedAtForPrivVar;
        public static string UsedProhibitedVar => ConsoleLocalization.Error_UsedProhibitedVar;

        public static string CannotGetKeyNotExistRunningFunction =>
            ConsoleLocalization.Error_CannotGetKeyNotExistRunningFunction;

        public static string UsedAtForGlobalVar => ConsoleLocalization.Error_UsedAtForGlobalVar;
        public static string InvalidAt => ConsoleLocalization.Error_InvalidAt;
        public static string CallfNonMethodFunc => ConsoleLocalization.Error_CallfNonMethodFunc;
        public static string UsedNonMethodFunc => ConsoleLocalization.Error_UsedNonMethodFunc;
        public static string DeclaringDisable => ConsoleLocalization.Error_DeclaringDisable;
        public static string VarNotDefinedThisFunc => ConsoleLocalization.Error_VarNotDefinedThisFunc;
        public static string IllegalUseReservedWord => ConsoleLocalization.Error_IllegalUseReservedWord;
        public static string UseVarLikeFunc => ConsoleLocalization.Error_UseVarLikeFunc;
        public static string UseFuncLikeVar => ConsoleLocalization.Error_UseFuncLikeVar;
        public static string UnexpectedMacro => ConsoleLocalization.Error_UnexpectedMacro;
        public static string UseInstructionLikeFunc => ConsoleLocalization.Error_UseInstructionLikeFunc;
        public static string UseInstructionLikeVar => ConsoleLocalization.Error_UseInstructionLikeVar;
        public static string CanNotInterpreted => ConsoleLocalization.Error_CanNotInterpreted;
        public static string AbnormalFirstOperand => ConsoleLocalization.Error_AbnormalFirstOperand;
        public static string EmptyBrace => ConsoleLocalization.Error_EmptyBrace;
        public static string EmptyPer => ConsoleLocalization.Error_EmptyPer;
        public static string NotSpecifiedLR => ConsoleLocalization.Error_NotSpecifiedLR;
        public static string OtherThanLR => ConsoleLocalization.Error_OtherThanLR;
        public static string ExtraCharacterLR => ConsoleLocalization.Error_ExtraCharacterLR;
        public static string IsNotNumericBrace => ConsoleLocalization.Error_IsNotNumericBrace;
        public static string IsNotStringPer => ConsoleLocalization.Error_IsNotStringPer;
        public static string OoRForcekanaArg => ConsoleLocalization.Error_OoRForcekanaArg;
        public static string MaxBarNotPositive => ConsoleLocalization.Error_MaxBarNotPositive;
        public static string BarNotPositive => ConsoleLocalization.Error_BarNotPositive;
        public static string TooLongBar => ConsoleLocalization.Error_TooLongBar;
        public static string NotCloseSBrackets => ConsoleLocalization.Error_NotCloseSBrackets;
        public static string NotCloseBrackets => ConsoleLocalization.Error_NotCloseBrackets;
        public static string UnexpectedBrackets => ConsoleLocalization.Error_UnexpectedBrackets;
        public static string UnexpectedSBrackets => ConsoleLocalization.Error_UnexpectedSBrackets;
        public static string CannotOmitFuncArg => ConsoleLocalization.Error_CannotOmitFuncArg;
        public static string NoExpressionAfterEqual => ConsoleLocalization.Error_NoExpressionAfterEqual;
        public static string DoesNotMatchEqual => ConsoleLocalization.Error_DoesNotMatchEqual;
        public static string CanNotInterpretedExpression => ConsoleLocalization.Error_CanNotInterpretedExpression;
        public static string ExpressionResultIsNotNumeric => ConsoleLocalization.Error_ExpressionResultIsNotNumeric;
        public static string EmptyStream => ConsoleLocalization.Error_EmptyStream;
        public static string SBracketsFuncNotImprement => ConsoleLocalization.Error_SBracketsFuncNotImprement;
        public static string ThrowFailed => ConsoleLocalization.Error_ThrowFailed;
        public static string NoOpAfterIs => ConsoleLocalization.Error_NoOpAfterIs;
        public static string NotBinaryOpAfterThis => ConsoleLocalization.Error_NotBinaryOpAfterThis;
        public static string NothingAfterIs => ConsoleLocalization.Error_NothingAfterIs;
        public static string CanNotOmitCaseArg => ConsoleLocalization.Error_CanNotOmitCaseArg;
        public static string NoExpressionAfterTo => ConsoleLocalization.Error_NoExpressionAfterTo;
        public static string DuplicateTo => ConsoleLocalization.Error_DuplicateTo;
        public static string DoesNotMatchTo => ConsoleLocalization.Error_DoesNotMatchTo;
        public static string InvalidTo => ConsoleLocalization.Error_InvalidTo;
        public static string InvalidIs => ConsoleLocalization.Error_InvalidIs;
        public static string UnexpectedOpInVarArg => ConsoleLocalization.Error_UnexpectedOpInVarArg;
        public static string EqualInExpression => ConsoleLocalization.Error_EqualInExpression;
        public static string ComparisonOpContinuous => ConsoleLocalization.Error_ComparisonOpContinuous;
        public static string MissingQuestion => ConsoleLocalization.Error_MissingQuestion;
        public static string NoContainExpressionInBrackets => ConsoleLocalization.Error_NoContainExpressionInBrackets;
        public static string UnexpectedSymbol => ConsoleLocalization.Error_UnexpectedSymbol;
        public static string TernaryBinaryError => ConsoleLocalization.Error_TernaryBinaryError;
        public static string FailedSolveMacro => ConsoleLocalization.Error_FailedSolveMacro;
        public static string UnrecognizedSyntax => ConsoleLocalization.Error_UnrecognizedSyntax;
        public static string MultipleUnaryOp => ConsoleLocalization.Error_MultipleUnaryOp;
        public static string DuplicateIncrementDecrement => ConsoleLocalization.Error_DuplicateIncrementDecrement;
        public static string InsufficientExpression => ConsoleLocalization.Error_InsufficientExpression;
        public static string EmptyFramelist => ConsoleLocalization.Error_EmptyFramelist;
        public static string OoRLasframe => ConsoleLocalization.Error_OoRLasframe;
        public static string SpriteTimeOut => ConsoleLocalization.Error_SpriteTimeOut;
        public static string IncrementNonVar => ConsoleLocalization.Error_IncrementNonVar;
        public static string IncrementConst => ConsoleLocalization.Error_IncrementConst;
        public static string NumericType => ConsoleLocalization.Error_NumericType;
        public static string StringType => ConsoleLocalization.Error_StringType;
        public static string UnknownType => ConsoleLocalization.Error_UnknownType;
        public static string RefType => ConsoleLocalization.Error_RefType;
        public static string MultidimType => ConsoleLocalization.Error_MultidimType;
        public static string CharaType => ConsoleLocalization.Error_CharaType;
        public static string CanNotAppliedUnaryOp => ConsoleLocalization.Error_CanNotAppliedUnaryOp;
        public static string CanNotAppliedBinaryOp => ConsoleLocalization.Error_CanNotAppliedBinaryOp;
        public static string InvalidTernaryOp => ConsoleLocalization.Error_InvalidTernaryOp;
        public static string MultiplyNegativeToStr => ConsoleLocalization.Error_MultiplyNegativeToStr;
        public static string Multiply10kToStr => ConsoleLocalization.Error_Multiply10kToStr;
        public static string DivideByZero => ConsoleLocalization.Error_DivideByZero;
        public static string XmlGetError => ConsoleLocalization.Error_XmlGetError;
        public static string XmlGetPathError => ConsoleLocalization.Error_XmlGetPathError;
        public static string FirstArg => ConsoleLocalization.Error_FirstArg;
        public static string NotVarFunc => ConsoleLocalization.Error_NotVarFunc;
        public static string IsCharaVarFunc => ConsoleLocalization.Error_IsCharaVarFunc;
        public static string Not1DFuncArg => ConsoleLocalization.Error_Not1DFuncArg;
        public static string NotDimVarFunc => ConsoleLocalization.Error_NotDimVarFunc;
        public static string AbnormalArray => ConsoleLocalization.Error_AbnormalArray;
        public static string SetStrToInt => ConsoleLocalization.Error_SetStrToInt;
        public static string SetIntToStr => ConsoleLocalization.Error_SetIntToStr;
        public static string InvalidRegexArg => ConsoleLocalization.Error_InvalidRegexArg;
        public static string XmlParseError => ConsoleLocalization.Error_XmlParseError;
        public static string XmlXPathParseError => ConsoleLocalization.Error_XmlXPathParseError;

        public static string ReturnTypeDifferentOrNotImpelemnt =>
            ConsoleLocalization.Error_ReturnTypeDifferentOrNotImpelemnt;

        public static string NotImplement => ConsoleLocalization.Error_NotImplement;
        public static string EmptyRefFunc => ConsoleLocalization.Error_EmptyRefFunc;
        public static string RefFuncHasNotArg => ConsoleLocalization.Error_RefFuncHasNotArg;
        public static string AbnormalData => ConsoleLocalization.Error_AbnormalData;
        public static string OoRSortKey => ConsoleLocalization.Error_OoRSortKey;
        public static string AbnormalVarDeclaration => ConsoleLocalization.Error_AbnormalVarDeclaration;
        public static string AssignToConst => ConsoleLocalization.Error_AssignToConst;
        public static string OoRCharaVar => ConsoleLocalization.Error_OoRCharaVar;
        public static string OoRArrayShift => ConsoleLocalization.Error_OoRArrayShift;
        public static string OoRArrayRemove => ConsoleLocalization.Error_OoRArrayRemove;
        public static string OoRArraySort => ConsoleLocalization.Error_OoRArraySort;
        public static string OoRCharaNum => ConsoleLocalization.Error_OoRCharaNum;
        public static string AddedUndefinedChara => ConsoleLocalization.Error_AddedUndefinedChara;
        public static string OoRDelChara => ConsoleLocalization.Error_OoRDelChara;
        public static string DuplicateDelChara => ConsoleLocalization.Error_DuplicateDelChara;
        public static string NotExistFromCopyChara => ConsoleLocalization.Error_NotExistFromCopyChara;
        public static string NotExistToCopyChara => ConsoleLocalization.Error_NotExistToCopyChara;
        public static string OoRSwapChara => ConsoleLocalization.Error_OoRSwapChara;
        public static string RefUndefinedChara => ConsoleLocalization.Error_RefUndefinedChara;
        public static string OoRCstr => ConsoleLocalization.Error_OoRCstr;
        public static string RefDoesNotExistData => ConsoleLocalization.Error_RefDoesNotExistData;
        public static string RefOoR => ConsoleLocalization.Error_RefOoR;
        public static string FailedCreateDataFolder => ConsoleLocalization.Error_FailedCreateDataFolder;
        public static string NothingFileName => ConsoleLocalization.Error_NothingFileName;
        public static string InvalidFileName => ConsoleLocalization.Error_InvalidFileName;
        public static string DifferentGame => ConsoleLocalization.Error_DifferentGame;
        public static string DifferentVersion => ConsoleLocalization.Error_DifferentVersion;
        public static string CorruptedSaveData => ConsoleLocalization.Error_CorruptedSaveData;
        public static string LoadError => ConsoleLocalization.Error_LoadError;
        public static string ErrorSavingGlobalData => ConsoleLocalization.Error_ErrorSavingGlobalData;
        public static string NotExistPath => ConsoleLocalization.Error_NotExistPath;
        public static string DelReadOnlyFile => ConsoleLocalization.Error_DelReadOnlyFile;
        public static string TooMany2DCharaVarArg => ConsoleLocalization.Error_TooMany2DCharaVarArg;
        public static string TooMany1DCharaVarArg => ConsoleLocalization.Error_TooMany1DCharaVarArg;
        public static string TooManyCharaVarArg => ConsoleLocalization.Error_TooManyCharaVarArg;
        public static string CanNotOmit1DCharaVarArg1 => ConsoleLocalization.Error_CanNotOmit1DCharaVarArg1;
        public static string CanNotOmit1DCharaVarArg2 => ConsoleLocalization.Error_CanNotOmit1DCharaVarArg2;
        public static string CanNotOmitCharaVarArg1 => ConsoleLocalization.Error_CanNotOmitCharaVarArg1;
        public static string CanNotOmitCharaVarArg2 => ConsoleLocalization.Error_CanNotOmitCharaVarArg2;
        public static string CanNotOmit3DVarArg => ConsoleLocalization.Error_CanNotOmit3DVarArg;
        public static string CanNotOmit2DVarArg => ConsoleLocalization.Error_CanNotOmit2DVarArg;
        public static string TooMany2DVarArg => ConsoleLocalization.Error_TooMany2DVarArg;
        public static string TooMany1DVarArg => ConsoleLocalization.Error_TooMany1DVarArg;
        public static string OmittedRandArg => ConsoleLocalization.Error_OmittedRandArg;
        public static string RandArgIsZero => ConsoleLocalization.Error_RandArgIsZero;
        public static string ZeroDVarHasArg => ConsoleLocalization.Error_ZeroDVarHasArg;
        public static string KeywordCanNotEmpty => ConsoleLocalization.Error_KeywordCanNotEmpty;
        public static string AssignToVarOoR => ConsoleLocalization.Error_AssignToVarOoR;
        public static string MissingVarArg => ConsoleLocalization.Error_MissingVarArg;
        public static string CallStrAsInt => ConsoleLocalization.Error_CallStrAsInt;
        public static string CallIntAsStr => ConsoleLocalization.Error_CallIntAsStr;
        public static string CallNDStrAsInt => ConsoleLocalization.Error_CallNDStrAsInt;
        public static string CallNDIntAsStr => ConsoleLocalization.Error_CallNDIntAsStr;
        public static string GetSize0DVar => ConsoleLocalization.Error_GetSize0DVar;
        public static string CallCharaVarAsVar => ConsoleLocalization.Error_CallCharaVarAsVar;
        public static string CallVarAsCharaVar => ConsoleLocalization.Error_CallVarAsCharaVar;
        public static string GetSize0DCharaVar => ConsoleLocalization.Error_GetSize0DCharaVar;
        public static string GetSizeCharaVarWithoutDim => ConsoleLocalization.Error_GetSizeCharaVarWithoutDim;
        public static string GetSizeCharaVarNonExistDim => ConsoleLocalization.Error_GetSizeCharaVarNonExistDim;
        public static string OoRCharaVarArg => ConsoleLocalization.Error_OoRCharaVarArg;
        public static string OoRInstructionArg => ConsoleLocalization.Error_OoRInstructionArg;
        public static string GetSizeDimError => ConsoleLocalization.Error_GetSizeDimError;
        public static string GetSizeNonExistDim => ConsoleLocalization.Error_GetSizeNonExistDim;
        public static string OoRVarArg => ConsoleLocalization.Error_OoRVarArg;
        public static string EmptyRefVar => ConsoleLocalization.Error_EmptyRefVar;
        public static string CanNotOmitRefToVar => ConsoleLocalization.Error_CanNotOmitRefToVar;
        public static string CanNotRefPseudoVar => ConsoleLocalization.Error_CanNotRefPseudoVar;
        public static string CanNotRefConstVar => ConsoleLocalization.Error_CanNotRefConstVar;
        public static string CanNotGlobalRefLocalVar => ConsoleLocalization.Error_CanNotGlobalRefLocalVar;
        public static string CanNotRefCharaVar => ConsoleLocalization.Error_CanNotRefCharaVar;
        public static string CanNotRefDifferentType => ConsoleLocalization.Error_CanNotRefDifferentType;
        public static string CanNotRefDifferentDim => ConsoleLocalization.Error_CanNotRefDifferentDim;
        public static string AssignToPseudoVar => ConsoleLocalization.Error_AssignToPseudoVar;
        public static string GetSizePseudoVar => ConsoleLocalization.Error_GetSizePseudoVar;
        public static string GetDimPseudoVar => ConsoleLocalization.Error_GetDimPseudoVar;
        public static string RandArgIsNegative => ConsoleLocalization.Error_RandArgIsNegative;
        public static string SpriteNameAlreadyUsed => ConsoleLocalization.Error_SpriteNameAlreadyUsed;
        public static string NotDeclaredAnimationSpriteSize => ConsoleLocalization.Error_NotDeclaredAnimationSpriteSize;
        public static string InvalidAnimationSpriteSize => ConsoleLocalization.Error_InvalidAnimationSpriteSize;
        public static string MissingSecondArgumentExtension => ConsoleLocalization.Error_MissingSecondArgumentExtension;
        public static string NotExistImageFile => ConsoleLocalization.Error_NotExistImageFile;
        public static string FailedLoadFile => ConsoleLocalization.Error_FailedLoadFile;
        public static string TooLargeImageFile => ConsoleLocalization.Error_TooLargeImageFile;
        public static string FailedCreateResource => ConsoleLocalization.Error_FailedCreateResource;
        public static string SpriteCreateFromFailedResource => ConsoleLocalization.Error_SpriteCreateFromFailedResource;
        public static string SpriteSizeIsNegatibe => ConsoleLocalization.Error_SpriteSizeIsNegatibe;
        public static string OoRParentImage => ConsoleLocalization.Error_OoRParentImage;
        public static string FrameTimeIsNegative => ConsoleLocalization.Error_FrameTimeIsNegative;
        public static string FailedAddSpriteFrame => ConsoleLocalization.Error_FailedAddSpriteFrame;
        public static string InvalidConfigName => ConsoleLocalization.Error_InvalidConfigName;
        public static string NotAllowGetConfigValue => ConsoleLocalization.Error_NotAllowGetConfigValue;
        public static string UseArgVarInHasNotArgFunc => ConsoleLocalization.Error_UseArgVarInHasNotArgFunc;
        public static string UseArgVarInSystemFunc => ConsoleLocalization.Error_UseArgVarInSystemFunc;
        public static string FailedOpenFile => ConsoleLocalization.Error_FailedOpenFile;
        public static string UnexpectedError => ConsoleLocalization.Error_UnexpectedError;
        public static string MissingComma => ConsoleLocalization.Error_MissingComma;
        public static string CanNotInterpretVarName => ConsoleLocalization.Error_CanNotInterpretVarName;
        public static string CanNotChange0DVarSize => ConsoleLocalization.Error_CanNotChange0DVarSize;
        public static string CanNotChangeVarSize => ConsoleLocalization.Error_CanNotChangeVarSize;
        public static string ArrayLengthIs0 => ConsoleLocalization.Error_ArrayLengthIs0;

        public static string CanNotDisableVarArrayLengthIsNegative =>
            ConsoleLocalization.Error_CanNotDisableVarArrayLengthIsNegative;

        public static string IgnoreNDData => ConsoleLocalization.Error_IgnoreNDData;
        public static string LocalVarSizeCanNotLessThan1 => ConsoleLocalization.Error_LocalVarSizeCanNotLessThan1;

        public static string InternalVarSizeCanNotLessThan100 =>
            ConsoleLocalization.Error_InternalVarSizeCanNotLessThan100;

        public static string OneDVarSizeCanNotGreaterThan1M => ConsoleLocalization.Error_OneDVarSizeCanNotGreaterThan1M;
        public static string VarSizeCanNotLessThan1 => ConsoleLocalization.Error_VarSizeCanNotLessThan1;
        public static string VarSizeCanNotGreaterThan1M => ConsoleLocalization.Error_VarSizeCanNotGreaterThan1M;
        public static string MissingVarSizeArg => ConsoleLocalization.Error_MissingVarSizeArg;
        public static string VarSizeLimitIs1M => ConsoleLocalization.Error_VarSizeLimitIs1M;
        public static string VarSizeAlreadyDefined => ConsoleLocalization.Error_VarSizeAlreadyDefined;
        public static string DifferentVarProhibitSetting => ConsoleLocalization.Error_DifferentVarProhibitSetting;
        public static string DifferentVarSize => ConsoleLocalization.Error_DifferentVarSize;

        public static string InappropriatePalamJuelPalamname =>
            ConsoleLocalization.Error_InappropriatePalamJuelPalamname;

        public static string PalamnameSizeLessThanJuelSize => ConsoleLocalization.Error_PalamnameSizeLessThanJuelSize;
        public static string DuplicateCharaDefine1 => ConsoleLocalization.Error_DuplicateCharaDefine1;
        public static string DuplicateCharaDefine2 => ConsoleLocalization.Error_DuplicateCharaDefine2;
        public static string StartedComma => ConsoleLocalization.Error_StartedComma;
        public static string CharaNoDefinedTwice => ConsoleLocalization.Error_CharaNoDefinedTwice;
        public static string CanNotConvertToInt => ConsoleLocalization.Error_CanNotConvertToInt;
        public static string StartedDataBeforeCharaNo => ConsoleLocalization.Error_StartedDataBeforeCharaNo;
        public static string ProgramError => ConsoleLocalization.Error_ProgramError;
        public static string IsProhibitedVar => ConsoleLocalization.Error_IsProhibitedVar;
        public static string OoRArray => ConsoleLocalization.Error_OoRArray;
        public static string MissingSecondIdentifier => ConsoleLocalization.Error_MissingSecondIdentifier;
        public static string MissingThirdIdentifier => ConsoleLocalization.Error_MissingThirdIdentifier;
        public static string VarKeyAreadyDefined => ConsoleLocalization.Error_VarKeyAreadyDefined;
        public static string FirstValueCanNotConvertToInt => ConsoleLocalization.Error_FirstValueCanNotConvertToInt;
        public static string ProhibitedArrayName => ConsoleLocalization.Error_ProhibitedArrayName;
        public static string CanNotReadAmountOfMoney => ConsoleLocalization.Error_CanNotReadAmountOfMoney;
        public static string SaveCodeIs0 => ConsoleLocalization.Error_SaveCodeIs0;
        public static string CanNotReadVersion => ConsoleLocalization.Error_CanNotReadVersion;
        public static string RequireLaterEmuera => ConsoleLocalization.Error_RequireLaterEmuera;
        public static string SomethingErrorInGamebase => ConsoleLocalization.Error_SomethingErrorInGamebase;
        public static string Instruction => ConsoleLocalization.Error_Instruction;
        public static string MissingArg => ConsoleLocalization.Error_MissingArg;
        public static string NotEnoughArguments => ConsoleLocalization.Error_NotEnoughArguments;
        public static string TooManyArg => ConsoleLocalization.Error_TooManyArg;
        public static string CanNotRecognizeArg => ConsoleLocalization.Error_CanNotRecognizeArg;
        public static string IncorrectArg => ConsoleLocalization.Error_IncorrectArg;
        public static string ArgIsNotVariable => ConsoleLocalization.Error_ArgIsNotVariable;
        public static string ArgIsConst => ConsoleLocalization.Error_ArgIsConst;
        public static string AbnormalSpecification => ConsoleLocalization.Error_AbnormalSpecification;
        public static string CanNotOmitArg => ConsoleLocalization.Error_CanNotOmitArg;
        public static string DifferentArgsCount => ConsoleLocalization.Error_DifferentArgsCount;
        public static string ArgIsNotRealNumber => ConsoleLocalization.Error_ArgIsNotRealNumber;
        public static string WrongFormat => ConsoleLocalization.Error_WrongFormat;
        public static string ArgIsStrVar => ConsoleLocalization.Error_ArgIsStrVar;
        public static string MissingArgAfterComma => ConsoleLocalization.Error_MissingArgAfterComma;
        public static string ArgIsNotRequired => ConsoleLocalization.Error_ArgIsNotRequired;
        public static string TransparentUnsupported => ConsoleLocalization.Error_TransparentUnsupported;
        public static string InvalidColorName => ConsoleLocalization.Error_InvalidColorName;
        public static string ArgIsNotArrayVar => ConsoleLocalization.Error_ArgIsNotArrayVar;
        public static string ExtraCharacterAfterArg => ConsoleLocalization.Error_ExtraCharacterAfterArg;
        public static string ArgIsNotCharaVar => ConsoleLocalization.Error_ArgIsNotCharaVar;
        public static string ArgIsNot1DVar => ConsoleLocalization.Error_ArgIsNot1DVar;
        public static string IsNotForwardBack => ConsoleLocalization.Error_IsNotForwardBack;
        public static string ArgIsNotNumber => ConsoleLocalization.Error_ArgIsNotNumber;
        public static string NotSpecifiedFuncName => ConsoleLocalization.Error_NotSpecifiedFuncName;
        public static string CanNotReadLeft => ConsoleLocalization.Error_CanNotReadLeft;
        public static string LeftHasExtraComma => ConsoleLocalization.Error_LeftHasExtraComma;
        public static string LeftIsNotVar => ConsoleLocalization.Error_LeftIsNotVar;
        public static string LeftIsConst => ConsoleLocalization.Error_LeftIsConst;
        public static string InvalidOpWithInt => ConsoleLocalization.Error_InvalidOpWithInt;
        public static string InvalidOpWithIncrement => ConsoleLocalization.Error_InvalidOpWithIncrement;
        public static string InvalidOpWithDecrement => ConsoleLocalization.Error_InvalidOpWithDecrement;
        public static string CanNotReadRight => ConsoleLocalization.Error_CanNotReadRight;
        public static string CanNotContainMultipleValue => ConsoleLocalization.Error_CanNotContainMultipleValue;
        public static string CanNotOmitRight => ConsoleLocalization.Error_CanNotOmitRight;
        public static string CanNotAssignStrToInt => ConsoleLocalization.Error_CanNotAssignStrToInt;
        public static string StrAssignIsPrihibited => ConsoleLocalization.Error_StrAssignIsPrihibited;
        public static string CanNotAssignIntToStr => ConsoleLocalization.Error_CanNotAssignIntToStr;
        public static string RightHasExtraComma => ConsoleLocalization.Error_RightHasExtraComma;
        public static string InvalidAssignmentOp => ConsoleLocalization.Error_InvalidAssignmentOp;
        public static string IgnoreArgBecauseNotInt => ConsoleLocalization.Error_IgnoreArgBecauseNotInt;
        public static string OmittedArg1 => ConsoleLocalization.Error_OmittedArg1;
        public static string OmittedArg2 => ConsoleLocalization.Error_OmittedArg2;
        public static string CanNotUseRepeat => ConsoleLocalization.Error_CanNotUseRepeat;
        public static string RepeatCountLessthan0 => ConsoleLocalization.Error_RepeatCountLessthan0;
        public static string ArgLessThan0 => ConsoleLocalization.Error_ArgLessThan0;
        public static string ArgIsNegativeValue => ConsoleLocalization.Error_ArgIsNegativeValue;
        public static string ReturnArgIsVar => ConsoleLocalization.Error_ReturnArgIsVar;
        public static string ReturnArgIsFormula => ConsoleLocalization.Error_ReturnArgIsFormula;
        public static string ArgIsFormula => ConsoleLocalization.Error_ArgIsFormula;
        public static string CharaVarCanNotSpecifiedArg => ConsoleLocalization.Error_CharaVarCanNotSpecifiedArg;
        public static string DifferentArgType => ConsoleLocalization.Error_DifferentArgType;
        public static string ArgIsOoRBit => ConsoleLocalization.Error_ArgIsOoRBit;
        public static string SpecifiedConst => ConsoleLocalization.Error_SpecifiedConst;
        public static string CanNotSetthirdLaterArg => ConsoleLocalization.Error_CanNotSetthirdLaterArg;
        public static string IgnoreThirdLaterArg => ConsoleLocalization.Error_IgnoreThirdLaterArg;
        public static string NotMatchTwoArg => ConsoleLocalization.Error_NotMatchTwoArg;
        public static string ArgIs2DVar => ConsoleLocalization.Error_ArgIs2DVar;
        public static string IgnoreFourthLaterArg => ConsoleLocalization.Error_IgnoreFourthLaterArg;
        public static string InvalidSetcolorArgCount => ConsoleLocalization.Error_InvalidSetcolorArgCount;
        public static string ArgIsRequiredNonCharaArrayVar => ConsoleLocalization.Error_ArgIsRequiredNonCharaArrayVar;
        public static string NotMatchFirstAndThirdVar => ConsoleLocalization.Error_NotMatchFirstAndThirdVar;
        public static string CanNotSaveCharaVar => ConsoleLocalization.Error_CanNotSaveCharaVar;
        public static string CanNotSavePrivVar => ConsoleLocalization.Error_CanNotSavePrivVar;
        public static string CanNotSaveLocalVar => ConsoleLocalization.Error_CanNotSaveLocalVar;
        public static string CanNotSaveConstVar => ConsoleLocalization.Error_CanNotSaveConstVar;
        public static string CanNotSavePseudoVar => ConsoleLocalization.Error_CanNotSavePseudoVar;
        public static string CanNotSaveRefVar => ConsoleLocalization.Error_CanNotSaveRefVar;
        public static string DuplicateVarSave => ConsoleLocalization.Error_DuplicateVarSave;
        public static string NotPositiveCharaNo => ConsoleLocalization.Error_NotPositiveCharaNo;
        public static string CharaNoOverInt32 => ConsoleLocalization.Error_CharaNoOverInt32;
        public static string DuplicateCharaSave => ConsoleLocalization.Error_DuplicateCharaSave;
        public static string ArgIsNotRef => ConsoleLocalization.Error_ArgIsNotRef;
        public static string NotDefinedUserFunc => ConsoleLocalization.Error_NotDefinedUserFunc;
        public static string CanNotRefFunc => ConsoleLocalization.Error_CanNotRefFunc;
        public static string NotDefinedVar => ConsoleLocalization.Error_NotDefinedVar;
        public static string ArraycopyArgIsNotDefined => ConsoleLocalization.Error_ArraycopyArgIsNotDefined;
        public static string ArraycopyArgIsNotArray => ConsoleLocalization.Error_ArraycopyArgIsNotArray;
        public static string ArraycopyArgIsCharaVar => ConsoleLocalization.Error_ArraycopyArgIsCharaVar;
        public static string ArraycopyArgIsConst => ConsoleLocalization.Error_ArraycopyArgIsConst;
        public static string DifferentArraycopyArgsDim => ConsoleLocalization.Error_DifferentArraycopyArgsDim;
        public static string DifferentArraycopyArgsType => ConsoleLocalization.Error_DifferentArraycopyArgsType;
        public static string MissingArgAfterColon => ConsoleLocalization.Error_MissingArgAfterColon;
        public static string AbnormalPrint => ConsoleLocalization.Error_AbnormalPrint;
        public static string AbnormalPrintdata => ConsoleLocalization.Error_AbnormalPrintdata;
        public static string InvalidArg => ConsoleLocalization.Error_InvalidArg;
        public static string NotDefinedFunc => ConsoleLocalization.Error_NotDefinedFunc;
        public static string SPCharaConfigIsOff => ConsoleLocalization.Error_SPCharaConfigIsOff;
        public static string OoRCvarsetArg => ConsoleLocalization.Error_OoRCvarsetArg;
        public static string CvarsetArgIsNotCharaVar => ConsoleLocalization.Error_CvarsetArgIsNotCharaVar;
        public static string OoRSavecharaArg => ConsoleLocalization.Error_OoRSavecharaArg;
        public static string DuplicateCharaNo => ConsoleLocalization.Error_DuplicateCharaNo;
        public static string ArgIsOoRColorCode => ConsoleLocalization.Error_ArgIsOoRColorCode;
        public static string ArgIsOoR => ConsoleLocalization.Error_ArgIsOoR;
        public static string AwaitArgIsNegative => ConsoleLocalization.Error_AwaitArgIsNegative;
        public static string AwaitArgIsOver10Seconds => ConsoleLocalization.Error_AwaitArgIsOver10Seconds;
        public static string CanNotUseInstructionInfunc => ConsoleLocalization.Error_CanNotUseInstructionInfunc;
        public static string NothingAfterSif => ConsoleLocalization.Error_NothingAfterSif;
        public static string FuncCanNotAfterSif => ConsoleLocalization.Error_FuncCanNotAfterSif;
        public static string LabelCanNotAfterSif => ConsoleLocalization.Error_LabelCanNotAfterSif;
        public static string EmptyAfterSif => ConsoleLocalization.Error_EmptyAfterSif;
        public static string AbnormalContinue => ConsoleLocalization.Error_AbnormalContinue;
        public static string CanNotUseReturnf => ConsoleLocalization.Error_CanNotUseReturnf;
        public static string ReturnfStrInIntFunc => ConsoleLocalization.Error_ReturnfStrInIntFunc;
        public static string ReturnfIntInStrFunc => ConsoleLocalization.Error_ReturnfIntInStrFunc;
        public static string CanNotUseCallevent => ConsoleLocalization.Error_CanNotUseCallevent;
        public static string NotDefinedLabelName => ConsoleLocalization.Error_NotDefinedLabelName;
        public static string InvalidLabelName => ConsoleLocalization.Error_InvalidLabelName;
        public static string NotStartedSharpLineInHeader => ConsoleLocalization.Error_NotStartedSharpLineInHeader;
        public static string CanNotInterpretSharpLine => ConsoleLocalization.Error_CanNotInterpretSharpLine;
        public static string UnknownPreprocessorInSharpLine => ConsoleLocalization.Error_UnknownPreprocessorInSharpLine;
        public static string MissingReplacementSource => ConsoleLocalization.Error_MissingReplacementSource;
        public static string FuncMacroArgIs0 => ConsoleLocalization.Error_FuncMacroArgIs0;
        public static string WrongFormatReplacementSource => ConsoleLocalization.Error_WrongFormatReplacementSource;

        public static string DuplicateCharacterReplcaementSource =>
            ConsoleLocalization.Error_DuplicateCharacterReplcaementSource;

        public static string MissingSubstitution => ConsoleLocalization.Error_MissingSubstitution;
        public static string CanNotDeclaredFuncMacro => ConsoleLocalization.Error_CanNotDeclaredFuncMacro;
        public static string UnexpectedErrorFrom => ConsoleLocalization.Error_UnexpectedErrorFrom;
        public static string HasTooManyArg => ConsoleLocalization.Error_HasTooManyArg;
        public static string DuplicateSkipstart => ConsoleLocalization.Error_DuplicateSkipstart;
        public static string MissingArguments => ConsoleLocalization.Error_MissingArguments;
        public static string IsInvalid => ConsoleLocalization.Error_IsInvalid;
        public static string UnexpectedSkipend => ConsoleLocalization.Error_UnexpectedSkipend;
        public static string UnexpectedMacroEndif => ConsoleLocalization.Error_UnexpectedMacroEndif;
        public static string UnrecognizedPreprosessor => ConsoleLocalization.Error_UnrecognizedPreprosessor;
        public static string TheresNo => ConsoleLocalization.Error_TheresNo;
        public static string InvalidSBrackets => ConsoleLocalization.Error_InvalidSBrackets;
        public static string IgnoreAfterPreprosessor => ConsoleLocalization.Error_IgnoreAfterPreprosessor;
        public static string InvalidSharp => ConsoleLocalization.Error_InvalidSharp;
        public static string FuncIsAlreadyDefined => ConsoleLocalization.Error_FuncIsAlreadyDefined;
        public static string LabelIsAlreadyDefined => ConsoleLocalization.Error_LabelIsAlreadyDefined;
        public static string LineBeforeFunc => ConsoleLocalization.Error_LineBeforeFunc;
        public static string FuncArgError => ConsoleLocalization.Error_FuncArgError;
        public static string CalledFailedFunc => ConsoleLocalization.Error_CalledFailedFunc;
        public static string EventFuncHasArg => ConsoleLocalization.Error_EventFuncHasArg;
        public static string SystemFuncHasArg => ConsoleLocalization.Error_SystemFuncHasArg;
        public static string WrongArgFormat => ConsoleLocalization.Error_WrongArgFormat;
        public static string CanNotEmptyFuncSBrackets => ConsoleLocalization.Error_CanNotEmptyFuncSBrackets;
        public static string CanNotOmitFuncDefineArg => ConsoleLocalization.Error_CanNotOmitFuncDefineArg;
        public static string FuncDefineArgOnlyConst => ConsoleLocalization.Error_FuncDefineArgOnlyConst;
        public static string ArgCanOnlyAssignableVar => ConsoleLocalization.Error_ArgCanOnlyAssignableVar;
        public static string ArgHasNotSubscript => ConsoleLocalization.Error_ArgHasNotSubscript;
        public static string ArgSubscriptOnlyConst => ConsoleLocalization.Error_ArgSubscriptOnlyConst;
        public static string DuplicateArg => ConsoleLocalization.Error_DuplicateArg;
        public static string ArgCanOnlyConst => ConsoleLocalization.Error_ArgCanOnlyConst;
        public static string ArgCanOnlyPrivVar => ConsoleLocalization.Error_ArgCanOnlyPrivVar;
        public static string RefArgCanNotInitialize => ConsoleLocalization.Error_RefArgCanNotInitialize;
        public static string NotMatchTypeArgAndInitialValue => ConsoleLocalization.Error_NotMatchTypeArgAndInitialValue;

        public static string BeNotFuncCheckBecauseUseCallform =>
            ConsoleLocalization.Error_BeNotFuncCheckBecauseUseCallform;

        public static string FuncNeverCalled => ConsoleLocalization.Error_FuncNeverCalled;
        public static string UndefinedFunctions => ConsoleLocalization.Error_UndefinedFunctions;
        public static string GeneralFunc => ConsoleLocalization.Error_GeneralFunc;
        public static string Occurrences => ConsoleLocalization.Error_Occurrences;
        public static string SentenceFunc => ConsoleLocalization.Error_SentenceFunc;
        public static string IgnoredFuncNeverCalled => ConsoleLocalization.Error_IgnoredFuncNeverCalled;
        public static string IgnoredUndefinedFuncCall => ConsoleLocalization.Error_IgnoredUndefinedFuncCall;
        public static string TotalFunc => ConsoleLocalization.Error_TotalFunc;
        public static string OverWriteSystemFuncWarn1 => ConsoleLocalization.Error_OverWriteSystemFuncWarn1;
        public static string OverWriteSystemFuncWarn2 => ConsoleLocalization.Error_OverWriteSystemFuncWarn2;
        public static string OverWriteSystemFuncWarn3 => ConsoleLocalization.Error_OverWriteSystemFuncWarn3;
        public static string OverWriteSystemFuncWarn4 => ConsoleLocalization.Error_OverWriteSystemFuncWarn4;
        public static string OverWriteSystemFuncWarn5 => ConsoleLocalization.Error_OverWriteSystemFuncWarn5;
        public static string OverWriteSystemFuncWarn6 => ConsoleLocalization.Error_OverWriteSystemFuncWarn6;
        public static string OverWriteSystemFuncWarn7 => ConsoleLocalization.Error_OverWriteSystemFuncWarn7;
        public static string FuncAnalysisError => ConsoleLocalization.Error_FuncAnalysisError;
        public static string CanNotUseInUserFunc => ConsoleLocalization.Error_CanNotUseInUserFunc;
        public static string CanNotLabelDefineInSyntax => ConsoleLocalization.Error_CanNotLabelDefineInSyntax;
        public static string InvalidInstructionInSyntax => ConsoleLocalization.Error_InvalidInstructionInSyntax;
        public static string OutsideSelectcase => ConsoleLocalization.Error_OutsideSelectcase;
        public static string NestedRepeat => ConsoleLocalization.Error_NestedRepeat;
        public static string RepeatInsideFor => ConsoleLocalization.Error_RepeatInsideFor;
        public static string InvalidLoopInstruction => ConsoleLocalization.Error_InvalidLoopInstruction;
        public static string InvalidElse => ConsoleLocalization.Error_InvalidElse;
        public static string InvalidElseAfterElse => ConsoleLocalization.Error_InvalidElseAfterElse;
        public static string UnexpectedEndif => ConsoleLocalization.Error_UnexpectedEndif;
        public static string InstructionNotClosed => ConsoleLocalization.Error_InstructionNotClosed;
        public static string InvalidCaseAfterCaseelse => ConsoleLocalization.Error_InvalidCaseAfterCaseelse;
        public static string UnexpectedEndselect => ConsoleLocalization.Error_UnexpectedEndselect;

        public static string NotMatchCaseTypeAndSelectcaseType =>
            ConsoleLocalization.Error_NotMatchCaseTypeAndSelectcaseType;

        public static string MissingCorresponding => ConsoleLocalization.Error_MissingCorresponding;
        public static string MissingTryc => ConsoleLocalization.Error_MissingTryc;
        public static string UnexpectedEndcatch => ConsoleLocalization.Error_UnexpectedEndcatch;
        public static string NestedPrintdata => ConsoleLocalization.Error_NestedPrintdata;
        public static string StrdataInsidePrintdata => ConsoleLocalization.Error_StrdataInsidePrintdata;
        public static string NestedStrdata => ConsoleLocalization.Error_NestedStrdata;
        public static string PrintdataInsideStrdata => ConsoleLocalization.Error_PrintdataInsideStrdata;
        public static string UnexpectedDatalist => ConsoleLocalization.Error_UnexpectedDatalist;
        public static string UnexpectedEndlist => ConsoleLocalization.Error_UnexpectedEndlist;
        public static string DatalistDataIsMissing => ConsoleLocalization.Error_DatalistDataIsMissing;
        public static string MissingPrintdata => ConsoleLocalization.Error_MissingPrintdata;
        public static string MissingPrintdataStrdata => ConsoleLocalization.Error_MissingPrintdataStrdata;
        public static string InstructionDataIsMissing => ConsoleLocalization.Error_InstructionDataIsMissing;
        public static string DatalistNotClosed => ConsoleLocalization.Error_DatalistNotClosed;
        public static string NestedTrycalllist => ConsoleLocalization.Error_NestedTrycalllist;
        public static string MissingTrycalllist => ConsoleLocalization.Error_MissingTrycalllist;

        public static string InvalidInstructionInTrycalllist =>
            ConsoleLocalization.Error_InvalidInstructionInTrycalllist;

        public static string TrygotolistToSBrackets => ConsoleLocalization.Error_TrygotolistToSBrackets;
        public static string TrygotolistTargetHasArg => ConsoleLocalization.Error_TrygotolistTargetHasArg;
        public static string NestedNoskip => ConsoleLocalization.Error_NestedNoskip;
        public static string MissingNoskip => ConsoleLocalization.Error_MissingNoskip;
        public static string DefaultError => ConsoleLocalization.Error_DefaultError;
        public static string UseSingleUserFunc => ConsoleLocalization.Error_UseSingleUserFunc;
        public static string UsableSingleEventFunc => ConsoleLocalization.Error_UsableSingleEventFunc;
        public static string DuplicateSingle => ConsoleLocalization.Error_DuplicateSingle;
        public static string OnlyWithSingle => ConsoleLocalization.Error_OnlyWithSingle;
        public static string UseLaterUserFunc => ConsoleLocalization.Error_UseLaterUserFunc;
        public static string UsableLaterEventFunc => ConsoleLocalization.Error_UsableLaterEventFunc;
        public static string DuplicateLater => ConsoleLocalization.Error_DuplicateLater;
        public static string OnlyWithLater => ConsoleLocalization.Error_OnlyWithLater;
        public static string PriWithLater => ConsoleLocalization.Error_PriWithLater;
        public static string UsePriUserFunc => ConsoleLocalization.Error_UsePriUserFunc;
        public static string UsablePriEventFunc => ConsoleLocalization.Error_UsablePriEventFunc;
        public static string DuplicatePri => ConsoleLocalization.Error_DuplicatePri;
        public static string OnlyWithPri => ConsoleLocalization.Error_OnlyWithPri;
        public static string UseOnlyUserFunc => ConsoleLocalization.Error_UseOnlyUserFunc;
        public static string UsableOnlyEventFunc => ConsoleLocalization.Error_UsableOnlyEventFunc;
        public static string DuplicateOnly => ConsoleLocalization.Error_DuplicateOnly;
        public static string AlreadyDeclaredOnly => ConsoleLocalization.Error_AlreadyDeclaredOnly;
        public static string BeIgnorePri => ConsoleLocalization.Error_BeIgnorePri;
        public static string BeIgnoreLater => ConsoleLocalization.Error_BeIgnoreLater;
        public static string BeIgnoreSingle => ConsoleLocalization.Error_BeIgnoreSingle;

        public static string CanNotDeclaredBeginNumberFunction =>
            ConsoleLocalization.Error_CanNotDeclaredBeginNumberFunction;

        public static string FuncNameBeginNumber => ConsoleLocalization.Error_FuncNameBeginNumber;
        public static string AlreadySharpDeclared => ConsoleLocalization.Error_AlreadySharpDeclared;
        public static string AlreadyDeclaredSharpFunction => ConsoleLocalization.Error_AlreadyDeclaredSharpFunction;
        public static string AlreadyDeclaredSharpFunctions => ConsoleLocalization.Error_AlreadyDeclaredSharpFunctions;
        public static string UseSharpInSystemFunc => ConsoleLocalization.Error_UseSharpInSystemFunc;
        public static string SharpHasNotValidValue => ConsoleLocalization.Error_SharpHasNotValidValue;
        public static string EventFuncIgnoreSpecified => ConsoleLocalization.Error_EventFuncIgnoreSpecified;
        public static string LocalsizeLessThan1 => ConsoleLocalization.Error_LocalsizeLessThan1;
        public static string TooManyLocalsize => ConsoleLocalization.Error_TooManyLocalsize;
        public static string LocalIsProhibited => ConsoleLocalization.Error_LocalIsProhibited;
        public static string DuplicateLocalsize => ConsoleLocalization.Error_DuplicateLocalsize;
        public static string DuplicateLocalssize => ConsoleLocalization.Error_DuplicateLocalssize;
        public static string VarNameAlreadyUsed => ConsoleLocalization.Error_VarNameAlreadyUsed;
        public static string ExtraCharacterAfterSharp => ConsoleLocalization.Error_ExtraCharacterAfterSharp;
        public static string InvalidFunc => ConsoleLocalization.Error_InvalidFunc;
        public static string LabelHasArg => ConsoleLocalization.Error_LabelHasArg;
        public static string StartedPlusButNotIncrement => ConsoleLocalization.Error_StartedPlusButNotIncrement;
        public static string StartedMinusButNotDecrement => ConsoleLocalization.Error_StartedMinusButNotDecrement;

        public static string InvalidCharacterAfterInstruction1 =>
            ConsoleLocalization.Error_InvalidCharacterAfterInstruction1;

        public static string InvalidCharacterAfterInstruction2 =>
            ConsoleLocalization.Error_InvalidCharacterAfterInstruction2;

        public static string CanNotInterpretedLine => ConsoleLocalization.Error_CanNotInterpretedLine;
        public static string Use2EqualToAssign => ConsoleLocalization.Error_Use2EqualToAssign;
        public static string CalleventToNonEventFunc => ConsoleLocalization.Error_CalleventToNonEventFunc;
        public static string CallToEventFunc => ConsoleLocalization.Error_CallToEventFunc;
        public static string CallToUserFunc => ConsoleLocalization.Error_CallToUserFunc;
        public static string CanNotOmitRefArg => ConsoleLocalization.Error_CanNotOmitRefArg;
        public static string RequireArrayBecauseRefArg => ConsoleLocalization.Error_RequireArrayBecauseRefArg;
        public static string NumberOfArg => ConsoleLocalization.Error_NumberOfArg;
        public static string CanNotOmitArgWithMessage => ConsoleLocalization.Error_CanNotOmitArgWithMessage;
        public static string CanNotConvertStrToInt => ConsoleLocalization.Error_CanNotConvertStrToInt;
        public static string CanNotConvertIntToStr => ConsoleLocalization.Error_CanNotConvertIntToStr;
        public static string CalltrainArgMoreThanSelectcom => ConsoleLocalization.Error_CalltrainArgMoreThanSelectcom;
        public static string SelectExitInfiniteLoopMB => ConsoleLocalization.Error_SelectExitInfiniteLoopMB;
        public static string OverflowFuncStack => ConsoleLocalization.Error_OverflowFuncStack;
        public static string FuncEndError => ConsoleLocalization.Error_FuncEndError;
        public static string FuncEndEmueraError => ConsoleLocalization.Error_FuncEndEmueraError;
        public static string FuncEndUnexpectedError => ConsoleLocalization.Error_FuncEndUnexpectedError;
        public static string ErrorFileAndLine => ConsoleLocalization.Error_ErrorFileAndLine;
        public static string ErrorFile => ConsoleLocalization.Error_ErrorFile;
        public static string HasThrow => ConsoleLocalization.Error_HasThrow;
        public static string HasError => ConsoleLocalization.Error_HasError;
        public static string ThrowMessage => ConsoleLocalization.Error_ThrowMessage;
        public static string ErrorMessage => ConsoleLocalization.Error_ErrorMessage;
        public static string ErrorInFunc => ConsoleLocalization.Error_ErrorInFunc;
        public static string FuncCallStack => ConsoleLocalization.Error_FuncCallStack;
        public static string ErrorFuncStack => ConsoleLocalization.Error_ErrorFuncStack;
        public static string HasEmueraError => ConsoleLocalization.Error_HasEmueraError;
        public static string HasUnexpectedError => ConsoleLocalization.Error_HasUnexpectedError;
        public static string SkipdispInputError1 => ConsoleLocalization.Error_SkipdispInputError1;
        public static string SkipdispInputError2 => ConsoleLocalization.Error_SkipdispInputError2;
        public static string SkipdispInputError3 => ConsoleLocalization.Error_SkipdispInputError3;
        public static string DoFailedLine => ConsoleLocalization.Error_DoFailedLine;
        public static string OoRPickupcharaArg => ConsoleLocalization.Error_OoRPickupcharaArg;
        public static string CanNotUseOutsideSystemtitle => ConsoleLocalization.Error_CanNotUseOutsideSystemtitle;
        public static string SavedataArgIsNegative => ConsoleLocalization.Error_SavedataArgIsNegative;
        public static string TooLargeSavedataArg => ConsoleLocalization.Error_TooLargeSavedataArg;

        public static string SavetextContainNewLineCharacter =>
            ConsoleLocalization.Error_SavetextContainNewLineCharacter;

        public static string UnexpectedErrorInSavedata => ConsoleLocalization.Error_UnexpectedErrorInSavedata;
        public static string PowerResultNonNumeric => ConsoleLocalization.Error_PowerResultNonNumeric;
        public static string PowerResultInfinite => ConsoleLocalization.Error_PowerResultInfinite;
        public static string PowerResultOverflow => ConsoleLocalization.Error_PowerResultOverflow;
        public static string VarsTypeDifferent => ConsoleLocalization.Error_VarsTypeDifferent;
        public static string UnknownVarType => ConsoleLocalization.Error_UnknownVarType;
        public static string SetcolorArgLessThan0 => ConsoleLocalization.Error_SetcolorArgLessThan0;
        public static string SetcolorArgOver255 => ConsoleLocalization.Error_SetcolorArgOver255;
        public static string InvalidAlignment => ConsoleLocalization.Error_InvalidAlignment;
        public static string MissingEndnoskip => ConsoleLocalization.Error_MissingEndnoskip;
        public static string IsUsableOnly1DVar => ConsoleLocalization.Error_IsUsableOnly1DVar;
        public static string tooLongEncodetouniArg => ConsoleLocalization.Error_tooLongEncodetouniArg;
        public static string AssertArgIs0 => ConsoleLocalization.Error_AssertArgIs0;
        public static string LoaddataArgIsNegative => ConsoleLocalization.Error_LoaddataArgIsNegative;
        public static string TooLargeLoaddataArg => ConsoleLocalization.Error_TooLargeLoaddataArg;
        public static string LoadCorruptedData => ConsoleLocalization.Error_LoadCorruptedData;
        public static string UnexpectedErrorInLoaddata => ConsoleLocalization.Error_UnexpectedErrorInLoaddata;
        public static string CanNotUseDotrainHere => ConsoleLocalization.Error_CanNotUseDotrainHere;
        public static string DotrainArgLessThan0 => ConsoleLocalization.Error_DotrainArgLessThan0;
        public static string DotrainArgOverTrainnameArray => ConsoleLocalization.Error_DotrainArgOverTrainnameArray;
        public static string UndefinedFunc => ConsoleLocalization.Error_UndefinedFunc;
        public static string InvalidBeginArg => ConsoleLocalization.Error_InvalidBeginArg;
        public static string CalleventBeforeFinishEvent => ConsoleLocalization.Error_CalleventBeforeFinishEvent;
        public static string FuncIsNotFound => ConsoleLocalization.Error_FuncIsNotFound;
        public static string InvalidValue => ConsoleLocalization.Error_InvalidValue;
        public static string ExecutedCom => ConsoleLocalization.Error_ExecutedCom;
        public static string CouldNotExecuteCom => ConsoleLocalization.Error_CouldNotExecuteCom;
        public static string AutoSaveError1 => ConsoleLocalization.Error_AutoSaveError1;
        public static string AutoSaveError2 => ConsoleLocalization.Error_AutoSaveError2;
        public static string NotEnoughMoney => ConsoleLocalization.Error_NotEnoughMoney;
        public static string OutOfStock => ConsoleLocalization.Error_OutOfStock;
        public static string UnexpectedSaveError => ConsoleLocalization.Error_UnexpectedSaveError;
        public static string NoData => ConsoleLocalization.Error_NoData;
        public static string UnexpectedScriptEnd => ConsoleLocalization.Error_UnexpectedScriptEnd;
        public static string CanNotSpecifiedKeyword => ConsoleLocalization.Error_CanNotSpecifiedKeyword;
        public static string NotIdentifierAfterKeyword => ConsoleLocalization.Error_NotIdentifierAfterKeyword;
        public static string NotIdentifierArg => ConsoleLocalization.Error_NotIdentifierArg;
        public static string RefArgIsNotArray => ConsoleLocalization.Error_RefArgIsNotArray;
        public static string RefArrayCanNotMoreThan4 => ConsoleLocalization.Error_RefArrayCanNotMoreThan4;
        public static string ExtraCharacterAfterDeclaration => ConsoleLocalization.Error_ExtraCharacterAfterDeclaration;
        public static string UnexpectedToken => ConsoleLocalization.Error_UnexpectedToken;
        public static string ArgParsingError => ConsoleLocalization.Error_ArgParsingError;
        public static string CanNotSpecifiedWith => ConsoleLocalization.Error_CanNotSpecifiedWith;
        public static string DuplicateKeyword => ConsoleLocalization.Error_DuplicateKeyword;
        public static string CanNotUseKeywordGlobalVar => ConsoleLocalization.Error_CanNotUseKeywordGlobalVar;
        public static string CanNotUseKeywordLocalVar => ConsoleLocalization.Error_CanNotUseKeywordLocalVar;
        public static string NotVarAfterKeyword => ConsoleLocalization.Error_NotVarAfterKeyword;
        public static string ConstHasNotInitialValue => ConsoleLocalization.Error_ConstHasNotInitialValue;
        public static string HasNotExpressionAfterComma => ConsoleLocalization.Error_HasNotExpressionAfterComma;
        public static string CanNotSizedRef => ConsoleLocalization.Error_CanNotSizedRef;
        public static string OoRDefinable => ConsoleLocalization.Error_OoRDefinable;
        public static string UnexpectedOp => ConsoleLocalization.Error_UnexpectedOp;
        public static string CanNotSetInitialValue => ConsoleLocalization.Error_CanNotSetInitialValue;
        public static string ArrayVarCanNotOmitInitialValue => ConsoleLocalization.Error_ArrayVarCanNotOmitInitialValue;
        public static string InitialValueMoreThanArraySize => ConsoleLocalization.Error_InitialValueMoreThanArraySize;

        public static string ConstInitialValueDifferentArraySize =>
            ConsoleLocalization.Error_ConstInitialValueDifferentArraySize;

        public static string InitialValueOnlyConst => ConsoleLocalization.Error_InitialValueOnlyConst;
        public static string NotMatchVarTypeAndInitialValue => ConsoleLocalization.Error_NotMatchVarTypeAndInitialValue;
        public static string CanNotDeclareConstArray => ConsoleLocalization.Error_CanNotDeclareConstArray;

        public static string CharaVarCanNotDeclareMoreThan3D =>
            ConsoleLocalization.Error_CharaVarCanNotDeclareMoreThan3D;

        public static string VarCanNotDeclareMoreThan4D => ConsoleLocalization.Error_VarCanNotDeclareMoreThan4D;
        public static string StrVarrRequiredBinaryOption => ConsoleLocalization.Error_StrVarrRequiredBinaryOption;
        public static string CharaStrRequiredBinaryOption => ConsoleLocalization.Error_CharaStrRequiredBinaryOption;
        public static string ForceQuitAndRestartError => ConsoleLocalization.Error_ForceQuitAndRestartError;
        public static string ProgramStatusError => ConsoleLocalization.Error_ProgramStatusError;
        public static string FailedOpenEditor => ConsoleLocalization.Error_FailedOpenEditor;
        public static string CanNotInputTimerWait => ConsoleLocalization.Error_CanNotInputTimerWait;
        public static string CanNotInputScriptRunning => ConsoleLocalization.Error_CanNotInputScriptRunning;
        public static string CanNotUseDebugWindow => ConsoleLocalization.Error_CanNotUseDebugWindow;
        public static string CanNotUseDebugCommand => ConsoleLocalization.Error_CanNotUseDebugCommand;
        public static string InvalidDebugCommand => ConsoleLocalization.Error_InvalidDebugCommand;
        public static string CanNotUseFlowInstruction => ConsoleLocalization.Error_CanNotUseFlowInstruction;
        public static string CanNotUseInstruction => ConsoleLocalization.Error_CanNotUseInstruction;
        public static string CanNotUseWhenError => ConsoleLocalization.Error_CanNotUseWhenError;
        public static string CanNotUseWhenInitialize => ConsoleLocalization.Error_CanNotUseWhenInitialize;
        public static string Warning1 => ConsoleLocalization.Error_Warning1;
        public static string Warning2 => ConsoleLocalization.Error_Warning2;
        public static string Warning3 => ConsoleLocalization.Error_Warning3;
        public static string EmptyDrawline => ConsoleLocalization.Error_EmptyDrawline;
        public static string TextAfterP => ConsoleLocalization.Error_TextAfterP;
        public static string TextAfterNobr => ConsoleLocalization.Error_TextAfterNobr;
        public static string NotFoundCloseTag => ConsoleLocalization.Error_NotFoundCloseTag;
        public static string NotFoundTerminateTag => ConsoleLocalization.Error_NotFoundTerminateTag;
        public static string TagIsNotClosed => ConsoleLocalization.Error_TagIsNotClosed;
        public static string CanNotUsePosWithoutNobr => ConsoleLocalization.Error_CanNotUsePosWithoutNobr;
        public static string CanOnlyUsePosAssignmentLR => ConsoleLocalization.Error_CanOnlyUsePosAssignmentLR;
        public static string MissingSemicolon => ConsoleLocalization.Error_MissingSemicolon;
        public static string ContinuouslyAndSemicolon => ConsoleLocalization.Error_ContinuouslyAndSemicolon;
        public static string InvalidCharacterReference => ConsoleLocalization.Error_InvalidCharacterReference;
        public static string OoRUnicodeHtml => ConsoleLocalization.Error_OoRUnicodeHtml;
        public static string UnexpectedCloseTag => ConsoleLocalization.Error_UnexpectedCloseTag;
        public static string CanNotInterpretCloseTag => ConsoleLocalization.Error_CanNotInterpretCloseTag;
        public static string AttributeSetToTag => ConsoleLocalization.Error_AttributeSetToTag;
        public static string DuplicateTag => ConsoleLocalization.Error_DuplicateTag;
        public static string TagIsNotBegin => ConsoleLocalization.Error_TagIsNotBegin;
        public static string TagHasNotAttribute => ConsoleLocalization.Error_TagHasNotAttribute;
        public static string CanNotInterpretPAttribute => ConsoleLocalization.Error_CanNotInterpretPAttribute;
        public static string CanNotInterpretAttribute => ConsoleLocalization.Error_CanNotInterpretAttribute;
        public static string DuplicateAttribute => ConsoleLocalization.Error_DuplicateAttribute;
        public static string AttributeCanNotInterpretNum => ConsoleLocalization.Error_AttributeCanNotInterpretNum;
        public static string CanNotInterpretAttributeName => ConsoleLocalization.Error_CanNotInterpretAttributeName;
        public static string NotSetAttribute => ConsoleLocalization.Error_NotSetAttribute;
        public static string NestedButtonTag => ConsoleLocalization.Error_NestedButtonTag;
        public static string NestedTag => ConsoleLocalization.Error_NestedTag;

        public static string ClearbuttonAttributeCanNotInterpretNum =>
            ConsoleLocalization.Error_ClearbuttonAttributeCanNotInterpretNum;

        public static string HtmlTagError => ConsoleLocalization.Error_HtmlTagError;
        public static string RequireColorCode => ConsoleLocalization.Error_RequireColorCode;
        public static string OoRColorValue => ConsoleLocalization.Error_OoRColorValue;
        public static string CanNotInterpretNumValue => ConsoleLocalization.Error_CanNotInterpretNumValue;
        public static string InvalidColorName2 => ConsoleLocalization.Error_InvalidColorName2;
        public static string BufferOverFlow => ConsoleLocalization.Error_BufferOverFlow;
        public static string CanNotUseFuncCurrentVer => ConsoleLocalization.Error_CanNotUseFuncCurrentVer;
        public static string AbnormalFileData => ConsoleLocalization.Error_AbnormalFileData;
        public static string AbnormalBinaryData => ConsoleLocalization.Error_AbnormalBinaryData;
        public static string InvalidStream => ConsoleLocalization.Error_InvalidStream;
        public static string NoStrToRead => ConsoleLocalization.Error_NoStrToRead;
        public static string NoNumToRead => ConsoleLocalization.Error_NoNumToRead;
        public static string CanNotInterpretNum => ConsoleLocalization.Error_CanNotInterpretNum;
        public static string InvalidArray => ConsoleLocalization.Error_InvalidArray;
        public static string UnexpectedSaveDataEnd => ConsoleLocalization.Error_UnexpectedSaveDataEnd;
        public static string InvalidSaveDataFormat => ConsoleLocalization.Error_InvalidSaveDataFormat;
        public static string NotSupportStringArray2D => ConsoleLocalization.Error_NotSupportStringArray2D;
        public static string UnexpectedContinuationEnd => ConsoleLocalization.Error_UnexpectedContinuationEnd;
        public static string CharacterAfterContinuation => ConsoleLocalization.Error_CharacterAfterContinuation;
        public static string NotCloseLineContinuation => ConsoleLocalization.Error_NotCloseLineContinuation;
        public static string CharacterAfterContinuationEnd => ConsoleLocalization.Error_CharacterAfterContinuationEnd;
        public static string UnexpectedContinuation => ConsoleLocalization.Error_UnexpectedContinuation;
        public static string OoRInt64 => ConsoleLocalization.Error_OoRInt64;
        public static string CanNotUseBinaryNotate => ConsoleLocalization.Error_CanNotUseBinaryNotate;
        public static string LineBeginsIllegalCharacter => ConsoleLocalization.Error_LineBeginsIllegalCharacter;
        public static string MacroOverLimit => ConsoleLocalization.Error_MacroOverLimit;
        public static string MacroIsNotAvailable => ConsoleLocalization.Error_MacroIsNotAvailable;
        public static string UnexpectedFullWidthSpace => ConsoleLocalization.Error_UnexpectedFullWidthSpace;
        public static string MissingCharacterAfterEscape => ConsoleLocalization.Error_MissingCharacterAfterEscape;
        public static string UnexpectedEqual => ConsoleLocalization.Error_UnexpectedEqual;
        public static string CanNotRecognizedOp => ConsoleLocalization.Error_CanNotRecognizedOp;
        public static string CanNotRecognizedAssignOp => ConsoleLocalization.Error_CanNotRecognizedAssignOp;
        public static string UnexpectedCharacter => ConsoleLocalization.Error_UnexpectedCharacter;
        public static string EmptyTwoSBrackets => ConsoleLocalization.Error_EmptyTwoSBrackets;
        public static string MissingTwoSBrackets => ConsoleLocalization.Error_MissingTwoSBrackets;
        public static string CanNotRenameKey => ConsoleLocalization.Error_CanNotRenameKey;
        public static string NotClosed => ConsoleLocalization.Error_NotClosed;
        public static string MacroHasNotArg => ConsoleLocalization.Error_MacroHasNotArg;
        public static string WrongMacroUsage => ConsoleLocalization.Error_WrongMacroUsage;
        public static string MacroDifferentArgCount => ConsoleLocalization.Error_MacroDifferentArgCount;
        public static string CanNotOmitMacroArg => ConsoleLocalization.Error_CanNotOmitMacroArg;
        public static string NotFoundCorresponding => ConsoleLocalization.Error_NotFoundCorresponding;
        public static string CannotRecommendCallLocalVar => ConsoleLocalization.Error_CannotRecommendCallLocalVar;
        public static string LabelNameMissing => ConsoleLocalization.Error_LabelNameMissing;

        public static string LabelContainsOtherThanUnderline =>
            ConsoleLocalization.Error_LabelContainsOtherThanUnderline;

        public static string LabelStartedHalfDigit => ConsoleLocalization.Error_LabelStartedHalfDigit;
        public static string LabelConflictReservedWord1 => ConsoleLocalization.Error_LabelConflictReservedWord1;
        public static string LabelConflictReservedWord2 => ConsoleLocalization.Error_LabelConflictReservedWord2;

        public static string LabelOverwriteInternalExpression =>
            ConsoleLocalization.Error_LabelOverwriteInternalExpression;

        public static string LabelNameAlreadyUsedInternalExpression =>
            ConsoleLocalization.Error_LabelNameAlreadyUsedInternalExpression;

        public static string LabelNameAlreadyUsedInternalVariable =>
            ConsoleLocalization.Error_LabelNameAlreadyUsedInternalVariable;

        public static string LabelNameAlreadyUsedInternalInstruction =>
            ConsoleLocalization.Error_LabelNameAlreadyUsedInternalInstruction;

        public static string LabelNameAlreadyUsedMacro => ConsoleLocalization.Error_LabelNameAlreadyUsedMacro;

        public static string LabelNameAlreadyUsedRefFunction =>
            ConsoleLocalization.Error_LabelNameAlreadyUsedRefFunction;

        public static string VarContainsOtherThanUnderline => ConsoleLocalization.Error_VarContainsOtherThanUnderline;
        public static string VarConflictReservedWord => ConsoleLocalization.Error_VarConflictReservedWord;

        public static string VarNameAlreadyUsedInternalInstruction =>
            ConsoleLocalization.Error_VarNameAlreadyUsedInternalInstruction;

        public static string VarNameAlreadyUsedInternalVariable =>
            ConsoleLocalization.Error_VarNameAlreadyUsedInternalVariable;

        public static string VarNameAlreadyUsedMacro => ConsoleLocalization.Error_VarNameAlreadyUsedMacro;

        public static string VarNameAlreadyUsedGlobalVariable =>
            ConsoleLocalization.Error_VarNameAlreadyUsedGlobalVariable;

        public static string VarNameAlreadyUsedRefFunction => ConsoleLocalization.Error_VarNameAlreadyUsedRefFunction;
        public static string VarStartedHalfDigit => ConsoleLocalization.Error_VarStartedHalfDigit;

        public static string MacroContainsOtherThanUnderline =>
            ConsoleLocalization.Error_MacroContainsOtherThanUnderline;

        public static string MacroConflictReservedWord => ConsoleLocalization.Error_MacroConflictReservedWord;

        public static string MacroNameAlreadyUsedInternalInstruction =>
            ConsoleLocalization.Error_MacroNameAlreadyUsedInternalInstruction;

        public static string MacroNameAlreadyUsedInternalVariable =>
            ConsoleLocalization.Error_MacroNameAlreadyUsedInternalVariable;

        public static string MacroNameAlreadyUsedMacro => ConsoleLocalization.Error_MacroNameAlreadyUsedMacro;

        public static string MacroNameAlreadyUsedGlobalVariable =>
            ConsoleLocalization.Error_MacroNameAlreadyUsedGlobalVariable;

        public static string MacroNameAlreadyUsedRefFunction =>
            ConsoleLocalization.Error_MacroNameAlreadyUsedRefFunction;

        public static string ArgCanNotBeNull => ConsoleLocalization.Error_ArgCanNotBeNull;
        public static string ArgIsNotCharacterVar => ConsoleLocalization.Error_ArgIsNotCharacterVar;
        public static string InvalidArgType => ConsoleLocalization.Error_InvalidArgType;
        public static string ArgIsNotStr => ConsoleLocalization.Error_ArgIsNotStr;
        public static string ArgIsNotInt => ConsoleLocalization.Error_ArgIsNotInt;
        public static string ArgIsNotVar => ConsoleLocalization.Error_ArgIsNotVar;
        public static string ArgIsNotStrVar => ConsoleLocalization.Error_ArgIsNotStrVar;
        public static string ArgIsNotIntVar => ConsoleLocalization.Error_ArgIsNotIntVar;
        public static string ArgIsNotArray => ConsoleLocalization.Error_ArgIsNotArray;
        public static string ArgIsNotStrArray => ConsoleLocalization.Error_ArgIsNotStrArray;
        public static string ArgIsNotIntArray => ConsoleLocalization.Error_ArgIsNotIntArray;
        public static string ArgIsNotNDArray => ConsoleLocalization.Error_ArgIsNotNDArray;
        public static string ArgIsNotNDStrArray => ConsoleLocalization.Error_ArgIsNotNDStrArray;
        public static string ArgIsNotNDIntArray => ConsoleLocalization.Error_ArgIsNotNDIntArray;
        public static string TooManyFuncArgs => ConsoleLocalization.Error_TooManyFuncArgs;
        public static string NotEnoughArgs => ConsoleLocalization.Error_NotEnoughArgs;
        public static string ArgsCountNotMatches => ConsoleLocalization.Error_ArgsCountNotMatches;
        public static string ArgsNotNeeded => ConsoleLocalization.Error_ArgsNotNeeded;
        public static string NotValidArgs => ConsoleLocalization.Error_NotValidArgs;
        public static string NotValidArgsReason => ConsoleLocalization.Error_NotValidArgsReason;
        public static string IsNotVar => ConsoleLocalization.Error_IsNotVar;
        public static string IsNotInt => ConsoleLocalization.Error_IsNotInt;
        public static string IsNotStr => ConsoleLocalization.Error_IsNotStr;
        public static string SPCharacterFeatureDisabled => ConsoleLocalization.Error_SPCharacterFeatureDisabled;
        public static string CharacterIndexOutOfRange => ConsoleLocalization.Error_CharacterIndexOutOfRange;
        public static string NotVariableName => ConsoleLocalization.Error_NotVariableName;
        public static string ArgIsNegative => ConsoleLocalization.Error_ArgIsNegative;
        public static string ArgIsNotMoreThan0 => ConsoleLocalization.Error_ArgIsNotMoreThan0;
        public static string ArgIsTooLarge => ConsoleLocalization.Error_ArgIsTooLarge;
        public static string FuncDeprecated => ConsoleLocalization.Error_FuncDeprecated;
        public static string ArgIsOutOfRange => ConsoleLocalization.Error_ArgIsOutOfRange;
        public static string ArgIsOutOfRangeExcept => ConsoleLocalization.Error_ArgIsOutOfRangeExcept;
        public static string InvalidFormat => ConsoleLocalization.Error_InvalidFormat;
        public static string NegativeMaximum => ConsoleLocalization.Error_NegativeMaximum;
        public static string MaximumLowerThanMinimum => ConsoleLocalization.Error_MaximumLowerThanMinimum;
        public static string ResultIsNaN => ConsoleLocalization.Error_ResultIsNaN;
        public static string ResultIsInfinity => ConsoleLocalization.Error_ResultIsInfinity;
        public static string ResultIsOutOfTheRangeOfInt64 => ConsoleLocalization.Error_ResultIsOutOfTheRangeOfInt64;
        public static string CharacterRangeInvalid => ConsoleLocalization.Error_CharacterRangeInvalid;
        public static string InvalidUnicode => ConsoleLocalization.Error_InvalidUnicode;
        public static string ArgShouldBeSpecificValue => ConsoleLocalization.Error_ArgShouldBeSpecificValue;
        public static string EncodeToUni2ndArgError => ConsoleLocalization.Error_EncodeToUni2ndArgError;
        public static string ArgIsEmptyString => ConsoleLocalization.Error_ArgIsEmptyString;
        public static string InvalidFormString => ConsoleLocalization.Error_InvalidFormString;
        public static string UnexectedFormStringErr => ConsoleLocalization.Error_UnexectedFormStringErr;
        public static string InvalidType => ConsoleLocalization.Error_InvalidType;
        public static string GIdIsNegative => ConsoleLocalization.Error_GIdIsNegative;
        public static string GIdIsTooLarge => ConsoleLocalization.Error_GIdIsTooLarge;
        public static string InvalidColorARGB => ConsoleLocalization.Error_InvalidColorARGB;
        public static string InvalidColorMatrix => ConsoleLocalization.Error_InvalidColorMatrix;
        public static string GDIPlusOnly => ConsoleLocalization.Error_GDIPlusOnly;
        public static string GParamIsNegative => ConsoleLocalization.Error_GParamIsNegative;
        public static string GParamTooLarge => ConsoleLocalization.Error_GParamTooLarge;
        public static string ImgRefOutOfRange => ConsoleLocalization.Error_ImgRefOutOfRange;
        public static string MinInt64CanNotApplyABS => ConsoleLocalization.Error_MinInt64CanNotApplyABS;
        public static string UnsupportedType => ConsoleLocalization.Error_UnsupportedType;
        public static string ArgsNotFitExpr => ConsoleLocalization.Error_ArgsNotFitExpr;
        public static string DTLackOfNamedColumn => ConsoleLocalization.Error_DTLackOfNamedColumn;
        public static string DTInvalidDataType => ConsoleLocalization.Error_DTInvalidDataType;
        public static string DTCanNotEditIdColumn => ConsoleLocalization.Error_DTCanNotEditIdColumn;
        public static string IsDefinedCsvVariable => ConsoleLocalization.Error_IsDefinedCsvVariable;
        public static string InitFatalError => ConsoleLocalization.Error_InitFatalError;
        public static string FileNotUTF8BOM => ConsoleLocalization.Error_FileNotUTF8BOM;
        public static string CanNotUseVAR => ConsoleLocalization.Error_CanNotUseVAR;
        public static string CanNotUseDumprand => ConsoleLocalization.Error_CanNotUseDumprand;
        public static string CanNotUseInitrand => ConsoleLocalization.Error_CanNotUseInitrand;
        public static string IgnoreRandomize => ConsoleLocalization.Error_IgnoreRandomize;
    }

    public static class SystemLine
    {
        public static string LoadingFile => ConsoleLocalization.SystemLine_LoadingFile;
        public static string ElapsedTimeLoad => ConsoleLocalization.SystemLine_ElapsedTimeLoad;
        public static string ElapsedTime => ConsoleLocalization.SystemLine_ElapsedTime;
        public static string BuildingUserFunc => ConsoleLocalization.SystemLine_BuildingUserFunc;
        public static string CheckingSyntax => ConsoleLocalization.SystemLine_CheckingSyntax;
        public static string LoadComplete => ConsoleLocalization.SystemLine_LoadComplete;
        public static string SelectExitConfigMB => ConsoleLocalization.SystemLine_SelectExitConfigMB;
        public static string ResourceReadError => ConsoleLocalization.SystemLine_ResourceReadError;
        public static string LoadingMacro => ConsoleLocalization.SystemLine_LoadingMacro;
        public static string LoadingReplace => ConsoleLocalization.SystemLine_LoadingReplace;
        public static string SelectExitReplaceMB => ConsoleLocalization.SystemLine_SelectExitReplaceMB;
        public static string LoadingRename => ConsoleLocalization.SystemLine_LoadingRename;
        public static string MissingRename => ConsoleLocalization.SystemLine_MissingRename;
        public static string GamebaseError => ConsoleLocalization.SystemLine_GamebaseError;
        public static string ErhLoadingError => ConsoleLocalization.SystemLine_ErhLoadingError;
        public static string DebugTraceCall => ConsoleLocalization.SystemLine_DebugTraceCall;
        public static string DebugTraceJump => ConsoleLocalization.SystemLine_DebugTraceJump;
        public static string AnalysisCompleted => ConsoleLocalization.SystemLine_AnalysisCompleted;
        public static string PressEnterOrClick => ConsoleLocalization.SystemLine_PressEnterOrClick;

        public static string ExitBecauseCanNotInterpreted1 =>
            ConsoleLocalization.SystemLine_ExitBecauseCanNotInterpreted1;

        public static string ExitBecauseCanNotInterpreted2 =>
            ConsoleLocalization.SystemLine_ExitBecauseCanNotInterpreted2;

        public static string ExitBecauseCanNotInterpreted3 =>
            ConsoleLocalization.SystemLine_ExitBecauseCanNotInterpreted3;

        public static string SaveQuestion => ConsoleLocalization.SystemLine_SaveQuestion;
        public static string LoadQuestion => ConsoleLocalization.SystemLine_LoadQuestion;
        public static string DisplaySaveSlot => ConsoleLocalization.SystemLine_DisplaySaveSlot;
        public static string DoYouOverwrite => ConsoleLocalization.SystemLine_DoYouOverwrite;
        public static string Yes => ConsoleLocalization.SystemLine_Yes;
        public static string No => ConsoleLocalization.SystemLine_No;
        public static string Remaining => ConsoleLocalization.SystemLine_Remaining;
        public static string Processing => ConsoleLocalization.SystemLine_Processing;
        public static string FileNone => ConsoleLocalization.SystemLine_FileNone;
        public static string LineFuncNone => ConsoleLocalization.SystemLine_LineFuncNone;
        public static string FileName => ConsoleLocalization.SystemLine_FileName;
        public static string LineFuncName => ConsoleLocalization.SystemLine_LineFuncName;
        public static string FuncCallStack => ConsoleLocalization.SystemLine_FuncCallStack;
        public static string ReloadingErb => ConsoleLocalization.SystemLine_ReloadingErb;
        public static string ReloadCompleted => ConsoleLocalization.SystemLine_ReloadCompleted;
        public static string LogFileHasBeenCreated => ConsoleLocalization.SystemLine_LogFileHasBeenCreated;
        public static string MinusWontWork => ConsoleLocalization.SystemLine_MinusWontWork;
        public static string Patch => ConsoleLocalization.SystemLine_Patch;
        public static string Enviroment => ConsoleLocalization.SystemLine_Enviroment;
        public static string Log => ConsoleLocalization.SystemLine_Log;
    }

    public static void SetLanguage(string language)
    {
        var culture = new CultureInfo(language);
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}