/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.Caching
{
	public class FileCache : ObjectCache
	{
		private static int _nameCounter = 1;
		private string _name = "";
		private SerializationBinder _binder;
		private string _cacheSubFolder = "cache";
		private string _policySubFolder = "policy";
		private TimeSpan _cleanInterval = new TimeSpan(7, 0, 0, 0); // default to 1 week
		private const string LastCleanedDateFile = "cache.lcd";
		private const string CacheSizeFile = "cache.size";
		// this is a file used to prevent multiple processes from trying to "clean" at the same time
		private const string SemaphoreFile = "cache.sem"; 
		private long _currentCacheSize = 0;
		public string CacheDir { get; protected set; }


		/// <summary>
		/// Used to store the default region when accessing the cache via [] calls
		/// </summary>
		public string DefaultRegion { get; set; }

		/// <summary>
		/// Used to set the default policy when setting cache values via [] calls
		/// </summary>
		public CacheItemPolicy DefaultPolicy { get; set; }

		/// <summary>
		/// Used to determine how long the FileCache will wait for a file to become 
		/// available.  Default (00:00:00) is indefinite.  Should the timeout be
		/// reached, an exception will be thrown.
		/// </summary>
		public TimeSpan AccessTimeout { get; set; }

		/// <summary>
		/// Used to specify the disk size, in bytes, that can be used by the File Cache
		/// </summary>
		public long MaxCacheSize { get; set; }

		/// <summary>
		/// Returns the approximate size of the file cache
		/// </summary>
		public long CurrentCacheSize 
		{
			get
			{
				// if this is the first query, we need to load the cache size from somewhere
				if (_currentCacheSize == 0)
				{
					// Read the system file for cache size
					object cacheSizeObj = ReadSysFile(CacheSizeFile);

					// Did we successfully get data from the file?
					if (cacheSizeObj != null)
					{
						_currentCacheSize = (long) cacheSizeObj;
					}
				}

				return _currentCacheSize;
			}
			private set
			{
				// no need to do a pointless re-store of the same value
				if (_currentCacheSize != value || value == 0)
				{
					WriteSysFile(CacheSizeFile, value);
					_currentCacheSize = value;
				}
			} 
		}

		/// <summary>
		/// Event that will be called when <see cref="MaxCacheSize"/> is reached.
		/// </summary>
		public event EventHandler<FileCacheEventArgs> MaxCacheSizeReached = delegate { };

		public event EventHandler<FileCacheEventArgs> CacheResized = delegate { }; 

		/// <summary>
		/// The default cache path used by FC.
		/// </summary>
		private string DefaultCachePath
		{
			get
			{
				return Directory.GetCurrentDirectory();
			}
		}

		#region constructors

		/// <summary>
		/// Creates a default instance of the file cache.  Don't use if you plan to serialize custom objects
		/// </summary>
		/// <param name="calculateCacheSize">If true, will calcualte the cache's current size upon new object creation.
		/// Turned off by default as directory traversal is somewhat expensive and may not always be necessary based on
		/// use case.
		/// </param>
		/// <param name="cleanInterval">If supplied, sets the interval of time that must occur between self cleans</param>
		public FileCache(bool calculateCacheSize = false, TimeSpan cleanInterval = new TimeSpan())
		{
			// CT note: I moved this code to an init method because if the user specified a cache root, that needs to
			// be set before checking if we should clean (otherwise it will look for the file in the wrong place)
			Init(calculateCacheSize, cleanInterval);
		}

		/// <summary>
		/// Creates an instance of the file cache using the supplied path as the root save path.
		/// </summary>
		/// <param name="cacheRoot">The cache's root file path</param>
		/// <param name="calculateCacheSize">If true, will calcualte the cache's current size upon new object creation.
		/// Turned off by default as directory traversal is somewhat expensive and may not always be necessary based on
		/// use case.
		/// </param>
		/// <param name="cleanInterval">If supplied, sets the interval of time that must occur between self cleans</param>
		public FileCache(string cacheRoot, bool calculateCacheSize = false, TimeSpan cleanInterval = new TimeSpan())
		{
			CacheDir = cacheRoot;
			Init(calculateCacheSize, cleanInterval, false);
		}

		/// <summary>
		/// Creates an instance of the file cache.
		/// </summary>
		/// <param name="binder">The SerializationBinder used to deserialize cached objects.  Needed if you plan
		/// to cache custom objects.
		/// </param>
		/// <param name="calculateCacheSize">If true, will calcualte the cache's current size upon new object creation.
		/// Turned off by default as directory traversal is somewhat expensive and may not always be necessary based on
		/// use case.
		/// </param>
		/// <param name="cleanInterval">If supplied, sets the interval of time that must occur between self cleans</param>
		public FileCache(SerializationBinder binder, bool calculateCacheSize = false, TimeSpan cleanInterval = new TimeSpan())
		{
			_binder = binder;
			Init(calculateCacheSize, cleanInterval, true, false);
		}

		/// <summary>
		/// Creates an instance of the file cache.
		/// </summary>
		/// <param name="cacheRoot">The cache's root file path</param>
		/// <param name="binder">The SerializationBinder used to deserialize cached objects.  Needed if you plan
		/// to cache custom objects.</param>
		/// <param name="calculateCacheSize">If true, will calcualte the cache's current size upon new object creation.
		/// Turned off by default as directory traversal is somewhat expensive and may not always be necessary based on
		/// use case.
		/// </param>
		/// <param name="cleanInterval">If supplied, sets the interval of time that must occur between self cleans</param>
		public FileCache(string cacheRoot, SerializationBinder binder, bool calculateCacheSize = false, TimeSpan cleanInterval = new TimeSpan())
		{
			_binder = binder;
			CacheDir = cacheRoot;
			Init(calculateCacheSize, cleanInterval, false, false);
		}

		#endregion

		#region custom methods

		private void Init(bool calculateCacheSize = false, TimeSpan cleanInterval = new TimeSpan(), bool setCacheDirToDefault = true, bool setBinderToDefault = true)
		{   
			_name = "FileCache_" + _nameCounter;
			_nameCounter++;
			
			DefaultRegion = null;
			DefaultPolicy = new CacheItemPolicy();
			AccessTimeout = new TimeSpan();
			MaxCacheSize = long.MaxValue;

			// set default values if not already set
			if (setCacheDirToDefault)
				CacheDir = DefaultCachePath;
			if (setBinderToDefault)
				_binder = new FileCacheBinder();

			// if it doesn't exist, we need to make it
			if (!Directory.Exists(CacheDir))
				Directory.CreateDirectory(CacheDir);
			
			// only set the clean interval if the user supplied it
			if (cleanInterval > new TimeSpan())
			{ 
				_cleanInterval = cleanInterval;
			}

			//check to see if cache is in need of immediate cleaning
			if (ShouldClean())
			{
				CleanCacheAsync();
			}
			else if (calculateCacheSize || CurrentCacheSize == 0)
			{
				// This is in an else if block, because CleanCacheAsync will 
				// update the cache size, so no need to do it twice.
				UpdateCacheSizeAsync();
			}

			MaxCacheSizeReached += FileCache_MaxCacheSizeReached;
		}

		private void FileCache_MaxCacheSizeReached(object sender, FileCacheEventArgs e)
		{
			Task.Factory.StartNew((Action) (() =>
			{
				// Shrink the cache to 75% of the max size
				// that way there's room for it to grow a bit
				// before we have to do this again.
				long newSize = ShrinkCacheToSize((long)(MaxCacheSize*0.75));
			}));
		}


		// Returns the cleanlock file if it can be opened, otherwise it is being used by another process so return null
		private FileStream GetCleaningLock()
		{
			try
			{
				return File.Open(Path.Combine(CacheDir, SemaphoreFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (Exception)
			{
				return null;
			}
		}

		// Determines whether or not enough time has passed that the cache should clean itself
		private bool ShouldClean()
		{
			try
			{
				// if the file can't be found, or is corrupt this will throw an exception
				DateTime? lastClean = ReadSysFile(LastCleanedDateFile) as DateTime?;

				//AC: rewrote to be safer in null cases
				if (lastClean == null)
				{
					return true;
				}

				// return true if the amount of time between now and the last clean is greater than or equal to the
				// clean interval, otherwise return false.
				return DateTime.Now - lastClean >= _cleanInterval;
			}
			catch (Exception)
			{
				return true;
			}
		}

		/// <summary>
		/// Shrinks the cache until the cache size is less than
		/// or equal to the size specified (in bytes). This is a
		/// rather expensive operation, so use with discretion.
		/// </summary>
		/// <returns>The new size of the cache</returns>
		public long ShrinkCacheToSize(long newSize, string regionName = null)
		{
			long originalSize = 0, amount = 0, removed = 0;
			
			//lock down other treads from trying to shrink or clean
			using (FileStream cLock = GetCleaningLock())
			{
				if (cLock == null)
					return -1;

				// if we're shrinking the whole cache, we can use the stored
				// size if it's available. If it's not available we calculate it and store
				// it for next time.
				if (regionName == null)
				{
					if (CurrentCacheSize == 0)
					{
						CurrentCacheSize = GetCacheSize();
					}

					originalSize = CurrentCacheSize;
				}
				else
				{
					originalSize = GetCacheSize(regionName);
				}

				// Find out how much we need to get rid of
				amount = originalSize - newSize;

				// CT note: This will update CurrentCacheSize
				removed = DeleteOldestFiles(amount, regionName);

				// unlock the semaphore for others
				cLock.Close();
			}

			// trigger the event
			CacheResized(this, new FileCacheEventArgs(originalSize - removed, MaxCacheSize));

			// return the final size of the cache (or region)
			return originalSize - removed;
		}

		public void CleanCacheAsync()
		{
			Task.Factory.StartNew((Action) (() =>
			{
				CleanCache();
			}));
		}

		/// <summary>
		/// Loop through the cache and delete all expired files
		/// </summary>
		/// <returns>The amount removed (in bytes)</returns>
		public long CleanCache(string regionName = null)
		{
			long removed = 0;
			
			//lock down other treads from trying to shrink or clean
			using (FileStream cLock = GetCleaningLock())
			{
				if (cLock == null)
					return 0;
				
				foreach (var key in GetKeys(regionName).ToArray())
				{
					CacheItemPolicy policy = GetPolicy(key.Key, regionName);
					if (policy.AbsoluteExpiration < DateTime.Now)
					{
						try
						{
							CacheItemReference ci = (CacheItemReference)key;
							InternalRemove(key); // CT note: Remove will update CurrentCacheSize
							removed += ci.Length;
						}
						catch (Exception) // skip if the file cannot be accessed
						{ }
					}
				}

				// mark that we've cleaned the cache
				WriteSysFile(LastCleanedDateFile, DateTime.Now);

				// unlock
				cLock.Close();
			}

			return removed;
		}

		/// <summary>
		/// Delete the oldest items in the cache to shrink the chache by the
		/// specified amount (in bytes).
		/// </summary>
		/// <returns>The amount of data that was actually removed</returns>
		private long DeleteOldestFiles(long amount, string regionName = null)
		{ 
			// Verify that we actually need to shrink
			if (amount <= 0)
			{ 
				return 0;
			}

			//Heap of all CacheReferences
			PriortyQueue<CacheItemReference> cacheReferences = new PriortyQueue<CacheItemReference>();

			//build a heap of all files in cache region
			foreach (var key in GetKeys(regionName))
			{
				try
				{
					//build item reference

					CacheItemReference ci = (CacheItemReference)key;
					cacheReferences.Enqueue(ci);
				}
				catch(FileNotFoundException)
				{
				}
			}

			//remove cache items until size requirement is met
			long removedBytes = 0;
			while(removedBytes < amount && cacheReferences.GetSize() > 0)
			{
				//remove oldest item
				CacheItemReference oldest = cacheReferences.Dequeue();
				removedBytes += oldest.Length;
				Remove(oldest.Key, regionName);
			}
			return removedBytes;
		}

		/// <summary>
		/// This method calls GetCacheSize on a separate thread to 
		/// calculate and then store the size of the cache.
		/// </summary>
		public void UpdateCacheSizeAsync()
		{
			Task.Factory.StartNew((Action) (() =>
			{
				CurrentCacheSize = GetCacheSize();
			}));
		}

		//AC Note: From MSDN / SO (http://stackoverflow.com/questions/468119/whats-the-best-way-to-calculate-the-size-of-a-directory-in-net)
		/// <summary>
		/// Calculates the size, in bytes of the file cache
		/// </summary>
		/// <param name="regionName">The region to calculate.  If NULL, will return total size.</param>
		/// <returns></returns>
		public long GetCacheSize(string regionName = null)
		{
			long size = 0;

			//AC note: First parameter is unused, so just pass in garbage ("DummyValue")
			string policyPath = GetPolicyDirectoryPath(regionName);
			string cachePath = GetCacheDirectoryPath(regionName);
			size += CacheSizeHelper(new DirectoryInfo(policyPath));
			size += CacheSizeHelper(new DirectoryInfo(cachePath));
			return size;
		}

		/// <summary>
		/// Helper method for public <see cref="GetCacheSize"/>.
		/// </summary>
		/// <param name="root"></param>
		/// <returns></returns>
		private long CacheSizeHelper(DirectoryInfo root)
		{
			long size = 0;

			// Add file sizes.
			FileInfo[] fis = root.GetFiles();
			foreach (FileInfo fi in fis)
			{
				size += fi.Length;
			}
			// Add subdirectory sizes.
			DirectoryInfo[] dis = root.GetDirectories();
			foreach (DirectoryInfo di in dis)
			{
				size += CacheSizeHelper(di);
			}
			return size;
		}

		/// <summary>
		/// Flushes the file cache using DateTime.Now as the minimum date
		/// </summary>
		/// <param name="regionName"></param>
		public void Flush(string regionName = null)
		{
			Flush(DateTime.Now, regionName);
		}

		/// <summary>
		/// Flushes the cache based on last access date, filtered by optional region
		/// </summary>
		/// <param name="minDate"></param>
		/// <param name="regionName"></param>
		public void Flush(DateTime minDate, string regionName = null)
		{
			// prevent other threads from altering stuff while we delete junk
			using (FileStream cLock = GetCleaningLock())
			{
				if (cLock == null)
					return;

				string policyPath = GetPolicyDirectoryPath(regionName);
				string cachePath = GetCacheDirectoryPath(regionName);
				FlushHelper(new DirectoryInfo(policyPath), minDate);
				FlushHelper(new DirectoryInfo(cachePath), minDate);

				// Update the Cache size
				CurrentCacheSize = GetCacheSize();

				// unlock
				cLock.Close();
			}
		}

		/// <summary>
		/// Helper method for public flush
		/// </summary>
		/// <param name="root"></param>
		/// <param name="minDate"></param>
		private void FlushHelper(DirectoryInfo root, DateTime minDate)
		{
			// check files.
			FileInfo[] fis = root.GetFiles();
			foreach (FileInfo fi in fis)
			{
				//is the file stale?
				if(minDate > File.GetLastAccessTime(fi.FullName))
				{
					File.Delete(fi.FullName);
				}
			}

			// check subdirectories
			DirectoryInfo[] dis = root.GetDirectories();
			foreach (DirectoryInfo di in dis)
			{
				FlushHelper(di, minDate);
			}
		}

		/// <summary>
		/// Returns the policy attached to a given cache item.  
		/// </summary>
		/// <param name="key">The key of the item</param>
		/// <param name="regionName">The region in which the key exists</param>
		/// <returns></returns>
		public CacheItemPolicy GetPolicy(string key, string regionName = null)
		{
		    var cacheKey = GetCacheKey(key, regionName);
			CacheItemPolicy policy = new CacheItemPolicy();
			FileCachePayload payload = ReadFile(cacheKey) as FileCachePayload;
			if (payload != null)
			{
				try
				{
					policy.SlidingExpiration = payload.Policy.SlidingExpiration;
					policy.AbsoluteExpiration = payload.Policy.AbsoluteExpiration;
				}
				catch (Exception)
				{
				}
			}
			return policy;
		}

		/// <summary>
		/// Returns a list of keys for a given region.  
		/// </summary>
		/// <param name="regionName"></param>
		/// <returns></returns>
		public IEnumerable<CacheKey> GetKeys(string regionName = null)
		{
			string cacheDirectory = GetCacheDirectoryPath(regionName);

			string policyDirectory = GetPolicyDirectoryPath(regionName);

			if (Directory.Exists(policyDirectory) == false)
				Directory.CreateDirectory(policyDirectory);

			if (Directory.Exists(cacheDirectory))
			{
				foreach (string file in Directory.GetFiles(cacheDirectory))
				{
					yield return new CacheKey(file, policyDirectory);
				}
			}
			else
			{
				Directory.CreateDirectory(cacheDirectory);
			}
		}


        #endregion

        #region helper methods

	    private string GetPolicyDirectoryPath(string regionName)
	    {
	        return string.IsNullOrWhiteSpace(regionName)
	            ? Path.Combine(CacheDir, _policySubFolder)
	            : Path.Combine(CacheDir, _policySubFolder, regionName);
	    }

	    private string GetCacheDirectoryPath(string regionName)
	    {
	        return string.IsNullOrWhiteSpace(regionName)
	            ? Path.Combine(CacheDir, _cacheSubFolder)
	            : Path.Combine(CacheDir, _cacheSubFolder, regionName);
	    }

	    private string GetCachePathForKey(string key, string regionName = null)
	    {
	        return Path.Combine(GetCacheDirectoryPath(regionName), $"{key}.dat");
	    }

	    private CacheKey GetCacheKey(string key, string regionName)
	    {
	        var cachePath = GetCachePathForKey(key, regionName);
	        return new CacheKey(cachePath, GetPolicyDirectoryPath(regionName));
	    }

	    private object InternalRemove(CacheKey key)
	    {
	        object valueToDelete = null;

	        // Because of the possibility of multiple threads accessing this, it's possible that
	        // while we're trying to remove something, another thread has already removed it.

	        //remove cache entry
	        // CT note: calling Get from remove leads to an infinite loop and stack overflow,
	        // so I replaced it with a simple ReadFile call. None of the code here actually
	        // uses this object returned, but just in case someone else's outside code does.
	        var cacheFile = new FileInfo(key.CachePath);
	        if (cacheFile.Exists)
	        {
	            try
	            {
	                FileCachePayload fcp = ReadFile(key);
	                valueToDelete = fcp.Payload;
	                CurrentCacheSize -= cacheFile.Length;
	                cacheFile.Delete();

	            }
	            catch (IOException) { }
	        }

	        var policyFile = new FileInfo(key.PolicyPath);
	        if (policyFile.Exists)
	        {
	            try
	            {
	                //remove policy file
	                CurrentCacheSize -= policyFile.Length;
	                policyFile.Delete();
	            }
	            catch (IOException) { }
	        }


	        return valueToDelete;
	    }

        /// <summary>
        /// This function servies to centralize file stream access within this class.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="mode"></param>
        /// <param name="access"></param>
        /// <param name="lockfile"></param>
        /// <returns></returns>
        private FileStream GetStream(string path, FileMode mode, FileAccess access, bool lockfile = false)
		{
			FileStream stream = null;
			TimeSpan interval = new TimeSpan(0, 0, 0, 0, 50);
			TimeSpan totalTime = new TimeSpan();
			while (stream == null)
			{
				try
				{
				    stream = File.Open(path, mode, access, lockfile ? FileShare.None : FileShare.ReadWrite);
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

		private FileCachePayload ReadFile(CacheKey key, SerializationBinder objectBinder = null)
	    {
	        object data = null;
	        SerializableCacheItemPolicy policy = new SerializableCacheItemPolicy();
	        FileCachePayload payload = new FileCachePayload(null);

	        if (File.Exists(key.CachePath))
	        {
	            using (FileStream stream = GetStream(key.CachePath, FileMode.Open, FileAccess.Read))
	            {
	                BinaryFormatter formatter = new BinaryFormatter();

	                //AC: From http://spazzarama.com//2009/06/25/binary-deserialize-unable-to-find-assembly/
	                //    Needed to deserialize custom objects
	                if (objectBinder != null)
	                {
	                    //take supplied binder over default binder
	                    formatter.Binder = objectBinder;
	                }
	                else if (_binder != null)
	                {
	                    formatter.Binder = _binder;
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
	        if (File.Exists(key.PolicyPath))
	        {
	            using (FileStream stream = GetStream(key.PolicyPath, FileMode.Open, FileAccess.Read))
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
        private void WriteFile(CacheKey key, FileCachePayload data)
		{
			//remove current item / policy from cache size calculations
            var cacheFile = new FileInfo(key.CachePath);
			if(cacheFile.Exists)
			{
				CurrentCacheSize -= new FileInfo(key.CachePath).Length;
			}

		    var policyFile = new FileInfo(key.PolicyPath);
			if(policyFile.Exists)
			{
				CurrentCacheSize -= policyFile.Length;
			}

			//write the object payload (lock the file so we can write to it and force others to wait for us to finish)
			using (FileStream stream = GetStream(key.CachePath, FileMode.Create, FileAccess.Write))
			{
				BinaryFormatter formatter = new BinaryFormatter();
				formatter.Serialize(stream, data.Payload);

				//adjust cache size (while we have the file to ourselves)
				CurrentCacheSize += new FileInfo(key.CachePath).Length;

				stream.Close();
			}
			
			//write the cache policy
			using (FileStream stream = GetStream(key.PolicyPath, FileMode.Create, FileAccess.Write))
			{
				BinaryFormatter formatter = new BinaryFormatter();
				formatter.Serialize(stream, data.Policy);
				
				// adjust cache size
				CurrentCacheSize += new FileInfo(key.PolicyPath).Length;

				stream.Close();
			}

			// try to update the last access time
			try
			{
				File.SetLastAccessTime(key.CachePath, DateTime.Now);
			}
			catch (IOException)
			{
			}

			//check to see if limit was reached
			if(CurrentCacheSize > MaxCacheSize)
			{
				MaxCacheSizeReached(this, new FileCacheEventArgs(CurrentCacheSize, MaxCacheSize));
			}
		}

		/// <summary>
		/// Reads data in from a system file. System files are not part of the
		/// cache itself, but serve as a way for the cache to store data it 
		/// needs to operate.
		/// </summary>
		/// <param name="filename">The name of the sysfile (without directory)</param>
		/// <returns>The data from the file</returns>
		private object ReadSysFile(string filename)
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
		private void WriteSysFile(string filename, object data)
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

		#endregion

		#region ObjectCache overrides

		public override object AddOrGetExisting(string key, object value, CacheItemPolicy policy, string regionName = null)
		{
			var cacheKey = GetCacheKey(key, regionName);
			object oldData = null;

			//pull old value if it exists
			if (File.Exists(cacheKey.CachePath))
			{
				try
				{
					oldData = Get(key, regionName);
				}
				catch (Exception)
				{
					oldData = null;
				}
			}
			SerializableCacheItemPolicy cachePolicy = new SerializableCacheItemPolicy(policy);
			FileCachePayload newPayload = new FileCachePayload(value, cachePolicy);
			WriteFile(cacheKey, newPayload);

			//As documented in the spec (http://msdn.microsoft.com/en-us/library/dd780602.aspx), return the old
			//cached value or null
			return oldData;
		}

		public override CacheItem AddOrGetExisting(CacheItem value, CacheItemPolicy policy)
		{
			object oldData = AddOrGetExisting(value.Key, value.Value, policy, value.RegionName);
			CacheItem returnItem = null;
			if (oldData != null)
			{
				returnItem = new CacheItem(value.Key)
				{
					Value = oldData,
					RegionName = value.RegionName
				};
			}
			return returnItem;
		}

		public override object AddOrGetExisting(string key, object value, DateTimeOffset absoluteExpiration, string regionName = null)
		{
			CacheItemPolicy policy = new CacheItemPolicy();
			policy.AbsoluteExpiration = absoluteExpiration;
			return AddOrGetExisting(key, value, policy, regionName);
		}

		public override bool Contains(string key, string regionName = null)
		{
			var cacheKey = GetCacheKey(key, regionName);
			return File.Exists(cacheKey.CachePath);
		}

		public override CacheEntryChangeMonitor CreateCacheEntryChangeMonitor(IEnumerable<string> keys, string regionName = null)
		{
			throw new NotImplementedException();
		}

		public override DefaultCacheCapabilities DefaultCacheCapabilities
		{
			get
			{
				//AC note: can use boolean OR "|" to set multiple flags.
				return DefaultCacheCapabilities.CacheRegions
					|
					DefaultCacheCapabilities.AbsoluteExpirations
					|
					DefaultCacheCapabilities.SlidingExpirations
					;
			}
		}
		
		public override object Get(string key, string regionName = null)
		{
		    var cacheKey = GetCacheKey(key, regionName);

			FileCachePayload payload = ReadFile(cacheKey);

			//null payload?
			if (payload != null)
			{
				//did the item expire?
				if (payload.Policy.AbsoluteExpiration < DateTime.Now)
				{
					//set the payload to null
					payload.Payload = null;

					//delete the file from the cache
					try
					{
						// CT Note: I changed this to Remove from File.Delete so that the coresponding 
						// policy file will be deleted as well, and CurrentCacheSize will be updated.
						Remove(key, regionName);
					}
					catch (Exception)
					{
					}
				}
				else
				{
					//does the item have a sliding expiration?
					if (payload.Policy.SlidingExpiration > new TimeSpan())
					{
						payload.Policy.AbsoluteExpiration = DateTime.Now.Add(payload.Policy.SlidingExpiration);
						WriteFile(cacheKey, payload);
					}

					//update the file's access time
					File.SetLastAccessTime(cacheKey.CachePath, DateTime.Now);
				}
			}
			else
			{
				//remove null payload
				Remove(key, regionName);

				//create dummy one for return
				payload = new FileCachePayload(null);
			}
			return payload.Payload;
		}

		public override CacheItem GetCacheItem(string key, string regionName = null)
		{
			object value = Get(key, regionName);
			CacheItem item = new CacheItem(key);
			item.Value = value;
			item.RegionName = regionName;
			return item;
		}

		public override long GetCount(string regionName = null)
		{
			if (regionName == null)
			{
				regionName = "";
			}
			string path = Path.Combine(CacheDir, _cacheSubFolder, regionName);
			if (Directory.Exists(path))
				return Directory.GetFiles(path).Count();
			else
				return 0;
		}

		/// <summary>
		/// Returns an enumerator for the specified region (defaults to base-level cache directory).
		/// This function *WILL NOT* recursively locate files in subdirectories.
		/// </summary>
		/// <param name="regionName"></param>
		/// <returns></returns>
		public IEnumerator<KeyValuePair<string, object>> GetEnumerator(string regionName = null)
		{
			string region = "";
			if (string.IsNullOrEmpty(regionName) == false)
			{
				region = regionName;
			}
			List<KeyValuePair<string, object>> enumerator = new List<KeyValuePair<string, object>>();

			string directory = Path.Combine(CacheDir, _cacheSubFolder, region);
			foreach (string filePath in Directory.EnumerateFiles(directory))
			{
				string key = Path.GetFileNameWithoutExtension(filePath);
				enumerator.Add(new KeyValuePair<string, object>(key, this.Get(key, regionName)));
			}
			return enumerator.GetEnumerator();
		}

		/// <summary>
		/// Will return an enumerator with all cache items listed in the root file path ONLY.  Use the other
		/// <see cref="GetEnumerator"/> if you want to specify a region
		/// </summary>
		/// <returns></returns>
		protected override IEnumerator<KeyValuePair<string, object>> GetEnumerator()
		{
			return GetEnumerator(null);
		}

		public override IDictionary<string, object> GetValues(IEnumerable<string> keys, string regionName = null)
		{
			Dictionary<string, object> values = new Dictionary<string, object>();
			foreach (string key in keys)
			{
				values[key] = Get(key, regionName);
			}
			return values;
		}

		public override string Name
		{
			get { return _name; }
		}

	    public override object Remove(string key, string regionName = null)
	    {
	        return InternalRemove(GetCacheKey(key, regionName));
	    }


		public override void Set(string key, object value, CacheItemPolicy policy, string regionName = null)
		{
			Add(key, value, policy, regionName);
		}

		public override void Set(CacheItem item, CacheItemPolicy policy)
		{
			Add(item, policy);
		}

		public override void Set(string key, object value, DateTimeOffset absoluteExpiration, string regionName = null)
		{
			Add(key, value, absoluteExpiration, regionName);
		}

		public override object this[string key]
		{
			get
			{
				return this.Get(key, DefaultRegion);
			}
			set
			{
				this.Set(key, value, DefaultPolicy, DefaultRegion);
			}
		}

		#endregion

		private class LocalCacheBinder : System.Runtime.Serialization.SerializationBinder
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

		// CT: This private class is used to help shrink the cache. 
		// It computes the total size of an entry including it's policy file.
		// It also implements IComparable functionality to allow for sorting based on access time
		private class CacheItemReference : IComparable<CacheItemReference>
		{
			public readonly DateTime LastAccessTime;
			public readonly long Length;
			public readonly string Key;

			public CacheItemReference(string key, string cachePath, string policyPath)
			{
				Key = key;
				FileInfo cfi = new FileInfo(cachePath);
				FileInfo pfi = new FileInfo(policyPath);
				cfi.Refresh();
				LastAccessTime = cfi.LastAccessTime;
				Length = cfi.Length + pfi.Length;
			}

			public int CompareTo(CacheItemReference other)
			{
				int i = LastAccessTime.CompareTo(other.LastAccessTime);

				// It's possible, although rare, that two different items will have
				// the same LastAccessTime. So in that case, we need to check to see
				// if they're actually the same.
				if (i == 0)
				{
					// second order should be length (but from smallest to largest,
					// that way we delete smaller files first)
					i = -1 * Length.CompareTo(other.Length);
					if (i == 0)
					{ 
						i = Key.CompareTo(other.Key);
					}
				}

				return i;
			}

			public static bool operator >(CacheItemReference lhs, CacheItemReference rhs)
			{
				if(lhs.CompareTo(rhs) > 0)
				{
					return true;
				}
				return false;
			}

			public static bool operator <(CacheItemReference lhs, CacheItemReference rhs)
			{
				if (lhs.CompareTo(rhs) < 0)
				{
					return true;
				}
				return false;
			}

			public static explicit operator CacheItemReference(CacheKey cacheKey)
			{
				return new CacheItemReference(cacheKey.Key, cacheKey.CachePath, cacheKey.PolicyPath);
			}
		}

	    public struct CacheKey
	    {
		    public CacheKey(string filePath, string policyDirectoryPath)
		    {
			    CachePath = filePath;
			    Key = Path.GetFileNameWithoutExtension(filePath);
			    PolicyPath = Path.Combine(policyDirectoryPath, $"{Key}.policy");
		    }

		    public readonly string Key;

		    public readonly string CachePath;

		    public readonly string PolicyPath;
	    }
	}
}
