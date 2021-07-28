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
        // Magic version for new sysfiles: 3.3.0 packed into a long.
        protected const ulong CACHE_VERSION = (  3 << 16
                                                 + 3 <<  8
                                                 + 0 <<  0);

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
        /// Differentiate outdated cache formats from newer.
        ///
        /// Older caches use "BinaryFormatter", which is a security risk:
        /// https://docs.microsoft.com/nl-nl/dotnet/standard/serialization/binaryformatter-security-guide#preferred-alternatives
        ///
        /// The newer caches have a 'magic' header we'll look for.
        /// </summary>
        /// <param name="reader">BinaryReader opened to stream containing the file contents.</param>
        /// <returns>boolean indicating validity</returns>
        protected bool HeaderVersionValid(BinaryReader reader)
        {
            // Don't much care about exceptions here - let them bubble up.
            ulong version = reader.ReadUInt64();
            // Valid if magic header version matches.
            return (version == CACHE_VERSION);
        }

        /// <summary>
        /// Differentiate outdated cache formats from newer.
        ///
        /// Older caches use "BinaryFormatter", which is a security risk:
        /// https://docs.microsoft.com/nl-nl/dotnet/standard/serialization/binaryformatter-security-guide#preferred-alternatives
        ///
        /// The newer caches have a 'magic' header we'll look for.
        /// </summary>
        /// <param name="reader">BinaryWriter opened to stream that will contain the file contents.</param>
        protected void HeaderVersionWrite(BinaryWriter writer)
        {
            // Don't much care about exceptions here - let them bubble up.
            writer.Write(CACHE_VERSION);
        }

        protected virtual object DeserializePayloadData(string fileName, SerializationBinder objectBinder = null)
        {
            object data = null;
            if (File.Exists(fileName))
            {
                using (FileStream stream = GetStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
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
            switch(mode)
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
                if (File.Exists(policyPath))
                {
                    using (FileStream stream = GetStream(policyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (BinaryReader reader = new BinaryReader(stream))
                        {
                            // TODO: In part of the merge it looked like the policy was force serialized with LocalCacheBinder(), is this intended?
                            payload.Policy = SerializableCacheItemPolicy.Deserialize(reader, stream.Length);
                        }
                    }
                }
                else
                {
                    payload.Policy = new SerializableCacheItemPolicy();
                }
            }
            catch
            {
                payload.Policy = new SerializableCacheItemPolicy();
            }
            return payload;
        }

        private byte[] LoadRawPayloadData(string fileName)
        {
            byte[] data = null;
            if (File.Exists(fileName))
            {
                using (FileStream stream = GetStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        // Check if it's valid version first.
                        if (!HeaderVersionValid(reader))
                        {
                            // Failure - return invalid data.
                            return null;

                            // `using` statements will clean up for us.
                        }

                        // Valid - read entire file.
                        data = reader.ReadBytes(int.MaxValue);
                    }
                }
            }
            return data;
        }

        protected virtual object Deserialize(string fileName, SerializationBinder objectBinder = null)
        {
            object data = null;
            if (File.Exists(fileName))
            {
                using (FileStream stream = GetStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
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
                            using (BinaryWriter writer = new BinaryWriter(stream))
                            {
                                if (data.Payload is byte[])
                                {
                                    byte[] dataPayload = (byte[])data.Payload;
                                    writer.Write(dataPayload);
                                }
                                else if (data.Payload is Stream)
                                {
                                    Stream dataPayload = (Stream)data.Payload;
                                    byte[] bytePayload = new byte[dataPayload.Length - dataPayload.Position];
                                    dataPayload.Read(bytePayload, (int)dataPayload.Position, bytePayload.Length);
                                    // no close or the like for data.Payload - we are not the owner
                                }
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
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    data.Policy.Serialize(writer);

                    // adjust cache size
                    cacheSizeDelta += new FileInfo(cachedPolicy).Length;
                }
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
        /// Generic version of ReadSysValue just throws an ArgumentException to error on unknown new types.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="value">The value read</param>
        /// <returns>success/failure boolean</returns>
        public bool ReadSysValue<T>(string filename, out T value) where T : struct
        {
            throw new ArgumentException(string.Format("Type is currently unsupported: {0}", typeof(T).ToString()), "value");

            // These types could be easily implemented following the `long` function as a template:
            //   - bool:
            //     + reader.ReadBoolean();
            //   - byte:
            //     + reader.ReadByte();
            //   - char:
            //     + reader.ReadChar();
            //   - decimal:
            //     + reader.ReadDecimal();
            //   - double:
            //     + reader.ReadDouble();
            //   - short:
            //     + reader.ReadInt16();
            //   - int:
            //     + reader.ReadInt32();
            //   - long:
            //     + reader.ReadInt64();
            //   - sbyte:
            //     + reader.ReadSbyte();
            //   - ushort:
            //     + reader.ReadUInt16();
            //   - uint:
            //     + reader.ReadUInt32();
            //   - ulong:
            //     + reader.ReadUInt64();
        }

        /// <summary>
        /// Read a `long` (64 bit signed int) from a sysfile.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="value">The value read or long.MinValue</param>
        /// <returns>success/failure boolean</returns>
        public bool ReadSysValue(string filename, out long value)
        {
            // Return min value on fail. Success/fail will be either exception or bool return.
            value = long.MinValue;
            bool success = false;

            // sys files go in the root directory
            string path = Path.Combine(CacheDir, filename);

            if (File.Exists(path))
            {
                for (int i = 5; i > 0; i--) // try 5 times to read the file, if we can't, give up
                {
                    try
                    {
                        using (FileStream stream = GetStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            using(BinaryReader reader = new BinaryReader(stream))
                            {
                                try
                                {
                                    // The old "BinaryFormatter" sysfiles will fail this check.
                                    if (HeaderVersionValid(reader))
                                    {
                                        value = reader.ReadInt64();
                                    }
                                    else
                                    {
                                        // Invalid version - return invalid value & failure.
                                        value = long.MinValue;
                                        success = false;
                                        break;
                                    }
                                }
                                catch (Exception)
                                {
                                    value = long.MinValue;
                                    // DriveCommerce: Need to rethrow to get the IOException caught.
                                    throw;
                                }
                            }
                            success = true;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        // we timed out... so try again
                    }
                }
            }

            // `value` already set correctly.
            return success;
        }

        /// <summary>
        /// Read a `DateTime` struct from a sysfile using `DateTime.FromBinary()`.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="value">The value read or DateTime.MinValue</param>
        /// <returns>success/failure boolean</returns>
        public bool ReadSysValue(string filename, out DateTime value)
        {
            // DateTime is serialized as a long, so use that `ReadSysValue()` function.
            long serialized;
            if (ReadSysValue(filename, out serialized))
            {
                value = DateTime.FromBinary(serialized);
                return true;
            }
            // else failed:
            value = DateTime.MinValue;
            return false;
        }

        /// <summary>
        /// Generic version of `WriteSysValue` just throws an ArgumentException on unknown new types.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="data">The data to write to the sysfile</param>
        public void WriteSysValue<T>(string filename, T data) where T : struct
        {
            throw new ArgumentException(string.Format("Type is currently unsupported: {0}", typeof(T).ToString()), "data");
        }

        /// <summary>
        /// Writes a long to a system file that is not part of the cache itself,
        /// but is used to help it function.
        /// </summary>
        /// <param name="filename">The name of the sysfile (without directory)</param>
        /// <param name="data">The long to write to the file</param>
        public void WriteSysValue(string filename, long data)
        {
            // sys files go in the root directory
            string path = Path.Combine(CacheDir, filename);

            // write the data to the file
            using (FileStream stream = GetStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    // Must write the magic version header first.
                    HeaderVersionWrite(writer);

                    writer.Write(data);
                }
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
