// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable SA1124 // Do not use regions

namespace BasicTest
{
    public class TaskCore : TaskCoreBase
    {
        public static TaskCoreBase Root { get; } = new(null);

        public TaskCore(TaskCoreBase parent, Action<object?> method)
            : base(parent)
        {
            this.Task = new Task(method, this);
            this.Task.Start();
        }

        /*public override void Start()
        {
            this.Task.Start();
        }*/

        public override bool IsRunning => this.Task.Status == TaskStatus.Running;

        public Task Task { get; }

        #region IDisposable Support

        /// <summary>
        /// Finalizes an instance of the <see cref="TaskCore"/> class.
        /// </summary>
        ~TaskCore()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// free managed/native resources.
        /// </summary>
        /// <param name="disposing">true: free managed resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.Task.Dispose();
                }

                base.Dispose(disposing);
            }
        }
        #endregion
    }

    public class TaskCore<T> : TaskCoreBase
    {
        public TaskCore(TaskCoreBase parent, Func<object?, T> method)
            : base(parent)
        {
            this.Task = new Task<T>(method, this);
            this.Task.Start();
        }

        /*public override void Start()
        {
            this.Task.Start();
        }*/

        public override bool IsRunning => this.Task.Status == TaskStatus.Running;

        public Task<T> Task { get; }

        #region IDisposable Support

        /// <summary>
        /// Finalizes an instance of the <see cref="TaskCore{T}"/> class.
        /// </summary>
        ~TaskCore()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// free managed/native resources.
        /// </summary>
        /// <param name="disposing">true: free managed resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.Task.Dispose();
                }

                base.Dispose(disposing);
            }
        }
        #endregion
    }

    public class ThreadCore : TaskCoreBase
    {
        public ThreadCore(TaskCoreBase parent, Action<object?> method)
            : base(parent)
        {
            this.Thread = new Thread(new ParameterizedThreadStart(method));
            this.Thread.Start();
        }

        public override bool IsRunning => this.Thread.IsAlive;

        public Thread Thread { get; }

        /// <summary>
        /// Finalizes an instance of the <see cref="ThreadCore"/> class.
        /// </summary>
        ~ThreadCore()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// free managed/native resources.
        /// </summary>
        /// <param name="disposing">true: free managed resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                }

                base.Dispose(disposing);
            }
        }
    }

    public class TaskCoreBase : IDisposable
    {
        internal TaskCoreBase(TaskCoreBase? parent)
        {
            this.CancellationToken = this.cancellationTokenSource.Token;
            lock (TreeSync)
            {
                this.parent = parent;
                if (parent != null && !parent.IsTerminated)
                {
                    parent.hashSet.Add(this);
                }
            }
        }

        public TaskCore CreateTask(Action<object?> method)
        {
            var c = new TaskCore(this, method);
            return c;
        }

        public TaskCore<T> CreateTask<T>(Func<object?, T> method)
        {
            var c = new TaskCore<T>(this, method);
            return c;
        }

        public ThreadCore CreateThread(Action<object?> method)
        {
            var c = new ThreadCore(this, method);
            return c;
        }

        public CancellationToken CancellationToken { get; }

        public bool IsTerminated => this.CancellationToken.IsCancellationRequested; // Volatile.Read(ref this.terminated);

        public bool IsPaused => Volatile.Read(ref this.paused);

        public virtual bool IsRunning => true;

        /*public virtual void Start()
        {
        }*/

        public void Terminate()
        {
            lock (TreeSync)
            {
                TerminateCore(this);
            }

            static void TerminateCore(TaskCoreBase c)
            {
                // c.terminated = true;
                c.cancellationTokenSource.Cancel();
                foreach (var x in c.hashSet)
                {
                    TerminateCore(x);
                }
            }
        }

        public Task<bool> AsyncWaitForTermination(int millisecondTimeout)
        {
            this.Terminate();

            int interval = 5;
            if (millisecondTimeout < 0)
            {
                millisecondTimeout = int.MaxValue;
            }

            return AsyncWaitForTerminationCore(this);

            async Task<bool> AsyncWaitForTerminationCore(TaskCoreBase c)
            {
                while (true)
                {
                    lock (TreeSync)
                    {
                        var array = c.hashSet.ToArray();
                        foreach (var x in array)
                        {
                            if (!x.IsRunning)
                            {
                                x.Dispose();
                            }
                        }

                        if (c.hashSet.Count == 0)
                        {
                            return true;
                        }
                    }

                    await Task.Delay(interval);
                    millisecondTimeout -= interval;
                    if (millisecondTimeout < 0)
                    {
                        return false;
                    }

                    continue;
                }
            }
        }

        public void Pause()
        {
            lock (TreeSync)
            {
                PauseCore(this);
            }

            static void PauseCore(TaskCoreBase c)
            {
                c.paused = true;
                foreach (var x in c.hashSet)
                {
                    PauseCore(x);
                }
            }
        }

        public void Resume()
        {
            lock (TreeSync)
            {
                ResumeCore(this);
            }

            static void ResumeCore(TaskCoreBase c)
            {
                c.paused = false;
                foreach (var x in c.hashSet)
                {
                    ResumeCore(x);
                }
            }
        }

        private static object TreeSync { get; } = new object();

        private TaskCoreBase? parent;
        private HashSet<TaskCoreBase> hashSet = new();
        // private bool terminated = false;
        private CancellationTokenSource cancellationTokenSource = new();
        private bool paused = false;

        #region IDisposable Support
#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1401 // Fields should be private
        protected bool disposed = false; // To detect redundant calls.
#pragma warning restore SA1401 // Fields should be private
#pragma warning restore SA1202 // Elements should be ordered by access

        /// <summary>
        /// Finalizes an instance of the <see cref="TaskCoreBase"/> class.
        /// </summary>
        ~TaskCoreBase()
        {
            this.Dispose(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// free managed/native resources.
        /// </summary>
        /// <param name="disposing">true: free managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    // free managed resources.
                    lock (TreeSync)
                    {
                        if (this.parent != null)
                        {
                            this.parent.hashSet.Remove(this);
                        }
                    }
                }

                // free native resources here if there are any.
                this.disposed = true;
            }
        }
        #endregion
    }
}
