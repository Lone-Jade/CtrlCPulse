using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 自检入口（编译为独立的 CtrlCPulse.SelfTest.exe）。
    /// 只验证纯逻辑与资源，不读写真实剪贴板，避免干扰用户正在复制的内容。
    /// 退出码 0 表示全部通过。
    /// </summary>
    internal static class TestMain
    {
        private static int _passed;
        private static int _failed;

        [STAThread]
        private static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
            }

            Console.WriteLine("CtrlCPulse 自检");
            Console.WriteLine("----------------");

            Run("文本快照 - 单行", TextSingleLine);
            Run("文本快照 - 多行统计", TextMultipleLines);
            Run("文本快照 - 空白折叠", TextWhitespaceCollapse);
            Run("文本快照 - 超长截断", TextTruncation);
            Run("文本快照 - 空白内容忽略", TextBlankIgnored);
            Run("文本快照 - 不切断代理对", TextSurrogateSafe);
            Run("文件快照 - 数量与体积", FilesSnapshot);
            Run("文件快照 - 多文件省略", FilesNameList);
            Run("文件快照 - 空输入", FilesEmpty);
            Run("图片快照 - 等比缩略图", ImageThumbnail);
            Run("图片快照 - 小图不放大", ImageNoUpscale);
            Run("图片快照 - 竖图高度受限", ImageTallThumbnail);
            Run("图片快照 - 空输入", ImageNull);
            Run("体积格式化", SizeFormatting);
            Run("设置读写往返", SettingsRoundtrip);
            Run("设置损坏回落", SettingsCorruptFallback);
            Run("设置范围钳制", SettingsClamp);
            Run("开机自启注册表", AutostartRegistry);
            Run("序列号去重", SequenceDedupe);
            Run("主题 DPI 缩放", ThemeScaling);
            Run("图标资源", IconAsset);
            Run("私密剪贴板标记", SecretFormatDetection);
            Run("类型标签", KindLabels);
            Run("快照释放", SnapshotDispose);
            Run("截断边界", TruncateBoundary);

            Console.WriteLine();
            Console.WriteLine("结果: " + _passed + " 通过, " + _failed + " 失败");
            return _failed == 0 ? 0 : 1;
        }

        // ---------------- 文本 ----------------

        private static void TextSingleLine()
        {
            using (Snapshot snapshot = ClipboardInspector.FromText("你好，世界", 7))
            {
                Assert(snapshot != null, "快照不应为空");
                Assert(snapshot.Kind == CopyKind.Text, "类型应为文本");
                Assert(snapshot.Title == "已复制", "标题应为“已复制”");
                Assert(snapshot.Detail == "你好，世界", "摘要应保留原文");
                Assert(snapshot.Meta.Contains("5"), "应统计 5 个字: " + snapshot.Meta);
                Assert(snapshot.Sequence == 7, "序列号应透传");
            }
        }

        private static void TextMultipleLines()
        {
            using (Snapshot snapshot = ClipboardInspector.FromText("第一行\n第二行\n第三行", 0))
            {
                Assert(snapshot.Meta.Contains("3 行"), "应统计 3 行: " + snapshot.Meta);
                Assert(snapshot.Detail == "第一行 第二行 第三行", "多行应折叠成一行: " + snapshot.Detail);
            }
        }

        private static void TextWhitespaceCollapse()
        {
            using (Snapshot snapshot = ClipboardInspector.FromText("  a\t\tb\r\nc  ", 0))
            {
                Assert(snapshot.Detail == "a b c", "空白应折叠: [" + snapshot.Detail + "]");
            }
        }

        private static void TextTruncation()
        {
            string text = new string('长', 300);
            using (Snapshot snapshot = ClipboardInspector.FromText(text, 0))
            {
                Assert(snapshot.Detail.Length <= 81, "摘要应截断到 81 字符以内: " + snapshot.Detail.Length);
                Assert(snapshot.Detail.EndsWith("…", StringComparison.Ordinal), "截断后应带省略号");
                Assert(snapshot.Meta.Contains("300"), "字数应按原文统计: " + snapshot.Meta);
            }
        }

        private static void TextBlankIgnored()
        {
            Assert(ClipboardInspector.FromText("   \r\n\t ", 0) == null, "纯空白不应提示");
            Assert(ClipboardInspector.FromText(string.Empty, 0) == null, "空串不应提示");
            Assert(ClipboardInspector.FromText(null, 0) == null, "null 不应提示");
        }

        private static void TextSurrogateSafe()
        {
            string text = new string('a', 79) + "\U0001F600" + new string('b', 40);
            using (Snapshot snapshot = ClipboardInspector.FromText(text, 0))
            {
                string detail = snapshot.Detail;
                Assert(detail.Length <= 81, "截断长度异常: " + detail.Length);
                for (int i = 0; i < detail.Length; i++)
                {
                    if (char.IsHighSurrogate(detail[i]))
                    {
                        Assert(i + 1 < detail.Length && char.IsLowSurrogate(detail[i + 1]), "代理对不应被切断");
                    }
                }
            }
        }

        // ---------------- 文件 ----------------

        private static void FilesSnapshot()
        {
            string directory = CreateTempDirectory();
            try
            {
                string pdf = Path.Combine(directory, "报告.pdf");
                string png = Path.Combine(directory, "照片.png");
                File.WriteAllBytes(pdf, new byte[2048]);
                File.WriteAllBytes(png, new byte[1024]);

                using (Snapshot snapshot = ClipboardInspector.FromFiles(new[] { pdf, png }, 3))
                {
                    Assert(snapshot.Kind == CopyKind.Files, "类型应为文件");
                    Assert(snapshot.Meta.Contains("2 个文件"), "应统计文件数: " + snapshot.Meta);
                    Assert(snapshot.Meta.Contains("3 KB"), "应统计总体积: " + snapshot.Meta);
                    Assert(snapshot.Detail.Contains("报告.pdf"), "应包含第一个文件名: " + snapshot.Detail);
                    Assert(snapshot.Detail.Contains("照片.png"), "应包含第二个文件名: " + snapshot.Detail);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void FilesNameList()
        {
            string directory = CreateTempDirectory();
            try
            {
                var paths = new string[4];
                for (int i = 0; i < paths.Length; i++)
                {
                    paths[i] = Path.Combine(directory, "文件" + i + ".txt");
                    File.WriteAllText(paths[i], "x");
                }

                using (Snapshot snapshot = ClipboardInspector.FromFiles(paths, 0))
                {
                    Assert(snapshot.Detail.Contains("等 4 项"), "超出 2 个应省略: " + snapshot.Detail);
                    Assert(!snapshot.Detail.Contains("文件2.txt"), "只应列出前两个文件名");
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void FilesEmpty()
        {
            Assert(ClipboardInspector.FromFiles(new string[0], 0) == null, "空文件列表不应提示");
            Assert(ClipboardInspector.FromFiles(null, 0) == null, "null 文件列表不应提示");
        }

        // ---------------- 图片 ----------------

        private static void ImageThumbnail()
        {
            using (var source = new Bitmap(400, 200))
            using (Snapshot snapshot = ClipboardInspector.FromImage(source, 0))
            {
                Assert(snapshot.Kind == CopyKind.Image, "类型应为图片");
                Assert(snapshot.Thumbnail != null, "应生成缩略图");
                Assert(snapshot.Thumbnail.Width <= 184 && snapshot.Thumbnail.Height <= 116, "缩略图超出上限");
                double ratio = (double)snapshot.Thumbnail.Width / snapshot.Thumbnail.Height;
                Assert(Math.Abs(ratio - 2.0) < 0.05, "应保持 2:1 比例: " + ratio);
                Assert(snapshot.Meta.Contains("400"), "应显示原始像素: " + snapshot.Meta);
            }
        }

        private static void ImageNoUpscale()
        {
            using (var source = new Bitmap(40, 20))
            using (Snapshot snapshot = ClipboardInspector.FromImage(source, 0))
            {
                Assert(snapshot.Thumbnail.Width == 40 && snapshot.Thumbnail.Height == 20, "小图不应放大");
            }
        }

        private static void ImageTallThumbnail()
        {
            using (var source = new Bitmap(100, 400))
            using (Snapshot snapshot = ClipboardInspector.FromImage(source, 0))
            {
                Assert(snapshot.Thumbnail.Height == 116, "竖图应受高度限制: " + snapshot.Thumbnail.Height);
                Assert(snapshot.Thumbnail.Width == 29, "宽度应等比缩放: " + snapshot.Thumbnail.Width);
            }
        }

        private static void ImageNull()
        {
            Assert(ClipboardInspector.FromImage(null, 0) == null, "null 图片不应提示");
        }

        // ---------------- 通用 ----------------

        private static void SizeFormatting()
        {
            Assert(ClipboardInspector.FormatSize(512) == "512 B", "字节格式: " + ClipboardInspector.FormatSize(512));
            Assert(ClipboardInspector.FormatSize(2048) == "2 KB", "KB 格式: " + ClipboardInspector.FormatSize(2048));
            Assert(ClipboardInspector.FormatSize(5L * 1024 * 1024) == "5 MB", "MB 格式: " + ClipboardInspector.FormatSize(5L * 1024 * 1024));
        }

        private static void SettingsRoundtrip()
        {
            string directory = CreateTempDirectory();
            try
            {
                string path = Path.Combine(directory, "settings.ini");
                var store = new SettingsStore(path);
                store.Enabled = false;
                store.IgnoreSecret = false;
                store.MaxVisible = 2;
                store.ToastMilliseconds = 1500;
                store.Welcomed = true;
                store.Save();

                SettingsStore loaded = SettingsStore.Load(path);
                Assert(!loaded.Enabled, "enabled 应保持 false");
                Assert(!loaded.IgnoreSecret, "ignoreSecret 应保持 false");
                Assert(loaded.MaxVisible == 2, "maxVisible 应保持 2: " + loaded.MaxVisible);
                Assert(loaded.ToastMilliseconds == 1500, "toastMilliseconds 应保持 1500: " + loaded.ToastMilliseconds);
                Assert(loaded.Welcomed, "welcomed 应保持 true");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void SettingsCorruptFallback()
        {
            string directory = CreateTempDirectory();
            try
            {
                string path = Path.Combine(directory, "settings.ini");
                File.WriteAllText(path, "这不是配置\nmaxVisible=abc\nenabled=maybe\nignoreSecret=\n");

                SettingsStore loaded = SettingsStore.Load(path);
                Assert(loaded.Enabled, "非法布尔值应回落 true");
                Assert(loaded.MaxVisible == 3, "非法数字应回落 3: " + loaded.MaxVisible);
                Assert(loaded.IgnoreSecret, "空值应回落 true");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void SettingsClamp()
        {
            string directory = CreateTempDirectory();
            try
            {
                string path = Path.Combine(directory, "settings.ini");
                File.WriteAllText(path, "maxVisible=99\ntoastMilliseconds=10\n");

                SettingsStore loaded = SettingsStore.Load(path);
                Assert(loaded.MaxVisible == 5, "maxVisible 上限应为 5: " + loaded.MaxVisible);
                Assert(loaded.ToastMilliseconds == 800, "toastMilliseconds 下限应为 800: " + loaded.ToastMilliseconds);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void AutostartRegistry()
        {
            const string subKey = @"Software\CtrlCPulse.SelfTest\Run";
            const string valueName = "CtrlCPulseTest";

            try
            {
                Assert(!Autostart.IsEnabled(subKey, valueName), "初始应为未启用");
                Assert(Autostart.Enable(subKey, valueName, "\"C:\\fake\\CtrlCPulse.exe\" --tray"), "写入应成功");
                Assert(Autostart.IsEnabled(subKey, valueName), "写入后应可读");
                Assert(Autostart.Disable(subKey, valueName), "删除应成功");
                Assert(!Autostart.IsEnabled(subKey, valueName), "删除后应为未启用");
            }
            finally
            {
                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\CtrlCPulse.SelfTest", false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void SequenceDedupe()
        {
            Assert(!ClipboardWatcher.ShouldNotify(0, 0), "初始序列号不应提示");
            Assert(!ClipboardWatcher.ShouldNotify(5, 5), "序列号未变不应提示");
            Assert(ClipboardWatcher.ShouldNotify(5, 6), "序列号变化应提示");
            Assert(ClipboardWatcher.ShouldNotify(0, 3), "从 0 开始应提示");
        }

        private static void ThemeScaling()
        {
            using (var theme = new Theme(1.5f))
            {
                Assert(theme.CardWidth == 516, "344 * 1.5 = 516，实际 " + theme.CardWidth);
                Assert(theme.CardHeight(CopyKind.Text) == 126, "文本卡片高度应为 126");
                Assert(theme.CardHeight(CopyKind.Files) == 144, "文件卡片高度应为 144");
                Assert(theme.CardHeight(CopyKind.Image) == 162, "图片卡片高度应为 162");
                Assert(theme.Px(10) == 15, "Px(10) 应为 15");
                Assert(theme.Radius == 18, "圆角应为 18");
            }
        }

        private static void IconAsset()
        {
            string path = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "assets", "app.ico"));

            Assert(File.Exists(path), "找不到图标文件: " + path);
            Assert(new FileInfo(path).Length > 1024, "图标文件过小，可能未正确生成");

            using (var icon = new Icon(path))
            {
                Assert(icon.Width >= 16 && icon.Height >= 16, "图标尺寸异常: " + icon.Width + "x" + icon.Height);
            }
        }

        private static void SecretFormatDetection()
        {
            var secret = new DataObject();
            secret.SetData(ClipboardInspector.SecretFormat, true);
            Assert(ClipboardInspector.HasFormat(secret, ClipboardInspector.SecretFormat), "应识别“剪贴板查看器忽略”标记");

            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, "hello");
            Assert(!ClipboardInspector.HasFormat(plain, ClipboardInspector.SecretFormat), "普通内容不应误判");
            Assert(ClipboardInspector.HasFormat(plain, DataFormats.UnicodeText), "普通文本应可识别");
        }

        private static void KindLabels()
        {
            Assert(new Snapshot { Kind = CopyKind.Text }.KindLabel == "文本", "文本标签错误");
            Assert(new Snapshot { Kind = CopyKind.Image }.KindLabel == "图片", "图片标签错误");
            Assert(new Snapshot { Kind = CopyKind.Files }.KindLabel == "文件", "文件标签错误");
        }

        private static void SnapshotDispose()
        {
            Snapshot snapshot = ClipboardInspector.FromImage(new Bitmap(10, 10), 0);
            Assert(snapshot.Thumbnail != null, "应先生成缩略图");
            snapshot.Dispose();
            Assert(snapshot.Thumbnail == null, "释放后应清空缩略图");
        }

        private static void TruncateBoundary()
        {
            Assert(ClipboardInspector.Truncate("abcdef", 3) == "abc…", "应截断并加省略号");
            Assert(ClipboardInspector.Truncate("abc", 3) == "abc", "未超长不应改动");
            Assert(ClipboardInspector.Truncate(string.Empty, 3) == string.Empty, "空串应原样返回");
        }

        // ---------------- 基础设施 ----------------

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name);
                Console.WriteLine("      " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CtrlCPulseTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
