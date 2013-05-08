MemoryMessagePipe
=================

Allows for communication between two processes using memory-mapped files.

Sending a message:
```csharp
using (var sender = new MemoryMappedFileMessageSender("foo"))
{
    sender.SendMessage(stream => serializer.Serialize(stream, new Foo {Bar = "foobar"}));
}
```

Receiving a message:
```csharp
using (var receiver = new MemoryMappedFileMessageReceiver("foo"))
{
    var message = receiver.ReceiveMessage(stream => serializer.Deserialize<Foo>(stream));
}
```

There should be exactly one sender and exactly one receiver per file.
Communication is uni-directional.
Use two files for bi-directional communication.
Multiple messages can be sent/received, but the sender will wait until the receiver has received a message before sending subsequent messages.
