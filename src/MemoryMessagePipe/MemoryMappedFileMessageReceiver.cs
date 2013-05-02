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
        }

        public void Dispose()
        {
            _bytesWrittenAccessor.Dispose();
            _messageCompletedAccessor.Dispose();
            _stream.Dispose();
            _bytesWrittenEvent.Dispose();
            _bytesReadEvent.Dispose();
        }

        public T ReceiveMessage<T>(Func<Stream, T> action)
        {
            _messageSendingEvent.WaitOne();

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

                if (_messageCompleted)
                    return 0;

                if (_shouldWait)
                {
                    _bytesWrittenEvent.WaitOne();
                    _shouldWait = false;

                    _messageCompleted = _messageCompletedAccessor.ReadBoolean(0);
                    _bytesRemainingToBeRead = _bytesWrittenAccessor.ReadInt32(0);
                }

                if (count >= _bytesRemainingToBeRead)
                {
                    _stream.Read(buffer, offset, _bytesRemainingToBeRead);

                    _shouldWait = true;
                    _bytesReadEvent.Set();

                    return _bytesRemainingToBeRead;
                }
                else
                {
                    _stream.Read(buffer, offset, count);

                    _bytesRemainingToBeRead -= count;

                    return count;
                }
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