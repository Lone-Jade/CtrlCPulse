using System;
using System.Drawing;

namespace CtrlCPulse
{
    /// <summary>一次复制的内容类型。</summary>
    internal enum CopyKind
    {
        Text,
        Image,
        Files
    }

    /// <summary>
    /// 一次复制的只读快照：提示卡片需要的全部信息都在这里，
    /// 之后不再访问剪贴板，避免卡片绘制期间剪贴板被其它程序改写。
    /// </summary>
    internal sealed class Snapshot : IDisposable
    {
        public CopyKind Kind { get; set; }

        public string Title { get; set; }

        /// <summary>卡片第二行：文字摘要 / 文件列表 / 图片说明。</summary>
        public string Detail { get; set; }

        /// <summary>卡片第三行：行数、字数、体积、像素等附加信息。</summary>
        public string Meta { get; set; }

        /// <summary>图片缩略图，其它类型为 null。</summary>
        public Bitmap Thumbnail { get; set; }

        /// <summary>剪贴板序列号，用于去重。</summary>
        public uint Sequence { get; set; }

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case CopyKind.Image:
                        return "图片";
                    case CopyKind.Files:
                        return "文件";
                    default:
                        return "文本";
                }
            }
        }

        public void Dispose()
        {
            if (Thumbnail != null)
            {
                Thumbnail.Dispose();
                Thumbnail = null;
            }
        }
    }
}
