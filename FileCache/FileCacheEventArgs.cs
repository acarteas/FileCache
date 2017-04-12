using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codeplex.FileCache
{
    public class FileCacheEventArgs : EventArgs
    {
        public long CurrentCacheSize { get; private set; }
        public long MaxCacheSize { get; private set; }
        public FileCacheEventArgs(long currentSize, long maxSize)
        {
            CurrentCacheSize = currentSize;
            MaxCacheSize = maxSize;
        }
    }
}
