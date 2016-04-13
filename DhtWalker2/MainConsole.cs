using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tancoder.Torrent.Dht;

namespace DhtWalker2
{
    class MainConsole : MainUI
    {
        protected override void MessageLoop_OnError(object sender, string e)
        {
            //Console.WriteLine(e);
        }

        protected override void MessageLoop_ReceivedMessage(object sender, MessageEventArgs e)
        {
        }

        protected override void MessageLoop_SentMessage(object sender, MessageEventArgs e)
        {
        }

        public override void Run()
        {
            base.Run();
            OnSavedMetadata += MainConsole_OnSavedMetadata;
        }

        private void MainConsole_OnSavedMetadata(object sender, string e)
        {
            //Console.WriteLine(e);
        }
    }
}
