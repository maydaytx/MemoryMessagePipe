using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Should;

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

            var task1 = new Task(() =>
            {
                using (var messageSender = new MemoryMappedFileMessageSender(mmfName))
                {
                    messageSender.SendMessage(x => WriteString(x, message1));
                    messageSender.SendMessage(x => WriteString(x, message2));
                }
            });

            string result1 = null, result2 = null;

            var task2 = new Task(() =>
            {
                using (var messageReceiver = new MemoryMappedFileMessageReceiver(mmfName))
                {
                    result1 = messageReceiver.ReceiveMessage(ReadString);
                    result2 = messageReceiver.ReceiveMessage(ReadString);
                }
            });

            task1.Start();
            task2.Start();

            task1.Wait();
            task2.Wait();

            result1.ShouldEqual(message1);
            result2.ShouldEqual(message2);
        }

        [Test]
        public void Should_be_able_to_cancel_message_reception()
        {
            var message = "not null";

            var messageReceiver = new MemoryMappedFileMessageReceiver("test");

            var task = new Task(() => message = messageReceiver.ReceiveMessage(ReadString));

            task.Start();

            messageReceiver.Dispose();

            message.ShouldBeNull();
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
    }
}