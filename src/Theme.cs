using System;
using System.Drawing;

namespace CtrlCPulse
{
    /// <summary>
    /// 视觉规格集中定义：颜色、字号、圆角、间距。
    /// 所有尺寸以 96 DPI 为基准，按当前屏幕 DPI 等比缩放。
    /// </summary>
    internal sealed class Theme : IDisposable
    {
        public const float BaseWidth = 344f;

        public Theme(float scale)
        {
            Scale = scale <= 0.5f ? 1f : scale;
            TitleFont = CreateFont(13f, FontStyle.Bold);
            DetailFont = CreateFont(11.5f, FontStyle.Regular);
            MetaFont = CreateFont(10f, FontStyle.Regular);
            ChipFont = CreateFont(10f, FontStyle.Bold);
        }

        public float Scale { get; private set; }

        public Font TitleFont { get; private set; }

        public Font DetailFont { get; private set; }

        public Font MetaFont { get; private set; }

        public Font ChipFont { get; private set; }

        // ---- 颜色 ----
        public static readonly Color CardTop = Color.FromArgb(235, 32, 33, 39);
        public static readonly Color CardBottom = Color.FromArgb(235, 23, 24, 29);
        public static readonly Color CardBorder = Color.FromArgb(30, 255, 255, 255);
        public static readonly Color TitleColor = Color.FromArgb(255, 243, 245, 250);
        public static readonly Color DetailColor = Color.FromArgb(255, 178, 184, 198);
        public static readonly Color MetaColor = Color.FromArgb(255, 132, 139, 154);
        public static readonly Color ShadowColor = Color.FromArgb(7, 0, 0, 0);

        public static readonly Color AccentText = Color.FromArgb(255, 92, 148, 255);
        public static readonly Color AccentImage = Color.FromArgb(255, 172, 116, 255);
        public static readonly Color AccentFiles = Color.FromArgb(255, 245, 176, 74);

        public static Color AccentFor(CopyKind kind)
        {
            switch (kind)
            {
                case CopyKind.Image:
                    return AccentImage;
                case CopyKind.Files:
                    return AccentFiles;
                default:
                    return AccentText;
            }
        }

        // ---- 尺寸（已按 DPI 缩放） ----
        public int Px(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public int CardWidth
        {
            get { return Px(BaseWidth); }
        }

        public int ShadowMargin
        {
            get { return Px(14); }
        }

        public int Gap
        {
            get { return Px(8); }
        }

        public int MarginRight
        {
            get { return Px(16); }
        }

        public int MarginBottom
        {
            get { return Px(12); }
        }

        public int Radius
        {
            get { return Px(12); }
        }

        public int CardHeight(CopyKind kind)
        {
            switch (kind)
            {
                case CopyKind.Image:
                    return Px(108);
                case CopyKind.Files:
                    return Px(96);
                default:
                    return Px(84);
            }
        }

        private Font CreateFont(float pixelSize, FontStyle style)
        {
            // 使用像素单位，避免 GDI+ 按 96 DPI 再换算一次。
            return new Font("Microsoft YaHei UI", pixelSize * Scale, style, GraphicsUnit.Pixel);
        }

        /// <summary>读取主屏 DPI 缩放比例（进程已在清单中声明 PerMonitorV2）。</summary>
        public static float DetectPrimaryScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96f;
                }
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        public void Dispose()
        {
            TitleFont.Dispose();
            DetailFont.Dispose();
            MetaFont.Dispose();
            ChipFont.Dispose();
        }
    }
}
