/*
Copyright 2012, 2013 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://fc.codeplex.com).

FileCache is distributed under the Microsoft Public License (Ms-PL).
Consult "LICENSE.txt" included in this package for the complete Ms-PL license.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;

namespace Codeplex.FileCache
{
    [Serializable]
    public class SerializableCacheItemPolicy
    {
        public DateTimeOffset AbsoluteExpiration { get; set; }

        private TimeSpan _slidingExpiration;
        public TimeSpan SlidingExpiration
        {
            get
            {
                return _slidingExpiration;
            }
            set
            {
                _slidingExpiration = value;
                if (_slidingExpiration > new TimeSpan())
                {
                    AbsoluteExpiration = DateTimeOffset.Now.Add(_slidingExpiration);
                }
            }
        }
        public SerializableCacheItemPolicy(CacheItemPolicy policy)
        {
            AbsoluteExpiration = policy.AbsoluteExpiration;
            SlidingExpiration = policy.SlidingExpiration;
        }

        public SerializableCacheItemPolicy()
        {
            SlidingExpiration = new TimeSpan();
        }
    }
}
