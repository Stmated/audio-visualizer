using System.Threading;

namespace Eliason.AudioVisualizer
{
    public class SyncEvents
    {
        public SyncEvents()
        {
            NewItemEvent = new AutoResetEvent(false);
            ExitThreadEvent = new ManualResetEvent(false);
            EventArray = new WaitHandle[2];
            EventArray[0] = NewItemEvent;
            EventArray[1] = ExitThreadEvent;
        }

        public EventWaitHandle ExitThreadEvent { get; }
        public EventWaitHandle NewItemEvent { get; }
        public WaitHandle[] EventArray { get; }
    }
}