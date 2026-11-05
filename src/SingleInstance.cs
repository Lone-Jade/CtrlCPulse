using System;
using System.Threading;

namespace CtrlCPulse
{
    /// <summary>
    /// 单实例：命名互斥体保证只有一个托盘进程；
    /// 重复启动时通过命名事件通知已有实例弹一张“已在运行”卡片，然后自身退出。
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "CtrlCPulse.Instance.v1";
        private const string EventName = "CtrlCPulse.Activate.v1";

        private readonly Mutex _mutex;
        private readonly EventWaitHandle _event;
        private readonly Thread _listener;
        private volatile bool _running = true;
        private bool _disposed;

        public SingleInstance()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            IsOwner = createdNew;

            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

            if (IsOwner)
            {
                _listener = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "CtrlCPulse.Activation"
                };
                _listener.Start();
            }
        }

        public event Action Activated;

        public bool IsOwner { get; private set; }

        public void SignalExisting()
        {
            try
            {
                _event.Set();
            }
            catch (Exception)
            {
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    if (!_event.WaitOne(500))
                    {
                        continue;
                    }

                    Action handler = Activated;
                    if (handler != null)
                    {
                        handler();
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _running = false;

            // 只有持有者需要唤醒监听线程以便退出；
            // 非持有者（重复启动的进程）在这里再 Set 一次会多弹一张“已在运行”卡片。
            if (IsOwner)
            {
                try
                {
                    _event.Set();
                }
                catch (Exception)
                {
                }

                if (_listener != null)
                {
                    _listener.Join(300);
                }
            }

            _event.Dispose();

            if (IsOwner)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            _mutex.Dispose();
        }
    }
}
