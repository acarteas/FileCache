FileCache Documentation
=======================

[![Build status](https://ci.appveyor.com/api/projects/status/pbeexohyjblb7mww?svg=true)](https://ci.appveyor.com/project/acarteas/filecache)  
[![NuGet](https://img.shields.io/nuget/v/filecache.svg)](https://www.nuget.org/packages/FileCache/)  
[![NuGet](https://img.shields.io/nuget/v/filecache.signed.svg)](https://www.nuget.org/packages/FileCache.Signed/)

How to Install FileCache
------------------------

The easiest way to get FileCache into your project is via NuGet, where you can find
both [signed][1] and [unsigned][2] versions of the DLLs.  Not sure which one to
use? Unless you are working with other signed projects (not common), you should
probably download the [unsigned][2] version.

Usage
-----

Using the file cache is fairly straightforward. After adding FileCache and
System.Runtime.Caching references to your project, add the appropriate using
statement: `using System.Runtime.Caching;`

Note that I've placed my FileCache code inside the same namespace as the default
.NET caching namespace for simplicity. Below are two examples of how to use FileCache:

### Basic Example ###

```csharp
//basic example
FileCache simpleCache = new FileCache();
string foo = "bar";
simpleCache["foo"] = foo;
Console.WriteLine("Reading foo from simpleCache: {0}", simpleCache["foo"]);
```

### New in Version 3
Version 3 allows for the building of custom caching schemes.  The first release contains
two caching schemes, `Basic` and `Hashed`.  

The Basic scheme is the tried-and-truescheme employed in all prior versions of FC.  When 
using the Basic scheme, file names are taking from the cache key.  For example, executing 
the command ```simpleCache["foo"] = foo;``` will create a ```foo.dat``` file
to store the value of foo.  This plaintext conversion can be convenient when debugging
or when accessing FC cache values from outside of FC.  However, it also has the 
downside of not supporting cache key names that cannot be used in file names (e.g. /).

Rather than using key names as file names, the Hashed scheme, introduced in Version 3.0, 
uses hashed representations of key names using the built-in .NET function 
```GetHashCode()```.  This function produces a numeric representation of each key that is 
guaranteed to produce a valid file name.  However, the downside of this approach is 
that ```GetHashCode()``` is not guaranteed to produce a unique key.  Therefore, FC must 
account for collisions when using the Hashed scheme.  This slight overhead is likely to 
correspond in slighly higher cache retrieval times.  

For now, the default caching scheme is set to `Basic` in order to maintain compatibility with
prior releases.  Furthermore, while the `Hashed` scheme passes all unit tests, it should 
be treated as experimental until additional field testing has been conducted.  

#### Using the Basic Caching Scheme
As the Basic scheme is the default, no special code is required to instantiate a FileCache
that uses the Basic scheme.  However, as the default might change in a future release, you
may want to start instantiating a Basic FileCache in the following manner:

```csharp
FileCache cache = new FileCache(FileCacheManagers.Basic);
```

#### Using the Hashed Caching Scheme
To use the Hashed caching scheme, simply change the CacheManager to Hashed:
```csharp
FileCache cache = new FileCache(FileCacheManagers.Hashed);
```

#### Setting the Default Cache Manager
It seems reasonable to assume that a programmer will want to maintain consistency of
caching scemes across a single program.  Alternatively, a programmer may want to 
upgrade an existing project from Basic to Hashed without having to specify the 
CacheManager for every FileCache instance.  For these cases, you can set the default
CacheManager used by setting the static `DefaultCacheManager` property:
```csharp
FileCache.DefaultCacheManager = FileCacheManagers.Hashed;
``` 
Now, instantiating a FileCache using the parameterless constructor 
(e.g. ```FileCache cache = new FileCache();```) returns a FileCache that 
uses the Hashed caching scheme.

### Serializing Custom Objects ###

Below is an example that allows the caching of custom objects. First, place the
following class in the assembly that contains the objects that need to be serialized:

```csharp
///
/// You should be able to copy & paste this code into your local project to enable
/// caching custom objects.
///
public sealed class ObjectBinder : System.Runtime.Serialization.SerializationBinder
{
   public override Type BindToType(string assemblyName, string typeName)
   {
      Type typeToDeserialize = null;
      String currentAssembly = Assembly.GetExecutingAssembly().FullName;

      // In this case we are always using the current assembly
      assemblyName = currentAssembly;

      // Get the type using the typeName and assemblyName
      typeToDeserialize = Type.GetType(String.Format("{0}, {1}",
      typeName, assemblyName));

      return typeToDeserialize;
      }
   }
}
```

Next, pass in the custom ObjectBinder into the FileCache's constructor:

```csharp
//example with custom data binder (needed for caching user defined classes)
FileCache binderCache = new FileCache(new ObjectBinder());
```

Now, use the cache like normal:

```csharp
GenericDTO dto = new GenericDTO()
   {
      IntProperty = 5,
      StringProperty = "foobar"
   };
binderCache["dto"] = dto;
GenericDTO fromCache = binderCache["dto"] as GenericDTO;
Console.WriteLine(
   "Reading DTO from binderCache:\n\tIntProperty:\t{0}\n\tStringProperty:\t{1}", 
   fromCache.IntProperty, 
    fromCache.StringProperty
);
```

Complete API
------------

FileCache implements [System.Runtime.Caching.ObjectCache][3]. For the complete base
API, see [the MSDN article on ObjectCache][3]. Additionally, FileCache exposes the
following methods and properties:

```csharp

/// <summary>
/// Allows for the setting of the default cache manager so that it doesn't have to be
/// specified on every instance creation.
/// </summary>
public static FileCacheManagers DefaultCacheManager { get; set; }

/// <summary>
/// Used to store the default region when accessing the cache via [] 
/// calls
/// </summary>
public string DefaultRegion { get; set; }

/// <summary>
/// Used to set the default policy when setting cache values via [] 
/// calls
/// </summary>
public CacheItemPolicy DefaultPolicy { get; set; }

/// <summary>
/// Used to determine how long the FileCache will wait for a file to 
/// become available.  Default (00:00:00) is indefinite.  Should the 
/// timeout be reached, an exception will be thrown.
/// </summary>
public TimeSpan AccessTimeout { get; set; }

/// <summary>
/// Returns a list of keys for a given region.  
/// </summary>
/// <param name="regionName" /></param>
/// <returns></returns>
public string[] GetKeys(string regionName = null)

/// <summary>
/// Returns the policy attached to a given cache item.  
/// </summary>
/// <param name="key" />The key of the item</param>
/// <param name="regionName" />The region in which the key exists</param>
/// <returns></returns>
public CacheItemPolicy GetPolicy(string key, string regionName = null)

/// <summary>
/// Used to specify the disk size, in bytes, that can be used by the File Cache.
/// Defaults to long.MaxValue
/// </summary>
public long MaxCacheSize { get; set; }

/// <summary>
/// Returns the approximate size of the file cache
/// </summary>
public long CurrentCacheSize { get; private set; }

/// <summary>
/// Event that will be called when  is reached.
/// </summary>
public event EventHandler MaxCacheSizeReached = delegate { };

/// <summary>
/// Calculates the size, in bytes of the file cache
/// </summary>
/// <param name="regionName" />The region to calculate.  If NULL, will return total
/// size.</param>
public long GetCacheSize(string regionName = null);

/// <summary>
/// Clears all FileCache-related items from the disk.  Throws an exception if the cache can't be
/// deleted.
/// </summary>
public void Clear();

/// <summary>
/// Flushes the file cache using DateTime.Now as the minimum date
/// </summary>
public void Flush(string regionName = null);

/// <summary>
/// Flushes the cache based on last access date, filtered by optional region
/// </summary>
public void Flush(DateTime minDate, string regionName = null);
```

  [1]: https://www.nuget.org/packages/FileCache.Signed
  [2]: https://www.nuget.org/packages/FileCache
  [3]: http://msdn.microsoft.com/en-us/library/system.runtime.caching.objectcache.aspx
