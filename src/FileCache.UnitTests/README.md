Unit-Testing FileCache
------------------------

This will tell dotnet to build FileCache and run all of the tests.

``` shell
cd /path/to/FileCache/src/FileCache.UnitTests
dotnet test FileCache.UnitTests.csproj
```

Unit-Test Suites
----------------

There are two test suites:

1. FileCacheTest.cs
   - These tests use the BasicFileCacheManager when running various unit-tests.

1. HashedFileCache.cs
   - These tests are similar, but use the HashedFileCacheManager.


WSL2 Warning!
----------------

Sometimes a WSL operating system's time can get out-of-sync with the actual time (see [this](https://github.com/microsoft/WSL/issues/4114)). If it's out of sync enough, some tests can fail if Windows is ultimately in charge of file stats like Last Accessed Time. This happens if your FileCache repo is on a Windows drive (i.e. something under /mnt/c, /mnt/d, etc).

For example, FileCache.FlushRegionTest will fail with this error when your WSL'd Linux OS is 1 minute in the past:

```
Failed FlushRegionTest [1 s]
Error Message:
 Expected result2 to be <null>, but found "Value2".
Stack Trace:
   at FluentAssertions.Execution.LateBoundTestFramework.Throw(String message)
 at FluentAssertions.Execution.TestFrameworkProvider.Throw(String message)
 at FluentAssertions.Execution.DefaultAssertionStrategy.HandleFailure(String message)
 at FluentAssertions.Execution.AssertionScope.FailWith(Func`1 failReasonFunc)
 at FluentAssertions.Execution.AssertionScope.FailWith(Func`1 failReasonFunc)
 at FluentAssertions.Execution.AssertionScope.FailWith(String message, Object[] args)
 at FluentAssertions.Primitives.ReferenceTypeAssertions`2.BeNull(String because, Object[] becauseArgs)
 at FC.UnitTests.FileCacheTest.FlushRegionTest() in /mnt/path/to/FileCache/src/FileCache.UnitTests/FileCacheTest.cs:line 266
```

To fix, you can try:
    `sudo hwclock -s`

If that doesn't work, you'll probably have to shutdown and restart the VMs:
    `wsl --shutdown`
