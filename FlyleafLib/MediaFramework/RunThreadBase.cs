using System;
using System.Diagnostics;
using System.Threading;

namespace FlyleafLib.MediaFramework
{
    public abstract class RunThreadBase : NotifyPropertyChanged
    {
        Status _Status = Status.Stopped;
        public Status               Status          {
            get => _Status; 
            set 
            { 
                lock (lockStatus)
                {
                    #if DEBUG
                    if (_Status != Status.QueueFull && value != Status.QueueFull && _Status != Status.QueueEmpty && value != Status.QueueEmpty) Log($"{_Status} -> {value}");
                    #endif
                    _Status = value;
                }
            } 
        }
        public bool                 IsRunning       {
            get
            {
                bool ret = false;
                lock (lockStatus) ret = thread != null && thread.IsAlive && Status != Status.Paused;
                return ret;
            }
        }

        public bool                 CriticalArea    { get; protected set; }
        public bool                 Disposed        { get; protected set; } = true;
        public int                  UniqueId        { get; protected set; } = -1;
        public bool                 PauseOnQueueFull{ get; set; }

        protected Thread            thread;
        protected AutoResetEvent    threadARE       = new AutoResetEvent(false);
        protected string            threadName      = "";

        internal object             lockActions     = new object();
        internal object             lockStatus      = new object();

        public RunThreadBase(int uniqueId = -1)
        {
            UniqueId= uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
        }

        public void Pause()
        {
            lock (lockActions)
            {
                lock (lockStatus)
                {
                    PauseOnQueueFull = false;

                    if (Disposed || thread == null || !thread.IsAlive || Status == Status.Stopping || Status == Status.Stopped || Status == Status.Ended || Status == Status.Pausing || Status == Status.Paused) return;
                    Status = Status.Pausing;
                }
                while (Status == Status.Pausing) Thread.Sleep(5);
            }
        }
        public void Start()
        {
            lock (lockActions)
            {
                int retries = 1;
                while (thread != null && thread.IsAlive && CriticalArea)
                {
                    Log($"Start Retry {retries}/5");
                    Thread.Sleep(20);
                    retries++;
                    if (retries > 5) return;
                }

                lock (lockStatus)
                {
                    if (Disposed) return;

                    PauseOnQueueFull = false;

                    if (Status == Status.Draining) while (Status != Status.Draining) Thread.Sleep(3);
                    if (Status == Status.Stopping) while (Status != Status.Stopping) Thread.Sleep(3);
                    if (Status == Status.Pausing)  while (Status != Status.Pausing)  Thread.Sleep(3);

                    if (Status == Status.Ended) return;

                    if (Status == Status.Paused)
                    {
                        threadARE.Set();
                        while (Status == Status.Paused) Thread.Sleep(3);
                        return; 
                    }

                    if (thread != null && thread.IsAlive) return; // might re-check CriticalArea

                    thread = new Thread(() => Run());
                    Status = Status.Running;

                    thread.Name = $"[#{UniqueId}] [{threadName}]"; thread.IsBackground= true; thread.Start();
                    while (!thread.IsAlive) { Log("Waiting thread to come up"); Thread.Sleep(3); }
                }
            }
        }
        public void Stop()
        {
            lock (lockActions)
            {
                lock (lockStatus)
                {
                    PauseOnQueueFull = false;

                    if (Disposed || thread == null || !thread.IsAlive || Status == Status.Stopping || Status == Status.Stopped || Status == Status.Ended) return;
                    if (Status == Status.Pausing) while (Status != Status.Pausing) Thread.Sleep(3);
                    Status = Status.Stopping;
                    threadARE.Set();
                }

                while (Status == Status.Stopping && thread != null && thread.IsAlive) Thread.Sleep(5);
            }
        }

        protected void Run()
        {
            Log($"[Thread] Started ({Status})");

            do
            {
                RunInternal();

                if (Status == Status.Pausing)
                {
                    threadARE.Reset();
                    Status = Status.Paused;
                    threadARE.WaitOne();
                    if (Status == Status.Paused)
                    {
                        #if DEBUG
                        Log($"{_Status} -> {Status.Running}");
                        #endif
                        _Status = Status.Running;
                    }
                }

            } while (Status == Status.Running);

            if (Status != Status.Ended) Status = Status.Stopped;

            Log($"[Thread] Stopped ({Status})");
        }
        protected abstract void RunInternal();

        internal void Log (string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [{threadName}] {msg}"); }
    }

    public enum Status
    {
        Opening,

        Stopping,
        Stopped,
        
        Pausing,
        Paused,

        Running,
        QueueFull,
        QueueEmpty,
        Draining,

        Ended
    }
}
