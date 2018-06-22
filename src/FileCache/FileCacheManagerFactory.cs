using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Runtime.Caching
{
    public enum FileCacheManagers { Basic }
    public class FileCacheManagerFactory
    {
        public static FileCacheManager Create(FileCacheManagers type)
        {
            switch(type)
            {
                case FileCacheManagers.Basic:
                    return new BasicFileCacheManager();
            }
            return new BasicFileCacheManager();
        }
    }
}
