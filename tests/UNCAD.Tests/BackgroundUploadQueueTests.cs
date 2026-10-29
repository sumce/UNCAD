using System;
using System.Collections.Generic;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 后台上传队列：串行、有界、连续失败熔断。
    /// 这些约束是为了避免上千个阻塞上传把线程池/内存打爆（批量 U1F/U1U 的卡死根因）。
    /// </summary>
    public sealed class BackgroundUploadQueueTests
    {
        [Fact]
        public void ProcessOne_RunsQueuedWorkInOrder()
        {
            var queue = new BackgroundUploadQueue();
            var order = new List<int>();
            queue.Enqueue(() => { order.Add(1); return true; });
            queue.Enqueue(() => { order.Add(2); return true; });

            Assert.True(queue.TryProcessOne());
            Assert.True(queue.TryProcessOne());
            Assert.False(queue.TryProcessOne());
            Assert.Equal(new[] { 1, 2 }, order);
            Assert.Equal(0, queue.Failed);
            Assert.False(queue.Suspended);
        }

        [Fact]
        public void Enqueue_RejectsWhenQueueIsFull()
        {
            var queue = new BackgroundUploadQueue { Limit = 2 };
            Assert.True(queue.Enqueue(() => true));
            Assert.True(queue.Enqueue(() => true));
            Assert.False(queue.Enqueue(() => true));
            Assert.Equal(1, queue.Dropped);
            Assert.Equal(2, queue.Pending);
        }

        [Fact]
        public void ConsecutiveFailures_SuspendQueueAndDropPendingWork()
        {
            var queue = new BackgroundUploadQueue { MaximumConsecutiveFailures = 3 };
            for (int index = 0; index < 5; index++) queue.Enqueue(() => false);

            Assert.True(queue.TryProcessOne());
            Assert.True(queue.TryProcessOne());
            Assert.False(queue.Suspended);
            Assert.True(queue.TryProcessOne());

            Assert.True(queue.Suspended);
            Assert.Equal(0, queue.Pending);          // 熔断后剩余排队项被丢弃
            Assert.Equal(3, queue.Failed);
            Assert.False(queue.Enqueue(() => true)); // 熔断后不再接收
        }

        [Fact]
        public void Success_ResetsTheFailureCounter()
        {
            var queue = new BackgroundUploadQueue { MaximumConsecutiveFailures = 2 };
            queue.Enqueue(() => false);
            queue.Enqueue(() => true);
            queue.Enqueue(() => false);

            queue.TryProcessOne();
            queue.TryProcessOne();
            queue.TryProcessOne();

            Assert.False(queue.Suspended);
            Assert.Equal(2, queue.Failed);
        }
    }
}
