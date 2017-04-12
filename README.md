<h1>FileCache Documentation</h1>
<h1>Usage</h1>
<p>Using the file cache is fairly straightforward.&nbsp; After adding FileCache and System.Runtime.Caching references to your project, add the appropriate using statement:</p>
<pre>using System.Runtime.Caching;</pre>
<p>Note that I&rsquo;ve placed my FileCache code inside the same namespace as the default .NET caching namespace for simplicity.&nbsp; Below are two examples of how to use FileCache:</p>
<h3>Basic Example</h3>
<pre>//basic example
FileCache simpleCache = new FileCache();
string foo = "bar";
simpleCache["foo"] = foo;
Console.WriteLine("Reading foo from simpleCache: {0}", simpleCache["foo"]);</pre>
<h3>Serializing Custom Objects</h3>
<p><span style="font-size: 10pt;">Below is an example that allows the caching of custom objects. First, place the following class in the assembly that contains the objects that need to be serialized:</span></p>
<pre>/// 
/// You should be able to copy &amp; paste this code into your local project to enable caching custom objects.
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
</pre>
<p>Next, pass in the custom ObjectBinder into the FileCache's constructor:</p>
<pre>//example with custom data binder (needed for caching user defined classes)
FileCache binderCache = new FileCache(new ObjectBinder());
</pre>
<p>Now, use the cache like normal:</p>
<pre>GenericDTO dto = new GenericDTO()
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
</pre>
<h2>Complete API</h2>
<p>FileCache implements <a href="http://msdn.microsoft.com/en-us/library/system.runtime.caching.objectcache.aspx"> System.Runtime.Caching.ObjectCache</a>. For the complete base API, see <a href="http://msdn.microsoft.com/en-us/library/system.runtime.caching.objectcache.aspx"> the MSDN article on ObjectCache</a>. &nbsp;Additionally, FileCache exposes the following methods and properties:</p>
<pre>/// &lt;summary&gt;
/// Used to store the default region when accessing the cache via [] 
/// calls
/// &lt;/summary&gt;
public string DefaultRegion { get; set; }

/// &lt;summary&gt;
/// Used to set the default policy when setting cache values via [] 
/// calls
/// &lt;/summary&gt;
public CacheItemPolicy DefaultPolicy { get; set; }

/// &lt;summary&gt;
/// Used to determine how long the FileCache will wait for a file to 
/// become available.  Default (00:00:00) is indefinite.  Should the 
/// timeout be reached, an exception will be thrown.
/// &lt;/summary&gt;
public TimeSpan AccessTimeout { get; set; }

/// &lt;summary&gt;
/// Returns a list of keys for a given region.  
/// &lt;/summary&gt;
/// &lt;param name="regionName" /&gt;&lt;/param&gt;
/// &lt;returns&gt;&lt;/returns&gt;
public string[] GetKeys(string regionName = null)

/// &lt;summary&gt;
/// Returns the policy attached to a given cache item.  
/// &lt;/summary&gt;
/// &lt;param name="key" /&gt;The key of the item&lt;/param&gt;
/// &lt;param name="regionName" /&gt;The region in which the key exists&lt;/param&gt;
/// &lt;returns&gt;&lt;/returns&gt;
public CacheItemPolicy GetPolicy(string key, string regionName = null)

/// &lt;summary&gt;
/// Used to specify the disk size, in bytes, that can be used by the File Cache.  Defaults to long.MaxValue
/// &lt;/summary&gt;
public long MaxCacheSize { get; set; }

/// &lt;summary&gt;
/// Returns the approximate size of the file cache
/// &lt;/summary&gt;
public long CurrentCacheSize { get; private set; }

/// &lt;summary&gt;
/// Event that will be called when  is reached.
/// &lt;/summary&gt;
public event EventHandler MaxCacheSizeReached = delegate { };

/// &lt;summary&gt;
/// Calculates the size, in bytes of the file cache
/// &lt;/summary&gt;
/// &lt;param name="regionName" /&gt;The region to calculate.  If NULL, will return total size.&lt;/param&gt;
public long GetCacheSize(string regionName = null);

/// &lt;summary&gt;
/// Flushes the file cache using DateTime.Now as the minimum date
/// &lt;/summary&gt;
public void Flush(string regionName = null);

/// &lt;summary&gt;
/// Flushes the cache based on last access date, filtered by optional region
/// &lt;/summary&gt;
public void Flush(DateTime minDate, string regionName = null);
</pre>
