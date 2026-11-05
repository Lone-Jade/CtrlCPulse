using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 单张提示卡片。
    /// 用 WS_EX_LAYERED + UpdateLayeredWindow 逐像素绘制，因此圆角、柔和阴影、
    /// 半透明底都能一次成型；窗口带 NOACTIVATE/TOOLWINDOW，不抢焦点、不进 Alt+Tab。
    /// 卡片内容只渲染一次，动画阶段仅改变窗口位置与整体透明度，避免每帧重绘。
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private const int EnterDurationMs = 180;
        private const int LeaveDurationMs = 160;
        private const int SlideDistance = 18;
        private const int LeaveSlide = 10;
        private const int TickMs = 15;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        private readonly Theme _theme;
        private readonly Snapshot _snapshot;
        private readonly int _cardWidth;
        private readonly int _cardHeight;
        private readonly int _shadow;
        private readonly int _dwellMilliseconds;

        private readonly Timer _anim;
        private readonly Timer _dwell;

        private Bitmap _surface;
        private IntPtr _screenDc;
        private IntPtr _memDc;
        private IntPtr _hBitmap;
        private IntPtr _oldBitmap;
        private bool _layered;

        private Point _cardTarget;
        private int _phase; // 0=进入 1=停留 2=离开
        private int _elapsed;
        private int _currentAlpha;
        private bool _closing;
        private bool _hoverPauseAllowed;

        public ToastForm(Theme theme, Snapshot snapshot, int dwellMilliseconds)
        {
            _theme = theme;
            _snapshot = snapshot;
            _dwellMilliseconds = Math.Max(600, dwellMilliseconds);
            _cardWidth = theme.CardWidth;
            _cardHeight = theme.CardHeight(snapshot.Kind);
            _shadow = theme.ShadowMargin;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Size = new Size(_cardWidth + _shadow * 2, _cardHeight + _shadow * 2);

            _anim = new Timer { Interval = TickMs };
            _anim.Tick += OnAnimationTick;

            _dwell = new Timer { Interval = _dwellMilliseconds };
            _dwell.Tick += OnDwellTick;
        }

        public event EventHandler Dismissed;

        public int CardHeight
        {
            get { return _cardHeight; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void ShowAnimated(Point cardTopLeft)
        {
            _cardTarget = cardTopLeft;

            RenderSurface();

            Location = new Point(
                cardTopLeft.X - _shadow,
                cardTopLeft.Y - _shadow + _theme.Px(SlideDistance));

            Show();
            PrepareLayered();
            Push(0);

            // 光标如果本来就停在卡片区域（比如用户把鼠标放在右下角），
            // 不把这一张的停留计时交给“悬停”控制，否则它会永远不消失。
            _hoverPauseAllowed = !IsCursorOverCard();

            _phase = 0;
            _elapsed = 0;
            _anim.Start();
        }

        /// <summary>已显示的卡片被新卡片顶上去时的瞬时移动。</summary>
        public void MoveTo(Point cardTopLeft)
        {
            _cardTarget = cardTopLeft;
            Location = new Point(cardTopLeft.X - _shadow, cardTopLeft.Y - _shadow);
            if (_layered)
            {
                Push(_currentAlpha);
            }
        }

        public void BeginLeave()
        {
            if (_closing || _phase == 2)
            {
                return;
            }

            _phase = 2;
            _elapsed = 0;
            _dwell.Stop();
            _anim.Start();
        }

        public void CloseImmediate()
        {
            FinishClose();
        }

        private bool IsCursorOverCard()
        {
            Rectangle card = new Rectangle(_cardTarget.X, _cardTarget.Y, _cardWidth, _cardHeight);
            return card.Contains(Cursor.Position);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (_hoverPauseAllowed)
            {
                _dwell.Stop();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverPauseAllowed = true;

            if (_phase == 1)
            {
                _dwell.Start();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            BeginLeave();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }

            // 阴影边距是透明的：让鼠标穿透，避免光标停在卡片旁边时被误判为悬停。
            if (m.Msg == WM_NCHITTEST)
            {
                long packed = m.LParam.ToInt64();
                int screenX = (short)(packed & 0xFFFF);
                int screenY = (short)((packed >> 16) & 0xFFFF);
                Point client = PointToClient(new Point(screenX, screenY));
                Rectangle card = new Rectangle(_shadow, _shadow, _cardWidth, _cardHeight);

                m.Result = card.Contains(client) ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        private void OnDwellTick(object sender, EventArgs e)
        {
            _dwell.Stop();
            BeginLeave();
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            _elapsed += TickMs;

            if (_phase == 0)
            {
                float progress = Math.Min(1f, _elapsed / (float)EnterDurationMs);
                float eased = 1f - (float)Math.Pow(1f - progress, 3);
                int offset = (int)Math.Round((1f - eased) * _theme.Px(SlideDistance));

                Location = new Point(_cardTarget.X - _shadow, _cardTarget.Y - _shadow + offset);
                Push((int)Math.Round(255 * eased));

                if (progress >= 1f)
                {
                    _phase = 1;
                    _anim.Stop();
                    Location = new Point(_cardTarget.X - _shadow, _cardTarget.Y - _shadow);
                    Push(255);
                    _dwell.Start();
                }

                return;
            }

            if (_phase == 2)
            {
                float progress = Math.Min(1f, _elapsed / (float)LeaveDurationMs);
                int offset = (int)Math.Round(progress * _theme.Px(LeaveSlide));

                Location = new Point(_cardTarget.X - _shadow, _cardTarget.Y - _shadow + offset);
                Push((int)Math.Round(255 * (1f - progress)));

                if (progress >= 1f)
                {
                    FinishClose();
                }
            }
        }

        private void FinishClose()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            _anim.Stop();
            _dwell.Stop();

            Hide();
            ReleaseLayered();

            EventHandler handler = Dismissed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }

            Dispose();
        }

        // ---------------- 分层窗口 ----------------

        private void PrepareLayered()
        {
            if (_layered || _surface == null)
            {
                return;
            }

            _screenDc = Native.GetDC(IntPtr.Zero);
            _memDc = Native.CreateCompatibleDC(_screenDc);
            _hBitmap = _surface.GetHbitmap(Color.FromArgb(0));
            _oldBitmap = Native.SelectObject(_memDc, _hBitmap);
            _layered = true;
        }

        private void ReleaseLayered()
        {
            if (!_layered)
            {
                return;
            }

            _layered = false;

            if (_oldBitmap != IntPtr.Zero && _memDc != IntPtr.Zero)
            {
                Native.SelectObject(_memDc, _oldBitmap);
            }

            if (_hBitmap != IntPtr.Zero)
            {
                Native.DeleteObject(_hBitmap);
            }

            if (_memDc != IntPtr.Zero)
            {
                Native.DeleteDC(_memDc);
            }

            if (_screenDc != IntPtr.Zero)
            {
                Native.ReleaseDC(IntPtr.Zero, _screenDc);
            }

            _oldBitmap = IntPtr.Zero;
            _hBitmap = IntPtr.Zero;
            _memDc = IntPtr.Zero;
            _screenDc = IntPtr.Zero;
        }

        private void Push(int alpha)
        {
            if (!_layered)
            {
                return;
            }

            int clamped = Math.Max(0, Math.Min(255, alpha));
            _currentAlpha = clamped;

            var size = new Native.SIZE(Width, Height);
            var source = new Native.POINT(0, 0);
            var destination = new Native.POINT(Left, Top);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)clamped,
                AlphaFormat = Native.AC_SRC_ALPHA
            };

            Native.UpdateLayeredWindow(
                Handle,
                _screenDc,
                ref destination,
                ref size,
                _memDc,
                ref source,
                0,
                ref blend,
                Native.ULW_ALPHA);
        }

        // ---------------- 绘制 ----------------

        private void RenderSurface()
        {
            _surface = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(_surface))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                var card = new RectangleF(_shadow, _shadow, _cardWidth, _cardHeight);
                DrawShadow(g, card);
                DrawCard(g, card);
                DrawContent(g, card);
            }
        }

        private void DrawShadow(Graphics g, RectangleF card)
        {
            float radius = _theme.Radius;

            // 用 8 层低透明度圆角矩形模拟柔和投影，避免依赖位图模糊。
            for (int layer = 8; layer >= 1; layer--)
            {
                float grow = layer * _theme.Scale;
                var rect = RectangleF.Inflate(card, grow, grow);
                rect.Offset(0, _theme.Scale * 2f);

                using (GraphicsPath path = RoundedRect(rect, radius + grow))
                using (var brush = new SolidBrush(Theme.ShadowColor))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        private void DrawCard(Graphics g, RectangleF card)
        {
            using (GraphicsPath path = RoundedRect(card, _theme.Radius))
            {
                using (var brush = new LinearGradientBrush(
                    new RectangleF(card.X, card.Y, card.Width, card.Height + 1f),
                    Theme.CardTop,
                    Theme.CardBottom,
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }

                using (var pen = new Pen(Theme.CardBorder, Math.Max(1f, _theme.Scale)))
                {
                    g.DrawPath(pen, path);
                }

                // 左侧类型色条：裁切到圆角卡片内，保留左圆角。
                GraphicsState state = g.Save();
                g.SetClip(path);
                using (var accentBrush = new SolidBrush(Theme.AccentFor(_snapshot.Kind)))
                {
                    g.FillRectangle(accentBrush, card.X, card.Y, Math.Max(3f, 4f * _theme.Scale), card.Height);
                }

                g.Restore(state);
            }
        }

        private void DrawContent(Graphics g, RectangleF card)
        {
            float scale = _theme.Scale;
            Color accent = Theme.AccentFor(_snapshot.Kind);

            float pad = 16f * scale;
            float iconSize = 26f * scale;
            float iconX = card.X + pad;
            float iconY = card.Y + (card.Height - iconSize) / 2f;
            DrawBadge(g, new RectangleF(iconX, iconY, iconSize, iconSize), accent);

            float textX = iconX + iconSize + 12f * scale;
            float rightLimit = card.Right - 14f * scale;

            bool hasThumbnail = _snapshot.Thumbnail != null;
            float thumbWidth = 92f * scale;
            float thumbHeight = 58f * scale;
            float thumbX = rightLimit - thumbWidth;
            float thumbY = card.Y + (card.Height - thumbHeight) / 2f;

            float chipWidth = 0f;
            if (!hasThumbnail)
            {
                string label = _snapshot.KindLabel;
                SizeF labelSize = g.MeasureString(label, _theme.ChipFont);
                chipWidth = labelSize.Width + 16f * scale;
                var chipRect = new RectangleF(rightLimit - chipWidth, card.Y + 16f * scale, chipWidth, 20f * scale);
                DrawChip(g, chipRect, label, accent);
            }

            float detailRight = hasThumbnail ? thumbX - 12f * scale : rightLimit - chipWidth - 10f * scale;
            float blockTop = card.Y + (hasThumbnail ? 22f : 16f) * scale;

            using (var titleBrush = new SolidBrush(Theme.TitleColor))
            using (StringFormat format = Typographic())
            {
                g.DrawString(_snapshot.Title, _theme.TitleFont, titleBrush, new PointF(textX, blockTop), format);
            }

            float detailY = blockTop + 17f * scale;
            using (var detailBrush = new SolidBrush(Theme.DetailColor))
            using (StringFormat format = Typographic())
            {
                var rect = new RectangleF(textX, detailY, Math.Max(10f, detailRight - textX), 16f * scale);
                format.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(_snapshot.Detail ?? string.Empty, _theme.DetailFont, detailBrush, rect, format);
            }

            float metaY = detailY + 17f * scale;
            using (var metaBrush = new SolidBrush(Theme.MetaColor))
            using (StringFormat format = Typographic())
            {
                g.DrawString(_snapshot.Meta ?? string.Empty, _theme.MetaFont, metaBrush, new PointF(textX, metaY), format);
            }

            if (hasThumbnail)
            {
                DrawThumbnail(g, new RectangleF(thumbX, thumbY, thumbWidth, thumbHeight));
            }
        }

        private void DrawBadge(Graphics g, RectangleF rect, Color accent)
        {
            float scale = _theme.Scale;

            using (var fill = new SolidBrush(Color.FromArgb(40, accent)))
            using (var ring = new Pen(Color.FromArgb(120, accent), Math.Max(1f, 1.2f * scale)))
            {
                g.FillEllipse(fill, rect);
                g.DrawEllipse(ring, rect);
            }

            float d = rect.Width;
            float x = rect.X;
            float y = rect.Y;

            using (var pen = new Pen(Color.FromArgb(255, 255, 255, 255), Math.Max(1.8f, 2.2f * scale)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[]
                {
                    new PointF(x + d * 0.28f, y + d * 0.52f),
                    new PointF(x + d * 0.44f, y + d * 0.68f),
                    new PointF(x + d * 0.72f, y + d * 0.36f)
                });
            }
        }

        private void DrawChip(Graphics g, RectangleF rect, string label, Color accent)
        {
            using (GraphicsPath path = RoundedRect(rect, rect.Height / 2f))
            using (var fill = new SolidBrush(Color.FromArgb(38, accent)))
            using (var brush = new SolidBrush(accent))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                g.FillPath(fill, path);
                g.DrawString(label, _theme.ChipFont, brush, rect, format);
            }
        }

        private void DrawThumbnail(Graphics g, RectangleF rect)
        {
            float radius = 8f * _theme.Scale;
            Bitmap image = _snapshot.Thumbnail;

            using (GraphicsPath clip = RoundedRect(rect, radius))
            {
                GraphicsState state = g.Save();
                g.SetClip(clip);

                using (var background = new SolidBrush(Color.FromArgb(255, 17, 18, 22)))
                {
                    g.FillRectangle(background, rect);
                }

                float fit = Math.Min(rect.Width / image.Width, rect.Height / image.Height);
                float width = image.Width * fit;
                float height = image.Height * fit;
                var destination = new RectangleF(
                    rect.X + (rect.Width - width) / 2f,
                    rect.Y + (rect.Height - height) / 2f,
                    width,
                    height);

                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, destination);

                g.Restore(state);

                using (var pen = new Pen(Color.FromArgb(38, 255, 255, 255), Math.Max(1f, _theme.Scale)))
                {
                    g.DrawPath(pen, clip);
                }
            }
        }

        private static StringFormat Typographic()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.None;
            return format;
        }

        internal static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float r = Math.Max(0f, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f));

            if (r <= 0.1f)
            {
                path.AddRectangle(rect);
                return path;
            }

            float d = r * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_anim != null)
                {
                    _anim.Stop();
                    _anim.Dispose();
                }

                if (_dwell != null)
                {
                    _dwell.Stop();
                    _dwell.Dispose();
                }

                ReleaseLayered();

                if (_surface != null)
                {
                    _surface.Dispose();
                    _surface = null;
                }

                if (_snapshot != null)
                {
                    _snapshot.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
