using System;
using System.Windows.Forms;

namespace CtrlCPulse
{
    /// <summary>--demo：依次演示三种卡片，用于人工/截图验收。</summary>
    internal sealed class DemoRunner : ApplicationContext
    {
        private readonly Theme _theme;
        private readonly ToastQueue _queue;
        private readonly Timer _timer;
        private int _step;
        private bool _finished;

        public DemoRunner()
        {
            _theme = new Theme(Theme.DetectPrimaryScale());
            _queue = new ToastQueue(_theme, 2600, 3);
            _timer = new Timer { Interval = 650 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _step++;

            switch (_step)
            {
                case 1:
                    _queue.Push(ToastContent.TextDemo());
                    break;
                case 2:
                    _queue.Push(ToastContent.ImageDemo());
                    break;
                case 3:
                    _queue.Push(ToastContent.FilesDemo());
                    break;
                case 8:
                    Finish();
                    break;
            }
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _timer.Stop();
            _queue.Dispose();
            _theme.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Finish();
            }

            base.Dispose(disposing);
        }
    }
}
