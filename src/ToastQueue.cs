using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 卡片队列：最新卡片贴在工作区底部，旧的向上叠，最多同时 3 张。
    /// 超出上限或屏幕装不下时，先淘汰最旧的一张。
    /// </summary>
    internal sealed class ToastQueue : IDisposable
    {
        private readonly Theme _theme;
        private readonly List<ToastForm> _cards = new List<ToastForm>();
        private readonly int _dwellMilliseconds;
        private readonly int _maxVisible;
        private bool _disposed;

        public ToastQueue(Theme theme, int dwellMilliseconds, int maxVisible)
        {
            _theme = theme;
            _dwellMilliseconds = dwellMilliseconds;
            _maxVisible = Math.Max(1, Math.Min(5, maxVisible));
        }

        public int Count
        {
            get { return _cards.Count; }
        }

        public void Push(Snapshot snapshot)
        {
            if (_disposed || snapshot == null)
            {
                return;
            }

            while (_cards.Count >= _maxVisible)
            {
                EvictOldest();
            }

            var form = new ToastForm(_theme, snapshot, _dwellMilliseconds);
            form.Dismissed += OnDismissed;
            _cards.Add(form);

            TrimToWorkArea();
            LayoutCards(form);
        }

        private void LayoutCards(ToastForm animated)
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            int bottom = workArea.Bottom - _theme.MarginBottom;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                ToastForm card = _cards[i];
                int top = bottom - card.CardHeight;
                var target = new Point(
                    workArea.Right - _theme.MarginRight - _theme.CardWidth,
                    top);

                if (ReferenceEquals(card, animated))
                {
                    card.ShowAnimated(target);
                }
                else
                {
                    card.MoveTo(target);
                }

                bottom = top - _theme.Gap;
            }
        }

        private void TrimToWorkArea()
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;

            while (_cards.Count > 1)
            {
                int total = 0;
                for (int i = 0; i < _cards.Count; i++)
                {
                    total += _cards[i].CardHeight;
                }

                total += _theme.Gap * (_cards.Count - 1);

                if (total <= workArea.Height - _theme.MarginBottom - _theme.Px(8))
                {
                    return;
                }

                EvictOldest();
            }
        }

        private void EvictOldest()
        {
            if (_cards.Count == 0)
            {
                return;
            }

            ToastForm oldest = _cards[0];
            _cards.RemoveAt(0);
            oldest.Dismissed -= OnDismissed;
            oldest.CloseImmediate();
        }

        private void OnDismissed(object sender, EventArgs e)
        {
            var form = sender as ToastForm;
            if (form == null)
            {
                return;
            }

            form.Dismissed -= OnDismissed;
            _cards.Remove(form);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                _cards[i].Dismissed -= OnDismissed;
                _cards[i].CloseImmediate();
            }

            _cards.Clear();
        }
    }
}
