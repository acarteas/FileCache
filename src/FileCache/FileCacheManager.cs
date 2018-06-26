using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
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

        protected virtual object Deserialize(string fileName, SerializationBinder objectBinder = null)
        {
            object data = null;
            if (File.Exists(fileName))
            {
                using (FileStream stream = GetStream(fileName, FileMode.Open, FileAccess.Read))
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
        /// This function serves to centralize file reads within this class.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="objectBinder"></param>
        /// <returns></returns>
        public virtual FileCachePayload ReadFile(string key, string regionName = null, SerializationBinder objectBinder = null)
        {
            string cachePath = GetCachePath(key, regionName);
            string policyPath = GetPolicyPath(key, regionName);
            FileCachePayload payload = new FileCachePayload(null);
            payload.Payload = Deserialize(cachePath);
            try
            {
                payload.Policy = Deserialize(policyPath) as SerializableCacheItemPolicy;
            }
            catch
            {
                payload.Policy = new SerializableCacheItemPolicy();
            }
            return payload;
        }

        /// <summary>
        /// This function serves to centralize file writes within this class
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <param name="regionName"></param>
        /// <returns>A long representing the size of the file written to the cache</returns>
        public virtual long WriteFile(string key, FileCachePayload data, string regionName = null)
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

            //write the object payload (lock the file so we can write to it and force others to wait
            //for us to finish)
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
        /// Builds a string that will place the specified file name within the appropriate 
        /// cache and workspace folder.
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract string GetCachePath(string key, string regionName = null);

        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        public abstract string[] GetKeys(string regionName = null);

        /// <summary>
        /// Builds a string that will get the path to the supplied file's policy file
        /// </summary>
        /// <param name="FileName"></param>
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
                        using (FileStream stream = GetStream(path, FileMode.Open, FileAccess.Read))
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
            using (FileStream stream = GetStream(path, FileMode.Create, FileAccess.Write))
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
        /// <returns></returns>
        protected FileStream GetStream(string path, FileMode mode, FileAccess access, bool lockfile = false)
        {
            FileStream stream = null;
            TimeSpan interval = new TimeSpan(0, 0, 0, 0, 50);
            TimeSpan totalTime = new TimeSpan();
            while (stream == null)
            {
                try
                {
                    if (lockfile)
                    {
                        stream = File.Open(path, mode, access, FileShare.None);
                    }
                    else
                    {
                        stream = File.Open(path, mode, access, FileShare.ReadWrite);
                    }
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
                FileCachePayload fcp = ReadFile(key, regionName);
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
            return bytesFreed;
        }

        protected class LocalCacheBinder : System.Runtime.Serialization.SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName)
            {
                Type typeToDeserialize = null;

                String currentAssembly = Assembly.GetAssembly(typeof(LocalCacheBinder)).FullName;
                assemblyName = currentAssembly;

                // Get the type using the typeName and assemblyName
                typeToDeserialize = Type.GetType(String.Format("{0}, {1}",
                    typeName, assemblyName));

                return typeToDeserialize;
            }
        }
    }
}
