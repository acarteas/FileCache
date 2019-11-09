using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

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
            string region = "";
            if (string.IsNullOrEmpty(regionName) == false)
            {
                region = regionName;
            }
            string directory = Path.Combine(CacheDir, CacheSubFolder, region);
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
            if (regionName == null)
            {
                regionName = "";
            }
            string directory = Path.Combine(CacheDir, CacheSubFolder, regionName);
            string filePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(FileName) + ".dat");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return filePath;
        }

        /// <summary>
        /// Builds a string that will get the path to the supplied file's policy file
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override string GetPolicyPath(string key, string regionName = null)
        {
            if (regionName == null)
            {
                regionName = "";
            }
            string directory = Path.Combine(CacheDir, PolicySubFolder, regionName);
            string filePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(key) + ".policy");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return filePath;
        }


    }
}