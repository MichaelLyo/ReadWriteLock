using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ConsoleApplicationLock
{

    // 线程的存储类，Count对应加锁次数
    internal class ThreadCount
    {
        public ThreadCount()
        {
            threadID = Thread.CurrentThread.ManagedThreadId;
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

    // 线程对象的集合，可以获取当前线程并获取其他线程加锁信息
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

    // 
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

    //
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
            rwLock = new TimeSpanWaitor();
            dispose = false;

        }

        #region Private

        private TimeSpanWaitor rwLock;

        private bool dispose;

        private ThreadCountCollection readThreadCounts;

        private ThreadCountCollection writeThreadCounts;

        #endregion

        public bool HasReadLock()
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

        private bool HasOtherThreadReadLock()
        {
            if (HasReadLock())
                return readThreadCounts.ContainOtherThreads();
            else
                return false;
        }


        public bool HasWriteLock()
        {
            return writeThreadCounts.Count > 0;
        }

        public bool HasCurrentThreadWriteLock()
        {
            if (HasWriteLock())
                return writeThreadCounts.GetCurrentThreadCount() != null;
            else
                return false;
        }

        private bool HasOtherThreadWriteLock()
        {
            if (HasReadLock())
                return writeThreadCounts.ContainOtherThreads();
            else
                return false;
        }

        private bool TryLockRead()
        {
            bool temp = HasOtherThreadWriteLock();
            if (!temp)
                readThreadCounts.AddThreadCount();
            return !temp;

        }

        private bool TryLockWrite()
        {
            bool temp = HasOtherThreadReadLock();
            if (!temp)
                temp = HasOtherThreadWriteLock();
            if (!temp)
                writeThreadCounts.AddThreadCount();
            return !temp;
        }

        internal void UnLock(MutiThreadDisposeState state)
        {
            Func<bool> ul = () =>
            {
                if (((IDisposeState)state).IsValid)
                {
                    if (state.IsWriteLock)
                        writeThreadCounts.RemoveThreadCount();
                    else
                        readThreadCounts.RemoveThreadCount();
                }
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, ul);
        }

        public IDisposeState LockRead(TimeSpan timeout)
        {
            return LockRead(timeout, null);
        }

        public IDisposeState LockWrite(TimeSpan timeout)
        {
            return LockWrite(timeout, null);
        }


        public IDisposeState LockRead(TimeSpan timeout, Func<bool> isvalidstate)
        {
            IDisposeState state = null;

            Func<bool> f = () =>
            {
                if (dispose)
                {
                    state = DisposeState.Empty;
                    return true;
                }

                if (TryLockRead())
                {
                    bool isvalid = isvalidstate != null ? isvalidstate() : true;
                    if (isvalid)
                        state = MutilThreadDisposeStatePools.GetMutilThreadDisposeState(true, true, this);
                    return true;
                }
                else
                {
                    return false;
                }
            };

            if (rwLock.WaitForTime(timeout, f))
                return state;
            else
                return DisposeState.Empty;
        }


        public IDisposeState LockWrite(TimeSpan timeout, Func<bool> isvalidstate)
        {
            IDisposeState state = null;
            Func<bool> f = () =>
            {
                if (dispose)
                {
                    state = DisposeState.Empty;
                    return true;
                }
                if (TryLockWrite())
                {
                    bool isvalid = isvalidstate != null ? isvalidstate() : true;
                    if (isvalid)
                        state =
                            MutilThreadDisposeStatePools.GetMutilThreadDisposeState(
                            isvalid, true, this);
                    else
                        state = DisposeState.Empty;
                    return true;
                }
                else
                {
                    return false;
                }
            };
            if (rwLock.WaitForTime(timeout, f))
                return state;
            else
                return DisposeState.Empty;
        }

        public void FreeAllLock()
        {
            Func<bool> f = () =>
            {
                readThreadCounts.CleanUp();
                writeThreadCounts.CleanUp();
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, f);
        }

        public bool HasCurrentLockWrite()
        {
            bool rb = false;
            Func<bool> f = () =>
            {
                rb = HasCurrentThreadWriteLock();
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, f);
            return rb;
        }

        public bool HasLockWrite()
        {
            bool rb = false;
            Func<bool> f = () =>
            {
                rb = HasWriteLock();
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, f);
            return rb;
        }

        public void Dispose()
        {
            Func<bool> f = () =>
            {
                dispose = true;
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, f);
        }

        public static readonly IDisposeState EmptyNullDisposedState;

        public static readonly IDisposeState IsValidDisposedState;
    }



    internal class MutiThreadDisposeState : IDisposeState
    {
        private bool isValid;

        private bool isWriteLock;

        private MutilThreadReadWriterLock owner;



        public bool IsValid
        {
            get { return isValid; }
        }


        public void Dispose()
        {
            if (owner != null)
                owner.UnLock(this);
            MutilThreadDisposeStatePools.MutilThreadDisposeStateToBuffer(this);
        }


        public bool IsWriteLock { get { return isWriteLock; } }

        public void Reset(bool isvalid, bool iswritelock, MutilThreadReadWriterLock rwl)
        {
            isValid = isvalid;
            isWriteLock = iswritelock;
            owner = rwl;
        }

    }


    // 存储所有线程中锁的状态，全局唯一
    internal class MutilThreadDisposeStatePools
    {
        static MutilThreadDisposeStatePools()
        {
            global = new MutilThreadDisposeStatePools();

        }

        public MutilThreadDisposeStatePools()
        {
            buffers = new List<MutiThreadDisposeState>();
        }

        private List<MutiThreadDisposeState> buffers;

        private static MutilThreadDisposeStatePools global;


        internal MutiThreadDisposeState GetState(
          bool isvalid, bool iswritelock, MutilThreadReadWriterLock owner)
        {
            lock (buffers)
            {
                if (buffers.Count > 0)
                {
                    MutiThreadDisposeState x = buffers[0];
                    x.Reset(isvalid, iswritelock, owner);
                    buffers.RemoveAt(0);
                    return x;
                }
                else
                {
                    MutiThreadDisposeState x = new MutiThreadDisposeState();
                    x.Reset(isvalid, iswritelock, owner);
                    return x;
                }
            }
        }

        internal void ToBuffer(MutiThreadDisposeState b)
        {
            lock (buffers)
            {
                b.Reset(false, false, null);
                buffers.Add(b);
            }
        }

        internal void ClearBuffer()
        {
            lock (buffers)
            {
                buffers.Clear();
            }
        }


        internal static MutiThreadDisposeState GetMutilThreadDisposeState(
            bool isvalid, bool iswritelock, MutilThreadReadWriterLock owner)
        {
            return global.GetState(isvalid, iswritelock, owner);
        }

        internal static void MutilThreadDisposeStateToBuffer(MutiThreadDisposeState state)
        {
            global.ToBuffer(state);
        }

        internal static void ClearGobalBuffer()
        {
            global.ClearBuffer();
        }

    }


    public sealed class TimeSpanWaitor
    {
        public TimeSpanWaitor(int min,int max)
        {
            asyncObject = new IntLock();
            waitTimeRandom = new Random();
            defaultWaitTime = new TimeSpan(0, 0, 1);
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

        //timeout最大等待时间，超出即超时
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
    


    // 表示状态的一个接口，用在其他对象锁定方法中的返回值
    public interface IDisposeState : IDisposable
    {
        bool IsValid { get; }
    }




    class Program
    {
        static int t = 0;

        static int id = 1;

        static int writeID = 1;

        static int readID = 1;

        static void Test()
        {
            MutilThreadReadWriterLock x = new MutilThreadReadWriterLock();

            TimeSpan sp = new TimeSpan(0, 0, 1, 0);

            Console.WriteLine("Thread ID: {0}", id);

            Interlocked.Increment(ref id);

            while (true)
            {
                using (IDisposeState y = x.LockRead(sp))
                {
                    if (y.IsValid)
                        Console.WriteLine("Number: {0}", t);
                }

                using (IDisposeState y = x.LockWrite(sp))
                {
                    if (y.IsValid)
                        t++;
                }
            }
        }


        static void TestRead()
        {
            MutilThreadReadWriterLock x = new MutilThreadReadWriterLock();

            TimeSpan sp = new TimeSpan(0, 0, 1, 0);

            Console.WriteLine("Thread ID: {0}", readID);

            Interlocked.Increment(ref readID);

            while (true)
            {
                using (IDisposeState y = x.LockRead(sp))
                {
                    if (y.IsValid)
                        Console.WriteLine("Number: {0}", t);
                }
            }
        }

        static void TestWrite()
        {
            MutilThreadReadWriterLock x = new MutilThreadReadWriterLock();

            TimeSpan sp = new TimeSpan(0, 0, 1, 0);

            Console.WriteLine("Thread ID: {0}", writeID);

            Interlocked.Increment(ref writeID);

            while (true)
            {

                using (IDisposeState y = x.LockWrite(sp))
                {
                    if (y.IsValid)
                        t++;
                }
            }
        }

        static void Main(string[] args)
        {
            int threadNum = 25;


            Thread[] threadreads = new Thread[threadNum];

            Thread[] threadwrites = new Thread[threadNum];

            for(int i=0; i<threadNum;i++)
            {
                threadreads[i] = new Thread(TestRead);
                threadwrites[i] = new Thread(TestWrite);
            }

            for(int i=0; i<threadNum;i++)
            {
                threadreads[i].Start();
                threadwrites[i].Start();
            }

            //Thread[] threads = new Thread[threadNum];
            //for(int i=0;i< threadNum; i++)
            //{
            //    threads[i] = new Thread(Test);
            //}

            //for (int i = 0; i < threadNum; i++)
            //{
            //    threads[i].Start();
            //}

            Console.WriteLine();
           
        }
    }
}
