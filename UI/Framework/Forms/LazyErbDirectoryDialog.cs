using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Forms;

// [Emuera改修:LAZY-04]
// ERB配下を動的に表示し、親を選ぶと子孫も対象表示する。保存時は重複する子パスを除く。
internal sealed class LazyErbDirectoryDialog : Form
{
    readonly string _dataRoot;
    readonly string _erbRoot;
    readonly HashSet<string> _selectedDirectories;
    readonly TreeView _tree = new StateImageDoubleClickTreeView { CheckBoxes = true, Dock = DockStyle.Fill, HideSelection = false };
    bool _updatingChecks;

    internal string[] SelectedDirectories { get; private set; }

    internal LazyErbDirectoryDialog(string dataRoot, string erbRoot, IEnumerable<string> selectedDirectories)
    {
        _dataRoot = Path.GetFullPath(dataRoot);
        _erbRoot = Path.GetFullPath(erbRoot);
        _selectedDirectories = new HashSet<string>(NormalizeSelectedDirectories(selectedDirectories), StringComparer.OrdinalIgnoreCase);

        Text = LocalizationManager.ConfigDialog.LazyErbDirectoryDialog_Title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(460, 520);

        Label description = new()
        {
            Dock = DockStyle.Top,
            Height = 66,
            Text = LocalizationManager.ConfigDialog.LazyErbDirectoryDialog_Description,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
        };
        Button ok = new() { Text = LocalizationManager.ConfigDialog.LazyErbDirectoryDialog_Ok, Width = 90, DialogResult = DialogResult.None };
        Button cancel = new() { Text = LocalizationManager.ConfigDialog.LazyErbDirectoryDialog_Cancel, Width = 90, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => CommitSelection();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(_tree);
        Controls.Add(buttons);
        Controls.Add(description);
        AcceptButton = ok;
        CancelButton = cancel;
        _tree.AfterCheck += TreeAfterCheck;

        TreeNode root = new("ERB") { Tag = _erbRoot, Checked = false };
        _tree.Nodes.Add(root);
        PopulateDirectories(root, _erbRoot);
        root.Expand();
    }

    internal static string[] NormalizeSelectedDirectories(IEnumerable<string> directories)
    {
        List<string> candidates = directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => directory.Trim().Replace('\\', '/').TrimEnd('/'))
            .Where(directory => directory.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HashSet<string> selected = new(candidates, StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string candidate in candidates)
        {
            bool hasSelectedParent = false;
            int separator = candidate.IndexOf('/');
            while (separator >= 0)
            {
                if (selected.Contains(candidate[..separator]))
                {
                    hasSelectedParent = true;
                    break;
                }
                separator = candidate.IndexOf('/', separator + 1);
            }
            if (!hasSelectedParent)
                result.Add(candidate);
        }
        return result.ToArray();
    }

    internal static bool ShouldSuppressCheckboxDoubleClick(TreeView tree, IntPtr lParam)
    {
        long coordinates = lParam.ToInt64();
        int x = unchecked((short)(coordinates & 0xffff));
        int y = unchecked((short)((coordinates >> 16) & 0xffff));
        return (tree.HitTest(x, y).Location & TreeViewHitTestLocations.StateImage) != 0;
    }

    sealed class StateImageDoubleClickTreeView : TreeView
    {
        const int WmLButtonDblClk = 0x0203;

        // WinForms native TreeViewはcheckbox上の高速double clickでstate imageだけを切り替え、
        // .NET側のAfterCheckと食い違う場合があるため、WM_LBUTTONDBLCLKをcheckbox部分だけ抑止する。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmLButtonDblClk && ShouldSuppressCheckboxDoubleClick(this, m.LParam))
                return;
            base.WndProc(ref m);
        }
    }

    void PopulateDirectories(TreeNode parent, string fullPath)
    {
        try
        {
            FileAttributes rootAttributes = File.GetAttributes(fullPath);
            if ((rootAttributes & FileAttributes.Directory) == 0
                || (rootAttributes & FileAttributes.ReparsePoint) != 0)
                return;

            foreach (string childPath in Directory.EnumerateDirectories(fullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(_dataRoot, childPath));
                if (!LazyErbPolicy.IsSafeDirectoryPath(_dataRoot, _erbRoot, relativePath))
                    continue;

                TreeNode child = new(Path.GetFileName(childPath))
                {
                    Tag = childPath,
                    Checked = parent.Checked || _selectedDirectories.Contains(relativePath),
                };
                parent.Nodes.Add(child);
                PopulateDirectories(child, childPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    void TreeAfterCheck(object sender, TreeViewEventArgs e)
    {
        if (_updatingChecks)
            return;

        _updatingChecks = true;
        try
        {
            if (e.Node.Parent is null)
            {
                e.Node.Checked = false;
                return;
            }

            SetDescendantChecks(e.Node, e.Node.Checked);
            if (!e.Node.Checked)
            {
                for (TreeNode parent = e.Node.Parent; parent is not null; parent = parent.Parent)
                    parent.Checked = false;
            }
        }
        finally
        {
            _updatingChecks = false;
        }
    }

    static void SetDescendantChecks(TreeNode node, bool isChecked)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Checked = isChecked;
            SetDescendantChecks(child, isChecked);
        }
    }

    void CommitSelection()
    {
        List<string> selected = [];
        AddSelected(_tree.Nodes, selected);
        SelectedDirectories = NormalizeSelectedDirectories(selected);
        DialogResult = DialogResult.OK;
        Close();
    }

    void AddSelected(TreeNodeCollection nodes, List<string> selected)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Parent is not null && node.Checked && node.Tag is string fullPath)
            {
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(_dataRoot, fullPath));
                if (LazyErbPolicy.IsSafeDirectoryPath(_dataRoot, _erbRoot, relativePath))
                    selected.Add(relativePath);
            }
            AddSelected(node.Nodes, selected);
        }
    }

    static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Equals("erb", StringComparison.OrdinalIgnoreCase))
            return "ERB";
        return normalized.StartsWith("erb/", StringComparison.OrdinalIgnoreCase)
            ? "ERB" + normalized[3..]
            : normalized;
    }
}
