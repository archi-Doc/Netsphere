// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BasicTest
{
    public class TaskCore : TaskCoreBase
    {
        public static TaskCoreBase Root { get; } = new();

        public TaskCore(Action<object?> method)
            : base()
        {
            this.Task = new Task(method, this);
        }

        public Task Task { get; }
    }

    public class TaskCore<T> : TaskCoreBase
    {
        public TaskCore(Func<object?, T> method)
            : base()
        {
            this.Task = new Task<T>(method, this);
        }

        public Task<T> Task { get; }
    }

    public class ThreadCore : TaskCoreBase
    {
        public ThreadCore(Action<object?> method)
            : base()
        {
            this.Thread = new Thread(new ParameterizedThreadStart(method));
        }

        public Thread Thread { get; }
    }

    public class TaskCoreBase
    {
        internal TaskCoreBase()
        {
        }

        public TaskCore CreateTask(Action<object?> method)
        {
            var c = new TaskCore(method);
            return c;
        }

        public Thread? Thread { get; }

        public bool IsTerminated
        {
            get
            {
                return true;
            }
        }

        public bool IsPaused
        {
            get
            {
                return true;
            }
        }

        public TaskCoreBase Create()
        {
            return new();
        }

        public void Terminate()
        {
        }

        public void Pause()
        {
        }
    }
}
