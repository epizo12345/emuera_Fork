namespace MinorShift.Emuera.Forms
{
	partial class DebugConfigDialog
	{
		/// <summary>
		/// 必要なデザイナ変数です。
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// 使用中のリソースをすべてクリーンアップします。
		/// </summary>
		/// <param name="disposing">マネージ リソースが破棄される場合 true、破棄されない場合は false です。</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows フォーム デザイナで生成されたコード

        /// <summary>
        /// デザイナ サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディタで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            buttonSave = new System.Windows.Forms.Button();
            buttonCancel = new System.Windows.Forms.Button();
            tabControl = new System.Windows.Forms.TabControl();
            tabPageDebug3 = new System.Windows.Forms.TabPage();
            label29 = new System.Windows.Forms.Label();
            checkBoxDWTM = new System.Windows.Forms.CheckBox();
            checkBoxShowDW = new System.Windows.Forms.CheckBox();
            checkBoxSetDWPos = new System.Windows.Forms.CheckBox();
            button5 = new System.Windows.Forms.Button();
            label25 = new System.Windows.Forms.Label();
            label26 = new System.Windows.Forms.Label();
            numericUpDownDWY = new System.Windows.Forms.NumericUpDown();
            numericUpDownDWX = new System.Windows.Forms.NumericUpDown();
            button6 = new System.Windows.Forms.Button();
            label27 = new System.Windows.Forms.Label();
            label28 = new System.Windows.Forms.Label();
            numericUpDownDWH = new System.Windows.Forms.NumericUpDown();
            numericUpDownDWW = new System.Windows.Forms.NumericUpDown();
            label16 = new System.Windows.Forms.Label();
            openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            toolTip1 = new System.Windows.Forms.ToolTip(components);
            tabControl.SuspendLayout();
            tabPageDebug3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWY).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWX).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWH).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWW).BeginInit();
            SuspendLayout();
            // 
            // buttonSave
            // 
            buttonSave.Location = new System.Drawing.Point(252, 593);
            buttonSave.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            buttonSave.Name = "buttonSave";
            buttonSave.Size = new System.Drawing.Size(65, 40);
            buttonSave.TabIndex = 1;
            buttonSave.Text = "保存";
            buttonSave.UseVisualStyleBackColor = true;
            buttonSave.Click += buttonSave_Click;
            // 
            // buttonCancel
            // 
            buttonCancel.Location = new System.Drawing.Point(325, 593);
            buttonCancel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new System.Drawing.Size(104, 40);
            buttonCancel.TabIndex = 2;
            buttonCancel.Text = "キャンセル";
            buttonCancel.UseVisualStyleBackColor = true;
            buttonCancel.Click += buttonCancel_Click;
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabPageDebug3);
            tabControl.Location = new System.Drawing.Point(16, 20);
            tabControl.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabControl.Multiline = true;
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new System.Drawing.Size(417, 543);
            tabControl.TabIndex = 35;
            // 
            // tabPageDebug3
            // 
            tabPageDebug3.Controls.Add(label29);
            tabPageDebug3.Controls.Add(checkBoxDWTM);
            tabPageDebug3.Controls.Add(checkBoxShowDW);
            tabPageDebug3.Controls.Add(checkBoxSetDWPos);
            tabPageDebug3.Controls.Add(button5);
            tabPageDebug3.Controls.Add(label25);
            tabPageDebug3.Controls.Add(label26);
            tabPageDebug3.Controls.Add(numericUpDownDWY);
            tabPageDebug3.Controls.Add(numericUpDownDWX);
            tabPageDebug3.Controls.Add(button6);
            tabPageDebug3.Controls.Add(label27);
            tabPageDebug3.Controls.Add(label28);
            tabPageDebug3.Controls.Add(numericUpDownDWH);
            tabPageDebug3.Controls.Add(numericUpDownDWW);
            tabPageDebug3.Location = new System.Drawing.Point(4, 29);
            tabPageDebug3.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageDebug3.Name = "tabPageDebug3";
            tabPageDebug3.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageDebug3.Size = new System.Drawing.Size(409, 510);
            tabPageDebug3.TabIndex = 7;
            tabPageDebug3.Text = "デバッグ";
            tabPageDebug3.UseVisualStyleBackColor = true;
            // 
            // label29
            // 
            label29.AutoSize = true;
            label29.Location = new System.Drawing.Point(8, 22);
            label29.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label29.Name = "label29";
            label29.Size = new System.Drawing.Size(347, 40);
            label29.TabIndex = 98;
            label29.Text = "※デバッグ関連のオプションはコマンドライン引数に-Debug\r\nを指定して起動した時のみ有効です";
            // 
            // checkBoxDWTM
            // 
            checkBoxDWTM.AutoSize = true;
            checkBoxDWTM.Location = new System.Drawing.Point(28, 122);
            checkBoxDWTM.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            checkBoxDWTM.Name = "checkBoxDWTM";
            checkBoxDWTM.Size = new System.Drawing.Size(253, 24);
            checkBoxDWTM.TabIndex = 97;
            checkBoxDWTM.Text = "デバッグウインドウを最前面に表示する";
            checkBoxDWTM.UseVisualStyleBackColor = true;
            // 
            // checkBoxShowDW
            // 
            checkBoxShowDW.AutoSize = true;
            checkBoxShowDW.Location = new System.Drawing.Point(28, 85);
            checkBoxShowDW.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            checkBoxShowDW.Name = "checkBoxShowDW";
            checkBoxShowDW.Size = new System.Drawing.Size(253, 24);
            checkBoxShowDW.TabIndex = 96;
            checkBoxShowDW.Text = "起動時にデバッグウインドウを表示する";
            checkBoxShowDW.UseVisualStyleBackColor = true;
            // 
            // checkBoxSetDWPos
            // 
            checkBoxSetDWPos.AutoSize = true;
            checkBoxSetDWPos.Location = new System.Drawing.Point(28, 300);
            checkBoxSetDWPos.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            checkBoxSetDWPos.Name = "checkBoxSetDWPos";
            checkBoxSetDWPos.Size = new System.Drawing.Size(224, 24);
            checkBoxSetDWPos.TabIndex = 95;
            checkBoxSetDWPos.Text = "デバッグウィンドウ位置を指定する";
            checkBoxSetDWPos.UseVisualStyleBackColor = true;
            // 
            // button5
            // 
            button5.Location = new System.Drawing.Point(89, 420);
            button5.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            button5.Name = "button5";
            button5.Size = new System.Drawing.Size(237, 40);
            button5.TabIndex = 94;
            button5.Text = "現在のウィンドウ位置を取得";
            button5.UseVisualStyleBackColor = true;
            button5.Click += button5_Click;
            // 
            // label25
            // 
            label25.AutoSize = true;
            label25.Location = new System.Drawing.Point(196, 382);
            label25.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label25.Name = "label25";
            label25.Size = new System.Drawing.Size(146, 20);
            label25.TabIndex = 93;
            label25.Text = "デバッグウィンドウ位置Y";
            // 
            // label26
            // 
            label26.AutoSize = true;
            label26.Location = new System.Drawing.Point(196, 340);
            label26.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label26.Name = "label26";
            label26.Size = new System.Drawing.Size(147, 20);
            label26.TabIndex = 92;
            label26.Text = "デバッグウィンドウ位置X";
            // 
            // numericUpDownDWY
            // 
            numericUpDownDWY.Location = new System.Drawing.Point(28, 378);
            numericUpDownDWY.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            numericUpDownDWY.Name = "numericUpDownDWY";
            numericUpDownDWY.Size = new System.Drawing.Size(160, 27);
            numericUpDownDWY.TabIndex = 91;
            // 
            // numericUpDownDWX
            // 
            numericUpDownDWX.Location = new System.Drawing.Point(28, 337);
            numericUpDownDWX.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            numericUpDownDWX.Name = "numericUpDownDWX";
            numericUpDownDWX.Size = new System.Drawing.Size(160, 27);
            numericUpDownDWX.TabIndex = 90;
            // 
            // button6
            // 
            button6.Location = new System.Drawing.Point(89, 250);
            button6.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            button6.Name = "button6";
            button6.Size = new System.Drawing.Size(237, 40);
            button6.TabIndex = 89;
            button6.Text = "現在のウィンドウサイズを取得";
            button6.UseVisualStyleBackColor = true;
            button6.Click += button6_Click;
            // 
            // label27
            // 
            label27.AutoSize = true;
            label27.Location = new System.Drawing.Point(196, 212);
            label27.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label27.Name = "label27";
            label27.Size = new System.Drawing.Size(133, 20);
            label27.TabIndex = 88;
            label27.Text = "デバッグウィンドウ高さ";
            // 
            // label28
            // 
            label28.AutoSize = true;
            label28.Location = new System.Drawing.Point(196, 170);
            label28.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label28.Name = "label28";
            label28.Size = new System.Drawing.Size(123, 20);
            label28.TabIndex = 87;
            label28.Text = "デバッグウィンドウ幅";
            // 
            // numericUpDownDWH
            // 
            numericUpDownDWH.Location = new System.Drawing.Point(28, 208);
            numericUpDownDWH.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            numericUpDownDWH.Name = "numericUpDownDWH";
            numericUpDownDWH.Size = new System.Drawing.Size(160, 27);
            numericUpDownDWH.TabIndex = 86;
            // 
            // numericUpDownDWW
            // 
            numericUpDownDWW.Location = new System.Drawing.Point(28, 167);
            numericUpDownDWW.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            numericUpDownDWW.Name = "numericUpDownDWW";
            numericUpDownDWW.Size = new System.Drawing.Size(160, 27);
            numericUpDownDWW.TabIndex = 85;
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new System.Drawing.Point(108, 568);
            label16.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            label16.Name = "label16";
            label16.Size = new System.Drawing.Size(247, 20);
            label16.TabIndex = 36;
            label16.Text = "※変更は再起動するまで反映されません";
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // DebugConfigDialog
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(120F, 120F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            ClientSize = new System.Drawing.Size(445, 642);
            Controls.Add(label16);
            Controls.Add(tabControl);
            Controls.Add(buttonCancel);
            Controls.Add(buttonSave);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "DebugConfigDialog";
            ShowIcon = false;
            ShowInTaskbar = false;
            Text = "ConfigDialog";
            tabControl.ResumeLayout(false);
            tabPageDebug3.ResumeLayout(false);
            tabPageDebug3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWY).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWX).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWH).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownDWW).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button buttonSave;
		private System.Windows.Forms.Button buttonCancel;
		private System.Windows.Forms.TabControl tabControl;
		private System.Windows.Forms.Label label16;
		private System.Windows.Forms.OpenFileDialog openFileDialog1;
		private System.Windows.Forms.TabPage tabPageDebug3;
		private System.Windows.Forms.CheckBox checkBoxShowDW;
		private System.Windows.Forms.CheckBox checkBoxSetDWPos;
		private System.Windows.Forms.Button button5;
		private System.Windows.Forms.Label label25;
		private System.Windows.Forms.Label label26;
		private System.Windows.Forms.NumericUpDown numericUpDownDWY;
		private System.Windows.Forms.NumericUpDown numericUpDownDWX;
		private System.Windows.Forms.Button button6;
		private System.Windows.Forms.Label label27;
		private System.Windows.Forms.Label label28;
		private System.Windows.Forms.NumericUpDown numericUpDownDWH;
		private System.Windows.Forms.NumericUpDown numericUpDownDWW;
		private System.Windows.Forms.CheckBox checkBoxDWTM;
		private System.Windows.Forms.Label label29;
        private System.Windows.Forms.ToolTip toolTip1;
	}
}