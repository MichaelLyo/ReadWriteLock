using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;

// 姓名：阚璇
// 学号：1353202

// 基本从0开始实现了读写锁功能（多线程读，单线程写），且写比读优先
// 但是因为通过一个dictionary进行多线程之间信息的共享，效率还能够进一步提高
// 除读写锁之外，理解了线程加锁时发生冲突的时候应该如何处理，但是这个方法是靠轮询实现的
// 还想看一看官方是否有更高效的方法

namespace ConsoleApplicationLock
{

    // 线程对象的集合，可以获取当前线程以及其他线程加锁信息
    // 用字典存储 key 为线程id, value 为某种锁在改线程的添加次数
    internal class ThreadCountCollection
    {
        public ThreadCountCollection()
        {
            threadCounts = new Dictionary<int, int>();
        }

        private Dictionary<int, int> threadCounts;


        public void RemoveThreadCount()
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            lock (threadCounts)
            {
                if (threadCounts.ContainsKey(id))
                {
                    threadCounts[id]--;
                    if (threadCounts[id] <= 0)
                    {
                        threadCounts.Remove(id);
                    }
                }
            }

        }

        public void AddThreadCount()
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            lock (threadCounts)
            {
                if (!threadCounts.ContainsKey(id))
                {

                    threadCounts.Add(Thread.CurrentThread.ManagedThreadId, 1);

                }
                else
                {
                    threadCounts[id]++;
                }
            }
        }

        public int Count { get { return threadCounts.Count; } }


        public bool HasCurrentThreadLock()
        {
            return threadCounts.ContainsKey(Thread.CurrentThread.ManagedThreadId);
        }

        public bool ContainOtherThreads()
        {
            int id = Thread.CurrentThread.ManagedThreadId;
            if (threadCounts.Count >= 2)
                return true;
            if (threadCounts.Count == 1)
            {
                threadCounts.ContainsKey(id);
                return false;
            }
            else
            {
                return true;
            }
        }

        public void CleanUp()
        {
            threadCounts.Clear();
        }
    }

    // 多线程中相关类的状态
    // 并提供可用和不可用两种静态状态，方便后面为一些错误值赋值
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

    // 锁的管理类 
    // 通过两个全局唯一变量readThreadCounts writeThreadCounts
    // 来对不同线程的锁进行管理
    public sealed class MutilThreadReadWriterLock : IDisposable
    {
        static MutilThreadReadWriterLock()
        {
            // static 变量 
            readThreadCounts = new ThreadCountCollection();
            writeThreadCounts = new ThreadCountCollection();
        }

        public MutilThreadReadWriterLock()
        {
            rwLock = new TimeSpanWaitor();
            dispose = false;

        }

        private TimeSpanWaitor rwLock;

        private bool dispose;

        static private ThreadCountCollection readThreadCounts;

        static private ThreadCountCollection writeThreadCounts;

        public bool HasReadLock()
        {
            return readThreadCounts.Count > 0;
        }

        public bool HasCurrentThreadReadLock()
        {
            if (HasReadLock())
                return readThreadCounts.HasCurrentThreadLock();
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
                return writeThreadCounts.HasCurrentThreadLock();
            else
                return false;
        }

        private bool HasOtherThreadWriteLock()
        {
            if (HasWriteLock())
                return writeThreadCounts.ContainOtherThreads();

            else
                return false;
        }


        // 允许同时读，不允许读同时写
        private bool TryLockRead()
        {
            bool temp = HasOtherThreadWriteLock();
            if (!temp)
                readThreadCounts.AddThreadCount();
            return !temp;

        }

        //不允许同时写，且写优先
        private bool TryLockWrite()
        {
            bool temp = HasOtherThreadWriteLock();
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

            // 为保证线程安全，所有对锁的操作都应通过WaitForTime执行
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
                        state = new MutiThreadDisposeState(isvalid, false, this);
                   
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
                        state = new MutiThreadDisposeState(isvalid, true, this);
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


        public void Dispose()
        {
            Func<bool> f = () =>
            {
                dispose = true;
                return true;
            };
            rwLock.WaitForTime(TimeSpan.MaxValue, f);
        }

    }



    internal class MutiThreadDisposeState : IDisposeState
    {
        private bool isValid;

        private bool isWriteLock;

        private MutilThreadReadWriterLock owner;

        public MutiThreadDisposeState(bool isvalid, bool iswritelock, MutilThreadReadWriterLock rwl)
        {
            isValid = isvalid;
            isWriteLock = iswritelock;
            owner = rwl;
        }



        public bool IsValid
        {
            get { return isValid; }
        }


        public void Dispose()
        {
            if (owner != null)
                owner.UnLock(this);
        }


        public bool IsWriteLock { get { return isWriteLock; } }

        public void Reset(bool isvalid, bool iswritelock, MutilThreadReadWriterLock rwl)
        {
            isValid = isvalid;
            isWriteLock = iswritelock;
            owner = rwl;
        }

    }



    // 让函数可以随机等待一段时间后再去执行
    // 并且在操作条件不满足时，等一段时间后再次执行
    // 直到执行完毕或者超时跳过
    public sealed class TimeSpanWaitor
    {
        public TimeSpanWaitor(int min, int max)
        {
            asyncObject = new IntLock();
            waitTimeRandom = new Random();

            int tmin = min, tmax = max;
            if (tmin < 0)
                tmin = 10;
            if (tmax < 0)
                tmax = 100;
            if (tmin > tmax)
            {
                int temp = tmax;
                tmax = tmin;
                tmin = temp;
            }
            if (tmin == tmax)
            {
                tmin = 10;
                tmax = 100;
            }
            minWaitMillSeconds = tmin;
            maxWaitMillSeconds = tmax;

        }

        public TimeSpanWaitor() : this(DefaultMinWaitTimeMillSeconds, DefaultMaxWaitTimeMillSeconds) { }

        public const int DefaultMaxWaitTimeMillSeconds = 100;

        public const int DefaultMinWaitTimeMillSeconds = 10;


        private IntLock asyncObject;

        private Random waitTimeRandom = null;

        private int maxWaitMillSeconds = 0;

        private int minWaitMillSeconds = 0;


        private PerWaitNum TryEnter(Func<bool> onEnter)
        {
            bool success = asyncObject.Lock();
            if (success)
            {
                PerWaitNum r = PerWaitNum.SuccessAndContinue;
                if (onEnter())
                    r = PerWaitNum.SuccessAndExits;
                asyncObject.UnLock();
                return r;
            }
            return PerWaitNum.Fail;
        }


        private bool WaitTime(ref TimeSpan waitTimeOut, ref DateTime dt)
        {
            if (TimeSpan.MaxValue == waitTimeOut)
            {
                Thread.Sleep(waitTimeRandom.Next(minWaitMillSeconds, maxWaitMillSeconds));
                dt = DateTime.Now;
                return true;
            }
            else if (TimeSpan.MinValue == waitTimeOut)
            {
                dt = DateTime.Now;
                return false;
            }
            else if (waitTimeOut == TimeSpan.Zero)
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

        // timeout最大等待时间，超出即超时
        // onEnter要执行的函数
        public bool WaitForTime(TimeSpan timeout, Func<bool> onEnter)
        {
            var temp = timeout;
            DateTime nowTime = DateTime.Now;
            PerWaitNum type = TryEnter(onEnter);
            while (type != PerWaitNum.SuccessAndExits)
            {
                if (!WaitTime(ref timeout, ref nowTime))
                {
                    break;
                }
                type = TryEnter(onEnter);
            }
            return type == PerWaitNum.SuccessAndExits;
        }
    }

    // 线程内保障锁的添加和移除,确保不会重复添加和移除
    internal sealed class IntLock
    {
        int radom;

        public IntLock()
        {
            radom = 0;
        }

        public bool Lock()
        {
            return Interlocked.CompareExchange(ref radom, 1, 0) == 0;
        }


        public bool UnLock()
        {
            return Interlocked.CompareExchange(ref radom, 0, 1) == 1;
        }
    }


    // 委托函数执行的状态
    internal enum PerWaitNum
    {
        SuccessAndExits,
        SuccessAndContinue,
        Fail
    }



    // 表示状态的一个接口
    // 使用 using 语法
    public interface IDisposeState : IDisposable
    {
        bool IsValid { get; }
    }



    // 测试代码
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

            for (int i = 0; i < threadNum; i++)
            {
                threadreads[i] = new Thread(TestRead);
                threadwrites[i] = new Thread(TestWrite);
            }

            for (int i = 0; i < threadNum; i++)
            {
                threadreads[i].Start();
                threadwrites[i].Start();
            }

            Thread[] threads = new Thread[threadNum];
            for (int i = 0; i < threadNum; i++)
            {
                threads[i] = new Thread(Test);
            }

            for (int i = 0; i < threadNum; i++)
            {
                threads[i].Start();
            }

            Console.WriteLine();

        }
    }
}
