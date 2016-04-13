using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tancoder.Torrent.BEncoding;
using Tancoder.Torrent.Client;
using Tancoder.Torrent.Dht;
using Tancoder.Torrent.Dht.Listeners;
using Tancoder.Torrent.Dht.Messages;

namespace DhtWalker2
{
    public abstract class MainUI
    {
        public readonly int initResolverCount = 10;
        public readonly int maxResolverCount = 50;
        public List<Task> activeTasks = new List<Task>();
        public SeedCargo cargo;
        public DhtListener listener;
        public DhtSpider spider;
        protected Timer timer;
        public MainUI()
        {
            Init();
            InitDb();
        }

        public event EventHandler<string> OnSavedMetadata;

        public event EventHandler OnSavingMetadata;

        public virtual void Run()
        {
            spider.Start();
            // Task.Run(new Action(MetaDataResolver));
            TaskFactory fact = new TaskFactory();
            for (int i = 0; i < initResolverCount; i++)
                activeTasks.Add(fact.StartNew(new Action<object>(MetadataResolver), false));
        }

        protected void Init()
        {
            listener = new DhtListener(new IPEndPoint(IPAddress.Any, 6881));
            spider = new DhtSpider(listener);
            spider.NewMetadata += Spider_NewMetadata;

            GetPeers.Hook = delegate (DhtMessage msg)
            {
                var m = msg as GetPeersResponse;
                var nid = spider.GetNeighborId(m.Id);
                m.Parameters["id"] = nid.BencodedString();
                return true;
            };
        }

        protected void InitDb()
        {
            if(!Program.NoMongo)
            {
                cargo = new SeedCargo();
                spider.Filter = new MongoSeedFilter(cargo);
            }
        }
        protected abstract void MessageLoop_OnError(object sender, string e);

        protected abstract void MessageLoop_ReceivedMessage(object sender, MessageEventArgs e);

        protected abstract void MessageLoop_SentMessage(object sender, MessageEventArgs e);

        private void MetadataResolver(object id)
        {
            while (true)
            {
                try
                {
                    var info = spider.Pop();
                    if (info.Key == null || info.Value == null)
                    {
                        if ((bool)(id ?? true))
                            return;
                        Thread.Sleep(1000);
                        continue;
                    }
                    OnSavingMetadata?.Invoke(Task.CurrentId, new EventArgs());
                    //using (WireClient client = new WireClient(info.Value, 60000 + (int)(id ?? 0)))
                    using (WireClient client = new WireClient(info.Value))
                    {
                        var metadata = client.GetMetaData(info.Key);
                        if (metadata != null)
                        {
                            var name = ((BEncodedString)metadata["name"]).Text;
                            var hash = BitConverter.ToString(info.Key.Hash).Replace("-", "");

                            if (Program.SaveSeed)
                                File.WriteAllBytes(@"seeds\" + hash + ".torrent", metadata.Encode());

                            Trace.Indent();
                            Trace.WriteLine(name, "Seed");
                            Trace.WriteLine(hash, "InfoHash");
                            Trace.Unindent();


                            if (!Program.NoMongo)
                            {
                                cargo.Add(metadata, info.Key.Hash);
                            }
                            OnSavedMetadata?.Invoke(Task.CurrentId, name);
                        }
                    }
                }
                catch (SocketException ex)
                {
                    MessageLoop_OnError(this, "Socket " + ex.ErrorCode.ToString());
                    Trace.WriteLine(ex.ErrorCode, "Socket");
                }
                catch (IOException)
                {
                    MessageLoop_OnError(this, "Socket Closed");
                }
                catch (AggregateException ex)
                {
                    MessageLoop_OnError(this, ex.ToString());
                }
                catch (Exception ex)
                {
                    Debug.Fail(ex.ToString());
                    MessageLoop_OnError(this, ex.ToString());
                }
                finally
                {
                    Trace.Flush();
                }
            }
        }

        private void Spider_NewMetadata(object sender, Tancoder.Torrent.NewMetadataEventArgs e)
        {
            Debug.Assert(!Program.NoMongo);
            if (activeTasks.Count <= maxResolverCount && spider.GetWaitSeedsCount() > 100)
            {
                var task = Task.Run(() => MetadataResolver(true));
                task.ContinueWith(n => activeTasks.Remove(task));
                activeTasks.Add(task);
            }
        }
    }
}