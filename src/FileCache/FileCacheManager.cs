using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace System.Runtime.Caching
{
    public abstract class FileCacheManager
    {
        public string CacheDir { get; set; }
        public string CacheSubFolder { get; set; }
        public string PolicySubFolder { get; set; }
        public SerializationBinder Binder { get; set; }

        /// <summary>
        /// Used to determine how long the FileCache will wait for a file to become 
        /// available.  Default (00:00:00) is indefinite.  Should the timeout be
        /// reached, an exception will be thrown.
        /// </summary>
        public TimeSpan AccessTimeout { get; set; }

        protected virtual object DeserializePayloadData(string fileName, SerializationBinder objectBinder = null)
        {
            object data = null;
            if (File.Exists(fileName))
            {
                using (var stream = OpenRead(fileName))
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
            }

            return data;
        }

        private FileStream OpenRead(string fileName)
        {
            return GetStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <summary>
        /// This function serves to centralize file reads within this class.
        /// </summary>
        /// <param name="mode">the payload reading mode</param>
        /// <param name="key"></param>
        /// <param name="objectBinder"></param>
        /// <returns></returns>
        // TODO: This was protected, but since Simon Thum's additions other methods require this. We need to merge with that change to move whatever is relevant behind this interface
        // and restore the visibility to protected
        public virtual FileCachePayload ReadFile(FileCache.PayloadMode mode, string key, string regionName = null, SerializationBinder objectBinder = null)
        {
            string cachePath = GetCachePath(key, regionName);
            string policyPath = GetPolicyPath(key, regionName);
            FileCachePayload payload = new FileCachePayload(null);
            switch (mode)
            {
                case FileCache.PayloadMode.Filename:
                    payload.Payload = cachePath;
                    break;
                case FileCache.PayloadMode.Serializable:
                    payload.Payload = Deserialize(cachePath);
                    break;
                case FileCache.PayloadMode.RawBytes:
                    payload.Payload = LoadRawPayloadData(cachePath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            try
            {
                // TODO: In part of the merge it looked like the policy was force serialized with LocalCacheBinder(), is this intended?
                payload.Policy = DeserializeCacheItemPolicy(policyPath);
            }
            catch
            {
                payload.Policy = new SerializableCacheItemPolicy();
            }

            return payload;
        }

        private object LoadRawPayloadData(string cachePath)
        {
            throw new NotSupportedException("Reading raw payload is not currently supported.");
        }

        protected SerializableCacheItemPolicy DeserializeCacheItemPolicy(string path)
        {
            return (SerializableCacheItemPolicy)Deserialize(path);
        }

        protected virtual object Deserialize(string fileName, SerializationBinder objectBinder = null)
        {
            object data = null;
            if (File.Exists(fileName))
            {
                using (var stream = OpenRead(fileName))
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

            return data;
        }

        /// <summary>
        /// This function serves to centralize file writes within this class
        /// </summary>
        public virtual long WriteFile(FileCache.PayloadMode mode, string key, FileCachePayload data, string regionName = null, bool policyUpdateOnly = false)
        {
            string cachedPolicy = GetPolicyPath(key, regionName);
            string cachedItemPath = GetCachePath(key, regionName);
            long cacheSizeDelta = 0;

            //ensure that the cache policy contains the correct key
            data.Policy.Key = key;

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
        /// Builds a string that will place the specified file name within the appropriate 
        /// cache and workspace folder.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract string GetCachePath(string key, string regionName = null);
        
        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract IEnumerable<string> GetKeys(string regionName = null);

        /// <summary>
        /// Returns a list of regions, including the root region.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetRegions()
        {
            string directory = Path.Combine(CacheDir, CacheSubFolder);
            DirectoryInfo di = new DirectoryInfo(directory);
            if (di.Exists)
            {
                yield return null;
                foreach (var d in di.EnumerateDirectories())
                {
                    yield return d.Name;
                }
            }
        }

        /// <summary>
        /// Builds a string that will get the path to the supplied file's policy file
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract string GetPolicyPath(string key, string regionName = null);

        /// <summary>
        /// Reads data in from a system file. System files are not part of the
        /// cache itself, but serve as a way for the cache to store data it 
        /// needs to operate.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <returns>The data from the file</returns>
        public object ReadSysFile(string filename)
        {
            // sys files go in the root directory
            string path = Path.Combine(CacheDir, filename);
            object data = null;

            if (File.Exists(path))
            {
                for (int i = 5; i > 0; i--) // try 5 times to read the file, if we can't, give up
                {
                    try
                    {
                        using (FileStream stream = GetStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            BinaryFormatter formatter = new BinaryFormatter();
                            try
                            {
                                data = formatter.Deserialize(stream);
                            }
                            catch (Exception)
                            {
                                data = null;
                            }
                            finally
                            {
                                stream.Close();
                            }
                        }
                        break;
                    }
                    catch (IOException)
                    {
                        // we timed out... so try again
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Writes data to a system file that is not part of the cache itself,
        /// but is used to help it function.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="data">The data to write to the file</param>
        public void WriteSysFile(string filename, object data)
        {
            // sys files go in the root directory
            string path = Path.Combine(CacheDir, filename);

            // write the data to the file
            using (FileStream stream = GetStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, data);
                stream.Close();
            }
        }

        /// <summary>
        /// This function servies to centralize file stream access within this class.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="mode"></param>
        /// <param name="access"></param>
        /// <param name="share"></param>
        /// <returns></returns>
        protected FileStream GetStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            FileStream stream = null;
            TimeSpan interval = new TimeSpan(0, 0, 0, 0, 50);
            TimeSpan totalTime = new TimeSpan();
            while (stream == null)
            {
                try
                {
                    stream = File.Open(path, mode, access, share);
                }
                catch (IOException ex)
                {
                    Thread.Sleep(interval);
                    totalTime += interval;

                    //if we've waited too long, throw the original exception.
                    if (AccessTimeout.Ticks != 0)
                    {
                        if (totalTime > AccessTimeout)
                        {
                            throw ex;
                        }
                    }
                }
            }
            return stream;
        }

        /// <summary>
        /// Deletes the specified key/region combo.  Returns bytes freed from delete.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public virtual long DeleteFile(string key, string regionName = null)
        {
            long bytesFreed = 0;

            // Because of the possibility of multiple threads accessing this, it's possible that
            // while we're trying to remove something, another thread has already removed it.
            try
            {
                FileCachePayload fcp = ReadFile(FileCache.PayloadMode.Filename, key, regionName);
                string path = GetCachePath(key, regionName);
                bytesFreed -= new FileInfo(path).Length;
                File.Delete(path);

                //remove policy file
                string cachedPolicy = GetPolicyPath(key, regionName);
                bytesFreed -= new FileInfo(cachedPolicy).Length;
                File.Delete(cachedPolicy);
            }
            catch (IOException ex)
            {
                //Owning FC might be interested in this exception.  
                throw ex;
            }

            return Math.Abs(bytesFreed);
        }
    }
}
