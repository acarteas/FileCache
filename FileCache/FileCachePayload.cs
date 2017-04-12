/*
Copyright 2012, 2013 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://fc.codeplex.com).

FileCache is distributed under the Microsoft Public License (Ms-PL).
Consult "LICENSE.txt" included in this package for the complete Ms-PL license.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Codeplex.FileCache
{
    [Serializable]
    public class FileCachePayload
    {
        public object Payload { get; set; }
        public SerializableCacheItemPolicy Policy { get; set; }

        public FileCachePayload(object payload)
        {
            Payload = payload;
            Policy = new SerializableCacheItemPolicy()
            {
                AbsoluteExpiration = DateTime.Now.AddYears(10)
            };
        }

        public FileCachePayload(object payload, SerializableCacheItemPolicy policy)
        {
            Payload = payload;
            Policy = policy;
        }
    }
}
