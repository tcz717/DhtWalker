using CLRCLI.Widgets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tancoder.Torrent.BEncoding;
using Tancoder.Torrent.Client;
using Tancoder.Torrent.Dht;
using Tancoder.Torrent.Dht.Listeners;
using Tancoder.Torrent.Dht.Messages;

namespace DhtWalker2
{
    class MainWindow :MainUI
    {
        Label tleLab;
        Label timeLab;
        ListBox totalLsb;
        ListBox seedLsb;
        ListBox errorLsb;
        ListBox sendLsb;
        ListBox receiveLsb;
        Button refindBtn;
        RootWindow root;


        int SendPerSec = 0;
        int RecvPerSec = 0;
        int SeedPerMin = 0;
        int _seedPerMin = 0;
        float sendFind = 0;
        float recvFind = 0;
        int getSeed = 0;
        int trySeed = 0;
        DateTime lastMin = DateTime.Now;
        DateTime InitTime = DateTime.Now;
        int seedindex = 0;

        public MainWindow()
        {
            root = new RootWindow();
        }
        
        private void InitUI()
        {
            tleLab = new Label(root)
            {
                Text = "Log Info"
            };
            timeLab = new Label(root)
            {
                Text = DateTime.Now.ToString(),
                Width = 20,
                Left = 36+40-20,
            };
            refindBtn = new Button(root)
            {
                Left = 10,
                Text = "ReStart Find",
                Width = 15,
                Height = 1,
            };
            refindBtn.Clicked += RefindBtn_Clicked;
            totalLsb = new ListBox(root)
            {
                Top = 1,
                Width = 35,
                Height = 10,
                Border = CLRCLI.BorderStyle.Thin,
            };
            seedLsb = new ListBox(root)
            {
                Top = 1 + 10 + 1,
                Width = 35,
                Height = 10,
                Border = CLRCLI.BorderStyle.Thin,
            };
            for (int i = 0; i < 10; i++) seedLsb.Items.Add("");
            new Label(root) { Text = "Seeds", Top = 1 + 10 };
            sendLsb = new ListBox(root)
            {
                Top = 1,
                Left = 36,
                Width = 40,
                Height = 6,
            };
            new Label(root) { Text = "Send Statistic", Left = 36, };
            receiveLsb = new ListBox(root)
            {
                Top = 1 + 6 + 1,
                Left = 36,
                Width = 40,
                Height = 6,
            };
            new Label(root) { Text = "Receive Statistic", Top = 1 + 6, Left = 36, };
            errorLsb = new ListBox(root)
            {
                Top = 1 + 6 + 6 + 2,
                Left = 36,
                Width = 40,
                Height = 9,
            };
            new Label(root) { Text = "Error Statistic", Top = 6 + 6 + 2, Left = 36, };

            timer = new Timer(delegate (object state)
            {
                spider.SendFindNodes();
                var repoert = spider.GetReport();
                UpdateUI(repoert);
            });

            spider.MessageLoop.ReceivedMessage += MessageLoop_ReceivedMessage;
            spider.MessageLoop.SentMessage += MessageLoop_SentMessage;
            spider.MessageLoop.OnError += MessageLoop_OnError;
            OnSavedMetadata += MainWindow_OnSavedMetadata;
            OnSavingMetadata += MainWindow_OnSavingMetadata;
        }

        private void MainWindow_OnSavingMetadata(object sender, EventArgs e)
        {
            trySeed++;
        }
        private void MainWindow_OnSavedMetadata(object sender, string name)
        {
            lock (seedLsb)
            {
                getSeed++;
                seedLsb.Items[(seedindex++) % 10] = name.Length > 34 ? name.Substring(0, 34) : name;
            }
        }

        private void RefindBtn_Clicked(object sender, EventArgs e)
        {
            spider.Start();
        }

        private void UpdateUI(Dictionary<string, object> repoter)
        {
            int i;

            repoter.Add(nameof(SeedPerMin), SeedPerMin.ToString());
            if ((DateTime.Now - lastMin).Minutes > 0)
            {
                lastMin = DateTime.Now;
                SeedPerMin = _seedPerMin;
                _seedPerMin = 0;
            }
            repoter.Add(nameof(SendPerSec), string.Format("{0}/{1}", SendPerSec, spider.MessageLoop.GetWaitSendCount())); SendPerSec = 0;
            repoter.Add(nameof(RecvPerSec), string.Format("{0}/{1}", RecvPerSec, spider.MessageLoop.GetWaitReceiveCount())); RecvPerSec = 0;
            repoter.Add("AnswerRate", (recvFind / sendFind * 100.0f).ToString("f2") + "%");
            repoter.Add("RunTime", (DateTime.Now - InitTime).ToString(@"hh\:mm\:ss"));
            repoter.Add("Got Seeds", string.Format("{0}/{1}({2:f2}%)", getSeed, trySeed, trySeed > 0 ? getSeed * 100.0 / trySeed : 0));
            repoter.Add("Active Tasks", activeTasks.Count);

            while (totalLsb.Items.Count < repoter.Count)
                totalLsb.Items.Add("");
            i = 0;
            foreach (var item in repoter)
            {
                totalLsb.Items[i] = (string.Format("{0,-15}{1,19}", item.Key, item.Value));
                i++;
            }
            lock (receivelog)
            {
                while (receiveLsb.Items.Count < receivelog.Count)
                    receiveLsb.Items.Add("");
                i = 0;
                foreach (var item in receivelog)
                {
                    receiveLsb.Items[i] = (string.Format("{0,-30}{1,9}", item.Key, item.Value));
                    i++;
                }
            }
            lock (sendlog)
            {
                while (sendLsb.Items.Count < sendlog.Count)
                    sendLsb.Items.Add("");
                i = 0;
                foreach (var item in sendlog)
                {
                    sendLsb.Items[i] = (string.Format("{0,-30}{1,9}", item.Key, item.Value));
                    i++;
                }
            }
            lock (errorlog)
            {
                while (errorLsb.Items.Count < errorlog.Count)
                    errorLsb.Items.Add("");
                i = 0;
                foreach (var item in errorlog)
                {
                    errorLsb.Items[i] = (string.Format("{0,-30}{1,9}", item.Key, item.Value));
                    i++;
                }
            }
        }

        Dictionary<string, int> sendlog = new Dictionary<string, int>();
        protected override void MessageLoop_SentMessage(object sender, MessageEventArgs e)
        {
            lock (sendlog)
            {
                var name = e.Message.GetType().Name.ToString();
                SendPerSec++;
                if (e.Message is FindNode)
                    sendFind++;
                if (sendlog.ContainsKey(name))
                    sendlog[name]++;
                else
                    sendlog[name] = 1;
            }
        }

        Dictionary<string, int> receivelog = new Dictionary<string, int>();
        protected override void MessageLoop_ReceivedMessage(object sender, MessageEventArgs e)
        {
            lock (receivelog)
            {
                var name = e.Message.GetType().Name.ToString();
                RecvPerSec++;
                if (e.Message is FindNodeResponse)
                    recvFind++;
                else if (e.Message is AnnouncePeer)
                    _seedPerMin++;
                if (receivelog.ContainsKey(name))
                    receivelog[name]++;
                else
                    receivelog[name] = 1;
            }
        }

        Dictionary<string, int> errorlog = new Dictionary<string, int>();
        protected override void MessageLoop_OnError(object sender, string e)
        {
            lock (errorlog)
            {
                var name = e.Substring(0, Math.Min(30, e.Length));
                if (errorlog.ContainsKey(name))
                    errorlog[name]++;
                else
                    errorlog[name] = 1;
            }
            //Trace.WriteLine(e);
        }
        
        public override void Run()
        {
            base.Run();
            InitUI();
            timer.Change(1000, 1000);
            root.Run();
        }
    }
}
