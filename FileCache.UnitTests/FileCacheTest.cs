/*
Copyright 2012, 2013, 2017 Adam Carter (http://adam-carter.com)

This file is part of FileCache (http://github.com/acarteas/FileCache).

FileCache is distributed under the Apache License 2.0.
Consult "LICENSE.txt" included in this package for the Apache License 2.0.
*/
using System.Runtime.Caching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using Codeplex.FileCache;

namespace FC.UnitTests
{


    /// <summary>
    ///This is a test class for FileCacheTest and is intended
    ///to contain all FileCacheTest Unit Tests
    ///</summary>
    [TestClass]
    public class FileCacheTest
    {

        

        #region Additional test attributes
        // 
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion

        [TestMethod]
        public void AbsoluteExpirationTest()
        {
            FileCache target = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();

            //add an item and have it expire yesterday
            policy.AbsoluteExpiration = (DateTimeOffset)DateTime.Now.AddDays(-1);
            target.Set("test", "test", policy);

            //then try to access the item
            object result = target.Get("test");
            Assert.AreEqual(null, result);
        }

        [TestMethod]
        public void PolicySaveTest()
        {
            FileCache target = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();
            policy.SlidingExpiration = new TimeSpan(1, 0, 0, 0, 0);
            target.Set("test", "test", policy);

            CacheItemPolicy returnPolicy = target.GetPolicy("test");
            Assert.AreEqual(policy.SlidingExpiration, returnPolicy.SlidingExpiration);
        }

        [TestMethod]
        public void SlidingExpirationTest()
        {
            FileCache target = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();

            //add an item and have it expire 500 ms from now
            policy.SlidingExpiration = new TimeSpan(0, 0, 0, 0, 500);
            target.Set("test", "test", policy);

            //sleep for 200
            Thread.Sleep(200);

            //then try to access the item
            object result = target.Get("test");
            Assert.AreEqual("test", result);

            //sleep for another 200
            Thread.Sleep(200);

            //then try to access the item
            result = target.Get("test");
            Assert.AreEqual("test", result);

            //then sleep for more than 500 ms.  Should be gone
            Thread.Sleep(600);
            result = target.Get("test");
            Assert.AreEqual(null, result);
        }

        [TestMethod]
        public void CustomObjectSaveTest()
        {
            FileCache target = new FileCache();

            //create custom object
            CustomObjB custom = new CustomObjB()
            {
                Num = 5,
                obj = new CustomObjA()
                {
                    Name = "test"
                }
            };

            CacheItem item = new CacheItem("foo")
            {
                Value = custom,
                RegionName = "foobar"
            };

            //set it
            target.Set(item, new CacheItemPolicy());

            //now get it back
            CacheItem fromCache = target.GetCacheItem("foo", "foobar");

            //pulling twice increases code coverage
            fromCache = target.GetCacheItem("foo", "foobar");
            custom = null;
            custom = fromCache.Value as CustomObjB;
            Assert.IsNotNull(custom);
            Assert.IsNotNull(custom.obj);
            Assert.AreEqual(custom.Num, 5);
            Assert.AreEqual(custom.obj.Name, "test");
        }

        [TestMethod]
        public void CacheSizeTest()
        {
            FileCache target = new FileCache("CacheSizeTest");

            target["foo"] = "bar";
            target["foo"] = "foobar";

            long cacheSize = target.GetCacheSize();
            Assert.AreNotEqual(0, cacheSize);

            target.Remove("foo");
            cacheSize = target.CurrentCacheSize;
            Assert.AreEqual(0, cacheSize);
        }

        [TestMethod]
        public void MaxCacheSizeTest()
        {
            FileCache target = new FileCache("MaxCacheSizeTest");
            target.MaxCacheSize = 0;
            bool isEventCalled = false;
            target.MaxCacheSizeReached += delegate(object sender, FileCacheEventArgs e)
            {
                isEventCalled = true;
            };
            target["foo"] = "bar";
            
            Assert.AreEqual(true, isEventCalled);
        }

        [TestMethod]
        public void ShrinkCacheTest()
        {
            FileCache target = new FileCache("ShrinkTest");

            // Test empty case
            Assert.AreEqual(0, target.ShrinkCacheToSize(0));

            // Insert 4 items, and keep track of their size
            //sleep to make sure that oldest item gets removed first
            target["item1"] = "bar1asdfasdfdfskjslkjlkjsdf sdlfkjasdlf asdlfkjskjfkjs d sdkfjksjd";
            Thread.Sleep(500);
            long size1 = target.GetCacheSize();
            target["item2"] = "bar2sdfjkjk skdfj sdflkj sdlkj lkjkjkjkjkjssss";
            Thread.Sleep(500);
            long size2 = target.GetCacheSize() - size1;
            target["item3"] = "bar3ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
            Thread.Sleep(500);
            long size3 = target.GetCacheSize() - size2 - size1; 
            target["item4"] = "bar3fffffffffsdfsdfffffffffffffsdfffffffffffdddddddddddddddddddffffff";
            long size4 = target.GetCacheSize() - size3 - size2 - size1;

            // Shrink to the size of the last 3 items (should remove item1 because it's the oldest, keeping the other 3)
            long newSize = target.ShrinkCacheToSize(size4 + size3 + size2);
            Assert.AreEqual(size4 + size3 + size2, newSize);

            // Shrink to just smaller than two items (should keep just item4, delete item2 and item3)
            newSize = target.ShrinkCacheToSize(size3 + size4 - 1);
            Assert.AreEqual(size4, newSize);

            // Shrink to size 1 (should delete everything)
            newSize = target.ShrinkCacheToSize(1);
            Assert.AreEqual(0, newSize);
        }

        [TestMethod]
        public void AutoShrinkTest()
        {
            FileCache target = new FileCache("AutoShrinkTest");

            target.Flush();
            target.MaxCacheSize = 20000;
            target.CacheResized += delegate(object sender, FileCacheEventArgs args)
            {
                Assert.IsNull(target["foo0"]);
                Assert.IsNotNull(target["foo10"]);
                Assert.IsNotNull(target["foo40"]);
            };


            for (int i = 0; i < 100; i++)
            {
                target["foo" + i] = "bar";

                // test to make sure it doesn't crash if one of the files is missing
                if (i == 10)
                    File.Delete(target.CacheDir + "/cache/foo9.dat");

                // test to make sure it leaves items that have been recently accessed.
                if (i%5 == 0 && i != 0)
                {
                    var foo10 = target.Get("foo10");
                    var foo40 = target.Get("foo40");
                }
            }
        }

        [TestMethod]
        public void FlushTest()
        {
            FileCache target = new FileCache("FlushTest");
            target["foo"] = "bar";

            //attempt flush
            target.Flush(DateTime.Now.AddDays(1));

            //check to see if size ends up at zero (expected result)
            Assert.AreEqual(0, target.GetCacheSize());
        }

        [TestMethod]
        public void RemoveTest()
        {
            FileCache target = new FileCache();
            target.Set("test", "test", DateTimeOffset.Now.AddDays(3));
            object result = target.Get("test");
            Assert.AreEqual("test", result);

            //now delete
            target.Remove("test");
            result = target["test"];
            Assert.AreEqual(null, result);

            //check file system to be sure item was removed
            string cachePath = Path.Combine(target.CacheDir, "cache", "test.dat");
            Assert.AreEqual(false, File.Exists(cachePath));

            //check file system to be sure that policy file was removed
            string policyPath = Path.Combine(target.CacheDir, "policy", "test.policy");
            Assert.AreEqual(false, File.Exists(policyPath));
        }

        [TestMethod]
        public void TestCount()
        {
            FileCache target = new FileCache("testCount");

            target["test"] = "test";

            object result = target.Get("test");
            
            Assert.AreEqual("test", result);
            Assert.AreEqual(1, target.GetCount());
        }

        [TestMethod]
        public void DefaultRegionTest()
        {
            FileCache cacheWithDefaultRegion = new FileCache();
            cacheWithDefaultRegion.DefaultRegion = "foo";
            FileCache defaultCache = new FileCache();
            cacheWithDefaultRegion["foo"] = "bar";
            object pull = defaultCache.Get("foo", "foo");
            Assert.AreEqual("bar", pull.ToString());
        }

        /// <summary>
        /// Generated to debug and address github issue #2
        /// </summary>
        [TestMethod]
        public void CustomRegionTest()
        {
            var cache = new FileCache();
            var policy = new CacheItemPolicy { SlidingExpiration = new TimeSpan(0, 5, 0) };

            var key = "my_key";
            var region = "my_region";
            object value = null;

            cache.Add(key, "foo", policy, region);
            cache.Add(key, "bar", policy);

            value = cache.Get(key); // returns: "bar"
            Assert.AreEqual("bar", value);
            value = cache.Get(key, region); // returns: "foo"
            Assert.AreEqual("foo", value);
            value = cache.Get(key); // returns: "foo" ?!
            Assert.AreEqual("bar", value);
        }

        [TestMethod]
        public void AccessTimeoutTest()
        {
            //AC: This test passes in debug mode, but not in rutime mode.  Why?

            FileCache target = new FileCache();
            target.AccessTimeout = new TimeSpan(1);
            target["primer"] = 0;
            string filePath = Path.Combine(target.CacheDir, "cache", "foo.dat");
            FileStream stream = File.Open(filePath, FileMode.Create);
            try
            {
                object result = target["foo"];

                //file access should fail.  If it doesn't, the test fails.
                Assert.AreNotEqual(true, true);
            }
            catch (IOException)
            {
                //we expect a file exception.
                Assert.AreEqual(true, true);
            }
            stream.Close();
        }

        [TestMethod]
        public void DefaultPolicyTest()
        {
            FileCache target = new FileCache();
            CacheItemPolicy policy = new CacheItemPolicy();
            policy.SlidingExpiration = new TimeSpan(1);
            target.DefaultPolicy = policy;
            target["foo"] = "bar";
            Thread.Sleep(2);
            object result = target["foo"];
            Assert.AreEqual(null, result);
        }

        [TestMethod]
        public void GetEnumeratorTest()
        {
            FileCache target = new FileCache();
            target["foo"] = 1;
            target["bar"] = 2;

            foreach (KeyValuePair<string, object> kvp in target)
            {
                Assert.AreEqual(target[kvp.Key], kvp.Value);
            }
        }

        [TestMethod]
        public void CleanCacheTest()
        {
            FileCache target = new FileCache("CleanCacheTest");

            target.Add("foo", 1, DateTime.Now); // expires immediately
            target.Add("bar", 2, DateTime.Now + TimeSpan.FromDays(1)); // set to expire tomorrow

            target.CleanCache();

            Assert.IsNull(target["foo"]);
            Assert.IsNotNull(target["bar"]);
        }
    }
}
