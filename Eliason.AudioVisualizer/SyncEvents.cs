using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Eliason.AudioVisualizer
{
    public class SyncEvents
    {
        public SyncEvents()
        {

            this.NewItemEvent = new AutoResetEvent(false);
            this.ExitThreadEvent = new ManualResetEvent(false);
            this.EventArray = new WaitHandle[2];
            this.EventArray[0] = this.NewItemEvent;
            this.EventArray[1] = this.ExitThreadEvent;
        }

        public EventWaitHandle ExitThreadEvent { get; private set; }
        public EventWaitHandle NewItemEvent { get; private set; }
        public WaitHandle[] EventArray { get; private set; }
    }
}
