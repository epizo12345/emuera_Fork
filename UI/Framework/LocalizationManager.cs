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

    public static void SetLanguage(string language)
    {
        var culture = new CultureInfo(language);
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}