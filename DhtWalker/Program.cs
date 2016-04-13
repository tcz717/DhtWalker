using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Dht;
using MonoTorrent.Dht.Listeners;
using System.Net;
using MonoTorrent.Dht.Messages;
using MonoTorrent.Dht.Tasks;
using MonoTorrent.BEncoding;

namespace DhtWalker
{
    class Program
    {
        static DhtEngine engine;
        static HashSet<Node> nodes;
        static Node[] bootstrap;
        static Queue<Node> find_queue = new Queue<Node>();
        static Dictionary<string, int> log = new Dictionary<string, int>();
        private static readonly int maxnodes = 200;
        private static int activities = 0;
        private static bool clear = true;
        static HashSet<NodeId> seeds = new HashSet<NodeId>();

        static void logAdd(string name)
        {
            lock(log)
            {
                if (log.ContainsKey(name))
                    log[name]++;
                else
                    log[name] = 1;
            }
        }

        static void Main(string[] args)
        {
            //var obj = Bencode.BencodeUtility.Decode(
            //    "d1:ad2:id20:abcdefghij01234567899:info_hash20:mnopqrstuvwxyz123456e1:q9:get_peers1:ti0e1:y1:qeDE<");

            DhtListener listener = new DhtListener(new IPEndPoint(IPAddress.Any, 6881));
            engine = new DhtEngine(listener) { Bootstrap = true };
            nodes = new HashSet<Node>();
            bootstrap = new Node[]
            {
                new Node
                (
                    NodeId.Create(),
                    new IPEndPoint(Dns.GetHostEntry("router.bittorrent.com").AddressList[0], 6881)
                ),
                new Node
                (
                    NodeId.Create(),
                    new IPEndPoint(Dns.GetHostEntry("dht.transmissionbt.com").AddressList[0], 6881)
                )
            };

            GetPeers.Hook = delegate (Message msg)
              {
                  var m = msg as GetPeersResponse;
                  byte[] nid = new byte[m.Id.Bytes.Length];
                  Array.Copy(m.Id.Bytes, nid, nid.Length / 2);
                  Array.Copy(engine.RoutingTable.LocalNode.Id.Bytes, nid.Length / 2,
                      nid, nid.Length / 2, nid.Length / 2);
                  m.Parameters["id"] = new NodeId(nid).BencodedString();
                  return true;
              };

            listener.MessageReceived += Listener_MessageReceived;
            
            engine.StateChanged += Engine_StateChanged;
            engine.Start();

            SendFindNode(bootstrap, NodeId.Create());

            System.Timers.Timer timer = new System.Timers.Timer(1000);
            timer.Elapsed += Timer_Elapsed;
            timer.Start();

            while (true)
            {
                ConsoleKey key = Console.ReadKey().Key;

                switch (key)
                {
                    case ConsoleKey.Execute:
                        return;
                    case ConsoleKey.Enter:
                        if(activities < 50)
                            SendFindNode(nodes, NodeId.Create());
                        break;
                    case ConsoleKey.Spacebar:
                        clear = !clear;
                        break;
                    default:
                        break;
                }
            }
        }

        private static void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            List<Node> next = new List<Node>(maxnodes);
            lock (find_queue)
            {
                for (int i = 0; i < maxnodes &&
                                find_queue.Count > 0 &&
                                activities < maxnodes * 10; i++)
                {
                    var n = find_queue.Dequeue();
                    if (!nodes.Contains(n))
                    {
                        next.Add(n);
                        logAdd("Send FindNode");
                    }
                    nodes.Add(n);
                }
            }
            SendFindNode(next, NodeId.Create());

            if (nodes.Count > 10000)
                nodes = new HashSet<Node>();

            Display();
        }

        private static void Display()
        {
            if (clear)
                Console.Clear();
            Console.WriteLine("Wait count {0}", find_queue.Count);
            Console.WriteLine("Seed count {0}", seeds.Count);
            Console.WriteLine("Node count {0}", nodes.Count);
            Console.WriteLine("Activity count {0}", activities);
            lock(log)
            {
                foreach (var item in log)
                {
                    Console.WriteLine("{0} count {1}", item.Key, item.Value);
                }
            }
        }

        private static void Listener_MessageReceived(byte[] buffer, IPEndPoint endpoint)
        {
            try
            {
                Message message;
                string error;
                var data = (BEncodedDictionary)BEncodedValue.Decode(buffer, 0, buffer.Length, false);
                if (MessageFactory.TryDecodeMessage(data, out message, out error))
                {
                    logAdd(message.GetType().Name);
                    Console.WriteLine("Get Msg {0}", message.GetType().Name);
                    //if (message is GetPeers)
                    //    seeds.Add(((GetPeers)message).InfoHash);
                    //else 
                    if (message is AnnouncePeer)
                        seeds.Add(((AnnouncePeer)message).InfoHash);
                }
                else
                {
                    logAdd(error);
                    if (message != null)
                        logAdd("Bad " + message.GetType().Name);
                    else
                        logAdd("Null Msg");
                    //Console.WriteLine(Convert.ToString(data));
                }
            }
            catch (MonoTorrent.Dht.MessageException ex)
            {
                Console.WriteLine("Message Exception: {0}", ex);
                // Caused by bad transaction id usually - ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine("OMGZERS! {0}", ex);
                //throw new Exception("IP:" + endpoint.Address.ToString() + "bad transaction:" + e.Message);
            }
        }

        private static void Engine_StateChanged(object sender, EventArgs e)
        {
            var engine = ((DhtEngine)sender);
            if (engine.State == DhtState.Ready)
            {
                Console.WriteLine("Engine Ready");
            }
        }

        private static void SendFindNode(IEnumerable<Node> knonwn, NodeId nodeId)
        {
            foreach (Node node in knonwn)
            {
                FindNode request = new FindNode(engine.LocalId, nodeId);
                SendQueryTask task = new SendQueryTask(engine, request, node);
                task.Completed += FindNodeComplete;
                task.Execute();
                lock(engine)
                    activities++;
            }
        }

        private static void FindNodeComplete(object sender, TaskCompleteEventArgs e)
        {
            SendQueryEventArgs args = (SendQueryEventArgs)e;
            lock (engine)
                activities--;
            if (!args.TimedOut)
            {
                logAdd("Visited");
                if (find_queue.Count < 10000)
                {
                    FindNodeResponse response = (FindNodeResponse)args.Response;
                    var ns = Node.FromCompactNode(response.Nodes);
                    foreach (var n in ns)
                    {
                        //Console.WriteLine("Find {1} Node: {0}", n.Id, nodes.Count);
                        lock (find_queue)
                            find_queue.Enqueue(n);
                    }
                }
            }
            else
                nodes.RemoveWhere(n => ((FindNode)args.Query).Target == n.Id);
        }
    }
}
