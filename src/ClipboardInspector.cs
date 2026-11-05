using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 读取剪贴板并生成 <see cref="Snapshot"/>。
    /// 判定优先级：文件 &gt; 图片 &gt; 文本；表格/文档类会同时提供文本与位图，此时按文本处理。
    /// 所有格式化逻辑都拆成纯函数，便于自检时不触碰真实剪贴板。
    /// </summary>
    internal static class ClipboardInspector
    {
        public const string SecretFormat = "Clipboard Viewer Ignore";
        private const string HtmlFormat = "HTML Format";
        private const string RtfFormat = "Rich Text Format";
        private const string PngFormat = "PNG";
        private const int MaxPreviewChars = 80;
        private const int MaxTextChars = 200000;
        private const int ThumbMaxWidth = 184;
        private const int ThumbMaxHeight = 116;

        /// <summary>
        /// 读取当前剪贴板。剪贴板被其它程序占用时会短暂重试，仍失败则返回 null（静默跳过）。
        /// </summary>
        public static Snapshot Read(uint sequence, bool ignoreSecret)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    return ReadOnce(sequence, ignoreSecret);
                }
                catch (ExternalException)
                {
                    // CLIPBRD_E_CANT_OPEN：另一个进程正在改写剪贴板，稍后重试。
                    // COMException 也是 ExternalException 的子类，无需单独捕获。
                }

                if (attempt < 5)
                {
                    Thread.Sleep(45);
                }
            }

            return null;
        }

        private static Snapshot ReadOnce(uint sequence, bool ignoreSecret)
        {
            IDataObject data = Clipboard.GetDataObject();
            if (data == null)
            {
                return null;
            }

            // 密码管理器等会打上“剪贴板查看器忽略”标记，尊重该约定。
            if (ignoreSecret && HasFormat(data, SecretFormat))
            {
                return null;
            }

            bool hasFiles = data.GetDataPresent(DataFormats.FileDrop);
            bool hasBitmap = data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(PngFormat);
            bool hasText = data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
            bool hasRichText = data.GetDataPresent(HtmlFormat) || data.GetDataPresent(RtfFormat);

            if (hasFiles)
            {
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    return FromFiles(files, sequence);
                }
            }

            // 表格、Word 等复制时同时带文本与位图，文本信息量更大。
            if (hasText && (hasRichText || !hasBitmap))
            {
                string text = GetText(data);
                if (!string.IsNullOrEmpty(text))
                {
                    return FromText(text, sequence);
                }
            }

            if (hasBitmap)
            {
                using (Image image = GetImage(data))
                {
                    if (image != null)
                    {
                        return FromImage(image, sequence);
                    }
                }
            }

            if (hasText)
            {
                string text = GetText(data);
                if (!string.IsNullOrEmpty(text))
                {
                    return FromText(text, sequence);
                }
            }

            return null;
        }

        private static string GetText(IDataObject data)
        {
            try
            {
                var unicode = data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(unicode))
                {
                    return unicode;
                }
            }
            catch (ExternalException)
            {
            }

            try
            {
                return data.GetData(DataFormats.Text) as string;
            }
            catch (ExternalException)
            {
                return null;
            }
        }

        private static Image GetImage(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    var image = data.GetData(DataFormats.Bitmap) as Image;
                    if (image != null)
                    {
                        return image;
                    }
                }
            }
            catch (ExternalException)
            {
            }

            try
            {
                if (data.GetDataPresent(PngFormat))
                {
                    var stream = data.GetData(PngFormat) as MemoryStream;
                    if (stream != null)
                    {
                        stream.Position = 0;
                        using (var decoded = Image.FromStream(stream))
                        {
                            return new Bitmap(decoded);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        internal static bool HasFormat(IDataObject data, string format)
        {
            if (data == null || string.IsNullOrEmpty(format))
            {
                return false;
            }

            try
            {
                return data.GetDataPresent(format, true);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        // ---------------- 纯函数：快照构造 ----------------

        internal static Snapshot FromText(string text, uint sequence)
        {
            if (text == null)
            {
                return null;
            }

            if (text.Length > MaxTextChars)
            {
                text = text.Substring(0, MaxTextChars);
            }

            if (text.Trim().Length == 0)
            {
                return null;
            }

            int lineCount = CountLines(text);
            string preview = BuildPreview(text);

            return new Snapshot
            {
                Kind = CopyKind.Text,
                Title = "已复制",
                Detail = preview,
                Meta = lineCount > 1
                    ? lineCount.ToString(CultureInfo.InvariantCulture) + " 行 · " + FormatCount(text.Length) + " 字"
                    : FormatCount(text.Length) + " 字",
                Sequence = sequence
            };
        }

        internal static Snapshot FromFiles(string[] paths, uint sequence)
        {
            if (paths == null || paths.Length == 0)
            {
                return null;
            }

            int count = paths.Length;
            long totalBytes = 0;
            bool sizeComplete = true;
            int probe = Math.Min(count, 100);

            for (int i = 0; i < probe; i++)
            {
                try
                {
                    var info = new FileInfo(paths[i]);
                    if (info.Exists)
                    {
                        totalBytes += info.Length;
                    }
                    else
                    {
                        sizeComplete = false;
                    }
                }
                catch (Exception)
                {
                    sizeComplete = false;
                }
            }

            if (count > probe)
            {
                sizeComplete = false;
            }

            string meta = count.ToString(CultureInfo.InvariantCulture) + " 个文件";
            if (sizeComplete && totalBytes > 0)
            {
                meta += " · " + FormatSize(totalBytes);
            }

            return new Snapshot
            {
                Kind = CopyKind.Files,
                Title = "已复制",
                Detail = BuildNameList(paths, 2),
                Meta = meta,
                Sequence = sequence
            };
        }

        internal static Snapshot FromImage(Image image, uint sequence)
        {
            if (image == null)
            {
                return null;
            }

            int width = image.Width;
            int height = image.Height;
            Bitmap thumbnail = MakeThumbnail(image, ThumbMaxWidth, ThumbMaxHeight);

            return new Snapshot
            {
                Kind = CopyKind.Image,
                Title = "已复制",
                Detail = "图片已放入剪贴板",
                Meta = width.ToString(CultureInfo.InvariantCulture) + " × " +
                       height.ToString(CultureInfo.InvariantCulture) + " 像素",
                Thumbnail = thumbnail,
                Sequence = sequence
            };
        }

        /// <summary>等比缩略图，小图不放大。</summary>
        internal static Bitmap MakeThumbnail(Image source, int maxWidth, int maxHeight)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return null;
            }

            float scale = Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height);
            if (scale > 1f)
            {
                scale = 1f;
            }

            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(thumbnail))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }

            return thumbnail;
        }

        internal static string BuildPreview(string text)
        {
            var builder = new StringBuilder(Math.Min(text.Length, MaxPreviewChars * 2));
            bool lastWasSpace = false;

            foreach (char c in text)
            {
                if (c == '\r')
                {
                    continue;
                }

                bool isSpace = c == '\n' || c == '\t' || char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!lastWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }

                    continue;
                }

                builder.Append(c);
                lastWasSpace = false;
                if (builder.Length > MaxPreviewChars + 8)
                {
                    break;
                }
            }

            return Truncate(builder.ToString().TrimEnd(), MaxPreviewChars);
        }

        /// <summary>按字符数截断，避免把代理对（emoji）从中间切开。</summary>
        internal static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value;
            }

            int cut = max;
            if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
            {
                cut--;
            }

            return value.Substring(0, cut) + "…";
        }

        internal static int CountLines(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        internal static string BuildNameList(string[] paths, int maxNames)
        {
            var parts = new List<string>();
            int take = Math.Min(paths.Length, maxNames);

            for (int i = 0; i < take; i++)
            {
                string path = paths[i];
                string name;

                try
                {
                    string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    name = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = trimmed;
                    }
                }
                catch (Exception)
                {
                    name = path;
                }

                if (Directory.Exists(path))
                {
                    name += "\\";
                }

                parts.Add(name);
            }

            string joined = string.Join("、", parts.ToArray());
            if (paths.Length > maxNames)
            {
                joined += " 等 " + paths.Length.ToString(CultureInfo.InvariantCulture) + " 项";
            }

            return joined;
        }

        internal static string FormatCount(int value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }

            double kb = bytes / 1024.0;
            if (kb < 1024)
            {
                return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            }

            double mb = kb / 1024.0;
            if (mb < 1024)
            {
                return mb.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            }

            double gb = mb / 1024.0;
            return gb.ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
