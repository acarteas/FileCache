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
        /// This function serves to centralize file reads within this class.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="objectBinder"></param>
        /// <returns></returns>
        public override FileCachePayload ReadFile(string key, string regionName = null, SerializationBinder objectBinder = null)
        {
            object data = null;
            SerializableCacheItemPolicy policy = new SerializableCacheItemPolicy();
            string cachePath = GetCachePath(key, regionName);
            string policyPath = GetPolicyPath(key, regionName);
            FileCachePayload payload = new FileCachePayload(null);

            if (File.Exists(cachePath))
            {
                using (FileStream stream = GetStream(cachePath, FileMode.Open, FileAccess.Read))
                {
                    BinaryFormatter formatter = new BinaryFormatter();

                    //AC: From http://spazzarama.com//2009/06/25/binary-deserialize-unable-to-find-assembly/
                    //    Needed to deserialize custom objects
                    if (objectBinder != null)
                    {
                        //take supplied binder over default binder
                        formatter.Binder = objectBinder;
                    }
                    else if (Binder != null)
                    {
                        formatter.Binder = Binder;
                    }
                    try
                    {
                        data = formatter.Deserialize(stream);
                    }
                    catch (SerializationException)
                    {
                        data = null;
                    }
                    finally
                    {
                        stream.Close();
                    }
                }
            }
            if (File.Exists(policyPath))
            {
                using (FileStream stream = GetStream(policyPath, FileMode.Open, FileAccess.Read))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Binder = new LocalCacheBinder();
                    try
                    {
                        policy = formatter.Deserialize(stream) as SerializableCacheItemPolicy;
                    }
                    catch (SerializationException)
                    {
                        policy = new SerializableCacheItemPolicy();
                    }
                    finally
                    {
                        stream.Close();
                    }
                }
            }
            payload.Payload = data;
            payload.Policy = policy;
            return payload;
        }

        /// <summary>
        /// This function serves to centralize file writes within this class
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <param name="regionName"></param>
        /// <returns>A long representing the size of the file written to the cache</returns>
        public override long WriteFile(string key, FileCachePayload data, string regionName = null)
        {
            string cachedPolicy = GetPolicyPath(key, regionName);
            string cachedItemPath = GetCachePath(key, regionName);
            long cacheSizeDelta = 0;

            //remove current item / policy from cache size calculations
            if (File.Exists(cachedItemPath))
            {
                cacheSizeDelta -= new FileInfo(cachedItemPath).Length;
            }
            if (File.Exists(cachedPolicy))
            {
                cacheSizeDelta -= new FileInfo(cachedPolicy).Length;
            }

            //write the object payload (lock the file so we can write to it and force others to wait for us to finish)
            using (FileStream stream = GetStream(cachedItemPath, FileMode.Create, FileAccess.Write))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, data.Payload);

                //adjust cache size (while we have the file to ourselves)
                cacheSizeDelta += new FileInfo(cachedItemPath).Length;

                stream.Close();
            }

            //write the cache policy
            using (FileStream stream = GetStream(cachedPolicy, FileMode.Create, FileAccess.Write))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, data.Policy);

                // adjust cache size
                cacheSizeDelta += new FileInfo(cachedPolicy).Length;

                stream.Close();
            }

            // try to update the last access time
            try
            {
                File.SetLastAccessTime(cachedItemPath, DateTime.Now);
            }
            catch (IOException)
            {
            }

            return cacheSizeDelta;
        }

        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override string[] GetKeys(string regionName = null)
        {
            string region = "";
            if (string.IsNullOrEmpty(regionName) == false)
            {
                region = regionName;
            }
            string directory = Path.Combine(CacheDir, CacheSubFolder, region);
            List<string> keys = new List<string>();
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    keys.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            return keys.ToArray();
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
        /// <param name="FileName"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public override string GetPolicyPath(string FileName, string regionName = null)
        {
            if (regionName == null)
            {
                regionName = "";
            }
            string directory = Path.Combine(CacheDir, PolicySubFolder, regionName);
            string filePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(FileName) + ".policy");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return filePath;
        }


    }
}
