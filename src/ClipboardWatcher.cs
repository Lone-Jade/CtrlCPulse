using System;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>
    /// 剪贴板变化监听：隐藏窗口 + WM_CLIPBOARDUPDATE，事件驱动、零轮询。
    /// 一次复制可能触发多条消息，用 120 ms 防抖合并；再用剪贴板序列号去重，
    /// 从而自然满足“没有选中内容时按 Ctrl+C 不提示”。
    /// </summary>
    internal sealed class ClipboardWatcher : NativeWindow, IDisposable
    {
        private const int DebounceMilliseconds = 120;

        private readonly Action<uint> _onChanged;
        private readonly Timer _debounce;
        private uint _lastSequence;
        private bool _disposed;

        public ClipboardWatcher(Action<uint> onChanged)
        {
            _onChanged = onChanged;
            CreateHandle(new CreateParams
            {
                Caption = "CtrlCPulse.ClipboardWatcher",
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Style = 0,
                ExStyle = Native.WS_EX_TOOLWINDOW
            });

            _lastSequence = Native.GetClipboardSequenceNumber();

            _debounce = new Timer { Interval = DebounceMilliseconds };
            _debounce.Tick += OnDebounceTick;

            Native.AddClipboardFormatListener(Handle);
        }

        public uint LastSequence
        {
            get { return _lastSequence; }
        }

        /// <summary>序列号变化才值得提示。</summary>
        internal static bool ShouldNotify(uint last, uint current)
        {
            return current != 0 && current != last;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_CLIPBOARDUPDATE)
            {
                // 重新计时，合并同一批复制产生的多条消息。
                _debounce.Stop();
                _debounce.Start();
            }

            base.WndProc(ref m);
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounce.Stop();

            uint sequence = Native.GetClipboardSequenceNumber();
            if (!ShouldNotify(_lastSequence, sequence))
            {
                return;
            }

            _lastSequence = sequence;

            if (_onChanged != null)
            {
                _onChanged(sequence);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_debounce != null)
            {
                _debounce.Stop();
                _debounce.Dispose();
            }

            if (Handle != IntPtr.Zero)
            {
                Native.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }
}
