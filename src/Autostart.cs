using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CtrlCPulse
{
    /// <summary>
    /// 当前用户级开机自启：写 HKCU\...\Run，无需管理员权限。
    /// 注册表路径可注入，便于自检时在独立子键中验证而不污染真实自启项。
    /// </summary>
    internal static class Autostart
    {
        public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "CtrlCPulse";

        public static string ExecutablePath
        {
            get { return Application.ExecutablePath; }
        }

        public static string CommandLine
        {
            get { return "\"" + ExecutablePath + "\" --tray"; }
        }

        public static bool IsEnabled()
        {
            return IsEnabled(DefaultRunKey, ValueName);
        }

        public static void Enable()
        {
            Enable(DefaultRunKey, ValueName, CommandLine);
        }

        public static void Disable()
        {
            Disable(DefaultRunKey, ValueName);
        }

        internal static bool IsEnabled(string subKey, string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    var value = key.GetValue(valueName) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool Enable(string subKey, string valueName, string commandLine)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    key.SetValue(valueName, commandLine, RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool Disable(string subKey, string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    key.DeleteValue(valueName, false);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
