using System;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Encoder
{
    public class CircularBuffer
    {
        private readonly byte[] buffer;
        private readonly object lockObject;
        private int writePosition;
        private int readPosition;
        private int byteCount;

        public CircularBuffer(int size)
        {
            buffer = new byte[size];
            lockObject = new object();
        }

        public int Write(byte[] data, int offset, int count)
        {
            lock (lockObject)
            {
                var bytesWritten = 0;
                if (count > buffer.Length - byteCount)
                {
                    count = buffer.Length - byteCount;
                }
                int writeToEnd = Math.Min(buffer.Length - writePosition, count);
                Array.Copy(data, offset, buffer, writePosition, writeToEnd);
                writePosition += writeToEnd;
                writePosition %= buffer.Length;
                bytesWritten += writeToEnd;
                if (bytesWritten < count)
                {
                    Array.Copy(data, offset + bytesWritten, buffer, writePosition, count - bytesWritten);
                    writePosition += (count - bytesWritten);
                    bytesWritten = count;
                }
                byteCount += bytesWritten;
                return bytesWritten;
            }
        }

        public int Write(IntPtr data, int offset, int count)
        {
            lock (lockObject)
            {
                var bytesWritten = 0;
                if (count > buffer.Length - byteCount)
                {
                    count = buffer.Length - byteCount;
                }
                int writeToEnd = Math.Min(buffer.Length - writePosition, count);
                Marshal.Copy(IntPtr.Add(data, offset), buffer, writePosition, writeToEnd);
                writePosition += writeToEnd;
                writePosition %= buffer.Length;
                bytesWritten += writeToEnd;
                if (bytesWritten < count)
                {
                    Marshal.Copy(IntPtr.Add(data, offset + bytesWritten), buffer, writePosition, count - bytesWritten);
                    writePosition += (count - bytesWritten);
                    bytesWritten = count;
                }
                byteCount += bytesWritten;
                return bytesWritten;
            }
        }

        public int Read(byte[] data, int offset, int count)
        {
            lock (lockObject)
            {
                if (count > byteCount)
                {
                    count = byteCount;
                }
                int bytesRead = 0;
                int readToEnd = Math.Min(buffer.Length - readPosition, count);
                Array.Copy(buffer, readPosition, data, offset, readToEnd);
                bytesRead += readToEnd;
                readPosition += readToEnd;
                readPosition %= buffer.Length;

                if (bytesRead < count)
                {
                    Array.Copy(buffer, readPosition, data, offset + bytesRead, count - bytesRead);
                    readPosition += (count - bytesRead);
                    bytesRead = count;
                }

                byteCount -= bytesRead;
                return bytesRead;
            }
        }

        public int Read(IntPtr data, int count)
        {
            lock (lockObject)
            {
                if (count > byteCount)
                {
                    count = byteCount;
                }
                int bytesRead = 0;
                int readToEnd = Math.Min(buffer.Length - readPosition, count);
                Marshal.Copy(buffer, readPosition, data, readToEnd);
                bytesRead += readToEnd;
                readPosition += readToEnd;
                readPosition %= buffer.Length;

                if (bytesRead < count)
                {
                    Marshal.Copy(buffer, readPosition, IntPtr.Add(data, bytesRead), count - bytesRead);
                    readPosition += (count - bytesRead);
                    bytesRead = count;
                }

                byteCount -= bytesRead;
                return bytesRead;
            }
        }

        public int MaxLength => buffer.Length;
        public int Count
        {
            get
            {
                lock (lockObject)
                {
                    return byteCount;
                }
            }
        }

        public void Reset()
        {
            lock (lockObject)
            {
                ResetInner();
            }
        }

        private void ResetInner()
        {
            byteCount = 0;
            readPosition = 0;
            writePosition = 0;
        }

        public void Advance(int count)
        {
            lock (lockObject)
            {
                if (count >= byteCount)
                {
                    ResetInner();
                }
                else
                {
                    byteCount -= count;
                    readPosition += count;
                    readPosition %= MaxLength;
                }
            }
        }

        public void Clear()
        {
            Reset();
        }

        public void WriteByte(byte item)
        {
            lock (lockObject)
            {
                if ((buffer.Length - byteCount) <= 0)
                {
                    readPosition = (readPosition + 1) % buffer.Length;
                    byteCount--;
                }
                buffer[writePosition] = item;
                writePosition = (writePosition + 1) % buffer.Length;
                byteCount++;
            }
        }

        public int ReadByte()
        {
            byte[] data = new byte[1];
            if (Read(data, 0, 1) == 1)
            {
                return data[0];
            }
            return -1;
        }
    }
}
