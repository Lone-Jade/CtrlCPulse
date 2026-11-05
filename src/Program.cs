using System;
using System.Threading;
using System.Windows.Forms;

namespace CtrlCPulse
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (HasFlag(args, "--reset"))
            {
                SettingsStore.Reset();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                CrashLog.Write(e.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                CrashLog.Write(e.ExceptionObject as Exception);
            };

            try
            {
                using (var instance = new SingleInstance())
                {
                    if (!instance.IsOwner)
                    {
                        instance.SignalExisting();
                        return;
                    }

                    if (HasFlag(args, "--demo"))
                    {
                        using (var demo = new DemoRunner())
                        {
                            Application.Run(demo);
                        }

                        return;
                    }

                    using (var context = new TrayContext(instance))
                    {
                        Application.Run(context);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show(
                    "CtrlCPulse 启动失败：" + Environment.NewLine + ex.Message,
                    "CtrlCPulse",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
