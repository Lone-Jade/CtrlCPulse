using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CtrlCPulse
{
    /// <summary>
    /// 内置的示例/状态内容，用于首次运行欢迎、托盘状态反馈与“预览提示样式”菜单。
    /// </summary>
    internal static class ToastContent
    {
        public static Snapshot Welcome()
        {
            return Status(
                "已就绪",
                "按 Ctrl + C 复制文字、图片或文件试试",
                "提示卡片会出现在屏幕右下角");
        }

        public static Snapshot Status(string title, string detail, string meta)
        {
            return new Snapshot
            {
                Kind = CopyKind.Text,
                Title = title,
                Detail = detail,
                Meta = meta,
                Sequence = 0
            };
        }

        public static Snapshot TextDemo()
        {
            return ClipboardInspector.FromText(
                "复制提示已经就绪：这是一段示例文本，用来展示卡片摘要、行数与字数统计。",
                0);
        }

        public static Snapshot ImageDemo()
        {
            using (var bitmap = CreateDemoImage())
            {
                return ClipboardInspector.FromImage(bitmap, 0);
            }
        }

        public static Snapshot FilesDemo()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "季度汇报.pptx"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "截图 2026-11-05 2231.png")
            };
            return ClipboardInspector.FromFiles(paths, 0);
        }

        private static Bitmap CreateDemoImage()
        {
            var bitmap = new Bitmap(320, 180);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, 320, 180),
                Color.FromArgb(255, 84, 120, 255),
                Color.FromArgb(255, 168, 92, 255),
                35f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(brush, 0, 0, 320, 180);

                using (var soft = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                {
                    g.FillEllipse(soft, 190, -40, 200, 200);
                    g.FillEllipse(soft, -30, 110, 160, 160);
                }

                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 4f))
                {
                    g.DrawLine(pen, 40, 120, 120, 70);
                    g.DrawLine(pen, 120, 70, 200, 112);
                }
            }

            return bitmap;
        }
    }
}
