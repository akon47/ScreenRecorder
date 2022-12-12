using System;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Encoder
{
    public class CircularBuffer
    {
        #region Fields

        private readonly byte[] _buffer;
        private readonly object _lockObject;
        private int _writePosition;
        private int _readPosition;
        private int _byteCount;

        #endregion

        #region Constructors

        public CircularBuffer(int size)
        {
            _buffer = new byte[size];
            _lockObject = new object();
        }

        #endregion

        #region Helpers

        public int Write(byte[] data, int offset, int count)
        {
            lock (_lockObject)
            {
                var bytesWritten = 0;
                if (count > _buffer.Length - _byteCount)
                {
                    count = _buffer.Length - _byteCount;
                }
                var writeToEnd = Math.Min(_buffer.Length - _writePosition, count);
                Array.Copy(data, offset, _buffer, _writePosition, writeToEnd);
                _writePosition += writeToEnd;
                _writePosition %= _buffer.Length;
                bytesWritten += writeToEnd;
                if (bytesWritten < count)
                {
                    Array.Copy(data, offset + bytesWritten, _buffer, _writePosition, count - bytesWritten);
                    _writePosition += (count - bytesWritten);
                    bytesWritten = count;
                }
                _byteCount += bytesWritten;
                return bytesWritten;
            }
        }

        public int Write(IntPtr data, int offset, int count)
        {
            lock (_lockObject)
            {
                var bytesWritten = 0;
                if (count > _buffer.Length - _byteCount)
                {
                    count = _buffer.Length - _byteCount;
                }
                var writeToEnd = Math.Min(_buffer.Length - _writePosition, count);
                Marshal.Copy(IntPtr.Add(data, offset), _buffer, _writePosition, writeToEnd);
                _writePosition += writeToEnd;
                _writePosition %= _buffer.Length;
                bytesWritten += writeToEnd;
                if (bytesWritten < count)
                {
                    Marshal.Copy(IntPtr.Add(data, offset + bytesWritten), _buffer, _writePosition, count - bytesWritten);
                    _writePosition += (count - bytesWritten);
                    bytesWritten = count;
                }
                _byteCount += bytesWritten;
                return bytesWritten;
            }
        }

        public int Read(byte[] data, int offset, int count)
        {
            lock (_lockObject)
            {
                if (count > _byteCount)
                {
                    count = _byteCount;
                }
                var bytesRead = 0;
                var readToEnd = Math.Min(_buffer.Length - _readPosition, count);
                Array.Copy(_buffer, _readPosition, data, offset, readToEnd);
                bytesRead += readToEnd;
                _readPosition += readToEnd;
                _readPosition %= _buffer.Length;

                if (bytesRead < count)
                {
                    Array.Copy(_buffer, _readPosition, data, offset + bytesRead, count - bytesRead);
                    _readPosition += (count - bytesRead);
                    bytesRead = count;
                }

                _byteCount -= bytesRead;
                return bytesRead;
            }
        }

        public int Read(IntPtr data, int count)
        {
            lock (_lockObject)
            {
                if (count > _byteCount)
                {
                    count = _byteCount;
                }
                var bytesRead = 0;
                var readToEnd = Math.Min(_buffer.Length - _readPosition, count);
                Marshal.Copy(_buffer, _readPosition, data, readToEnd);
                bytesRead += readToEnd;
                _readPosition += readToEnd;
                _readPosition %= _buffer.Length;

                if (bytesRead < count)
                {
                    Marshal.Copy(_buffer, _readPosition, IntPtr.Add(data, bytesRead), count - bytesRead);
                    _readPosition += (count - bytesRead);
                    bytesRead = count;
                }

                _byteCount -= bytesRead;
                return bytesRead;
            }
        }

        public int MaxLength => _buffer.Length;
        public int Count
        {
            get
            {
                lock (_lockObject)
                {
                    return _byteCount;
                }
            }
        }

        public void Reset()
        {
            lock (_lockObject)
            {
                ResetInner();
            }
        }

        private void ResetInner()
        {
            _byteCount = 0;
            _readPosition = 0;
            _writePosition = 0;
        }

        public void Advance(int count)
        {
            lock (_lockObject)
            {
                if (count >= _byteCount)
                {
                    ResetInner();
                }
                else
                {
                    _byteCount -= count;
                    _readPosition += count;
                    _readPosition %= MaxLength;
                }
            }
        }

        public void Clear()
        {
            Reset();
        }

        public void WriteByte(byte item)
        {
            lock (_lockObject)
            {
                if ((_buffer.Length - _byteCount) <= 0)
                {
                    _readPosition = (_readPosition + 1) % _buffer.Length;
                    _byteCount--;
                }
                _buffer[_writePosition] = item;
                _writePosition = (_writePosition + 1) % _buffer.Length;
                _byteCount++;
            }
        }

        public int ReadByte()
        {
            var data = new byte[1];
            if (Read(data, 0, 1) == 1)
            {
                return data[0];
            }
            return -1;
        }

        #endregion
    }
}
