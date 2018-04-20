/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using System;

namespace FC.UnitTests
{
    [Serializable]
    public class CustomObjB
    {
        public CustomObjA obj { get; set; }
        public int Num { get; set; }
    }
}
