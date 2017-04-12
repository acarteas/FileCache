using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FC.UnitTests
{
    [Serializable]
    public class CustomObjB
    {
        public CustomObjA obj { get; set; }
        public int Num { get; set; }
    }
}
