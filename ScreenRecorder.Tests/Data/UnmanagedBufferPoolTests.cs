using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using MediaEncoder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenRecorder.Tests.Data
{
    [TestClass]
    public sealed unsafe class UnmanagedBufferPoolTests
    {
        private const int KiB = 1024;
        private const int MiB = 1024 * 1024;

        #region RoundUp

        [TestMethod]
        // Everything up to the minimum buffer size.
        [DataRow(int.MinValue, 4096)]
        [DataRow(-1, 4096)]
        [DataRow(0, 4096)]
        [DataRow(1, 4096)]
        [DataRow(4095, 4096)]
        [DataRow(4096, 4096)]
        // Up to 1 MiB: next power of two (encoded packets of slightly different sizes share a buffer).
        [DataRow(4097, 8192)]
        [DataRow(5000, 8192)]
        [DataRow(8192, 8192)]
        [DataRow(8193, 16384)]
        [DataRow(65536, 65536)]
        [DataRow(65537, 131072)]
        [DataRow(524289, 1048576)]
        [DataRow(1048575, 1048576)]
        [DataRow(1048576, 1048576)]
        // Above 1 MiB: next whole MiB (a 5.5 MB frame must not balloon to 8 MB).
        [DataRow(1048577, 2 * MiB)]
        [DataRow(2 * MiB, 2 * MiB)]
        [DataRow(2 * MiB + 1, 3 * MiB)]
        [DataRow(5_529_600, 6 * MiB)]   // 2560x1440 NV12
        [DataRow(3_110_400, 3 * MiB)]   // 1920x1080 NV12
        [DataRow(1 << 30, 1 << 30)]
        // Beyond 1 GiB: exact (no rounding that could overflow).
        [DataRow((1 << 30) + 1, (1 << 30) + 1)]
        [DataRow(int.MaxValue, int.MaxValue)]
        public void RoundUp_ReturnsTheBucketSize(int minSize, int expected)
        {
            Assert.AreEqual(expected, UnmanagedBufferPool.RoundUp(minSize));
        }

        [TestMethod]
        public void RoundUp_IsNeverSmallerThanRequested_AndIsIdempotent()
        {
            var random = new Random(7);
            for (int i = 0; i < 20_000; i++)
            {
                int size = random.Next(1, 64 * MiB);
                int rounded = UnmanagedBufferPool.RoundUp(size);

                Assert.IsTrue(rounded >= size, $"RoundUp({size}) = {rounded}");
                Assert.IsTrue(rounded >= 4096);
                Assert.AreEqual(rounded, UnmanagedBufferPool.RoundUp(rounded), $"RoundUp is not stable at {rounded}");

                // Waste is bounded: < 2x below 1 MiB, < 1 MiB above.
                if (size > 4096)
                    Assert.IsTrue(size > MiB ? rounded - size < MiB : rounded < 2L * size, $"RoundUp({size}) = {rounded} wastes too much");
            }
        }

        #endregion

        #region Rent / Return

        [TestMethod]
        public void Rent_ReturnsWritableMemoryOfAtLeastTheRequestedSize()
        {
            using var pool = new UnmanagedBufferPool();
            const int size = 100_000;

            nint buffer = pool.Rent(size);

            Assert.AreNotEqual((nint)0, buffer);
            var span = new Span<byte>((void*)buffer, size);
            span.Fill(0xA5);
            Assert.AreEqual(0xA5, span[0]);
            Assert.AreEqual(0xA5, span[size - 1]);
            Assert.AreEqual(1, pool.BufferCount);

            pool.Return(buffer);
        }

        [TestMethod]
        public void Rent_AfterReturn_ReusesTheSameBuffer()
        {
            using var pool = new UnmanagedBufferPool();

            nint first = pool.Rent(5000);
            pool.Return(first);
            nint second = pool.Rent(6000); // same 8 KiB bucket

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, pool.BufferCount);
        }

        [TestMethod]
        public void Rent_ReusedBuffer_KeepsItsContents()
        {
            // The pool recycles, it does not clear: callers always overwrite what they rent.
            using var pool = new UnmanagedBufferPool();
            nint first = pool.Rent(4096);
            Marshal.WriteInt32(first, 0x1234_5678);
            pool.Return(first);

            nint second = pool.Rent(4096);

            Assert.AreEqual(first, second);
            Assert.AreEqual(0x1234_5678, Marshal.ReadInt32(second));
        }

        [TestMethod]
        public void Rent_WhileABufferIsInUse_HandsOutADifferentOne()
        {
            using var pool = new UnmanagedBufferPool();

            nint a = pool.Rent(4096);
            nint b = pool.Rent(4096);
            nint c = pool.Rent(4096);

            Assert.AreEqual(3, new HashSet<nint> { a, b, c }.Count, "a buffer was handed out twice");
            Assert.AreEqual(3, pool.BufferCount);

            // The buffers do not overlap.
            new Span<byte>((void*)a, 4096).Fill(1);
            new Span<byte>((void*)b, 4096).Fill(2);
            new Span<byte>((void*)c, 4096).Fill(3);
            Assert.AreEqual(1, Marshal.ReadByte(a, 4095));
            Assert.AreEqual(2, Marshal.ReadByte(b, 4095));
            Assert.AreEqual(3, Marshal.ReadByte(c, 0));
        }

        [TestMethod]
        public void Rent_TooSmallFreeBuffer_IsNotUsed()
        {
            using var pool = new UnmanagedBufferPool();
            nint small = pool.Rent(4096);
            pool.Return(small);

            nint large = pool.Rent(64 * KiB);

            Assert.AreNotEqual(small, large);
            Assert.AreEqual(2, pool.BufferCount);
        }

        [TestMethod]
        public void Rent_PicksTheSmallestFreeBufferThatFits()
        {
            using var pool = new UnmanagedBufferPool();
            nint b4 = pool.Rent(4 * KiB);
            nint b64 = pool.Rent(64 * KiB);
            nint b16 = pool.Rent(16 * KiB);
            nint b256 = pool.Rent(256 * KiB);
            pool.Return(b4);
            pool.Return(b64);
            pool.Return(b16);
            pool.Return(b256);

            Assert.AreEqual(b16, pool.Rent(10_000), "10 KB fits the 16 KiB buffer best");
            Assert.AreEqual(b4, pool.Rent(100), "100 bytes fits the 4 KiB buffer best");
            Assert.AreEqual(b64, pool.Rent(100), "with 4 KiB and 16 KiB taken, 64 KiB is the smallest free one");
            Assert.AreEqual(b256, pool.Rent(100));
            Assert.AreEqual(4, pool.BufferCount, "nothing new was allocated");

            nint extra = pool.Rent(100);
            Assert.AreEqual(5, pool.BufferCount, "all buffers in use: a new one is allocated");
            Assert.IsFalse(new[] { b4, b16, b64, b256 }.Contains(extra));
        }

        [TestMethod]
        public void Rent_MoreBuffersThanTheInitialTable_AllDistinct()
        {
            using var pool = new UnmanagedBufferPool();
            var buffers = new List<nint>();

            for (int i = 0; i < 100; i++)
            {
                nint buffer = pool.Rent(4096);
                Marshal.WriteInt32(buffer, i);
                buffers.Add(buffer);
            }

            Assert.AreEqual(100, pool.BufferCount);
            Assert.AreEqual(100, buffers.Distinct().Count());
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(i, Marshal.ReadInt32(buffers[i]), "a buffer was overwritten by another rent");

            foreach (nint buffer in buffers)
                pool.Return(buffer);

            // All of them are reusable again: 100 more rents allocate nothing.
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(buffers.Contains(pool.Rent(4096)));
            Assert.AreEqual(100, pool.BufferCount);
        }

        [TestMethod]
        public void Return_NullOrUnknownPointer_IsHarmless()
        {
            using var pool = new UnmanagedBufferPool();
            nint real = pool.Rent(4096);

            pool.Return(0);
            pool.Return((nint)0x1234_5678);
            pool.Return(real + 16); // inside a pooled buffer, but not its start

            Assert.AreEqual(1, pool.BufferCount);
            Assert.AreNotEqual(real, pool.Rent(4096), "the in-use buffer was released by a bogus Return");
        }

        [TestMethod]
        public void Return_Twice_DoesNotDuplicateTheBuffer()
        {
            using var pool = new UnmanagedBufferPool();
            nint buffer = pool.Rent(4096);

            pool.Return(buffer);
            pool.Return(buffer);

            nint a = pool.Rent(4096);
            nint b = pool.Rent(4096);
            Assert.AreEqual(buffer, a);
            Assert.AreNotEqual(a, b, "a double Return made the pool hand the same buffer out twice");
        }

        #endregion

        #region Trimming

        [TestMethod]
        public void CleanupOldBuffers_FreesBuffersIdleLongerThanTheRetention()
        {
            using var pool = new UnmanagedBufferPool(retentionRents: 2);

            nint idle = pool.Rent(4 * KiB);   // rent #1
            pool.Return(idle);
            pool.CleanupOldBuffers();
            Assert.AreEqual(1, pool.BufferCount, "idle for 0 rents: kept");

            nint b = pool.Rent(8 * KiB);      // rent #2
            nint c = pool.Rent(16 * KiB);     // rent #3
            pool.CleanupOldBuffers();
            Assert.AreEqual(3, pool.BufferCount, "idle for exactly the retention (2 rents): kept");

            nint d = pool.Rent(32 * KiB);     // rent #4 → the 4 KiB buffer has now been idle for 3 rents
            pool.CleanupOldBuffers();
            Assert.AreEqual(3, pool.BufferCount, "the idle buffer was not released");

            // The freed one is really gone: asking for 4 KiB again allocates anew.
            pool.Return(b);
            pool.Return(c);
            pool.Return(d);
            Assert.AreEqual(b, pool.Rent(4 * KiB), "smallest remaining free buffer is the 8 KiB one");
        }

        [TestMethod]
        public void CleanupOldBuffers_NeverFreesABufferInUse()
        {
            using var pool = new UnmanagedBufferPool(retentionRents: 1);
            nint held = pool.Rent(4 * KiB);
            Marshal.WriteInt64(held, 0x0123_4567_89AB_CDEF);

            for (int i = 0; i < 50; i++)
                pool.Return(pool.Rent(64 * KiB));
            pool.CleanupOldBuffers();

            Assert.AreEqual(2, pool.BufferCount);
            Assert.AreEqual(0x0123_4567_89AB_CDEF, Marshal.ReadInt64(held), "memory still in use was freed or recycled");
            pool.Return(held);
        }

        [TestMethod]
        public void CleanupOldBuffers_RecentlyReturnedBuffer_IsKept()
        {
            // Idle time counts from the Return, not from the Rent: a buffer held for a long
            // time and just returned is not "old".
            using var pool = new UnmanagedBufferPool(retentionRents: 2);
            nint longHeld = pool.Rent(4 * KiB);
            for (int i = 0; i < 20; i++)
                pool.Return(pool.Rent(64 * KiB));

            pool.Return(longHeld);
            pool.CleanupOldBuffers();

            Assert.AreEqual(2, pool.BufferCount);
            Assert.AreEqual(longHeld, pool.Rent(4 * KiB));
        }

        [TestMethod]
        public void Rent_TrimsAutomatically_SoABurstDoesNotPinMemory()
        {
            // Small packets, one huge keyframe, then small packets again. The big buffer must be
            // released by the pool's own periodic trim (every 256th rent), without any explicit
            // cleanup call.
            using var pool = new UnmanagedBufferPool(retentionRents: 10);
            nint small = pool.Rent(4 * KiB);
            pool.Return(small);

            pool.Return(pool.Rent(8 * MiB));
            Assert.AreEqual(2, pool.BufferCount);

            for (int i = 0; i < 600; i++)
            {
                nint again = pool.Rent(4 * KiB);
                Assert.AreEqual(small, again, "the steady-state buffer must keep being reused");
                pool.Return(again);
            }

            Assert.AreEqual(1, pool.BufferCount, "the idle 8 MiB buffer is still pinned");
        }

        [TestMethod]
        public void Retention_BelowOne_IsTreatedAsOne()
        {
            using var pool = new UnmanagedBufferPool(retentionRents: 0);
            nint a = pool.Rent(4 * KiB);
            pool.Return(a);
            nint b = pool.Rent(8 * KiB); // one rent later

            pool.CleanupOldBuffers();
            Assert.AreEqual(2, pool.BufferCount, "idle for one rent with retention 1: kept");

            pool.Rent(16 * KiB);
            pool.CleanupOldBuffers();
            Assert.AreEqual(2, pool.BufferCount, "idle for two rents: released (8 KiB + 16 KiB remain, both in use)");
            Assert.AreNotEqual((nint)0, b);
        }

        #endregion

        #region Dispose

        [TestMethod]
        public void Rent_AfterDispose_Throws()
        {
            var pool = new UnmanagedBufferPool();
            pool.Return(pool.Rent(4096));
            pool.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => pool.Rent(4096));
        }

        [TestMethod]
        public void Dispose_FreesEverything_AndIsIdempotent()
        {
            var pool = new UnmanagedBufferPool();
            nint inUse = pool.Rent(4096);
            pool.Return(pool.Rent(8192));
            Assert.AreEqual(2, pool.BufferCount);

            pool.Dispose();
            pool.Dispose();

            Assert.AreEqual(0, pool.BufferCount);

            // Late returns from pipeline threads that are still winding down are ignored.
            pool.Return(inUse);
            pool.Return(0);
            pool.CleanupOldBuffers();
            Assert.AreEqual(0, pool.BufferCount);
        }

        #endregion

        #region Allocation / concurrency

        [TestMethod]
        public void SteadyState_RentAndReturn_AllocateNoManagedMemory()
        {
            using var pool = new UnmanagedBufferPool();
            int[] sizes = { 1_000, 30_000, 5_000, 3_110_400, 30_500, 900 };

            // Warm-up: creates the buffers and JIT-compiles the paths.
            for (int i = 0; i < 2_000; i++)
            {
                nint a = pool.Rent(sizes[i % sizes.Length]);
                nint b = pool.Rent(sizes[(i + 1) % sizes.Length]);
                pool.Return(a);
                pool.Return(b);
            }

            int buffersAfterWarmUp = pool.BufferCount;

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50_000; i++)
            {
                nint a = pool.Rent(sizes[i % sizes.Length]);
                nint b = pool.Rent(sizes[(i + 1) % sizes.Length]);
                pool.Return(a);
                pool.Return(b);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "steady-state rent/return must not allocate managed memory");
            Assert.AreEqual(buffersAfterWarmUp, pool.BufferCount, "steady state must not grow the pool");
        }

        [TestMethod, Timeout(120_000)]
        public void ConcurrentRentAndReturn_NeverSharesABuffer()
        {
            using var pool = new UnmanagedBufferPool();
            const int threadCount = 4;
            const int iterations = 20_000;
            int corruption = 0;

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int id = t + 1;
                threads[t] = new Thread(() =>
                {
                    var random = new Random(id);
                    for (int i = 0; i < iterations; i++)
                    {
                        int size = random.Next(1, 20_000);
                        nint buffer = pool.Rent(size);
                        int stamp = (id << 24) | i;

                        // Stamp both ends; if another thread got the same buffer the stamp changes.
                        Marshal.WriteInt32(buffer, stamp);
                        Marshal.WriteByte(buffer, size - 1 < 4 ? 4 : size - 1, (byte)id);
                        Thread.Yield();
                        if (Marshal.ReadInt32(buffer) != stamp || Marshal.ReadByte(buffer, size - 1 < 4 ? 4 : size - 1) != (byte)id)
                            Interlocked.Increment(ref corruption);

                        pool.Return(buffer);
                    }
                })
                { IsBackground = true };
            }

            foreach (Thread thread in threads)
                thread.Start();
            foreach (Thread thread in threads)
                Assert.IsTrue(thread.Join(60_000));

            Assert.AreEqual(0, corruption, "two threads held the same buffer at the same time");
            Assert.IsTrue(pool.BufferCount <= threadCount * 4, $"{pool.BufferCount} buffers for {threadCount} threads");
        }

        #endregion
    }
}
