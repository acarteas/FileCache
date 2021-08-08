﻿/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using System.Reflection;

namespace System.Runtime.Caching
{
    /// <summary>
    /// You should be able to copy & paste this code into your local project to enable caching custom objects.
    /// </summary>
    public class FileCacheBinder : System.Runtime.Serialization.SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            assemblyName = GetContainingAssembly().FullName;

            // Get the type using the typeName and assemblyName
            return Type.GetType($"{typeName}, {assemblyName}");
        }

        protected virtual Assembly GetContainingAssembly()
        {
            // In this case we are always using the current assembly
            return Assembly.GetExecutingAssembly();
        }
    }
}
