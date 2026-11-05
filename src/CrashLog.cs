using System;
using System.IO;

namespace CtrlCPulse
{
    /// <summary>
    /// 崩溃日志：只写本地文件，不上报。
    /// 应用是托盘常驻程序，出错后尽量保住主流程，不让用户看到弹窗。
    /// </summary>
    internal static class CrashLog
    {
        public static void Write(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                string path = Path.Combine(SettingsStore.DefaultDirectory, "error.log");
                string entry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                               exception + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(path, entry);
            }
            catch (Exception)
            {
                // 日志写不进去也不能让程序挂掉。
            }
        }
    }
}
