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
using System.Runtime.Caching;

namespace Example
{
    class Program
    {
        static void Main(string[] args)
        {
            //basic example
            FileCache simpleCache = new FileCache();
            string foo = "bar";
            simpleCache["foo"] = foo;
            Console.WriteLine("Reading foo from simpleCache: {0}", simpleCache["foo"]);

            //example with custom data binder (needed for caching user defined classes)
            FileCache binderCache = new FileCache();//new ObjectBinder());
            GenericDTO dto = new GenericDTO()
            {
                IntProperty = 5,
                StringProperty = "foobar"
            };
            binderCache["dto"] = dto;
            GenericDTO fromCache = binderCache["dto"] as GenericDTO;
            Console.WriteLine(
                                "Reading DTO from binderCache:\n\tIntProperty:\t{0}\n\tStringProperty:\t{1}", 
                                fromCache.IntProperty, 
                                fromCache.StringProperty
                             );
        }
    }
}
