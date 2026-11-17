using System;
using System.Collections.Generic;
using System.Threading;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Data
{
    [TestClass]
    public sealed class WorkQueueTests
    {
        /// <summary>Upper bound for anything that is expected to happen.</summary>
        private const int HappensWithinMs = 10_000;

        /// <summary>How long a call is observed to prove it is (still) blocked.</summary>
        private const int StaysBlockedForMs = 150;

        #region Basics

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        [DataRow(int.MinValue)]
        public void Constructor_CapacityBelowOne_Throws(int capacity)
        {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WorkQueue<int>(capacity));
            Assert.AreEqual("capacity", ex.ParamName);
        }

        [TestMethod]
        public void NewQueue_IsEmptyAndOpen()
        {
            var queue = new WorkQueue<int>(8);

            Assert.AreEqual(8, queue.Capacity);
            Assert.AreEqual(0, queue.Count);
            Assert.IsFalse(queue.IsCompleted);
        }

        [TestMethod]
        public void TryEnqueueTryDequeue_PreserveFifoOrder()
        {
            var queue = new WorkQueue<int>(16);

            for (int i = 0; i < 16; i++)
                Assert.IsTrue(queue.TryEnqueue(i * 10));

            Assert.AreEqual(16, queue.Count);

            for (int i = 0; i < 16; i++)
            {
                Assert.IsTrue(queue.TryDequeue(out int item));
                Assert.AreEqual(i * 10, item);
                Assert.AreEqual(15 - i, queue.Count);
            }
        }

        [TestMethod]
        public void Enqueue_WithSpace_PreservesFifoOrderWithTryEnqueue()
        {
            var queue = new WorkQueue<int>(4);

            Assert.IsTrue(queue.Enqueue(1));
            Assert.IsTrue(queue.TryEnqueue(2));
            Assert.IsTrue(queue.Enqueue(3));

            Assert.IsTrue(queue.TryDequeue(out int a));
            Assert.IsTrue(queue.TryDequeue(out int b));
            Assert.IsTrue(queue.TryDequeue(out int c));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, new[] { a, b, c });
        }

        [TestMethod]
        public void TryEnqueue_WhenFull_ReturnsFalseAndKeepsTheContents()
        {
            var queue = new WorkQueue<int>(3);
            Assert.IsTrue(queue.TryEnqueue(1));
            Assert.IsTrue(queue.TryEnqueue(2));
            Assert.IsTrue(queue.TryEnqueue(3));

            Assert.IsFalse(queue.TryEnqueue(4));
            Assert.IsFalse(queue.TryEnqueue(5));

            Assert.AreEqual(3, queue.Count);
            Assert.IsTrue(queue.TryDequeue(out int first));
            Assert.AreEqual(1, first, "a rejected item must not displace a queued one");

            Assert.IsTrue(queue.TryEnqueue(6), "space was freed");
            CollectionAssert.AreEqual(new[] { 2, 3, 6 }, Drain(queue));
        }

        [TestMethod]
        public void CapacityOne_AlternatesCorrectly()
        {
            var queue = new WorkQueue<int>(1);

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(queue.TryEnqueue(i));
                Assert.IsFalse(queue.TryEnqueue(-1));
                Assert.IsTrue(queue.TryDequeue(out int item));
                Assert.AreEqual(i, item);
            }
        }

        [TestMethod]
        public void WrapAround_KeepsOrderOverManyCycles()
        {
            // Random interleaving checked against a reference queue: the ring's head passes
            // every slot many times at every fill level from empty to full.
            const int capacity = 5;
            var queue = new WorkQueue<int>(capacity);
            var model = new Queue<int>();
            var random = new Random(12345);
            int next = 0;
            int timesFull = 0;

            for (int step = 0; step < 20_000; step++)
            {
                if (random.Next(2) == 0)
                {
                    bool accepted = queue.TryEnqueue(next);
                    Assert.AreEqual(model.Count < capacity, accepted, $"step {step}");
                    if (accepted)
                        model.Enqueue(next);
                    else
                        timesFull++;
                    next++;
                }
                else if (model.Count > 0)
                {
                    Assert.IsTrue(queue.TryDequeue(out int item));
                    Assert.AreEqual(model.Dequeue(), item, $"step {step}");
                }

                Assert.AreEqual(model.Count, queue.Count);
            }

            Assert.IsTrue(timesFull > 0, "the script never filled the queue");
            CollectionAssert.AreEqual(model.ToArray(), Drain(queue));
        }

        [TestMethod]
        public void ValueTypeItems_AreCopiedIntact()
        {
            nint pointer = unchecked((nint)0x7FFF_1234_5678L);
            var queue = new WorkQueue<(long Pts, nint Data, bool Key)>(2);
            Assert.IsTrue(queue.TryEnqueue((123456789012345, pointer, true)));

            Assert.IsTrue(queue.TryDequeue(out var item));

            Assert.AreEqual(123456789012345, item.Pts);
            Assert.AreEqual(pointer, item.Data);
            Assert.IsTrue(item.Key);
        }

        [TestMethod]
        public void SteadyState_AllocatesNothing()
        {
            var queue = new WorkQueue<long>(64);
            for (int i = 0; i < 1000; i++)
            {
                queue.TryEnqueue(i);
                queue.Enqueue(i);
                queue.TryDequeue(out _);
                queue.TryDequeue(out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50_000; i++)
            {
                queue.TryEnqueue(i);
                queue.Enqueue(i);
                queue.TryDequeue(out _);
                queue.TryDequeue(out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "handing items over must not allocate");
        }

        #endregion

        #region Completion

        [TestMethod]
        public void TryDequeue_AfterComplete_DrainsTheRemainingItemsThenReturnsFalse()
        {
            var queue = new WorkQueue<int>(8);
            queue.TryEnqueue(1);
            queue.TryEnqueue(2);
            queue.TryEnqueue(3);

            queue.Complete();

            Assert.IsTrue(queue.IsCompleted);
            Assert.AreEqual(3, queue.Count, "completing must not discard queued items");

            Assert.IsTrue(queue.TryDequeue(out int a));
            Assert.IsTrue(queue.TryDequeue(out int b));
            Assert.IsTrue(queue.TryDequeue(out int c));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, new[] { a, b, c });

            Assert.IsFalse(queue.TryDequeue(out int none));
            Assert.AreEqual(0, none);
            Assert.IsFalse(queue.TryDequeue(out _), "stays finished");
        }

        [TestMethod]
        public void Enqueue_AfterComplete_ReturnsFalseAndAddsNothing()
        {
            var queue = new WorkQueue<int>(8);
            queue.TryEnqueue(1);
            queue.Complete();

            Assert.IsFalse(queue.Enqueue(2));
            Assert.IsFalse(queue.TryEnqueue(3));

            Assert.AreEqual(1, queue.Count);
            CollectionAssert.AreEqual(new[] { 1 }, Drain(queue));
        }

        [TestMethod]
        public void Complete_Twice_IsHarmless()
        {
            var queue = new WorkQueue<int>(2);
            queue.TryEnqueue(7);

            queue.Complete();
            queue.Complete();

            Assert.IsTrue(queue.IsCompleted);
            CollectionAssert.AreEqual(new[] { 7 }, Drain(queue));
        }

        #endregion

        #region Blocking behaviour

        [TestMethod, Timeout(60_000)]
        public void Enqueue_WhenFull_BlocksUntilSpaceIsFreed()
        {
            var queue = new WorkQueue<int>(2);
            queue.TryEnqueue(1);
            queue.TryEnqueue(2);

            using var entered = new ManualResetEventSlim();
            using var returned = new ManualResetEventSlim();
            bool result = false;
            var producer = StartThread(() =>
            {
                entered.Set();
                result = queue.Enqueue(3);
                returned.Set();
            });

            Assert.IsTrue(entered.Wait(HappensWithinMs));
            Assert.IsFalse(returned.Wait(StaysBlockedForMs), "Enqueue returned although the queue was full");
            Assert.AreEqual(2, queue.Count);

            Assert.IsTrue(queue.TryDequeue(out int first));
            Assert.AreEqual(1, first);

            Assert.IsTrue(returned.Wait(HappensWithinMs), "Enqueue stayed blocked after space was freed");
            Assert.IsTrue(producer.Join(HappensWithinMs));
            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { 2, 3 }, DrainAvailable(queue));
        }

        [TestMethod, Timeout(60_000)]
        public void Enqueue_BlockedOnFullQueue_ReturnsFalseWhenCompleted()
        {
            var queue = new WorkQueue<int>(1);
            queue.TryEnqueue(1);

            using var entered = new ManualResetEventSlim();
            using var returned = new ManualResetEventSlim();
            bool result = true;
            var producer = StartThread(() =>
            {
                entered.Set();
                result = queue.Enqueue(2);
                returned.Set();
            });

            Assert.IsTrue(entered.Wait(HappensWithinMs));
            Assert.IsFalse(returned.Wait(StaysBlockedForMs));

            queue.Complete();

            Assert.IsTrue(returned.Wait(HappensWithinMs), "Complete did not release the blocked producer");
            Assert.IsTrue(producer.Join(HappensWithinMs));
            Assert.IsFalse(result, "an item must not be added to a completed queue");
            CollectionAssert.AreEqual(new[] { 1 }, Drain(queue));
        }

        [TestMethod, Timeout(60_000)]
        public void TryDequeue_WhenEmpty_BlocksUntilAnItemArrives()
        {
            var queue = new WorkQueue<int>(4);

            using var entered = new ManualResetEventSlim();
            using var returned = new ManualResetEventSlim();
            bool result = false;
            int item = 0;
            var consumer = StartThread(() =>
            {
                entered.Set();
                result = queue.TryDequeue(out item);
                returned.Set();
            });

            Assert.IsTrue(entered.Wait(HappensWithinMs));
            Assert.IsFalse(returned.Wait(StaysBlockedForMs), "TryDequeue returned on an empty, open queue");

            Assert.IsTrue(queue.TryEnqueue(42));

            Assert.IsTrue(returned.Wait(HappensWithinMs), "TryDequeue stayed blocked after an item arrived");
            Assert.IsTrue(consumer.Join(HappensWithinMs));
            Assert.IsTrue(result);
            Assert.AreEqual(42, item);
        }

        [TestMethod, Timeout(60_000)]
        public void TryDequeue_BlockedOnEmptyQueue_ReturnsFalseWhenCompleted()
        {
            var queue = new WorkQueue<int>(4);

            using var entered = new ManualResetEventSlim();
            using var returned = new ManualResetEventSlim();
            bool result = true;
            var consumer = StartThread(() =>
            {
                entered.Set();
                result = queue.TryDequeue(out _);
                returned.Set();
            });

            Assert.IsTrue(entered.Wait(HappensWithinMs));
            Assert.IsFalse(returned.Wait(StaysBlockedForMs));

            queue.Complete();

            Assert.IsTrue(returned.Wait(HappensWithinMs), "Complete did not release the blocked consumer");
            Assert.IsTrue(consumer.Join(HappensWithinMs));
            Assert.IsFalse(result);
        }

        [TestMethod, Timeout(60_000)]
        public void Complete_ReleasesEveryBlockedProducerAndConsumer()
        {
            var full = new WorkQueue<int>(1);
            full.TryEnqueue(0);
            var empty = new WorkQueue<int>(1);

            int released = 0;
            using var allEntered = new CountdownEvent(6);
            var threads = new List<Thread>();
            for (int i = 0; i < 3; i++)
            {
                threads.Add(StartThread(() =>
                {
                    allEntered.Signal();
                    if (!full.Enqueue(1))
                        Interlocked.Increment(ref released);
                }));
                threads.Add(StartThread(() =>
                {
                    allEntered.Signal();
                    if (!empty.TryDequeue(out _))
                        Interlocked.Increment(ref released);
                }));
            }

            Assert.IsTrue(allEntered.Wait(HappensWithinMs));
            Thread.Sleep(StaysBlockedForMs); // let them reach Monitor.Wait
            Assert.AreEqual(0, Volatile.Read(ref released));

            full.Complete();
            empty.Complete();

            foreach (Thread thread in threads)
                Assert.IsTrue(thread.Join(HappensWithinMs), "a waiter was not released by Complete");
            Assert.AreEqual(6, released);
        }

        #endregion

        #region Concurrency

        [TestMethod, Timeout(120_000)]
        [DataRow(1)]
        [DataRow(16)]
        [DataRow(1024)]
        public void MultipleProducers_NothingLostDuplicatedOrReordered(int capacity)
        {
            const int producers = 4;
            const int itemsPerProducer = 10_000;

            var queue = new WorkQueue<long>(capacity);
            var producerThreads = new Thread[producers];
            int rejected = 0;

            for (int p = 0; p < producers; p++)
            {
                int id = p;
                producerThreads[p] = StartThread(() =>
                {
                    for (int i = 0; i < itemsPerProducer; i++)
                    {
                        // Back-pressure path: must never lose an item.
                        if (!queue.Enqueue(((long)id << 32) | (uint)i))
                            Interlocked.Increment(ref rejected);
                    }
                });
            }

            var nextExpected = new int[producers];
            long count = 0;
            long sum = 0;
            string orderError = null;
            var consumer = StartThread(() =>
            {
                while (queue.TryDequeue(out long item))
                {
                    int id = (int)(item >> 32);
                    int sequence = (int)(item & 0xFFFFFFFF);
                    if (sequence != nextExpected[id] && orderError == null)
                        orderError = $"producer {id}: got {sequence}, expected {nextExpected[id]}";
                    nextExpected[id] = sequence + 1;
                    count++;
                    sum += sequence;
                }
            });

            foreach (Thread thread in producerThreads)
                Assert.IsTrue(thread.Join(60_000), "a producer did not finish");

            queue.Complete();
            Assert.IsTrue(consumer.Join(60_000), "the consumer did not finish after Complete");

            Assert.AreEqual(0, rejected);
            Assert.IsNull(orderError, orderError);
            Assert.AreEqual((long)producers * itemsPerProducer, count);
            Assert.AreEqual((long)producers * itemsPerProducer * (itemsPerProducer - 1) / 2, sum);
            Assert.AreEqual(0, queue.Count);
            foreach (int next in nextExpected)
                Assert.AreEqual(itemsPerProducer, next);
        }

        [TestMethod, Timeout(120_000)]
        public void DroppingProducer_EveryAcceptedItemIsDeliveredInOrder()
        {
            // The capture path: TryEnqueue drops when the consumer is behind. What was accepted
            // must come out exactly once and in order; accepted + dropped == offered.
            const int offered = 200_000;
            var queue = new WorkQueue<int>(8);

            long accepted = 0;
            var producer = StartThread(() =>
            {
                for (int i = 0; i < offered; i++)
                {
                    if (queue.TryEnqueue(i))
                        accepted++;
                }

                queue.Complete();
            });

            long received = 0;
            int previous = -1;
            bool ordered = true;
            var consumer = StartThread(() =>
            {
                while (queue.TryDequeue(out int item))
                {
                    if (item <= previous)
                        ordered = false;
                    previous = item;
                    received++;
                }
            });

            Assert.IsTrue(producer.Join(60_000));
            Assert.IsTrue(consumer.Join(60_000));

            Assert.IsTrue(ordered, "items arrived out of order or twice");
            Assert.AreEqual(accepted, received);
            Assert.IsTrue(accepted > 0 && accepted <= offered);
        }

        #endregion

        private static Thread StartThread(Action body)
        {
            var thread = new Thread(() => body()) { IsBackground = true };
            thread.Start();
            return thread;
        }

        /// <summary>Completes the queue and returns everything left in it.</summary>
        private static int[] Drain(WorkQueue<int> queue)
        {
            queue.Complete();
            return DrainAvailable(queue);
        }

        /// <summary>Takes what is currently queued without blocking on an open, empty queue.</summary>
        private static int[] DrainAvailable(WorkQueue<int> queue)
        {
            var items = new List<int>();
            while (queue.Count > 0 && queue.TryDequeue(out int item))
                items.Add(item);
            return items.ToArray();
        }
    }
}
