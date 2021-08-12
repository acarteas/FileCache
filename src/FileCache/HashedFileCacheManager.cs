using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YYProject.XXHash;

namespace System.Runtime.Caching
{
    /// <summary>
    /// File-based caching using xxHash.  Collisions are handled by appending
    /// numerically ascending identifiers to each hash key (e.g. _1, _2, etc.).
    /// </summary>
    public class HashedFileCacheManager : FileCacheManager
    {
        private static XXHash64 _hasher = new XXHash64();
        /// <summary>
        /// Returns a 64bit hash in hex of supplied key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string ComputeHash(string key)
        {
            _hasher.ComputeHash(Encoding.UTF8.GetBytes(key));
            return _hasher.HashUInt64.ToString("x");
        }

        /// <summary>
        /// Because hash collisions prevent us from knowing the exact file name of the supplied key, we need to probe through
        /// all possible fine name combinations.  This function is used internally by the Delete and Get functions in this class.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        private string GetFileName(string key, string regionName = null)
        {
            if (regionName == null)
            {
                regionName = "";
            }

            //CacheItemPolicies have references to the original key, which is why we look there.  This implies that
            //manually deleting a policy in the file system has dire implications for any keys that probe after
            //the policy.  It also means that deleting a policy file makes the related .dat "invisible" to FC.
            string directory = Path.Combine(CacheDir, PolicySubFolder, regionName);

            string hash = ComputeHash(key);
            int hashCounter = 0;
            string fileName = Path.Combine(directory, string.Format("{0}_{1}.policy", hash, hashCounter));
            bool found = false;
            while (found == false)
            {
                fileName = Path.Combine(directory, string.Format("{0}_{1}.policy", hash, hashCounter));
                if (File.Exists(fileName) == true)
                {
                    //check for correct key
                    try
                    {
                        SerializableCacheItemPolicy policy = Deserialize(fileName) as SerializableCacheItemPolicy;
                        if (key.CompareTo(policy.Key) == 0)
                        {
                            //correct key found!
                            found = true;
                        }
                        else
                        {
                            //wrong key, try again
                            hashCounter++;
                        }
                    }
                    catch
                    {
                        //Corrupt file?  Assume usable for current key.
                        found = true;
                    }

                }
                else
                {
                    //key not found, must not exist.  Return last generated file name.
                    found = true;
                }

            }

            return Path.GetFileNameWithoutExtension(fileName);
        }

        /// <summary>
        /// Builds a string that will place the specified file name within the appropriate 
        /// cache and workspace folder.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        public override string GetCachePath(string key, string regionName = null)
        {
            if (regionName == null)
            {
                regionName = "";
            }
            string directory = Path.Combine(CacheDir, CacheSubFolder, regionName);
            string filePath = Path.Combine(directory, GetFileName(key, regionName) + ".dat");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return filePath;
        }

        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        public override IEnumerable<string> GetKeys(string regionName = null)
        {
            string region = "";
            if (string.IsNullOrEmpty(regionName) == false)
            {
                region = regionName;
            }
            string directory = Path.Combine(CacheDir, PolicySubFolder, region);
            List<string> keys = new List<string>();
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    try
                    {
                        SerializableCacheItemPolicy policy = Deserialize(file) as SerializableCacheItemPolicy;
                        keys.Add(policy.Key);
                    }
                    catch
                    {

                    }
                }
            }
            return keys.ToArray();
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
            string filePath = Path.Combine(directory, GetFileName(key, regionName) + ".policy");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return filePath;
        }
    }
}
