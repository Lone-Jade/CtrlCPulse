using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 托盘常驻主体：通知区域图标、右键菜单、剪贴板监听与设置持久化。
    /// 左键单击切换“暂停/恢复”，避免误操作，也不打开额外窗口。
    /// </summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly SettingsStore _settings;
        private readonly Theme _theme;
        private readonly ToastQueue _queue;
        private readonly ClipboardWatcher _watcher;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly Control _marshal;
        private readonly SingleInstance _instance;

        private ToolStripMenuItem _enableItem;
        private ToolStripMenuItem _secretItem;
        private ToolStripMenuItem _autostartItem;
        private bool _enabled;
        private bool _disposed;

        public TrayContext(SingleInstance instance)
        {
            _instance = instance;

            // 构造发生在 Application.Run 之前，此时 SynchronizationContext 尚不可用；
            // 用一个隐藏控件把后台线程（单实例通知）切回 UI 线程，保证卡片计时器正常工作。
            _marshal = new Control();
            _marshal.CreateControl();

            _settings = SettingsStore.Load();
            _enabled = _settings.Enabled;
            _theme = new Theme(Theme.DetectPrimaryScale());
            _queue = new ToastQueue(_theme, _settings.ToastMilliseconds, _settings.MaxVisible);
            _menu = BuildMenu();
            _tray = BuildTray();

            if (instance != null)
            {
                instance.Activated += OnSecondInstance;
            }

            _watcher = new ClipboardWatcher(OnClipboardChanged);

            if (!_settings.Welcomed)
            {
                _settings.Welcomed = true;
                _settings.Save();
                ShowToast(ToastContent.Welcome());
            }
        }

        private NotifyIcon BuildTray()
        {
            var tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = TooltipText(),
                Visible = true,
                ContextMenuStrip = _menu
            };
            tray.MouseClick += OnTrayClick;
            return tray;
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var header = new ToolStripMenuItem("CtrlCPulse · 复制提示") { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            _enableItem = new ToolStripMenuItem("启用复制提示") { Checked = _enabled };
            _enableItem.Click += delegate { ToggleEnabled(); };
            menu.Items.Add(_enableItem);

            _secretItem = new ToolStripMenuItem("忽略私密剪贴板内容") { Checked = _settings.IgnoreSecret };
            _secretItem.Click += OnToggleSecret;
            menu.Items.Add(_secretItem);

            _autostartItem = new ToolStripMenuItem("开机自动启动") { Checked = Autostart.IsEnabled() };
            _autostartItem.Click += OnToggleAutostart;
            menu.Items.Add(_autostartItem);

            menu.Items.Add(new ToolStripSeparator());

            var demo = new ToolStripMenuItem("预览提示样式");
            demo.DropDownItems.Add("文本", null, delegate { ShowToast(ToastContent.TextDemo()); });
            demo.DropDownItems.Add("图片", null, delegate { ShowToast(ToastContent.ImageDemo()); });
            demo.DropDownItems.Add("文件", null, delegate { ShowToast(ToastContent.FilesDemo()); });
            menu.Items.Add(demo);

            menu.Items.Add("关于 CtrlCPulse", null, delegate { ShowAbout(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApplication(); });

            return menu;
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleEnabled();
            }
        }

        private void ToggleEnabled()
        {
            _enabled = !_enabled;
            _settings.Enabled = _enabled;
            _settings.Save();
            _enableItem.Checked = _enabled;
            _tray.Text = TooltipText();

            ShowToast(_enabled
                ? ToastContent.Status("已启用复制提示", "按 Ctrl + C 就会在右下角提示", "托盘左键可再次暂停")
                : ToastContent.Status("已暂停复制提示", "剪贴板变化不再提示", "托盘左键可恢复"));
        }

        private void OnToggleSecret(object sender, EventArgs e)
        {
            _settings.IgnoreSecret = !_settings.IgnoreSecret;
            _settings.Save();
            _secretItem.Checked = _settings.IgnoreSecret;

            ShowToast(_settings.IgnoreSecret
                ? ToastContent.Status("已忽略私密剪贴板", "带“剪贴板查看器忽略”标记的内容不再提示", "密码管理器常用该标记")
                : ToastContent.Status("已提示全部剪贴板", "包括带忽略标记的私密内容", "可在托盘菜单中改回"));
        }

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            if (Autostart.IsEnabled())
            {
                Autostart.Disable();
            }
            else
            {
                Autostart.Enable();
            }

            bool enabled = Autostart.IsEnabled();
            _autostartItem.Checked = enabled;

            ShowToast(enabled
                ? ToastContent.Status("已开启开机自启", "下次登录 Windows 时自动运行", Autostart.CommandLine)
                : ToastContent.Status("已关闭开机自启", "下次登录需要手动启动", "随时可在托盘菜单恢复"));
        }

        private void OnClipboardChanged(uint sequence)
        {
            if (!_enabled)
            {
                return;
            }

            Snapshot snapshot = ClipboardInspector.Read(sequence, _settings.IgnoreSecret);
            ShowToast(snapshot);
        }

        private void ShowToast(Snapshot snapshot)
        {
            if (_disposed || snapshot == null)
            {
                return;
            }

            try
            {
                _queue.Push(snapshot);
            }
            catch (Exception ex)
            {
                snapshot.Dispose();
                CrashLog.Write(ex);
            }
        }

        private void OnSecondInstance()
        {
            try
            {
                _marshal.BeginInvoke((MethodInvoker)delegate
                {
                    ShowToast(ToastContent.Status(
                        "CtrlCPulse 已在运行",
                        "提示卡片就在屏幕右下角",
                        "托盘左键暂停，右键更多"));
                });
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        }

        private void ShowAbout()
        {
            string version = typeof(TrayContext).Assembly.GetName().Version.ToString(3);
            string text =
                "按下 Ctrl + C 后，在屏幕右下角提示复制成功。" + Environment.NewLine +
                "支持文本、图片、文件三类内容。" + Environment.NewLine + Environment.NewLine +
                "托盘左键：暂停或恢复提示" + Environment.NewLine +
                "开机自启：当前用户注册表 Run 项，无需管理员权限" + Environment.NewLine + Environment.NewLine +
                "版本 " + version + " · 本地原创实现，不联网、不记录剪贴板内容";
            MessageBox.Show(text, "关于 CtrlCPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string TooltipText()
        {
            // NotifyIcon.Text 上限 63 个字符。
            return _enabled ? "CtrlCPulse · 已启用" : "CtrlCPulse · 已暂停";
        }

        private static Icon LoadIcon()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("CtrlCPulse.app.ico"))
                {
                    if (stream != null)
                    {
                        return new Icon(stream, SystemInformation.SmallIconSize);
                    }
                }
            }
            catch (Exception)
            {
            }

            return SystemIcons.Application;
        }

        private void ExitApplication()
        {
            DisposeResources();
            ExitThread();
        }

        private void DisposeResources()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_instance != null)
            {
                _instance.Activated -= OnSecondInstance;
            }

            if (_watcher != null)
            {
                _watcher.Dispose();
            }

            if (_queue != null)
            {
                _queue.Dispose();
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }

            if (_menu != null)
            {
                _menu.Dispose();
            }

            if (_theme != null)
            {
                _theme.Dispose();
            }

            if (_marshal != null)
            {
                _marshal.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeResources();
            }

            base.Dispose(disposing);
        }
    }
}
