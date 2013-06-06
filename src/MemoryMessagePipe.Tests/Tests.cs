using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Should;
using Should.Core.Exceptions;

namespace MemoryMessagePipe.Tests
{
    [TestFixture]
    public class Tests
    {
        [Test]
        public void Should_have_ability_to_send_multiple_messages()
        {
            const string mmfName = "Local\\test";
            const string message1 = "message1";
            const string message2 = "message2";
            string result1 = null, result2 = null;

            RunToCompletion(
                () =>
                {
                    using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                    {
                        messageSender.SendMessage(x => WriteString(x, message1));
                        messageSender.SendMessage(x => WriteString(x, message2));
                    }
                },
                () =>
                {
                    using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                    {
                        result1 = messageReceiver.ReceiveMessage(ReadString);
                        result2 = messageReceiver.ReceiveMessage(ReadString);
                    }
                });

            result1.ShouldEqual(message1);
            result2.ShouldEqual(message2);
        }

        [Test]
        public void Should_be_able_to_cancel_message_reception()
        {
            const string mmfName = "Local\\test";
            var message = "not null";
            var messageCancelled = new EventWaitHandle(false, EventResetMode.ManualReset, mmfName + "_MessageCancelled");

            messageCancelled.Set();

            Task task;

            using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
            {
                task = new Task(() => message = messageReceiver.ReceiveMessage(ReadString));

                task.Start();

                var isSet = true;

                while (isSet)
                    isSet = messageCancelled.WaitOne(0);
            }

            task.Wait();

            message.ShouldBeNull();
        }

        [Test]
        [ExpectedException(typeof (ObjectDisposedException))]
        public void Should_throw_exception_if_disposed()
        {
            var messageReceiver = new MemoryMappedFileMessageReceiver("test");

            messageReceiver.Dispose();

            messageReceiver.ReceiveMessage(ReadString);
        }

        [Serializable]
        private class Foo
        {
            public string Bar { get; set; }
        }

        [Test]
        public void Should_be_able_to_use_binary_formatter()
        {
            const string mmfName = "Local\\test";
            var message = new Foo {Bar = "FooBar"};
            Foo result = null;
            var formatter = new BinaryFormatter();

            RunToCompletion(
                () =>
                {
                    using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                    {
                        messageSender.SendMessage(x => formatter.Serialize(x, message));
                    }
                },
                () =>
                {
                    using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                    {
                        result = (Foo) messageReceiver.ReceiveMessage(formatter.Deserialize);
                    }
                });

            result.Bar.ShouldEqual(message.Bar);
        }

        [Test]
        public void Should_be_able_to_send_messages_much_larger_than_size_of_stream()
        {
            const string mmfName = "Local\\test";
            var message = RandomBuffer((int) (Environment.SystemPageSize*2.5));
            byte[] result = null;

            RunToCompletion(
                () =>
                {
                    using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                    {
                        messageSender.SendMessage(x => x.Write(message, 0, message.Length));
                    }
                },
                () =>
                {
                    using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                    {
                        result = messageReceiver.ReceiveMessage(ReadBytes);
                    }
                });

            result.Length.ShouldEqual(message.Length);

            for (var i = 0; i < result.Length; ++i)
            {
                if (result[i] != message[i])
                {
                    throw new EqualException(message[i], result[i], string.Format("Buffer doesn't match at position {0} (Expected {1} was {2})", i, message[i], result[i]));
                }
            }
        }

        [Test]
        public void Should_cancel_a_message_if_exception_occurs_during_sending_and_return_null_on_receiving_end()
        {
            const string mmfName = "Local\\test";
            string result = null;
            Exception exception = null;

            RunToCompletion(
                () =>
                {
                    using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                    {
                        try
                        {
                            messageSender.SendMessage(x =>
                            {
                                WriteString(x, "message");
                                throw new Exception();
                            });
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }
                },
                () =>
                {
                    using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                    {
                        result = messageReceiver.ReceiveMessage(ReadString);
                    }
                });

            exception.ShouldNotBeNull();
            result.ShouldBeNull();
        }

        [Test]
        public void Should_cancel_a_message_if_exception_occurs_during_receiving()
        {
            const string mmfName = "Local\\test";
            string result = "not null";
            Exception exception = null;

            RunToCompletion(
                () =>
                {
                    using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                    {
                        messageSender.SendMessage(x => WriteString(x, "message"));
                    }
                },
                () =>
                {
                    using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                    {
                        try
                        {
                            result = messageReceiver.ReceiveMessage<string>(x =>
                            {
                                ReadString(x);
                                throw new Exception();
                            });
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }
                });

            exception.ShouldNotBeNull();
            result.ShouldNotBeNull();
        }

        private static void WriteString(Stream stream, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);

            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadString(Stream stream)
        {
            var result = "";
            var buffer = new byte[1024];

            int numRead;

            while ((numRead = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                result += new string(Encoding.UTF8.GetChars(buffer, 0, numRead));
            }

            return result;
        }

        private static byte[] RandomBuffer(int length)
        {
            var random = new Random();
            var buffer = new byte[length];

            random.NextBytes(buffer);

            return buffer;
        }

        private static byte[] ReadBytes(Stream stream)
        {
            var result = new List<byte>();
            var buffer = new byte[1024];

            int numRead;

            while ((numRead = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                result.AddRange(buffer.Take(numRead));
            }

            return result.ToArray();
        }

        private static void RunToCompletion(params Action[] actions)
        {
            var tasks = actions.Select(x => new Task(x)).ToList();

            foreach (var task in tasks)
                task.Start();

            foreach (var task in tasks)
                task.Wait();
        }
    }
}