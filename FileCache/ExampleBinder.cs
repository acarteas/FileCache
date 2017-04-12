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
using System.Reflection;

namespace System.Runtime.Caching
{
    /// <summary>
    /// You should be able to copy & paste this code into your local project to enable caching custom objects.
    /// </summary>
    public sealed class ExampleBinder : System.Runtime.Serialization.SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            Type typeToDeserialize = null;

            String currentAssembly = Assembly.GetExecutingAssembly().FullName;

            // In this case we are always using the current assembly
            assemblyName = currentAssembly;

            // Get the type using the typeName and assemblyName
            typeToDeserialize = Type.GetType(String.Format("{0}, {1}",
                typeName, assemblyName));

            return typeToDeserialize;
        }
    }
}
