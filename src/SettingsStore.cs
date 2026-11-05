using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CtrlCPulse
{
    /// <summary>
    /// 极简 INI 配置：键值对 + 注释，损坏时整份回落到默认值，
    /// 不引入 JSON 序列化依赖，也方便用户手工检查。
    /// </summary>
    internal sealed class SettingsStore
    {
        private const string FileName = "settings.ini";

        private readonly string _path;

        public SettingsStore(string path)
        {
            _path = path;
            Enabled = true;
            IgnoreSecret = true;
            MaxVisible = 3;
            ToastMilliseconds = 2400;
            Welcomed = false;
        }

        public bool Enabled { get; set; }

        /// <summary>剪贴板带 “Clipboard Viewer Ignore” 标记时是否跳过提示。</summary>
        public bool IgnoreSecret { get; set; }

        public int MaxVisible { get; set; }

        public int ToastMilliseconds { get; set; }

        public bool Welcomed { get; set; }

        /// <summary>
        /// 配置与日志目录：跟随 exe 所在目录，保持便携。
        /// 程序安装在非系统盘时，不会向 C 盘写任何东西。
        /// </summary>
        public static string DefaultDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DefaultPath
        {
            get { return Path.Combine(DefaultDirectory, FileName); }
        }

        public static SettingsStore Load()
        {
            return Load(DefaultPath);
        }

        public static SettingsStore Load(string path)
        {
            var settings = new SettingsStore(path);
            try
            {
                if (!File.Exists(path))
                {
                    return settings;
                }

                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) ||
                        trimmed.StartsWith(";", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }

                    string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = trimmed.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "enabled":
                            settings.Enabled = ParseBool(value, true);
                            break;
                        case "ignoresecret":
                            settings.IgnoreSecret = ParseBool(value, true);
                            break;
                        case "maxvisible":
                            settings.MaxVisible = Clamp(ParseInt(value, 3), 1, 5);
                            break;
                        case "toastmilliseconds":
                            settings.ToastMilliseconds = Clamp(ParseInt(value, 2400), 800, 10000);
                            break;
                        case "welcomed":
                            settings.Welcomed = ParseBool(value, false);
                            break;
                    }
                }
            }
            catch (IOException)
            {
                // 配置文件读不了就用默认值，不影响主流程。
            }
            catch (UnauthorizedAccessException)
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var lines = new List<string>
                {
                    "# CtrlCPulse 设置（修改后重启应用生效）",
                    "enabled=" + (Enabled ? "true" : "false"),
                    "ignoreSecret=" + (IgnoreSecret ? "true" : "false"),
                    "maxVisible=" + MaxVisible.ToString(CultureInfo.InvariantCulture),
                    "toastMilliseconds=" + ToastMilliseconds.ToString(CultureInfo.InvariantCulture),
                    "welcomed=" + (Welcomed ? "true" : "false")
                };
                File.WriteAllLines(_path, lines);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static void Reset()
        {
            try
            {
                if (File.Exists(DefaultPath))
                {
                    File.Delete(DefaultPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return fallback;
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
