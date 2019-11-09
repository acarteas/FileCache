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
        /// <param name="mode">the payload reading mode</param>
        /// <param name="key"></param>
        /// <param name="objectBinder"></param>
        /// <returns></returns>
        protected internal override FileCachePayload ReadFile(FileCache.PayloadMode mode, string key, string regionName = null, SerializationBinder objectBinder = null)
        {
            object data = null;
            SerializableCacheItemPolicy policy = new SerializableCacheItemPolicy();
            string cachePath = GetCachePath(key, regionName);
            string policyPath = GetPolicyPath(key, regionName);
            FileCachePayload payload = new FileCachePayload(null);

            if (File.Exists(cachePath))
            {
                switch (mode)
                {
                    default:
                    case FileCache.PayloadMode.Filename:
                        data = cachePath;
                        break;
                    case FileCache.PayloadMode.Serializable:
                        data = DeserializePayloadData(objectBinder, cachePath);
                        break;
                    case FileCache.PayloadMode.RawBytes:
                        data = LoadRawPayloadData(cachePath);
                        break;
                }
            }
            if (File.Exists(policyPath))
            {
                using (FileStream stream = GetStream(policyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    // TODO: WAS LocalCacheBinder() - not sure if this merge was correct
                    formatter.Binder = this.Binder;
                    try
                    {
                        policy = formatter.Deserialize(stream) as SerializableCacheItemPolicy;
                    }
                    catch (SerializationException)
                    {
                        policy = new SerializableCacheItemPolicy();
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
        public override long WriteFile(FileCache.PayloadMode mode, string key, FileCachePayload data, string regionName = null, bool policyUpdateOnly = false)
        {
            string cachedPolicy = GetPolicyPath(key, regionName);
            string cachedItemPath = GetCachePath(key, regionName);
            long cacheSizeDelta = 0;

            if (!policyUpdateOnly)
            {
                long oldBlobSize = 0;
                if (File.Exists(cachedItemPath))
                {
                    oldBlobSize = new FileInfo(cachedItemPath).Length;
                }

                switch (mode)
                {
                    case FileCache.PayloadMode.Serializable:
                        using (FileStream stream = GetStream(cachedItemPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {

                            BinaryFormatter formatter = new BinaryFormatter();
                            formatter.Serialize(stream, data.Payload);
                        }
                        break;
                    case FileCache.PayloadMode.RawBytes:
                        using (FileStream stream = GetStream(cachedItemPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {

                            if (data.Payload is byte[])
                            {
                                byte[] dataPayload = (byte[])data.Payload;
                                stream.Write(dataPayload, 0, dataPayload.Length);
                            }
                            else if (data.Payload is Stream)
                            {
                                Stream dataPayload = (Stream)data.Payload;
                                dataPayload.CopyTo(stream);
                                // no close or the like, we are not the owner
                            }
                        }
                        break;

                    case FileCache.PayloadMode.Filename:
                        File.Copy((string)data.Payload, cachedItemPath, true);
                        break;
                }

                //adjust cache size (while we have the file to ourselves)
                cacheSizeDelta += new FileInfo(cachedItemPath).Length - oldBlobSize;
            }

            //remove current policy file from cache size calculations
            if (File.Exists(cachedPolicy))
            {
                cacheSizeDelta -= new FileInfo(cachedPolicy).Length;
            }

            //write the cache policy
            using (FileStream stream = GetStream(cachedPolicy, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, data.Policy);

                // adjust cache size
                cacheSizeDelta += new FileInfo(cachedPolicy).Length;

                stream.Close();
            }

            return cacheSizeDelta;
        }

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

        private object LoadRawPayloadData(string cachePath)
        {
            throw new NotSupportedException("Reading raw payload is not currently supported.");
        }

        private object DeserializePayloadData(SerializationBinder objectBinder, string cachePath)
        {
            object data;
            using (FileStream stream = GetStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read))
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
            }

            return data;
        }


    }
}