// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BasicTest
{
    public class TaskCore
    {
        public TaskCore()
        {
        }

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

        public TaskCore Create()
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
