using System;
using System.Collections.Generic;
using System.Threading;

namespace UNCAD.Infra
{
    /// <summary>
    /// 后台上传队列：**单线程串行**执行、队列长度受限、连续失败自动熔断。
    ///
    /// 为什么需要它：U1F/U1U 批量会为每个回路排队一次上传（清单 + 等轴测场景）。
    /// 以前直接 <c>ThreadPool.QueueUserWorkItem</c>，网络异常时每个请求要等 7~20 秒才超时，
    /// 上千个阻塞任务会把线程池撑爆（不断注入新线程，每个线程 1MB 栈 + 每个任务还持有
    /// 场景字符串）——表现为内存冲到 GB 级、CPU 却只有 10%、进度慢到像卡死。
    /// 现在最多一个请求在飞、队列有上限、连续失败 3 次直接熔断并丢弃剩余排队项。
    /// </summary>
    internal sealed class BackgroundUploadQueue
    {
        public static BackgroundUploadQueue Shared { get; } =
            new BackgroundUploadQueue(true);

        private readonly object _gate = new object();
        private readonly Queue<Func<bool>> _pending = new Queue<Func<bool>>();
        private readonly bool _startWorker;
        private Thread _worker;
        private int _consecutiveFailures;
        private bool _stopped;

        public BackgroundUploadQueue(bool startWorker = false)
        {
            _startWorker = startWorker;
            Limit = 64;
            MaximumConsecutiveFailures = 3;
            if (startWorker) EnsureWorker();
        }

        /// <summary>队列上限（排队中的任务数）。</summary>
        public int Limit { get; set; }

        /// <summary>连续失败多少次后熔断。</summary>
        public int MaximumConsecutiveFailures { get; set; }

        public bool Suspended { get; private set; }
        public int Dropped { get; private set; }
        public int Queued { get; private set; }
        public int Failed { get; private set; }

        public int Pending
        {
            get { lock (_gate) return _pending.Count; }
        }

        /// <summary>入队一个上传任务（返回 true 表示已排队）。</summary>
        public bool Enqueue(Func<bool> work)
        {
            if (work == null) return false;
            lock (_gate)
            {
                if (Suspended || _pending.Count >= Limit)
                {
                    Dropped++;
                    return false;
                }
                _pending.Enqueue(work);
                Queued++;
                Monitor.Pulse(_gate);
                return true;
            }
        }

        /// <summary>执行一个排队任务（工作线程与测试共用）。返回是否有任务被执行。</summary>
        internal bool TryProcessOne()
        {
            Func<bool> work;
            lock (_gate)
            {
                if (_pending.Count == 0) return false;
                work = _pending.Dequeue();
            }
            Process(work);
            return true;
        }

        private void Process(Func<bool> work)
        {
            bool ok;
            try
            {
                ok = work();
            }
            catch (Exception ex)
            {
                Log.Warn("后台上传异常（已忽略）: " + ex.Message);
                ok = false;
            }
            lock (_gate)
            {
                if (ok)
                {
                    _consecutiveFailures = 0;
                    return;
                }
                Failed++;
                _consecutiveFailures++;
                if (!Suspended && _consecutiveFailures >= MaximumConsecutiveFailures)
                {
                    Suspended = true;
                    int abandoned = _pending.Count;
                    _pending.Clear();
                    Log.Warn("上传服务连续失败，已暂停本批剩余上传（丢弃 " + abandoned
                        + " 个排队项；图纸与清单不受影响）。");
                }
            }
        }

        private void EnsureWorker()
        {
            if (_worker != null) return;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "UNCAD 上传队列"
            };
            _worker.Start();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                Func<bool> work;
                lock (_gate)
                {
                    while (_pending.Count == 0 && !_stopped) Monitor.Wait(_gate);
                    if (_stopped) return;
                    work = _pending.Dequeue();
                }
                Process(work);
            }
        }

        /// <summary>停止工作线程（测试/退出用）。</summary>
        internal void Stop()
        {
            lock (_gate)
            {
                _stopped = true;
                Monitor.PulseAll(_gate);
            }
        }
    }
}
