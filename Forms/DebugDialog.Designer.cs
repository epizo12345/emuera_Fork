namespace MinorShift.Emuera.Forms
{
	partial class DebugDialog
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            menuStrip1 = new System.Windows.Forms.MenuStrip();
            toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            ウォッチリストの保存ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ウォッチリストの読込ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            閉じるToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            設定ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            設定ToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            tabControlMain = new System.Windows.Forms.TabControl();
            tabPageWatch = new System.Windows.Forms.TabPage();
            listViewWatch = new System.Windows.Forms.ListView();
            columnHeader1 = new System.Windows.Forms.ColumnHeader();
            columnHeader3 = new System.Windows.Forms.ColumnHeader();
            tabPageTrace = new System.Windows.Forms.TabPage();
            textBoxTrace = new System.Windows.Forms.TextBox();
            tabPageConsole = new System.Windows.Forms.TabPage();
            textBoxCommand = new System.Windows.Forms.TextBox();
            textBoxConsole = new System.Windows.Forms.TextBox();
            checkBoxTopMost = new System.Windows.Forms.CheckBox();
            button1 = new System.Windows.Forms.Button();
            button2 = new System.Windows.Forms.Button();
            splitContainer1 = new System.Windows.Forms.SplitContainer();
            menuStrip1.SuspendLayout();
            tabControlMain.SuspendLayout();
            tabPageWatch.SuspendLayout();
            tabPageTrace.SuspendLayout();
            tabPageConsole.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { toolStripMenuItem1, 設定ToolStripMenuItem });
            menuStrip1.Location = new System.Drawing.Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new System.Windows.Forms.Padding(8, 3, 0, 3);
            menuStrip1.Size = new System.Drawing.Size(512, 30);
            menuStrip1.TabIndex = 3;
            menuStrip1.Text = "menuStrip1";
            // 
            // toolStripMenuItem1
            // 
            toolStripMenuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { ウォッチリストの保存ToolStripMenuItem, ウォッチリストの読込ToolStripMenuItem, 閉じるToolStripMenuItem });
            toolStripMenuItem1.Name = "toolStripMenuItem1";
            toolStripMenuItem1.Size = new System.Drawing.Size(82, 24);
            toolStripMenuItem1.Text = "ファイル(&F)";
            // 
            // ウォッチリストの保存ToolStripMenuItem
            // 
            ウォッチリストの保存ToolStripMenuItem.Name = "ウォッチリストの保存ToolStripMenuItem";
            ウォッチリストの保存ToolStripMenuItem.Size = new System.Drawing.Size(208, 26);
            ウォッチリストの保存ToolStripMenuItem.Text = "ウォッチリストの保存";
            ウォッチリストの保存ToolStripMenuItem.Click += ウォッチリストの保存ToolStripMenuItem_Click;
            // 
            // ウォッチリストの読込ToolStripMenuItem
            // 
            ウォッチリストの読込ToolStripMenuItem.Name = "ウォッチリストの読込ToolStripMenuItem";
            ウォッチリストの読込ToolStripMenuItem.Size = new System.Drawing.Size(208, 26);
            ウォッチリストの読込ToolStripMenuItem.Text = "ウォッチリストの読込";
            ウォッチリストの読込ToolStripMenuItem.Click += ウォッチリストの読込ToolStripMenuItem_Click;
            // 
            // 閉じるToolStripMenuItem
            // 
            閉じるToolStripMenuItem.Name = "閉じるToolStripMenuItem";
            閉じるToolStripMenuItem.Size = new System.Drawing.Size(208, 26);
            閉じるToolStripMenuItem.Text = "閉じる";
            閉じるToolStripMenuItem.Click += 閉じるToolStripMenuItem_Click;
            // 
            // 設定ToolStripMenuItem
            // 
            設定ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { 設定ToolStripMenuItem1 });
            設定ToolStripMenuItem.Name = "設定ToolStripMenuItem";
            設定ToolStripMenuItem.Size = new System.Drawing.Size(72, 24);
            設定ToolStripMenuItem.Text = "設定(&C)";
            // 
            // 設定ToolStripMenuItem1
            // 
            設定ToolStripMenuItem1.Name = "設定ToolStripMenuItem1";
            設定ToolStripMenuItem1.Size = new System.Drawing.Size(163, 26);
            設定ToolStripMenuItem1.Text = "コンフィグ(&C)";
            設定ToolStripMenuItem1.Click += 設定ToolStripMenuItem1_Click;
            // 
            // tabControlMain
            // 
            tabControlMain.Controls.Add(tabPageWatch);
            tabControlMain.Controls.Add(tabPageTrace);
            tabControlMain.Controls.Add(tabPageConsole);
            tabControlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            tabControlMain.Location = new System.Drawing.Point(0, 30);
            tabControlMain.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabControlMain.Name = "tabControlMain";
            tabControlMain.SelectedIndex = 0;
            tabControlMain.Size = new System.Drawing.Size(512, 359);
            tabControlMain.TabIndex = 4;
            tabControlMain.Selected += tabControlMain_Selected;
            // 
            // tabPageWatch
            // 
            tabPageWatch.Controls.Add(listViewWatch);
            tabPageWatch.Location = new System.Drawing.Point(4, 29);
            tabPageWatch.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageWatch.Name = "tabPageWatch";
            tabPageWatch.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageWatch.Size = new System.Drawing.Size(504, 326);
            tabPageWatch.TabIndex = 0;
            tabPageWatch.Text = "変数ウォッチ";
            tabPageWatch.UseVisualStyleBackColor = true;
            // 
            // listViewWatch
            // 
            listViewWatch.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { columnHeader1, columnHeader3 });
            listViewWatch.Dock = System.Windows.Forms.DockStyle.Fill;
            listViewWatch.LabelEdit = true;
            listViewWatch.Location = new System.Drawing.Point(4, 5);
            listViewWatch.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            listViewWatch.Name = "listViewWatch";
            listViewWatch.Size = new System.Drawing.Size(496, 316);
            listViewWatch.TabIndex = 0;
            listViewWatch.UseCompatibleStateImageBehavior = false;
            listViewWatch.View = System.Windows.Forms.View.Details;
            listViewWatch.KeyUp += listViewWatch_KeyUp;
            listViewWatch.MouseUp += listViewWatch_MouseUp;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "対象";
            columnHeader1.Width = 147;
            // 
            // columnHeader3
            // 
            columnHeader3.Text = "値";
            columnHeader3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            columnHeader3.Width = 97;
            // 
            // tabPageTrace
            // 
            tabPageTrace.Controls.Add(textBoxTrace);
            tabPageTrace.Location = new System.Drawing.Point(4, 29);
            tabPageTrace.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageTrace.Name = "tabPageTrace";
            tabPageTrace.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageTrace.Size = new System.Drawing.Size(505, 375);
            tabPageTrace.TabIndex = 1;
            tabPageTrace.Text = "スタックトレース";
            tabPageTrace.UseVisualStyleBackColor = true;
            // 
            // textBoxTrace
            // 
            textBoxTrace.Dock = System.Windows.Forms.DockStyle.Fill;
            textBoxTrace.Font = new System.Drawing.Font("ＭＳ ゴシック", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 128);
            textBoxTrace.Location = new System.Drawing.Point(4, 5);
            textBoxTrace.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            textBoxTrace.Multiline = true;
            textBoxTrace.Name = "textBoxTrace";
            textBoxTrace.ReadOnly = true;
            textBoxTrace.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            textBoxTrace.Size = new System.Drawing.Size(497, 365);
            textBoxTrace.TabIndex = 0;
            // 
            // tabPageConsole
            // 
            tabPageConsole.Controls.Add(textBoxCommand);
            tabPageConsole.Controls.Add(textBoxConsole);
            tabPageConsole.Location = new System.Drawing.Point(4, 29);
            tabPageConsole.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageConsole.Name = "tabPageConsole";
            tabPageConsole.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            tabPageConsole.Size = new System.Drawing.Size(505, 375);
            tabPageConsole.TabIndex = 2;
            tabPageConsole.Text = "コンソール";
            tabPageConsole.UseVisualStyleBackColor = true;
            // 
            // textBoxCommand
            // 
            textBoxCommand.Dock = System.Windows.Forms.DockStyle.Bottom;
            textBoxCommand.Location = new System.Drawing.Point(4, 343);
            textBoxCommand.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            textBoxCommand.Name = "textBoxCommand";
            textBoxCommand.Size = new System.Drawing.Size(497, 27);
            textBoxCommand.TabIndex = 0;
            textBoxCommand.KeyDown += textBoxCommand_KeyDown;
            // 
            // textBoxConsole
            // 
            textBoxConsole.Dock = System.Windows.Forms.DockStyle.Top;
            textBoxConsole.Font = new System.Drawing.Font("ＭＳ ゴシック", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 128);
            textBoxConsole.Location = new System.Drawing.Point(4, 5);
            textBoxConsole.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            textBoxConsole.Multiline = true;
            textBoxConsole.Name = "textBoxConsole";
            textBoxConsole.ReadOnly = true;
            textBoxConsole.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            textBoxConsole.Size = new System.Drawing.Size(497, 236);
            textBoxConsole.TabIndex = 1;
            // 
            // checkBoxTopMost
            // 
            checkBoxTopMost.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            checkBoxTopMost.Appearance = System.Windows.Forms.Appearance.Button;
            checkBoxTopMost.AutoSize = true;
            checkBoxTopMost.Location = new System.Drawing.Point(8, 8);
            checkBoxTopMost.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            checkBoxTopMost.Name = "checkBoxTopMost";
            checkBoxTopMost.Size = new System.Drawing.Size(106, 30);
            checkBoxTopMost.TabIndex = 6;
            checkBoxTopMost.Text = "最前面に表示";
            checkBoxTopMost.UseVisualStyleBackColor = true;
            checkBoxTopMost.CheckedChanged += checkBoxTopMost_CheckedChanged;
            // 
            // button1
            // 
            button1.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            button1.Location = new System.Drawing.Point(211, 5);
            button1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            button1.Name = "button1";
            button1.Size = new System.Drawing.Size(119, 37);
            button1.TabIndex = 7;
            button1.Text = "閉じる";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // button2
            // 
            button2.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            button2.Location = new System.Drawing.Point(84, 5);
            button2.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            button2.Name = "button2";
            button2.Size = new System.Drawing.Size(119, 37);
            button2.TabIndex = 8;
            button2.Text = "データ更新";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = System.Windows.Forms.DockStyle.Bottom;
            splitContainer1.Location = new System.Drawing.Point(0, 389);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(checkBoxTopMost);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(button2);
            splitContainer1.Panel2.Controls.Add(button1);
            splitContainer1.Size = new System.Drawing.Size(512, 49);
            splitContainer1.SplitterDistance = 170;
            splitContainer1.TabIndex = 9;
            // 
            // DebugDialog
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(512, 438);
            Controls.Add(tabControlMain);
            Controls.Add(menuStrip1);
            Controls.Add(splitContainer1);
            MainMenuStrip = menuStrip1;
            Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new System.Drawing.Size(421, 369);
            Name = "DebugDialog";
            ShowIcon = false;
            ShowInTaskbar = false;
            Text = "Emuera - デバッグウインドウ";
            Activated += DebugDialog_Activated;
            FormClosing += DebugDialog_FormClosing;
            Resize += DebugDialog_Resize;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            tabControlMain.ResumeLayout(false);
            tabPageWatch.ResumeLayout(false);
            tabPageTrace.ResumeLayout(false);
            tabPageTrace.PerformLayout();
            tabPageConsole.ResumeLayout(false);
            tabPageConsole.PerformLayout();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem ウォッチリストの保存ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ウォッチリストの読込ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 閉じるToolStripMenuItem;
        private System.Windows.Forms.TabControl tabControlMain;
        private System.Windows.Forms.TabPage tabPageWatch;
        private System.Windows.Forms.ListView listViewWatch;
        private System.Windows.Forms.TabPage tabPageTrace;
        private System.Windows.Forms.CheckBox checkBoxTopMost;
        private System.Windows.Forms.Button button1;
		private System.Windows.Forms.ColumnHeader columnHeader1;
		private System.Windows.Forms.ColumnHeader columnHeader3;
		private System.Windows.Forms.TextBox textBoxTrace;
		private System.Windows.Forms.TabPage tabPageConsole;
		private System.Windows.Forms.TextBox textBoxConsole;
		private System.Windows.Forms.Button button2;
		private System.Windows.Forms.TextBox textBoxCommand;
		private System.Windows.Forms.ToolStripMenuItem 設定ToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem 設定ToolStripMenuItem1;
        private System.Windows.Forms.SplitContainer splitContainer1;
    }
}