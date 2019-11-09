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
        private PayloadMode _readMode = PayloadMode.Serializable;
        public string CacheDir { get; protected set; }
        
        /// <summary>
        /// Used to abstract away the low-level details of file management.  This allows
        /// for multiple file formatting schemes based on use case.  
        /// </summary>
        public FileCacheManager CacheManager { get; protected set; }


        /// <summary>
        /// Used to store the default region when accessing the cache via [] calls
        /// </summary>
        public string DefaultRegion { get; set; }

        /// <summary>
        /// Used to set the default policy when setting cache values via [] calls
        /// </summary>
        public CacheItemPolicy DefaultPolicy { get; set; }

        /// <summary>
        /// Specified how the cache payload is to be handled.
        /// </summary>
        public enum PayloadMode
        {
            /// <summary>
            /// Treat the payload a a serializable object.
            /// </summary>
            Serializable,
            /// <summary>
            /// Treat the payload as a file name. File content will be copied on add, while get returns the file name.
            /// </summary>
            Filename,
            /// <summary>
            /// Treat the paylad as raw bytes. A byte[] and readable streams are supported on add.
            /// </summary>
            RawBytes
        }

        /// <summary>
        /// Specified whether the payload is deserialized or just the file name.
        /// </summary>
        public PayloadMode PayloadReadMode {
            get => _readMode;
            set {
                if (value == PayloadMode.RawBytes)
                {
                    throw new ArgumentException("The read mode cannot be set to RawBytes. Use the file name please.");
                }
                _readMode = value;
            }
        }

        /// <summary>
        /// Specified how the payload is to be handled on add operations.
        /// </summary>
        public PayloadMode PayloadWriteMode { get; set; } = PayloadMode.Serializable;

        /// <summary>
        /// The amount of time before expiry that a filename will be used as a payoad. I.e.
        /// the amount of time the cache's user can safely use the file delivered as a payload.
        /// Default 10 minutes.
        /// </summary>
        public TimeSpan FilenameAsPayloadSafetyMargin = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Used to determine how long the FileCache will wait for a file to become 
        /// available.  Default (00:00:00) is indefinite.  Should the timeout be
        /// reached, an exception will be thrown.
        /// </summary>
        public TimeSpan AccessTimeout
        {
            get
            {
                return CacheManager.AccessTimeout;
            }
            set
            {
                CacheManager.AccessTimeout = value;
            }
        }

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
                    object cacheSizeObj = CacheManager.ReadSysFile(CacheSizeFile);

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
                    CacheManager.WriteSysFile(CacheSizeFile, value);
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
        /// Creates a default instance of the file cache using the supplied file cache manager.
        /// </summary>
        /// <param name="manager"></param>
        public FileCache(FileCacheManagers manager)
        {
            Init(false, new TimeSpan(), true, true, manager);
        }

        /// <summary>
        /// Creates a new instance of the file cache using the supplied cache directory and cache manager.
        /// </summary>
        /// <param name="cacheRoot"></param>
        /// <param name="manager"></param>
        public FileCache(string cacheRoot, FileCacheManagers manager)
        {
            CacheDir = cacheRoot;
            Init(false, new TimeSpan(), false, true, manager);
        }

        /// <summary>
        /// Creates a default instance of the file cache.  Don't use if you plan to serialize custom objects
        /// </summary>
        /// <param name="calculateCacheSize">If true, will calcualte the cache's current size upon new object creation.
        /// Turned off by default as directory traversal is somewhat expensive and may not always be necessary based on
        /// use case.
        /// </param>
        /// <param name="cleanInterval">If supplied, sets the interval of time that must occur between self cleans</param>
        public FileCache(
            bool calculateCacheSize = false,
            TimeSpan cleanInterval = new TimeSpan(),
            FileCacheManagers manager = FileCacheManagers.Basic
            )
        {
            // CT note: I moved this code to an init method because if the user specified a cache root, that needs to
            // be set before checking if we should clean (otherwise it will look for the file in the wrong place)
            Init(calculateCacheSize, cleanInterval, true, true, manager);
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
        public FileCache(
            string cacheRoot,
            bool calculateCacheSize = false,
            TimeSpan cleanInterval = new TimeSpan(),
            FileCacheManagers manager = FileCacheManagers.Basic)
        {
            CacheDir = cacheRoot;
            Init(calculateCacheSize, cleanInterval, false, true, manager);
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
        public FileCache(
            SerializationBinder binder,
            bool calculateCacheSize = false,
            TimeSpan cleanInterval = new TimeSpan(),
            FileCacheManagers manager = FileCacheManagers.Basic
            )
        {
            _binder = binder;
            Init(calculateCacheSize, cleanInterval, true, false, manager);
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
        public FileCache(
            string cacheRoot,
            SerializationBinder binder,
            bool calculateCacheSize = false,
            TimeSpan cleanInterval = new TimeSpan(),
            FileCacheManagers manager = FileCacheManagers.Basic
            )
        {
            _binder = binder;
            CacheDir = cacheRoot;
            Init(calculateCacheSize, cleanInterval, false, false, manager);
        }

        #endregion

        #region custom methods

        private void Init(
            bool calculateCacheSize = false,
            TimeSpan cleanInterval = new TimeSpan(),
            bool setCacheDirToDefault = true,
            bool setBinderToDefault = true,
            FileCacheManagers manager = FileCacheManagers.Basic
            )
        {   
            _name = "FileCache_" + _nameCounter;
            _nameCounter++;
            
            DefaultRegion = null;
            DefaultPolicy = new CacheItemPolicy();
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

            //set up cache manager
            CacheManager = FileCacheManagerFactory.Create(manager);
            CacheManager.CacheDir = CacheDir;
            CacheManager.CacheSubFolder = _cacheSubFolder;
            CacheManager.PolicySubFolder = _policySubFolder;
            CacheManager.Binder = _binder;
            CacheManager.AccessTimeout = new TimeSpan();

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
                DateTime? lastClean = CacheManager.ReadSysFile(LastCleanedDateFile) as DateTime?;

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
                
                foreach (string key in GetKeys(regionName))
                {
                    CacheItemPolicy policy = GetPolicy(key, regionName);
                    if (policy.AbsoluteExpiration < DateTime.Now)
                    {
                        try
                        {
                            string cachePath = CacheManager.GetCachePath(key, regionName);
                            string policyPath = CacheManager.GetPolicyPath(key, regionName);
                            CacheItemReference ci = new CacheItemReference(key, cachePath, policyPath);
                            Remove(key, regionName); // CT note: Remove will update CurrentCacheSize
                            removed += ci.Length;
                        }
                        catch (Exception) // skip if the file cannot be accessed
                        { }
                    }
                }

                // mark that we've cleaned the cache
                CacheManager.WriteSysFile(LastCleanedDateFile, DateTime.Now);

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
            foreach (string key in GetKeys(regionName))
            {
                try
                {
                    //build item reference
                    string cachePath = CacheManager.GetCachePath(key, regionName);
                    string policyPath = CacheManager.GetPolicyPath(key, regionName);
                    CacheItemReference ci = new CacheItemReference(key, cachePath, policyPath);
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
            string policyPath = Path.GetDirectoryName(CacheManager.GetPolicyPath("DummyValue", regionName));
            string cachePath = Path.GetDirectoryName(CacheManager.GetCachePath("DummyValue", regionName));
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
            var fis = root.EnumerateFiles();
            foreach (FileInfo fi in fis)
            {
                size += fi.Length;
            }
            // Add subdirectory sizes.
            var dis = root.EnumerateDirectories();
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
                //AC: Not added by me.  What does this do?
                if (cLock == null)
                {
                    return;
                }

                IEnumerable<string> keys = CacheManager.GetKeys();
                foreach (string key in keys)
                {
                    string policyPath = CacheManager.GetPolicyPath(key, regionName);
                    string cachePath = CacheManager.GetCachePath(key, regionName);

                    // Update the Cache size
                    CurrentCacheSize = GetCacheSize();
                    //if either policy or cache are stale, delete both
                    if (File.GetLastAccessTime(policyPath) < minDate || File.GetLastAccessTime(cachePath) < minDate)
                    {
                        CurrentCacheSize -= CacheManager.DeleteFile(key, regionName);
                    }
                }

                // unlock
                cLock.Close();
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
            CacheItemPolicy policy = new CacheItemPolicy();
            FileCachePayload payload = CacheManager.ReadFile(PayloadMode.Filename, key, regionName) as FileCachePayload;
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

        public IEnumerable<string> GetKeys(string regionName = null)
        {
            return CacheManager.GetKeys(regionName);
        }

        #endregion

        #region private helpers

        private void WriteHelper(PayloadMode mode, string key, FileCachePayload data, string regionName = null, bool policyUpdateOnly = false)
        {
            CurrentCacheSize += CacheManager.WriteFile(mode, key, data, regionName, policyUpdateOnly);

            //check to see if limit was reached
            if (CurrentCacheSize > MaxCacheSize)
                if (CurrentCacheSize > MaxCacheSize)
                {
                    MaxCacheSizeReached(this, new FileCacheEventArgs(CurrentCacheSize, MaxCacheSize));
                }
        }

        #endregion

        #region ObjectCache overrides

        public override object AddOrGetExisting(string key, object value, CacheItemPolicy policy, string regionName = null)
        {
            string path = CacheManager.GetCachePath(key, regionName);
            object oldData = null;

            //pull old value if it exists
            if (File.Exists(path))
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
            WriteHelper(PayloadWriteMode, key, newPayload, regionName);

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
            string path = CacheManager.GetCachePath(key, regionName);
            return File.Exists(path);
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
            FileCachePayload payload = CacheManager.ReadFile(PayloadReadMode, key, regionName) as FileCachePayload;
            string cachedItemPath = CacheManager.GetCachePath(key, regionName);

            DateTime cutoff = DateTime.Now;
            if (PayloadReadMode == PayloadMode.Filename)
            {
                cutoff += FilenameAsPayloadSafetyMargin;
            }

            //null payload?
            if (payload != null)
            {
                //did the item expire?
                if (payload.Policy.AbsoluteExpiration < cutoff)
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
                        WriteHelper(PayloadWriteMode, cachedItemPath, payload, regionName, true);
                    }
                    
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

            //AC: This seems inefficient.  Wouldn't it be better to do this using a cursor?
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
            object valueToDelete = null;
            if (Contains(key, regionName))
            {
                // Because of the possibility of multiple threads accessing this, it's possible that
                // while we're trying to remove something, another thread has already removed it.
                try
                {
                    //remove cache entry
                    // CT note: calling Get from remove leads to an infinite loop and stack overflow,
                    // so I replaced it with a simple CacheManager.ReadFile call. None of the code here actually
                    // uses this object returned, but just in case someone else's outside code does.
                    FileCachePayload fcp = CacheManager.ReadFile(PayloadMode.Filename, key, regionName);
                    valueToDelete = fcp.Payload;
                    string path = CacheManager.GetCachePath(key, regionName);
                    CurrentCacheSize -= new FileInfo(path).Length;
                    File.Delete(path);

                    //remove policy file
                    string cachedPolicy = CacheManager.GetPolicyPath(key, regionName);
                    CurrentCacheSize -= new FileInfo(cachedPolicy).Length;
                    File.Delete(cachedPolicy);
                }
                catch (IOException)
                {
                }
                
            }
            return valueToDelete;
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
        }
    }
}
