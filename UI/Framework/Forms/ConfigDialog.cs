using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Forms;

internal enum ConfigDialogResult
{
    Cancel = 0,
    Save = 1,
    SaveReboot = 2,
}

internal sealed partial class ConfigDialog : Form
{
    public ConfigDialog()
    {
        InitializeComponent();
        numericUpDown1.Minimum = 1;//PrintCPerLine
        numericUpDown1.Maximum = 100;
        numericUpDown2.Minimum = 128;//ConfigCode.WindowX(Width)
        numericUpDown2.Maximum = 5000;
        numericUpDown3.Minimum = 128;//ConfigCode.WindowY(Height)
        numericUpDown3.Maximum = 5000;
        numericUpDown4.Minimum = 500;//MaxLog
        numericUpDown4.Maximum = 1000000;
        numericUpDown5.Minimum = 8;//FontSize
        numericUpDown5.Maximum = 144;
        numericUpDown6.Minimum = 8;//LineHeight
        numericUpDown6.Maximum = 144;
        numericUpDown7.Minimum = 1;//FPS
        numericUpDown7.Maximum = 240;
        numericUpDown8.Minimum = 1;//ScrollHeight
        numericUpDown8.Maximum = 10;
        numericUpDown9.Minimum = 1;//PrintCLength
        numericUpDown9.Maximum = 100;
        numericUpDown10.Minimum = 0;//InfiniteLoopAlertTime
        numericUpDown10.Maximum = 100000;
        numericUpDown11.Minimum = 20;//SaveDataNos
        numericUpDown11.Maximum = 80;
        numericUpDownPosX.Maximum = 10000;//WindowPosX
        numericUpDownPosY.Maximum = 10000;
        Localize();
    }

    private void shown(object sender, EventArgs e)
    {
        //フォントを事前読み込み
        foreach (var ff in new InstalledFontCollection().Families)
        {
            if (ff.IsStyleAvailable(FontStyle.Regular) &&
                ff.IsStyleAvailable(FontStyle.Bold) &&
                ff.IsStyleAvailable(FontStyle.Italic) &&
                ff.IsStyleAvailable(FontStyle.Strikeout) &&
                ff.IsStyleAvailable(FontStyle.Underline))
            {
                comboBox2.Items.Add(ff.Name);
            }
        }
    }

    private void buttonSave_Click(object sender, EventArgs e)
    {
        SaveConfig();
        Result = ConfigDialogResult.Save;
        this.Close();
    }

    private void buttonReboot_Click(object sender, EventArgs e)
    {
        SaveConfig();
        Result = ConfigDialogResult.SaveReboot;
        this.Close();
    }

    private void buttonCancel_Click(object sender, EventArgs e)
    {
        Result = ConfigDialogResult.Cancel;
        this.Close();
    }
    public ConfigDialogResult Result = ConfigDialogResult.Cancel;

    static void setCheckBox(CheckBox checkbox, ConfigCode code)
    {
        ConfigItem<bool> item = (ConfigItem<bool>)ConfigData.Instance.GetConfigItem(code);
        checkbox.Checked = item.Value;
        checkbox.Enabled = !item.Fixed;
    }

    static void setNumericUpDown(NumericUpDown updown, ConfigCode code)
    {
        ConfigItem<int> item = (ConfigItem<int>)ConfigData.Instance.GetConfigItem(code);
        decimal value = item.Value;
        if (updown.Maximum < value)
            updown.Maximum = value;
        if (updown.Minimum > value)
            updown.Minimum = value;
        updown.Value = value;
        updown.Enabled = !item.Fixed;
    }

    static void setNumericUpDown(NumericUpDown updown, int configValue)
    {
        decimal value = configValue;
        if (updown.Maximum < value)
            updown.Maximum = value;
        if (updown.Minimum > value)
            updown.Minimum = value;
        updown.Value = value;
    }

    static void setColorBox(ColorBox colorBox, ConfigCode code)
    {
        ConfigItem<Color> item = (ConfigItem<Color>)ConfigData.Instance.GetConfigItem(code);
        colorBox.SelectingColor = item.Value;
        colorBox.Enabled = !item.Fixed;
    }
    /*		void setTextBox(TextBox textBox, ConfigCode code)
				{
					ConfigItem<string> item = (ConfigItem<string>)ConfigData.Instance.GetConfigItem(code);
					textBox.Text = item.Value;
					textBox.Enabled = !item.Fixed;
				}
		*/
    MainWindow parent;
    public void SetConfig(MainWindow mainWindow)
    {
        parent = mainWindow;
        //ConfigData config = ConfigData.Instance;
        setCheckBox(checkBox1, ConfigCode.IgnoreCase);
        setCheckBox(checkBox2, ConfigCode.UseRenameFile);
        setCheckBox(checkBox3, ConfigCode.UseMouse);
        setCheckBox(checkBox4, ConfigCode.UseMenu);
        setCheckBox(checkBox5, ConfigCode.UseDebugCommand);
        setCheckBox(checkBox6, ConfigCode.AllowMultipleInstances);
        setCheckBox(checkBox7, ConfigCode.AutoSave);
        setCheckBox(checkBox8, ConfigCode.SizableWindow);
        setCheckBox(checkBox10, ConfigCode.UseReplaceFile);
        setCheckBox(checkBox11, ConfigCode.IgnoreUncalledFunction);
        //setCheckBox(checkBox12, ConfigCode.ReduceFormattedStringOnLoad);
        setCheckBox(checkBox13, ConfigCode.DisplayReport);
        setCheckBox(checkBox14, ConfigCode.ButtonWrap);
        setCheckBox(checkBox15, ConfigCode.SearchSubdirectory);
        setCheckBox(checkBox16, ConfigCode.SortWithFilename);
        setCheckBox(checkBox17, ConfigCode.SetWindowPos);
        setCheckBox(checkBox18, ConfigCode.UseKeyMacro);
        setCheckBox(checkBox20, ConfigCode.AllowFunctionOverloading);
        setCheckBox(checkBox19, ConfigCode.WarnFunctionOverloading);
        setCheckBox(checkBox21, ConfigCode.WindowMaximixed);
        setCheckBox(checkBox22, ConfigCode.WarnNormalFunctionOverloading);
        setCheckBox(checkBox23, ConfigCode.WarnBackCompatibility);
        setCheckBox(checkBoxCompatiErrorLine, ConfigCode.CompatiErrorLine);
        setCheckBox(checkBoxCompatiCALLNAME, ConfigCode.CompatiCALLNAME);
        setCheckBox(checkBox24, ConfigCode.UseSaveFolder);
        setCheckBox(checkBox27, ConfigCode.SystemSaveInUTF8);
        setCheckBox(checkBoxCompatiRAND, ConfigCode.CompatiRAND);
        setCheckBox(checkBoxCompatiLinefeedAs1739, ConfigCode.CompatiLinefeedAs1739);
        setCheckBox(checkBox28, ConfigCode.CompatiCallEvent);
        setCheckBox(checkBoxFuncNoIgnoreCase, ConfigCode.CompatiFunctionNoignoreCase);
        setCheckBox(checkBoxSystemFullSpace, ConfigCode.SystemAllowFullSpace);
        setCheckBox(checkBox12, ConfigCode.CompatiFuncArgOptional);
        setCheckBox(checkBox25, ConfigCode.CompatiFuncArgAutoConvert);
        setCheckBox(checkBox26, ConfigCode.SystemSaveInBinary);
        setCheckBox(checkBoxSystemTripleSymbol, ConfigCode.SystemIgnoreTripleSymbol);
        setCheckBox(checkBoxCompatiSP, ConfigCode.CompatiSPChara);
        setCheckBox(checkBox9, ConfigCode.TimesNotRigorousCalculation);
        setCheckBox(checkBox29, ConfigCode.SystemNoTarget);
        setNumericUpDown(numericUpDown2, ConfigCode.WindowX);
        setNumericUpDown(numericUpDown3, ConfigCode.WindowY);
        setNumericUpDown(numericUpDown4, ConfigCode.MaxLog);
        setNumericUpDown(numericUpDown1, ConfigCode.PrintCPerLine);
        setNumericUpDown(numericUpDown9, ConfigCode.PrintCLength);
        setNumericUpDown(numericUpDown6, ConfigCode.LineHeight);
        setNumericUpDown(numericUpDown7, ConfigCode.FPS);
        setNumericUpDown(numericUpDown8, ConfigCode.ScrollHeight);
        setNumericUpDown(numericUpDown5, ConfigCode.FontSize);
        setNumericUpDown(numericUpDown10, ConfigCode.InfiniteLoopAlertTime);
        setNumericUpDown(numericUpDown11, ConfigCode.SaveDataNos);

        setNumericUpDown(numericUpDownPosX, ConfigCode.WindowPosX);
        setNumericUpDown(numericUpDownPosY, ConfigCode.WindowPosY);

        setColorBox(colorBoxFG, ConfigCode.ForeColor);
        setColorBox(colorBoxBG, ConfigCode.BackColor);
        setColorBox(colorBoxSelecting, ConfigCode.FocusColor);
        setColorBox(colorBoxBacklog, ConfigCode.LogColor);

        ConfigItem<string> itemStr = (ConfigItem<string>)ConfigData.Instance.GetConfigItem(ConfigCode.FontName);
        string fontname = itemStr.Value;
        int nameIndex = comboBox2.Items.IndexOf(fontname);
        if (nameIndex >= 0)
            comboBox2.SelectedIndex = nameIndex;
        else
        {
            comboBox2.Text = fontname;
            //nameIndex = comboBox2.Items.IndexOf("ＭＳ ゴシック");
            //if (nameIndex >= 0)
            //    comboBox2.SelectedIndex = nameIndex;
        }
        comboBox2.Enabled = !itemStr.Fixed;


        ConfigItem<ReduceArgumentOnLoadFlag> itemRA = (ConfigItem<ReduceArgumentOnLoadFlag>)ConfigData.Instance.GetConfigItem(ConfigCode.ReduceArgumentOnLoad);
        switch (itemRA.Value)
        {
            case ReduceArgumentOnLoadFlag.NO:
                comboBoxReduceArgumentOnLoad.SelectedIndex = 0; break;
            case ReduceArgumentOnLoadFlag.ONCE:
                comboBoxReduceArgumentOnLoad.SelectedIndex = 1; break;
            case ReduceArgumentOnLoadFlag.YES:
                comboBoxReduceArgumentOnLoad.SelectedIndex = 2; break;
        }
        comboBoxReduceArgumentOnLoad.Enabled = !itemRA.Fixed;


        ConfigItem<int> itemInt = (ConfigItem<int>)ConfigData.Instance.GetConfigItem(ConfigCode.DisplayWarningLevel);
        if (itemInt.Value <= 0)
            comboBox5.SelectedIndex = 0;
        else if (itemInt.Value >= 3)
            comboBox5.SelectedIndex = 3;
        else
            comboBox5.SelectedIndex = itemInt.Value;
        comboBox5.Enabled = !itemInt.Fixed;


        ConfigItem<DisplayWarningFlag> itemDWF = (ConfigItem<DisplayWarningFlag>)ConfigData.Instance.GetConfigItem(ConfigCode.FunctionNotFoundWarning);
        switch (itemDWF.Value)
        {
            case DisplayWarningFlag.IGNORE:
                comboBox3.SelectedIndex = 0; break;
            case DisplayWarningFlag.LATER:
                comboBox3.SelectedIndex = 1; break;
            case DisplayWarningFlag.ONCE:
                comboBox3.SelectedIndex = 2; break;
            case DisplayWarningFlag.DISPLAY:
                comboBox3.SelectedIndex = 3; break;
        }
        comboBox3.Enabled = !itemDWF.Fixed;

        itemDWF = (ConfigItem<DisplayWarningFlag>)ConfigData.Instance.GetConfigItem(ConfigCode.FunctionNotCalledWarning);
        switch (itemDWF.Value)
        {
            case DisplayWarningFlag.IGNORE:
                comboBox4.SelectedIndex = 0; break;
            case DisplayWarningFlag.LATER:
                comboBox4.SelectedIndex = 1; break;
            case DisplayWarningFlag.ONCE:
                comboBox4.SelectedIndex = 2; break;
            case DisplayWarningFlag.DISPLAY:
                comboBox4.SelectedIndex = 3; break;
        }
        comboBox4.Enabled = !itemDWF.Fixed;

        ConfigItem<UseLanguage> itemLang = (ConfigItem<UseLanguage>)ConfigData.Instance.GetConfigItem(ConfigCode.useLanguage);
        switch (itemLang.Value)
        {
            case UseLanguage.JAPANESE:
                comboBox1.SelectedIndex = 0; break;
            case UseLanguage.KOREAN:
                comboBox1.SelectedIndex = 1; break;
            case UseLanguage.CHINESE_HANS:
                comboBox1.SelectedIndex = 2; break;
            case UseLanguage.CHINESE_HANT:
                comboBox1.SelectedIndex = 3; break;
        }

        ConfigItem<TextEditorType> itemET = (ConfigItem<TextEditorType>)ConfigData.Instance.GetConfigItem(ConfigCode.EditorType);
        switch (itemET.Value)
        {
            case TextEditorType.SAKURA:
                comboBox6.SelectedIndex = 0; break;
            case TextEditorType.TERAPAD:
                comboBox6.SelectedIndex = 1; break;
            case TextEditorType.EMEDITOR:
                comboBox6.SelectedIndex = 2; break;
            case TextEditorType.USER_SETTING:
                comboBox6.SelectedIndex = 3; break;
        }
        comboBox6.Enabled = !itemET.Fixed;


        textBox1.Text = Config.TextEditor;
        textBox2.Text = Config.EditorArg;
        textBox2.Enabled = itemET.Value == TextEditorType.USER_SETTING;

        _useButtonFocusColor.Checked = JSONConfig.Game.UseButtonFocusBackgroundColor;
        _useNewRandom.Checked = JSONConfig.Game.UseNewRandom;
        _useScopedVariableInstruction.Checked = JSONConfig.Game.UseScopedVariableInstruction;

        #region EE_AnchorのCB機能移植
        checkBoxCBIgnoreTags.Checked = JSONConfig.User.CBIgnoreTags;
        textBoxCBReplaceTags.Text = JSONConfig.User.CBReplaceTags;
        checkBoxCBNewLinesOnly.Checked = JSONConfig.User.CBNewLinesOnly;
        checkBoxCBClearBuffer.Checked = JSONConfig.User.CBClearBuffer;
        checkBoxCBTriggerLeftClick.Checked = JSONConfig.User.CBTriggerLeftClick;
        checkBoxCBTriggerMiddleClick.Checked = JSONConfig.User.CBTriggerMiddleClick;
        checkBoxCBTriggerDoubleLeftClick.Checked = JSONConfig.User.CBTriggerDoubleLeftClick;
        checkBoxCBTriggerAnyKeyWait.Checked = JSONConfig.User.CBTriggerAnyKeyWait;
        checkBoxCBTriggerInputWait.Checked = JSONConfig.User.CBTriggerInputWait;
        setNumericUpDown(numericUpDownCBMaxCB, JSONConfig.User.CBMaxCB);
        setNumericUpDown(numericUpDownCBBufferSize, JSONConfig.User.CBBufferSize);
        setNumericUpDown(numericUpDownCBScrollCount, JSONConfig.User.CBScrollCount);
        setNumericUpDown(numericUpDownCBMinTimer, JSONConfig.User.CBMinTimer);
        #endregion

        _checkUTF8withBOM.Checked = JSONConfig.Game.CheckUTF8withBOM;
        _fontAntialias.SelectedIndex = (int)JSONConfig.Game.FontAntialias;
        _imageSampling.SelectedIndex = (int)JSONConfig.Game.ImageSamplingOption;
    }

    private void SaveConfig()
    {
        ConfigData config = ConfigData.Instance.Copy();
        config.GetConfigItem(ConfigCode.IgnoreCase).SetValue<bool>(checkBox1.Checked);
        config.GetConfigItem(ConfigCode.UseRenameFile).SetValue<bool>(checkBox2.Checked);
        config.GetConfigItem(ConfigCode.UseMouse).SetValue<bool>(checkBox3.Checked);
        config.GetConfigItem(ConfigCode.UseMenu).SetValue<bool>(checkBox4.Checked);
        config.GetConfigItem(ConfigCode.UseDebugCommand).SetValue<bool>(checkBox5.Checked);
        config.GetConfigItem(ConfigCode.AllowMultipleInstances).SetValue<bool>(checkBox6.Checked);
        config.GetConfigItem(ConfigCode.AutoSave).SetValue<bool>(checkBox7.Checked);
        config.GetConfigItem(ConfigCode.SizableWindow).SetValue<bool>(checkBox8.Checked);
        config.GetConfigItem(ConfigCode.UseReplaceFile).SetValue<bool>(checkBox10.Checked);
        config.GetConfigItem(ConfigCode.IgnoreUncalledFunction).SetValue<bool>(checkBox11.Checked);
        //config.GetConfigItem(ConfigCode.ReduceFormattedStringOnLoad).SetValue<bool>(checkBox12.Checked);
        config.GetConfigItem(ConfigCode.DisplayReport).SetValue<bool>(checkBox13.Checked);
        config.GetConfigItem(ConfigCode.ButtonWrap).SetValue<bool>(checkBox14.Checked);
        config.GetConfigItem(ConfigCode.SearchSubdirectory).SetValue<bool>(checkBox15.Checked);
        config.GetConfigItem(ConfigCode.SortWithFilename).SetValue<bool>(checkBox16.Checked);
        config.GetConfigItem(ConfigCode.SetWindowPos).SetValue<bool>(checkBox17.Checked);
        config.GetConfigItem(ConfigCode.UseKeyMacro).SetValue<bool>(checkBox18.Checked);
        config.GetConfigItem(ConfigCode.AllowFunctionOverloading).SetValue<bool>(checkBox20.Checked);
        config.GetConfigItem(ConfigCode.WarnFunctionOverloading).SetValue<bool>(checkBox19.Checked);
        config.GetConfigItem(ConfigCode.WindowMaximixed).SetValue<bool>(checkBox21.Checked);
        config.GetConfigItem(ConfigCode.WarnNormalFunctionOverloading).SetValue<bool>(checkBox22.Checked);
        config.GetConfigItem(ConfigCode.WarnBackCompatibility).SetValue<bool>(checkBox23.Checked);
        config.GetConfigItem(ConfigCode.CompatiErrorLine).SetValue<bool>(checkBoxCompatiErrorLine.Checked);
        config.GetConfigItem(ConfigCode.CompatiCALLNAME).SetValue<bool>(checkBoxCompatiCALLNAME.Checked);
        config.GetConfigItem(ConfigCode.UseSaveFolder).SetValue<bool>(checkBox24.Checked);
        config.GetConfigItem(ConfigCode.CompatiRAND).SetValue<bool>(checkBoxCompatiRAND.Checked);
        config.GetConfigItem(ConfigCode.CompatiLinefeedAs1739).SetValue<bool>(checkBoxCompatiLinefeedAs1739.Checked);
        config.GetConfigItem(ConfigCode.CompatiCallEvent).SetValue<bool>(checkBox28.Checked);
        config.GetConfigItem(ConfigCode.SystemSaveInUTF8).SetValue<bool>(checkBox27.Checked);

        config.GetConfigItem(ConfigCode.CompatiFuncArgOptional).SetValue<bool>(checkBox12.Checked);
        config.GetConfigItem(ConfigCode.CompatiFuncArgAutoConvert).SetValue<bool>(checkBox25.Checked);
        config.GetConfigItem(ConfigCode.SystemSaveInBinary).SetValue<bool>(checkBox26.Checked);
        config.GetConfigItem(ConfigCode.SystemIgnoreTripleSymbol).SetValue<bool>(checkBoxSystemTripleSymbol.Checked);

        config.GetConfigItem(ConfigCode.CompatiFunctionNoignoreCase).SetValue<bool>(checkBoxFuncNoIgnoreCase.Checked);
        config.GetConfigItem(ConfigCode.SystemAllowFullSpace).SetValue<bool>(checkBoxSystemFullSpace.Checked);
        config.GetConfigItem(ConfigCode.CompatiSPChara).SetValue<bool>(checkBoxCompatiSP.Checked);
        config.GetConfigItem(ConfigCode.TimesNotRigorousCalculation).SetValue<bool>(checkBox9.Checked);
        config.GetConfigItem(ConfigCode.SystemNoTarget).SetValue<bool>(checkBox29.Checked);


        config.GetConfigItem(ConfigCode.WindowX).SetValue<int>((int)numericUpDown2.Value);
        config.GetConfigItem(ConfigCode.WindowY).SetValue<int>((int)numericUpDown3.Value);
        config.GetConfigItem(ConfigCode.MaxLog).SetValue<int>((int)numericUpDown4.Value);
        config.GetConfigItem(ConfigCode.PrintCPerLine).SetValue<int>((int)numericUpDown1.Value);
        config.GetConfigItem(ConfigCode.PrintCLength).SetValue<int>((int)numericUpDown9.Value);
        config.GetConfigItem(ConfigCode.LineHeight).SetValue<int>((int)numericUpDown6.Value);
        config.GetConfigItem(ConfigCode.FPS).SetValue<int>((int)numericUpDown7.Value);
        config.GetConfigItem(ConfigCode.ScrollHeight).SetValue<int>((int)numericUpDown8.Value);
        config.GetConfigItem(ConfigCode.InfiniteLoopAlertTime).SetValue<int>((int)numericUpDown10.Value);
        config.GetConfigItem(ConfigCode.SaveDataNos).SetValue<int>((int)numericUpDown11.Value);

        config.GetConfigItem(ConfigCode.WindowPosX).SetValue<int>((int)numericUpDownPosX.Value);
        config.GetConfigItem(ConfigCode.WindowPosY).SetValue<int>((int)numericUpDownPosY.Value);

        config.GetConfigItem(ConfigCode.FontSize).SetValue<int>((int)numericUpDown5.Value);
        int nameIndex = comboBox2.SelectedIndex;
        if (nameIndex >= 0)
            config.GetConfigItem(ConfigCode.FontName).SetValue<string>((string)comboBox2.SelectedItem);
        else
            config.GetConfigItem(ConfigCode.FontName).SetValue<string>(comboBox2.Text);



        config.GetConfigItem(ConfigCode.ForeColor).SetValue<Color>(colorBoxFG.SelectingColor);
        config.GetConfigItem(ConfigCode.BackColor).SetValue<Color>(colorBoxBG.SelectingColor);
        config.GetConfigItem(ConfigCode.FocusColor).SetValue<Color>(colorBoxSelecting.SelectingColor);
        config.GetConfigItem(ConfigCode.LogColor).SetValue<Color>(colorBoxBacklog.SelectingColor);

        switch (comboBoxReduceArgumentOnLoad.SelectedIndex)
        {
            case 0:
                config.GetConfigItem(ConfigCode.ReduceArgumentOnLoad).SetValue<ReduceArgumentOnLoadFlag>(ReduceArgumentOnLoadFlag.NO); break;
            case 1:
                config.GetConfigItem(ConfigCode.ReduceArgumentOnLoad).SetValue<ReduceArgumentOnLoadFlag>(ReduceArgumentOnLoadFlag.ONCE); break;
            case 2:
                config.GetConfigItem(ConfigCode.ReduceArgumentOnLoad).SetValue<ReduceArgumentOnLoadFlag>(ReduceArgumentOnLoadFlag.YES); break;
        }
        config.GetConfigItem(ConfigCode.DisplayWarningLevel).SetValue<int>(comboBox5.SelectedIndex);


        switch (comboBox3.SelectedIndex)
        {
            case 0:
                config.GetConfigItem(ConfigCode.FunctionNotFoundWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.IGNORE); break;
            case 1:
                config.GetConfigItem(ConfigCode.FunctionNotFoundWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.LATER); break;
            case 2:
                config.GetConfigItem(ConfigCode.FunctionNotFoundWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.ONCE); break;
            case 3:
                config.GetConfigItem(ConfigCode.FunctionNotFoundWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.DISPLAY); break;
        }
        switch (comboBox4.SelectedIndex)
        {
            case 0:
                config.GetConfigItem(ConfigCode.FunctionNotCalledWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.IGNORE); break;
            case 1:
                config.GetConfigItem(ConfigCode.FunctionNotCalledWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.LATER); break;
            case 2:
                config.GetConfigItem(ConfigCode.FunctionNotCalledWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.ONCE); break;
            case 3:
                config.GetConfigItem(ConfigCode.FunctionNotCalledWarning).SetValue<DisplayWarningFlag>(DisplayWarningFlag.DISPLAY); break;
        }
        switch (comboBox1.SelectedIndex)
        {
            case 0:
                config.GetConfigItem(ConfigCode.useLanguage).SetValue<UseLanguage>(UseLanguage.JAPANESE); break;
            case 1:
                config.GetConfigItem(ConfigCode.useLanguage).SetValue<UseLanguage>(UseLanguage.KOREAN); break;
            case 2:
                config.GetConfigItem(ConfigCode.useLanguage).SetValue<UseLanguage>(UseLanguage.CHINESE_HANS); break;
            case 3:
                config.GetConfigItem(ConfigCode.useLanguage).SetValue<UseLanguage>(UseLanguage.CHINESE_HANT); break;
        }
        switch (comboBox6.SelectedIndex)
        {
            case 0:
                config.GetConfigItem(ConfigCode.EditorType).SetValue<TextEditorType>(TextEditorType.SAKURA); break;
            case 1:
                config.GetConfigItem(ConfigCode.EditorType).SetValue<TextEditorType>(TextEditorType.TERAPAD); break;
            case 2:
                config.GetConfigItem(ConfigCode.EditorType).SetValue<TextEditorType>(TextEditorType.EMEDITOR); break;
            case 3:
                config.GetConfigItem(ConfigCode.EditorType).SetValue<TextEditorType>(TextEditorType.USER_SETTING); break;
        }

        config.GetConfigItem(ConfigCode.TextEditor).SetValue<string>(textBox1.Text);
        config.GetConfigItem(ConfigCode.EditorArgument).SetValue<string>(textBox2.Text);

        config.SaveConfig();

        JSONConfig.Save();
    }


    private void comboBoxReduceArgumentOnLoad_SelectedIndexChanged(object sender, EventArgs e)
    {
        //いちいち切り替えるのが面倒なのでまとめて却下
        /*if (comboBoxReduceArgumentOnLoad.SelectedIndex == 0)
			{
				comboBox3.Enabled = false;
				comboBox4.Enabled = false;
				comboBox5.Enabled = false;
				checkBox12.Enabled = false;
				checkBox11.Enabled = false;
			}
			else
			{
				comboBox3.Enabled = true;
				comboBox4.Enabled = true;
				comboBox5.Enabled = true;
				checkBox12.Enabled = true;
				checkBox11.Enabled = true;
			}*/


    }


    private void button1_Click(object sender, EventArgs e)
    {
        if (parent == null)
            return;
        if (numericUpDown2.Enabled)
            numericUpDown2.Value = parent.MainPicBox.Width;
        if (numericUpDown3.Enabled)
            numericUpDown3.Value = parent.MainPicBox.Height + (int)Config.LineHeight;
    }

    private void button3_Click(object sender, EventArgs e)
    {
        if (parent == null)
            return;
        if (numericUpDownPosX.Enabled)
        {
            if (numericUpDownPosX.Maximum < parent.Location.X)
                numericUpDownPosX.Maximum = parent.Location.X;
            if (numericUpDownPosX.Minimum > parent.Location.X)
                numericUpDownPosX.Minimum = parent.Location.X;
            numericUpDownPosX.Value = parent.Location.X;
        }
        if (numericUpDownPosY.Enabled)
        {
            if (numericUpDownPosY.Maximum < parent.Location.Y)
                numericUpDownPosY.Maximum = parent.Location.Y;
            if (numericUpDownPosY.Minimum > parent.Location.Y)
                numericUpDownPosY.Minimum = parent.Location.Y;
            numericUpDownPosY.Value = parent.Location.Y;
        }

    }

    private void button2_Click(object sender, EventArgs e)
    {
        if (!comboBox2.Enabled)
            return;
        foreach (var ff in new InstalledFontCollection().Families)
        {
            if (ff.IsStyleAvailable(FontStyle.Regular) &&
                ff.IsStyleAvailable(FontStyle.Bold) &&
                ff.IsStyleAvailable(FontStyle.Italic) &&
                ff.IsStyleAvailable(FontStyle.Strikeout) &&
                ff.IsStyleAvailable(FontStyle.Underline))
            {
                comboBox2.Items.Add(ff.Name);
            }
        }

        var selectedFontName = comboBox2.Text;
        if (!string.IsNullOrEmpty(selectedFontName))
        {
            int nowFontIndex = comboBox2.Items.IndexOf(selectedFontName);
            if (nowFontIndex >= 0)
                comboBox2.SelectedIndex = nowFontIndex;
        }
    }

    private void button4_Click(object sender, EventArgs e)
    {
        openFileDialog1.InitialDirectory = @"c:\Program Files";
        openFileDialog1.FileName = "";
        DialogResult res = openFileDialog1.ShowDialog();
        if (res == DialogResult.OK)
        {
            textBox1.Text = openFileDialog1.FileName;
        }
    }

    static int setCheckBoxChecked(CheckBox checkbox, bool flag)
    {
        if (checkbox.Checked == flag)
            return 0;//変更不要
        if (!checkbox.Enabled)
            return -1;//変更したいけど許可されなかった
        checkbox.Checked = flag;
        return 1;//変更した
    }

    static int setComboBoxChanged(ComboBox combobox, int value)
    {
        if (combobox.SelectedIndex == value)
            return 0;//変更不要
        if (!combobox.Enabled)
            return -1;//変更したいけど許可されなかった
        combobox.SelectedIndex = value;
        return 1;//変更した
    }

    private void button7_Click(object sender, EventArgs e)
    {//eramaker仕様
        bool disenabled = false;
        disenabled |= setCheckBoxChecked(checkBoxCompatiErrorLine, true) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiCALLNAME, true) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiRAND, true) < 0;
        disenabled |= setCheckBoxChecked(checkBoxFuncNoIgnoreCase, true) < 0;
        disenabled |= setCheckBoxChecked(checkBox28, true) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiSP, true) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiLinefeedAs1739, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox12, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox25, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox9, true) < 0;
        if (disenabled)
            Dialog.Show(LocalizationManager.MsgBox.NotAllowChangeSetting, LocalizationManager.MsgBox.UnableChangeSetting);
    }

    private void button8_Click(object sender, EventArgs e)
    {//最新Emuera仕様 - 全部false
        bool disenabled = false;
        disenabled |= setCheckBoxChecked(checkBoxCompatiErrorLine, false) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiCALLNAME, false) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiRAND, false) < 0;
        disenabled |= setCheckBoxChecked(checkBoxFuncNoIgnoreCase, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox28, false) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiLinefeedAs1739, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox12, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox25, false) < 0;
        disenabled |= setCheckBoxChecked(checkBoxCompatiSP, false) < 0;
        disenabled |= setCheckBoxChecked(checkBox9, false) < 0;
        if (disenabled)
            Dialog.Show(LocalizationManager.MsgBox.NotAllowChangeSetting, LocalizationManager.MsgBox.UnableChangeSetting);
    }

    //互換性チェックはいじらないように変更
    private void button5_Click(object sender, EventArgs e)
    {//解析のユーザー向け設定（デフォルト設定と同じ）
        bool disenabled = false;
        //disenabled |= setCheckBoxChecked(checkBox23, true) < 0;
        disenabled |= setCheckBoxChecked(checkBox13, false) < 0;
        disenabled |= setComboBoxChanged(comboBoxReduceArgumentOnLoad, 0) < 0;
        disenabled |= setComboBoxChanged(comboBox5, 1) < 0;
        disenabled |= setCheckBoxChecked(checkBox11, true) < 0;
        disenabled |= setComboBoxChanged(comboBox3, 0) < 0;
        disenabled |= setComboBoxChanged(comboBox4, 0) < 0;
        if (disenabled)
            Dialog.Show(LocalizationManager.MsgBox.NotAllowChangeSetting, LocalizationManager.MsgBox.UnableChangeSetting);
    }

    private void button6_Click(object sender, EventArgs e)
    {//解析の開発者向け設定（関数名以外はしっかりチェックする）
        bool disenabled = false;
        //disenabled |= setCheckBoxChecked(checkBox23, true) < 0;
        disenabled |= setCheckBoxChecked(checkBox13, true) < 0;
        disenabled |= setComboBoxChanged(comboBoxReduceArgumentOnLoad, 2) < 0;
        disenabled |= setComboBoxChanged(comboBox5, 0) < 0;
        disenabled |= setCheckBoxChecked(checkBox11, true) < 0;
        disenabled |= setComboBoxChanged(comboBox3, 0) < 0;
        disenabled |= setComboBoxChanged(comboBox4, 0) < 0;
        if (disenabled)
            Dialog.Show(LocalizationManager.MsgBox.NotAllowChangeSetting, LocalizationManager.MsgBox.UnableChangeSetting);
    }

    private void comboBox6_SelectedIndexChanged(object sender, EventArgs e)
    {
        textBox2.Enabled = ((ComboBox)sender).SelectedIndex == 3;
    }

    private void numericUpDown5_ValueChanged(object sender, EventArgs e)
    {

    }

    private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
    {

    }

    private void UseButtonFocusColor_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.Game.UseButtonFocusBackgroundColor = _useButtonFocusColor.Checked;
    }

    private void IgnoreRandmizeSeed_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.Game.UseNewRandom = _useNewRandom.Checked;
    }

    private void _useVAR_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.Game.UseScopedVariableInstruction = _useScopedVariableInstruction.Checked;
    }

    private void checkBox27_CheckedChanged(object sender, EventArgs e)
    {

    }

    internal void Localize()
    {
        Text = LocalizationManager.ConfigDialog.Title;

        tabEnvironment.Text = LocalizationManager.ConfigDialog.Environment;
        checkBox3.Text = LocalizationManager.ConfigDialog.Environment_UseMouse;
        checkBox4.Text = LocalizationManager.ConfigDialog.Environment_UseMenu;
        checkBox5.Text = LocalizationManager.ConfigDialog.Environment_UseDebugCommand;
        checkBox6.Text = LocalizationManager.ConfigDialog.Environment_AllowMultipleInstances;
        checkBox18.Text = LocalizationManager.ConfigDialog.Environment_UseKeyMacro;
        checkBox7.Text = LocalizationManager.ConfigDialog.Environment_AutoSave;
        checkBox24.Text = LocalizationManager.ConfigDialog.Environment_UseSaveFolder;
        //checkBox33.Text = LocalizationManager.ConfigDialog.Environment_EnglishConfigOutput;
        label6.Text = LocalizationManager.ConfigDialog.Environment_MaxLog;
        label17.Text = LocalizationManager.ConfigDialog.Environment_InfiniteLoopAlertTime;
        label20.Text = LocalizationManager.ConfigDialog.Environment_SaveDataPerPage;
        label22.Text = LocalizationManager.ConfigDialog.Environment_TextEditor;
        button4.Text = LocalizationManager.ConfigDialog.Environment_Browse;
        label23.Text = LocalizationManager.ConfigDialog.Environment_TextEditorCommandline;
        comboBox6.Items[3] = LocalizationManager.ConfigDialog.Environment_TextEditorCommandline_UserSetting;

        tabPageView.Text = LocalizationManager.ConfigDialog.Display;
        label9.Text = LocalizationManager.ConfigDialog.Display_FPS;
        label5.Text = LocalizationManager.ConfigDialog.Display_PrintCPerLine;
        label1.Text = LocalizationManager.ConfigDialog.Display_PrintCLength;
        checkBox14.Text = LocalizationManager.ConfigDialog.Display_ButtonWrap;
        _useButtonFocusColor.Text = LocalizationManager.ConfigDialog.Display_UseButtonFocusColor;
        //label26.Text = LocalizationManager.ConfigDialog.Display_EmueraLang;

        tabPageWindow.Text = LocalizationManager.ConfigDialog.Window;
        label2.Text = LocalizationManager.ConfigDialog.Window_WindowWidth;
        label3.Text = LocalizationManager.ConfigDialog.Window_WindowHeight;
        button1.Text = LocalizationManager.ConfigDialog.Window_GetWindowSize;
        checkBox8.Text = LocalizationManager.ConfigDialog.Window_ChangeableWindowHeight;
        checkBox21.Text = LocalizationManager.ConfigDialog.Window_WindowMaximixed;
        checkBox17.Text = LocalizationManager.ConfigDialog.Window_SetWindowPos;
        label19.Text = LocalizationManager.ConfigDialog.Window_WindowX;
        label10.Text = LocalizationManager.ConfigDialog.Window_WindowY;
        button3.Text = LocalizationManager.ConfigDialog.Window_GetWindowPos;
        ScrollRange.Text = LocalizationManager.ConfigDialog.Window_LinesPerScroll;

        tabPageFont.Text = LocalizationManager.ConfigDialog.Font;
        colorBoxBG.LabelText = LocalizationManager.ConfigDialog.Font_BackgroundColor;
        colorBoxFG.LabelText = LocalizationManager.ConfigDialog.Font_TextColor;
        colorBoxSelecting.LabelText = LocalizationManager.ConfigDialog.Font_HighlightColor;
        colorBoxBacklog.LabelText = LocalizationManager.ConfigDialog.Font_LogHistoryColor;
        label4.Text = LocalizationManager.ConfigDialog.Font_FontName;
        label8.Text = LocalizationManager.ConfigDialog.Font_FontSize;
        label7.Text = LocalizationManager.ConfigDialog.Font_LineHeight;

        tabPageSystem.Text = LocalizationManager.ConfigDialog.System;
        label21.Text = LocalizationManager.ConfigDialog.System_Warning;
        checkBox1.Text = LocalizationManager.ConfigDialog.System_IgnoreCase;
        checkBox2.Text = LocalizationManager.ConfigDialog.System_UseRename;
        checkBox10.Text = LocalizationManager.ConfigDialog.System_UseReplace;
        checkBox15.Text = LocalizationManager.ConfigDialog.System_SearchSubfolder;
        checkBox16.Text = LocalizationManager.ConfigDialog.System_SortFileNames;
        checkBox20.Text = LocalizationManager.ConfigDialog.System_SystemFuncOverride;
        checkBox19.Text = LocalizationManager.ConfigDialog.System_SystemFuncOverrideWarn;
        checkBox22.Text = LocalizationManager.ConfigDialog.System_DuplicateFuncWarn;
        checkBoxSystemFullSpace.Text = LocalizationManager.ConfigDialog.System_WSIncludesFullWidth;
        label11.Text = LocalizationManager.ConfigDialog.System_ANSI;

        tabPageSystem2.Text = LocalizationManager.ConfigDialog.System2;
        label24.Text = LocalizationManager.ConfigDialog.System_Warning;
        checkBoxSystemTripleSymbol.Text = LocalizationManager.ConfigDialog.System2_IgnoreTripleSymbol;
        checkBox26.Text = LocalizationManager.ConfigDialog.System2_SaveInBinary;
        checkBox27.Text = LocalizationManager.ConfigDialog.System2_SaveInUTF8;
        //checkBox32.Text = LocalizationManager.ConfigDialog.System2_CompressSave;
        checkBox29.Text = LocalizationManager.ConfigDialog.System2_NoAutoCompleteCVar;
        _useNewRandom.Text = LocalizationManager.ConfigDialog.System2_UseNewRandom;
        _useScopedVariableInstruction.Text = LocalizationManager.ConfigDialog.System2_UseScopedVariableInstruction;
        // checkBox30.Text = LocalizationManager.ConfigDialog.System2_DisallowUpdateCheck;
        // checkBox31.Text = LocalizationManager.ConfigDialog.System2_UseERD;
        // checkBox34.Text = LocalizationManager.ConfigDialog.System2_VarsizeDimConfig;
        // label25.Text = LocalizationManager.ConfigDialog.System2_SaveLoadExt;

        tabPageCompati.Text = LocalizationManager.ConfigDialog.Compatibility;
        label30.Text = LocalizationManager.ConfigDialog.Compatibility_Warning;
        checkBoxCompatiErrorLine.Text = LocalizationManager.ConfigDialog.Compatibility_ExecuteErrorLine;
        checkBoxCompatiCALLNAME.Text = LocalizationManager.ConfigDialog.Compatibility_NameForCallname;
        checkBoxCompatiRAND.Text = LocalizationManager.ConfigDialog.Compatibility_EramakerRAND;
        checkBox9.Text = LocalizationManager.ConfigDialog.Compatibility_EramakerTIMES;
        checkBoxFuncNoIgnoreCase.Text = LocalizationManager.ConfigDialog.Compatibility_NoIgnoreCase;
        checkBox28.Text = LocalizationManager.ConfigDialog.Compatibility_CallEvent;
        checkBoxCompatiSP.Text = LocalizationManager.ConfigDialog.Compatibility_UseSPCharacters;
        checkBoxCompatiLinefeedAs1739.Text = LocalizationManager.ConfigDialog.Compatibility_ButtonWarp;
        checkBox12.Text = LocalizationManager.ConfigDialog.Compatibility_OmitArgs;
        checkBox25.Text = LocalizationManager.ConfigDialog.Compatibility_AutoTOSTR;
        button7.Text = LocalizationManager.ConfigDialog.Compatibility_EramakerStandard;
        button8.Text = LocalizationManager.ConfigDialog.Compatibility_EmueraStandard;

        tabPageDebug.Text = LocalizationManager.ConfigDialog.Debug;
        checkBox23.Text = LocalizationManager.ConfigDialog.Debug_CompatibilityWarn;
        checkBox13.Text = LocalizationManager.ConfigDialog.Debug_LoadingReport;
        //checkBox35.Text = LocalizationManager.ConfigDialog.Debug_CheckDuplicateIdentifier;
        label12.Text = LocalizationManager.ConfigDialog.Debug_ReduceArgs;
        comboBoxReduceArgumentOnLoad.Items[0] = LocalizationManager.ConfigDialog.Debug_ReduceArgs_Never;
        comboBoxReduceArgumentOnLoad.Items[1] = LocalizationManager.ConfigDialog.Debug_ReduceArgs_OnUpdate;
        comboBoxReduceArgumentOnLoad.Items[2] = LocalizationManager.ConfigDialog.Debug_ReduceArgs_Always;
        label15.Text = LocalizationManager.ConfigDialog.Debug_WarnLevel;
        comboBox5.Items[0] = LocalizationManager.ConfigDialog.Debug_WarnLevel_Level0;
        comboBox5.Items[1] = LocalizationManager.ConfigDialog.Debug_WarnLevel_Level1;
        comboBox5.Items[2] = LocalizationManager.ConfigDialog.Debug_WarnLevel_Level2;
        comboBox5.Items[3] = LocalizationManager.ConfigDialog.Debug_WarnLevel_Level3;
        checkBox11.Text = LocalizationManager.ConfigDialog.Debug_IgnoreUnusedFuncs;
        label13.Text = LocalizationManager.ConfigDialog.Debug_FuncNotFoundWarn;
        comboBox3.Items[0] = LocalizationManager.ConfigDialog.Debug_WarnSetting_Ignore;
        comboBox3.Items[1] = LocalizationManager.ConfigDialog.Debug_WarnSetting_TotalNumber;
        comboBox3.Items[2] = LocalizationManager.ConfigDialog.Debug_WarnSetting_OncePerFile;
        comboBox3.Items[3] = LocalizationManager.ConfigDialog.Debug_WarnSetting_Always;
        label14.Text = LocalizationManager.ConfigDialog.Debug_UnusedFuncWarn;
        comboBox4.Items[0] = LocalizationManager.ConfigDialog.Debug_WarnSetting_Ignore;
        comboBox4.Items[1] = LocalizationManager.ConfigDialog.Debug_WarnSetting_TotalNumber;
        comboBox4.Items[2] = LocalizationManager.ConfigDialog.Debug_WarnSetting_OncePerFile;
        comboBox4.Items[3] = LocalizationManager.ConfigDialog.Debug_WarnSetting_Always;
        button5.Text = LocalizationManager.ConfigDialog.Debug_PlayerStandard;
        button6.Text = LocalizationManager.ConfigDialog.Debug_DeveloperStandard;

        tabPageClipboard.Text = LocalizationManager.ConfigDialog.Clipboard;
        checkBoxCBIgnoreTags.Text = LocalizationManager.ConfigDialog.Clipboard_IgnoreTags;
        label29.Text = LocalizationManager.ConfigDialog.Clipboard_ReplaceTags;
        checkBoxCBNewLinesOnly.Text = LocalizationManager.ConfigDialog.Clipboard_NewLineOnly;
        checkBoxCBClearBuffer.Text = LocalizationManager.ConfigDialog.Clipboard_ClearClipboard;
        label27.Text = LocalizationManager.ConfigDialog.Clipboard_TriggerToUse;
        checkBoxCBTriggerLeftClick.Text = LocalizationManager.ConfigDialog.Clipboard_LClick;
        checkBoxCBTriggerMiddleClick.Text = LocalizationManager.ConfigDialog.Clipboard_MClick;
        checkBoxCBTriggerDoubleLeftClick.Text = LocalizationManager.ConfigDialog.Clipboard_DoubleClick;
        checkBoxCBTriggerAnyKeyWait.Text = LocalizationManager.ConfigDialog.Clipboard_AnyKeyWait;
        checkBoxCBTriggerInputWait.Text = LocalizationManager.ConfigDialog.Clipboard_InputWait;
        label28.Text = LocalizationManager.ConfigDialog.Clipboard_LinesToClipboard;
        label31.Text = LocalizationManager.ConfigDialog.Clipboard_TotalBuffer;
        label32.Text = LocalizationManager.ConfigDialog.Clipboard_LinesToScroll;
        label33.Text = LocalizationManager.ConfigDialog.Clipboard_UpdateTime;
        label34.Text = LocalizationManager.ConfigDialog.Clipboard_ScrollThrough;
        //
        // tabPageRikai.Text = LocalizationManager.ConfigDialog.Rikai;
        // rikaiCheckBoxEnable.Text = LocalizationManager.ConfigDialog.Rikai_RikaiEnable;
        // rikaiDictFilenameLabel.Text = LocalizationManager.ConfigDialog.Rikai_RikaiFilename;
        // rikaiColorBoxBG.Text = LocalizationManager.ConfigDialog.Font_BackgroundColor;
        // rikaiColorBoxText.Text = LocalizationManager.ConfigDialog.Font_TextColor;
        // rikaiCheckBoxSeparateBoxes.Text = LocalizationManager.ConfigDialog.Rikai_RikaiSeparateBox;
        // rikaiNote1.Text = LocalizationManager.ConfigDialog.Rikai_RikaiLink;
        // rikaiNote3.Text = LocalizationManager.ConfigDialog.Rikai_OtherEDICT1;


        buttonSave.Text = LocalizationManager.ConfigDialog.Save;
        buttonReboot.Text = LocalizationManager.ConfigDialog.SaveAndRestart;
        buttonCancel.Text = LocalizationManager.ConfigDialog.Cancel;
        label16.Text = LocalizationManager.ConfigDialog.ChangeWontTakeEffectUntilRestart;

        // var diff = tabControl_Size - tabControl.DisplayRectangle.Size + ((Size)tabControl.Padding);
        // var size = new Size(0, 0);
        // foreach (var page in pages)
        // {
        // 	if (page.Size.Width + page.Margin.Size.Width > size.Width) size.Width = page.Size.Width + page.Margin.Size.Width;
        // 	if (page.Size.Height + page.Margin.Size.Height > size.Height) size.Height = page.Size.Height + page.Margin.Size.Height;
        // }
        // tabControl.Size = new Size(size.Width + diff.Width, tabControl.Size.Height);
        // diff = tabControl.Size - tabControl.DisplayRectangle.Size + ((Size)tabControl.Padding);
        // tabControl.Size = size + diff;
        //
        // foreach (var page in pages)
        // {
        // 	diff = tabControl.DisplayRectangle.Size - page.Size;
        // 	page.Location = new Point(diff.Width / 2, diff.Height / 2);
        // }

        _checkUTF8withBOM.Text = LocalizationManager.ConfigDialog.Check_UTF8withBOM;
        _fontAntialiasLabel.Text = LocalizationManager.ConfigDialog.FontAntialias;
        _imageSamplingLabel.Text = LocalizationManager.ConfigDialog.ImageSampling;
    }

    private void checkBoxCBIgnoreTags_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBIgnoreTags = checkBoxCBIgnoreTags.Checked;
    }

    private void textBoxCBReplaceTags_TextChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBReplaceTags = textBoxCBReplaceTags.Text;
    }

    private void checkBoxCBNewLinesOnly_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBNewLinesOnly = checkBoxCBNewLinesOnly.Checked;
    }

    private void checkBoxCBClearBuffer_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBClearBuffer = checkBoxCBClearBuffer.Checked;
    }

    private void checkBoxCBTriggerLeftClick_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBTriggerLeftClick = checkBoxCBTriggerLeftClick.Checked;
    }

    private void checkBoxCBTriggerMiddleClick_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBTriggerMiddleClick = checkBoxCBTriggerMiddleClick.Checked;
    }

    private void checkBoxCBTriggerDoubleLeftClick_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBTriggerDoubleLeftClick = checkBoxCBTriggerDoubleLeftClick.Checked;
    }

    private void checkBoxCBTriggerAnyKeyWait_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBTriggerAnyKeyWait = checkBoxCBTriggerAnyKeyWait.Checked;
    }

    private void checkBoxCBTriggerInputWait_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBTriggerInputWait = checkBoxCBTriggerInputWait.Checked;
    }

    private void numericUpDownCBMaxCB_ValueChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBMaxCB = (int)numericUpDownCBMaxCB.Value;
        GlobalStatic.Console.CBProc.SetMaxCB(JSONConfig.User.CBMaxCB);
    }

    private void numericUpDownCBBufferSize_ValueChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBBufferSize = (int)numericUpDownCBBufferSize.Value;
    }

    private void numericUpDownCBScrollCount_ValueChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBScrollCount = (int)numericUpDownCBScrollCount.Value;
        GlobalStatic.Console.CBProc.SetScrollCount(JSONConfig.User.CBScrollCount);
    }

    private void numericUpDownCBMinTimer_ValueChanged(object sender, EventArgs e)
    {
        JSONConfig.User.CBMinTimer = (int)numericUpDownCBMinTimer.Value;
        GlobalStatic.Console.CBProc.SetTimerInterval(JSONConfig.User.CBMinTimer);
    }

    private void _checkUTF8withBOM_CheckedChanged(object sender, EventArgs e)
    {
        JSONConfig.Game.CheckUTF8withBOM = _checkUTF8withBOM.Checked;
    }

    private void comboBox7_SelectionChangeCommitted(object sender, EventArgs e)
    {
        JSONConfig.Game.ImageSamplingOption = (Resampler)_imageSampling.SelectedIndex;
    }

    private void _fontAntialias_SelectionChangeCommitted(object sender, EventArgs e)
    {
        JSONConfig.Game.FontAntialias = (FontAntialias)_fontAntialias.SelectedIndex;
    }
}