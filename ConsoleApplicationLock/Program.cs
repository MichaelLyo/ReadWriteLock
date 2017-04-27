using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ConsoleApplicationLock
{
    internal class ThreadCount
    {
        public ThreadCount()
        {
            
        }

        private int threadID;

        public int ThreadID
        {
            get { return threadID; }
        }

        private int count;

        public int Count {
            get { return count; }
            set { count = value; }
        }


    }

    internal class ThreadCountCollection
    {
        public ThreadCountCollection()
        {
            threadCounts = new List<ThreadCount>();
        }

        private List<ThreadCount> threadCounts;


        public ThreadCount GetCurrentThreadCount()
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            foreach(var i in threadCounts)
            {
                if (id == i.ThreadID)
                    return i;
            }
            return null;
        }

        public void RemoveThreadCount()
        {
            var temp = GetCurrentThreadCount();
            if(temp!=null)
            {
                temp.Count--;
                if(temp.Count<=0)
                {
                    threadCounts.Remove(temp);
                }
            }

        }

        public void AddThreadCount()
        {
            var temp = GetCurrentThreadCount();
            if(temp==null)
            {
                threadCounts.Add(new ThreadCount { Count = 1 });
            }
            else
            {
                temp.Count++;
            }
        }

        public int Count { get { return threadCounts.Count; } }

        public bool ContainOtherThreads()
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            foreach(var i in threadCounts)
            {
                if (i.ThreadID != id)
                    return true;
            }
            return false;
        }

        public void CleanUp()
        {
            threadCounts.Clear();
        }
    }

    internal class DisposeState : IDisposeState
    {
        static DisposeState()
        {
            Empty = new DisposeState();
            Valid = new DisposeState(true);
        }

        public DisposeState()
        {
            isValid = false;
        }

        public DisposeState(bool isvalid)
        {
            isValid = isvalid;
        }

        private bool isValid;
        
        public bool IsValid { get { return isValid; } }

        internal static readonly DisposeState Empty;

        internal static readonly DisposeState Valid;

        public void Dispose() { }
    }

    public sealed class MutilThreadReadWriterLock : IDisposable
    {
        static MutilThreadReadWriterLock()
        {
            EmptyNullDisposedState = DisposeState.Empty;
            IsValidDisposedState = DisposeState.Valid;
        }

        public MutilThreadReadWriterLock()
        {
            readThreadCounts = new ThreadCountCollection();
            writeThreadCounts = new ThreadCountCollection();
            timeLock = new TimeSpanWaitor();
            dispose = false;

        }

        #region Private

        private TimeSpanWaitor timeLock;

        private bool dispose;

        private ThreadCountCollection readThreadCounts;

        private ThreadCountCollection writeThreadCounts;

        #endregion

        private bool HasReadLock()
        {
            return readThreadCounts.Count > 0;
        }

        public bool HasCurrentThreadReadLock()
        {
            if (HasReadLock())
                return readThreadCounts.GetCurrentThreadCount() != null;
            else
                return false;
        }

        public static readonly IDisposeState EmptyNullDisposedState;

        public static readonly IDisposeState IsValidDisposedState;
    }



    internal class MutiThreadDisposeState : IDisposeState
    {

    }


    public sealed class TimeSpanWaitor
    {
        public TimeSpanWaitor(int min,int max)
        {
            asyncObject = new IntLock();
            waitTimeRandom = new Random();
            int tmin = min, tmax = max;
            if (tmin < 0)
                tmin = 10;
            if (tmax < 0)
                tmax = 100;
            if(tmin>tmax)
            {
                int temp = tmax;
                tmax = tmin;
                tmin = temp;
            }
            if(tmin==tmax)
            {
                tmin = 10;
                tmax = 100;
            }
            minWaitMillSeconds = tmin;
            maxWaitMillSeconds = tmax;

        }

        public TimeSpanWaitor():this(DefaultMinWaitTimeMillSeconds, DefaultMaxWaitTimeMillSeconds) { }

        public const int DefaultMaxWaitTimeMillSeconds = 100;

        public const int DefaultMinWaitTimeMillSeconds = 10;


        public static readonly IDisposeState EmptyNullDisposedState;

        public static readonly IDisposeState IsValidDisposedState;


        #region Private 

        private IntLock asyncObject;

        private TimeSpan defaultWaitTime;

        private Random waitTimeRandom = null;

        private int maxWaitMillSeconds = 0;

        private int minWaitMillSeconds = 0;

        #endregion

        private PerWaitNum TryEnter(Func<bool> onEnter)
        {
            bool success = asyncObject.Lock();
            if(success)
            {
                PerWaitNum r = PerWaitNum.SuccessAndContinue;
                Exception err = null;

                try
                {
                    if (onEnter())
                        r = PerWaitNum.SuccessAndExits;

                }
                catch(Exception e)
                {
                    err = e;
                }
                finally
                {
                    asyncObject.UnLock();
                }
                if (err != null)
                    throw err;
                return r;
            }
            return PerWaitNum.Fail;
        }


        private bool WaitTime(ref TimeSpan waitTimeOut, ref DateTime dt)
        {
            if(TimeSpan.MaxValue == waitTimeOut)
            {
                Thread.Sleep(waitTimeRandom.Next(minWaitMillSeconds, maxWaitMillSeconds));
                dt = DateTime.Now;
                return true;
            }
            else if(TimeSpan.MinValue==waitTimeOut)
            {
                dt = DateTime.Now;
                return false;
            }
            else if (waitTimeOut==TimeSpan.Zero)
            {
                dt = DateTime.Now;
                return false;
            }
            else
            {
                Thread.Sleep(waitTimeRandom.Next(minWaitMillSeconds, maxWaitMillSeconds));
                waitTimeOut -= getNowDateTimeSpan(ref dt);
                return (waitTimeOut.Ticks > 0);
            }
        }

        private TimeSpan getNowDateTimeSpan(ref DateTime tp)
        {
            DateTime temp = tp;
            tp = DateTime.Now;
            return tp.Subtract(temp);
        }

        public bool WaitForTime(TimeSpan timeout, Func<bool> onEnter)
        {
            var temp = timeout;
            DateTime nowTime = DateTime.Now;
            PerWaitNum type = TryEnter(onEnter);
            while(type != PerWaitNum.SuccessAndExits)
            {
                if(!WaitTime(ref timeout, ref nowTime))
                {
                    break;
                }
                type = TryEnter(onEnter);
            }
            return type == PerWaitNum.SuccessAndExits;
        }
    }


    internal sealed class IntLock
    {
        int radom;

        public IntLock()
        {
            radom = 0;
        }

        public bool Lock()
        {
            return Interlocked.CompareExchange(ref radom, 1, 0)==0;
        }
        

        public bool UnLock()
        {
            return Interlocked.CompareExchange(ref radom, 0, 1)==1;
        }
    }



    internal enum PerWaitNum
    {
        SuccessAndExits,
        SuccessAndContinue,
        Fail
    }
    



    public interface IDisposeState : IDisposable
    {
        bool IsValid { get; }
    }



    public class MyLock
    {
        int writeNum = 0;
        int readNum = 0;

        public void myLock(Object o)
        {

        }
            
           








    }



    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
