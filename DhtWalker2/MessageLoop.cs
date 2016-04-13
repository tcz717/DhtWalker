#if !DISABLE_DHT
//
// MessageLoop.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2008 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections.Generic;
using System.Text;
using Tancoder.Torrent.Dht.Messages;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using Tancoder.Torrent.BEncoding;
using Tancoder.Torrent.Dht.Listeners;
using Tancoder.Torrent.Common;
using System.Diagnostics;

namespace Tancoder.Torrent.Dht
{
    public class MessageLoop
    {
        private struct SendDetails
        {
            public SendDetails(IPEndPoint destination, DhtMessage message)
            {
                Destination = destination;
                Message = message;
                SentAt = DateTime.MinValue;
            }
            public IPEndPoint Destination;
            public DhtMessage Message;
            public DateTime SentAt;
        }

        public event EventHandler<MessageEventArgs> SentMessage;
        public event EventHandler<MessageEventArgs> ReceivedMessage;
        public event EventHandler<string> OnError;

        IDhtEngine engine;
        DateTime lastSent;
        DhtListener listener;
        private object locker = new object();
        Queue<SendDetails> sendQueue = new Queue<SendDetails>();
        Queue<KeyValuePair<IPEndPoint, DhtMessage>> receiveQueue = new Queue<KeyValuePair<IPEndPoint, DhtMessage>>();
        Thread handleThread;

        private bool CanSend
        {
            get { return  sendQueue.Count > 0 && (DateTime.Now - lastSent) > TimeSpan.FromMilliseconds(5); }
        }

        public int GetWaitSendCount()
        {
            lock(locker)
            {
                return sendQueue.Count;
            }
        }
        public int GetWaitReceiveCount()
        {
            lock (locker)
            {
                return receiveQueue.Count;
            }
        }

        public MessageLoop(IDhtEngine engine, DhtListener listener)
        {
            this.engine = engine;
            this.listener = listener;
            listener.MessageReceived += new MessageReceived(OnMessageReceived);
            handleThread = new Thread(new ThreadStart(delegate
            {
                while(true)
                {
                    if (engine.Disposed)
                        return;
                    try
                    {
                        SendMessage();
                        ReceiveMessage();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error in DHT main loop:");
                        Debug.WriteLine(ex);
                    }

                    Thread.Sleep(3);
                }
            }));
        }

        void OnMessageReceived(byte[] buffer, IPEndPoint endpoint)
        {
            lock (locker)
            {
                // I should check the IP address matches as well as the transaction id
                // FIXME: This should throw an exception if the message doesn't exist, we need to handle this
                // and return an error message (if that's what the spec allows)
                try
                {
                    DhtMessage message;
                    string error;
                    if (MessageFactory.TryNoTraceDecodeMessage((BEncodedDictionary)BEncodedValue.Decode(buffer, 0, buffer.Length, false), out message, out error))
                    {
                        if (message is FindNode && receiveQueue.Count > 200)
                        {
                            RaiseOnError("Dump excess msg");
                            return;
                        }
                        receiveQueue.Enqueue(new KeyValuePair<IPEndPoint, DhtMessage>(endpoint, message));
                    }
                    else
                        RaiseOnError(error ?? "Bad Message");
                }
                catch (Exception ex)
                {
                    RaiseOnError(string.Format("OMGZERS! {0}", ex));
                    Debug.WriteLine(ex);
                    //throw new Exception("IP:" + endpoint.Address.ToString() + "bad transaction:" + e.Message);
                }
            }
        }

        private void RaiseMessageSent(IPEndPoint endpoint, DhtMessage query)
        {
            EventHandler<MessageEventArgs> h = SentMessage;
            if (h != null)
                h(this, new MessageEventArgs(endpoint, query));
        }
        private void RaiseOnError(string ex)
        {
            EventHandler<string> h = OnError;
            if (h != null)
                h(this, ex);
        }

        private void SendMessage()
        {
            SendDetails? send = null;
            if (CanSend)
                send = sendQueue.Dequeue();

            if (send != null)
            {
                SendMessage(send.Value.Message, send.Value.Destination);
                SendDetails details = send.Value;
                details.SentAt = DateTime.UtcNow;
            }

        }

        public void Start()
        {
            if (listener.Status != ListenerStatus.Listening)
            {
                listener.Start();
                handleThread.Start();
            }
        }

        public void Stop()
        {
            if (listener.Status != ListenerStatus.NotListening)
            {
                listener.Stop();
                handleThread.Start();
            }
        }

        private void ReceiveMessage()
        {
            if (receiveQueue.Count == 0)
                return;

            KeyValuePair<IPEndPoint, DhtMessage> receive;
            lock (locker)
            {
                receive = receiveQueue.Dequeue();
            }
            DhtMessage m = receive.Value;
            IPEndPoint source = receive.Key;

            if (m == null || source == null)
            {
                return;
            }
            try
            {
                if (m is QueryMessage)
                    m.Handle(engine, new Node(m.Id, source));
                else if (m is ErrorMessage)
                    RaiseOnError(((ErrorMessage)m).ErrorList.ToString());
                RaiseMessageReceived(source, m);
            }
            catch (Exception ex)
            {
                RaiseOnError(string.Format("Handle Error for message: {0}", ex));
                Debug.WriteLine(ex);
            }
        }

        private void RaiseMessageReceived(IPEndPoint endPoint, DhtMessage message)
        {
            EventHandler<MessageEventArgs> h = ReceivedMessage;
            if (h != null)
                h(this, new MessageEventArgs(endPoint, message));
        }

        private void SendMessage(DhtMessage message, IPEndPoint endpoint)
        {
            lastSent = DateTime.Now;
            byte[] buffer = message.Encode();
            listener.Send(buffer, endpoint);
            RaiseMessageSent(endpoint, message);
        }

        public void EnqueueSend(DhtMessage message, IPEndPoint endpoint)
        {
            lock (locker)
            {
                if (message.TransactionId == null)
                {
                    if (message is ResponseMessage)
                        throw new ArgumentException("Message must have a transaction id");
                    //do
                    //{
                        message.TransactionId = TransactionId.NextId();
                    //} while (MessageFactory.IsRegistered(message.TransactionId));
                }

                // We need to be able to cancel a query message if we time out waiting for a response
                //if (message is QueryMessage)
                //    MessageFactory.RegisterSend((QueryMessage)message);

                sendQueue.Enqueue(new SendDetails(endpoint, message));
            }
        }

        public void EnqueueSend(DhtMessage message, Node node)
        {
            EnqueueSend(message, node.EndPoint);
        }
    }
}
#endif