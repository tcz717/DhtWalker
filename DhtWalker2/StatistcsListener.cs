using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DhtWalker2
{
    public class StatistcsListener : TraceListener
    {
        public Dictionary<string, int> Statistics { get; set; } = new Dictionary<string, int>();
        public override void Write(string message)
        {
        }

        public override void WriteLine(string message)
        {
            if (Statistics.Keys.Contains(message))
            {
                Statistics[message]++;
            }
            else
            {
                Statistics[message] = 0;
            }
        }
    }
}
