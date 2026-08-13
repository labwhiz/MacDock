using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacDockSetup
{
    internal class SetupForm : Form
    {
        private TextBox txtPath;
        private Button btnBrowse;
        private CheckBox chkShortcut;
        private CheckBox chkLaunch;
        private Button btnInstall;
        private Button btnUninstall;
        private ProgressBar progress;
        private Label lblStatus;
        private bool busy;

        public SetupForm()
        {
            Text = AppInfo.DisplayName + " 安装程序";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(470, 330);
            BackColor = Color.White;
            BuildControls();
            Shown += delegate { RefreshState(); };
        }

        private void BuildControls()
        {
            Label lblTitle = new Label();
            lblTitle.Text = "MacDock 安装程序";
            lblTitle.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(30, 30, 30);
            lblTitle.Location = new Point(20, 14);
            lblTitle.AutoSize = true;
            Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "轻量 Win11 桌面美化 · 仿 macOS Dock 栏";
            lblSub.ForeColor = Color.FromArgb(120, 120, 120);
            lblSub.Location = new Point(22, 48);
            lblSub.AutoSize = true;
            Controls.Add(lblSub);

            Label lblPath = new Label();
            lblPath.Text = "安装位置：";
            lblPath.Location = new Point(20, 86);
            lblPath.AutoSize = true;
            Controls.Add(lblPath);

            txtPath = new TextBox();
            txtPath.Text = Installer.DefaultInstallDir();
            txtPath.Location = new Point(20, 106);
            txtPath.Size = new Size(330, 24);
            txtPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(txtPath);

            btnBrowse = new Button();
            btnBrowse.Text = "浏览…";
            btnBrowse.Location = new Point(356, 104);
            btnBrowse.Size = new Size(94, 28);
            btnBrowse.Click += OnBrowseClick;
            Controls.Add(btnBrowse);

            chkShortcut = new CheckBox();
            chkShortcut.Text = "创建桌面快捷方式";
            chkShortcut.Checked = true;
            chkShortcut.Location = new Point(20, 144);
            chkShortcut.AutoSize = true;
            Controls.Add(chkShortcut);

            chkLaunch = new CheckBox();
            chkLaunch.Text = "安装完成后立即启动 MacDock";
            chkLaunch.Checked = true;
            chkLaunch.Location = new Point(20, 172);
            chkLaunch.AutoSize = true;
            Controls.Add(chkLaunch);

            btnInstall = new Button();
            btnInstall.Text = "安  装";
            btnInstall.Location = new Point(20, 212);
            btnInstall.Size = new Size(110, 32);
            btnInstall.BackColor = Color.FromArgb(37, 99, 235);
            btnInstall.ForeColor = Color.White;
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += OnInstallClick;
            Controls.Add(btnInstall);

            btnUninstall = new Button();
            btnUninstall.Text = "卸  载";
            btnUninstall.Location = new Point(140, 212);
            btnUninstall.Size = new Size(110, 32);
            btnUninstall.FlatStyle = FlatStyle.Flat;
            btnUninstall.Enabled = false;
            btnUninstall.Click += OnUninstallClick;
            Controls.Add(btnUninstall);

            progress = new ProgressBar();
            progress.Location = new Point(20, 256);
            progress.Size = new Size(430, 14);
            progress.Minimum = 0;
            progress.Maximum = 100;
            Controls.Add(progress);

            lblStatus = new Label();
            lblStatus.Text = "就绪";
            lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
            lblStatus.Location = new Point(20, 278);
            lblStatus.AutoSize = true;
            Controls.Add(lblStatus);

            Label lblFooter = new Label();
            lblFooter.Text = "版本 " + AppInfo.Version + " · 需要 Windows 10/11（内置 .NET Framework 4.8）";
            lblFooter.ForeColor = Color.FromArgb(160, 160, 160);
            lblFooter.Location = new Point(20, 304);
            lblFooter.AutoSize = true;
            Controls.Add(lblFooter);
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 MacDock 的安装位置";
                string cur = txtPath.Text.Trim();
                try { if (Directory.Exists(cur)) dlg.SelectedPath = cur; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtPath.Text = dlg.SelectedPath;
                }
            }
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            if (busy) return;
            string err;
            string dir = Installer.NormalizePath(txtPath.Text, out err);
            if (dir == null)
            {
                MessageBox.Show(this, err, "安装位置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0)
                {
                    DialogResult r = MessageBox.Show(this,
                        "该目录已存在文件，将覆盖其中的 MacDock 文件，是否继续？\n\n" + dir,
                        "确认安装", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                }
            }
            catch { }

            if (!Installer.DotNetFramework48Present())
            {
                DialogResult r = MessageBox.Show(this,
                    "未检测到 .NET Framework 4.8，安装后 MacDock 可能无法启动。\n" +
                    "Win10/11 均内置 .NET Framework 4.8，请先更新 Windows，是否仍然继续安装？",
                    "缺少 .NET Framework", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            busy = true;
            SetBusy(true);
            lblStatus.Text = "正在安装…";
            progress.Value = 0;
            try
            {
                bool ok = Installer.Install(dir, chkShortcut.Checked, out err,
                    delegate (string msg, int pct)
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke(new Action(delegate
                            {
                                lblStatus.Text = msg;
                                progress.Value = Math.Max(0, Math.Min(pct, progress.Maximum));
                            }));
                        }
                    }, true);
                if (ok)
                {
                    lblStatus.Text = "安装完成。";
                    progress.Value = progress.Maximum;
                    MessageBox.Show(this,
                        "MacDock 安装完成！\n\n安装位置：\n" + dir,
                        "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    if (chkLaunch.Checked)
                    {
                        try
                        {
                            Process.Start(Path.Combine(dir, "MacDock.exe"));
                        }
                        catch { }
                    }
                }
                else
                {
                    MessageBox.Show(this, "安装失败：" + err, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                busy = false;
                SetBusy(false);
                RefreshState();
            }
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            if (busy) return;
            string dir;
            if (!Installer.IsInstalled(out dir))
            {
                MessageBox.Show(this, "未找到已安装的 MacDock。", "卸载", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshState();
                return;
            }
            DialogResult r = MessageBox.Show(this,
                "确定要卸载 MacDock 吗？\n\n位置：\n" + dir,
                "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            busy = true;
            SetBusy(true);
            lblStatus.Text = "正在卸载…";
            try
            {
                string err;
                bool ok = Installer.Uninstall(dir, out err, true);
                if (ok)
                {
                    lblStatus.Text = "已卸载。";
                    MessageBox.Show(this, "MacDock 已卸载。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, "卸载失败：" + err, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                busy = false;
                SetBusy(false);
                RefreshState();
            }
        }

        private void RefreshState()
        {
            string dir;
            bool installed = Installer.IsInstalled(out dir);
            btnUninstall.Enabled = installed && !busy;
            if (installed && !busy && string.IsNullOrWhiteSpace(txtPath.Text))
            {
                txtPath.Text = dir;
            }
            if (!busy && lblStatus.Text != "安装完成。" && lblStatus.Text != "已卸载。")
            {
                lblStatus.Text = installed ? "已检测到已安装版本，可覆盖安装或卸载。" : "就绪";
            }
        }

        private void SetBusy(bool value)
        {
            btnInstall.Enabled = !value;
            btnBrowse.Enabled = !value;
            txtPath.Enabled = !value;
            chkShortcut.Enabled = !value;
            chkLaunch.Enabled = !value;
            Cursor = value ? Cursors.WaitCursor : Cursors.Default;
        }
    }
}
