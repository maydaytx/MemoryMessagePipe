using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace MemoryMessagePipe
{
    public class MemoryMappedFileMessageReceiver : IDisposable
    {
        private static readonly int SizeOfFile = Environment.SystemPageSize;
        private const int SizeOfInt32 = sizeof(int);
        private const int SizeOfBool = sizeof(bool);
        private static readonly int SizeOfStream = SizeOfFile - SizeOfInt32 - SizeOfBool - SizeOfBool;

        private readonly MemoryMappedFile _file;
        private readonly MemoryMappedViewAccessor _bytesWrittenAccessor;
        private readonly MemoryMappedViewAccessor _messageCompletedAccessor;
        private readonly MemoryMappedViewStream _stream;
        private readonly EventWaitHandle _messageSendingEvent;
        private readonly EventWaitHandle _messageReadEvent;
        private readonly EventWaitHandle _bytesWrittenEvent;
        private readonly EventWaitHandle _bytesReadEvent;
        private readonly EventWaitHandle _disposingEvent;

        private bool _disposed;

        public MemoryMappedFileMessageReceiver(string name)
        {
            _file = MemoryMappedFile.CreateOrOpen(name, SizeOfFile);
            _bytesWrittenAccessor = _file.CreateViewAccessor(0, SizeOfInt32);
            _messageCompletedAccessor = _file.CreateViewAccessor(SizeOfInt32, SizeOfBool);
            _stream = _file.CreateViewStream(SizeOfInt32 + SizeOfBool + SizeOfBool, SizeOfStream);
            _messageSendingEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_MessageSending");
            _messageReadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_MessageRead");
            _bytesWrittenEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_BytesWritten");
            _bytesReadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + "_BytesRead");
            _disposingEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        public void Dispose()
        {
            _disposed = true;
            _disposingEvent.Set();

            _bytesWrittenAccessor.Dispose();
            _messageCompletedAccessor.Dispose();
            _stream.Dispose();
            _bytesWrittenEvent.Dispose();
            _bytesReadEvent.Dispose();
        }

        public T ReceiveMessage<T>(Func<Stream, T> action)
        {
            WaitHandle.WaitAny(new WaitHandle[] {_messageSendingEvent, _disposingEvent});

            if (_disposed)
                return default(T);

            T result;

            using (var stream = new MemoryMappedInputStream(_bytesWrittenAccessor, _messageCompletedAccessor, _stream, _bytesWrittenEvent, _bytesReadEvent))
            {
                result = action(stream);
            }

            _messageReadEvent.Set();

            return result;
        }

        private class MemoryMappedInputStream : Stream
        {
            private readonly MemoryMappedViewAccessor _bytesWrittenAccessor;
            private readonly MemoryMappedViewAccessor _messageCompletedAccessor;
            private readonly MemoryMappedViewStream _stream;
            private readonly EventWaitHandle _bytesWrittenEvent;
            private readonly EventWaitHandle _bytesReadEvent;

            public MemoryMappedInputStream(MemoryMappedViewAccessor bytesWrittenAccessor,
                                           MemoryMappedViewAccessor messageCompletedAccessor,
                                           MemoryMappedViewStream stream,
                                           EventWaitHandle bytesWrittenEvent,
                                           EventWaitHandle bytesReadEvent)
            {
                _bytesWrittenAccessor = bytesWrittenAccessor;
                _messageCompletedAccessor = messageCompletedAccessor;
                _stream = stream;
                _bytesWrittenEvent = bytesWrittenEvent;
                _bytesReadEvent = bytesReadEvent;
            }

            private int _bytesRemainingToBeRead;
            private int _offset;
            private bool _shouldWait = true;
            private bool _messageCompleted;

            public override void Close()
            {
                _stream.Seek(0, SeekOrigin.Begin);

                base.Close();
            }

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (buffer == null)
                    throw new ArgumentNullException("buffer", "Buffer cannot be null.");
                if (offset < 0)
                    throw new ArgumentOutOfRangeException("offset", "Non-negative number required.");
                if (count < 0)
                    throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
                if (buffer.Length - offset < count)
                    throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");

                if (_messageCompleted && _bytesRemainingToBeRead == 0)
                    return 0;

                if (_shouldWait)
                {
                    _bytesWrittenEvent.WaitOne();

                    _bytesRemainingToBeRead = _bytesWrittenAccessor.ReadInt32(0);
                    _offset = 0;
                    _shouldWait = false;
                    _messageCompleted = _messageCompletedAccessor.ReadBoolean(0);
                }

                var numberOfBytesToRead = count >= _bytesRemainingToBeRead ? _bytesRemainingToBeRead : count;
                var offsetAndBytesToRead = offset + numberOfBytesToRead;

                if (_offset == 0)
                {
                    _stream.Read(buffer, offset, numberOfBytesToRead);
                }
                else
                {
                    var readBuffer = new byte[_offset + offsetAndBytesToRead];
                    _stream.Read(readBuffer, _offset + offset, numberOfBytesToRead);
                    Buffer.BlockCopy(readBuffer, _offset + offset, buffer, offset, numberOfBytesToRead);
                }

                _offset += offsetAndBytesToRead;
                _bytesRemainingToBeRead -= offsetAndBytesToRead;

                if (_bytesRemainingToBeRead == 0)
                {
                    _shouldWait = true;
                    _bytesReadEvent.Set();
                }

                return numberOfBytesToRead;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }
        }
    }
}