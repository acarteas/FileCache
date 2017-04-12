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

namespace Example
{
    [Serializable]
    class GenericDTO
    {
        public string StringProperty { get; set; }
        public int IntProperty { get; set; }
    }
}
