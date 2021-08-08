namespace System.Runtime.Caching
{
    public enum FileCacheManagers { Basic, Hashed }

    public class FileCacheManagerFactory
    {
        public static FileCacheManager Create(FileCacheManagers type)
        {
            switch (type)
            {
                case FileCacheManagers.Basic: return new BasicFileCacheManager();
                case FileCacheManagers.Hashed: return new HashedFileCacheManager();
                default: return new BasicFileCacheManager();
            }
        }
    }
}
