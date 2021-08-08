using System.Reflection;

namespace System.Runtime.Caching
{
    internal class LocalCacheBinder : FileCacheBinder
    {
        protected override Assembly GetContainingAssembly()
        {
            return Assembly.GetAssembly(typeof(LocalCacheBinder));
        }
    }
}
