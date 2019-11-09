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

        /// <summary>
        /// This function serves to centralize file reads within this class.
        /// </summary>
        /// <param name="mode">the payload reading mode</param>
        /// <param name="key"></param>
        /// <param name="objectBinder"></param>
        /// <returns></returns>
        // TODO: This was protected, but since Simon Thum's additions other methods require this. We need to merge with that change to move whatever is relevant behind this interface
        // and restore the visibility to protected
        protected internal abstract FileCachePayload ReadFile(FileCache.PayloadMode mode, string key, string regionName = null, SerializationBinder objectBinder = null);

        /// <summary>
        /// This function serves to centralize file writes within this class
        /// </summary>
        public abstract long WriteFile(FileCache.PayloadMode mode, string key, FileCachePayload data, string regionName = null, bool policyUpdateOnly = false);

        /// <summary>
        /// Builds a string that will place the specified file name within the appropriate 
        /// cache and workspace folder.
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract string GetCachePath(string FileName, string regionName = null);
        
        /// <summary>
        /// Returns a list of keys for a given region.  
        /// </summary>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract IEnumerable<string> GetKeys(string regionName = null);

        /// <summary>
        /// Builds a string that will get the path to the supplied file's policy file
        /// </summary>
        /// <param name="FileName"></param>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public abstract string GetPolicyPath(string FileName, string regionName = null);

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