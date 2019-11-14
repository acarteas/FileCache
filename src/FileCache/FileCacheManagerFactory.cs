using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Runtime.Caching
{
    public enum FileCacheManagers { Basic, Hashed }
    public class FileCacheManagerFactory
    {
        public static FileCacheManager Create(FileCacheManagers type)
        {
            FileCacheManager instance = null;

            switch (type)
            {
                case FileCacheManagers.Basic:
                    instance = new BasicFileCacheManager();
                    break;
                case FileCacheManagers.Hashed:
                    instance = new HashedFileCacheManager();
                    break;
                default:
                    instance = new BasicFileCacheManager();
                    break;
            }
            return instance;
        }
    }
}
