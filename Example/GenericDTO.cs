/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Example
{
    [Serializable]
    class GenericDTO
    {
        public string StringProperty { get; set; }
        public int IntProperty { get; set; }
    }
}
