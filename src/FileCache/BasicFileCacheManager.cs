using System.Collections.Generic;
using System.IO;

namespace System.Runtime.Caching
{
    public class BasicFileCacheManager : FileCacheManager
    {
        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override IEnumerable<string> GetKeys(string regionName = null)
        {
            string directory = Path.Combine(CacheDir, CacheSubFolder, regionName ?? string.Empty);
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    yield return Path.GetFileNameWithoutExtension(file);
                }
            }
        }

        /// <summary>
        /// Builds a string that will place the specified file name within the appropriate 
        /// cache and workspace folder.
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override string GetCachePath(string FileName, string regionName = null)
        {
            string directory = Path.Combine(CacheDir, CacheSubFolder, regionName ?? string.Empty);
            return GetOrCreateFilePath(directory, FileName, ".dat");
        }

        /// <summary>
        /// Builds a string that will get the path to the supplied file's policy file
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override string GetPolicyPath(string key, string regionName = null)
        {
            string directory = Path.Combine(CacheDir, PolicySubFolder, regionName ?? string.Empty);
            return GetOrCreateFilePath(directory, key, ".policy");
        }

        /// <summary>
        /// Builds a file name and ensures that the containing directory exists.
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="fileName"></param>
        /// <param name="extension"></param>
        /// <returns></returns>
        private string GetOrCreateFilePath(string directory, string fileName, string extension)
        {
            string filePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + extension);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return filePath;
        }
    }
}
