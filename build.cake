//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Build Configuration
var configuration = EnvironmentVariable("CONFIGURATION") ?? "Release";
var isAppVeyorBuild = EnvironmentVariable("APPVEYOR") == "True";
var isPullRequest = EnvironmentVariable("APPVEYOR_PULL_REQUEST_NUMBER") != null;

// File/Directory paths
var artifactDirectory = MakeAbsolute(Directory("./artifacts")).FullPath;
var solutionFile = "./src/FileCache.sln";

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .WithCriteria(!isAppVeyorBuild)
    .Does(() =>
{
    CleanDirectories(string.Format("./src/**/obj/{0}", configuration));
    CleanDirectories(string.Format("./src/**/bin/{0}", configuration));
    CleanDirectories("./artifacts");
});

Task("Restore-NuGet-Packages")
    .Does(() =>
{
    DotNetCoreRestore(solutionFile);
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    DotNetCoreBuild(solutionFile, new DotNetCoreBuildSettings
    {
        Configuration = Argument("configuration", configuration)
    });
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    DotNetCoreTest("./src/FileCache.UnitTests/FileCache.UnitTests.csproj", new DotNetCoreTestSettings{
        Configuration = configuration,
        //Logger = $"trx;LogFileName=TestResults.xml",
        NoBuild = true, // Build will fail for the gitversion task, dotnet build does not yet play nicely with gitverion, until gitversion adds support
        NoRestore = true
    });
});

Task("Create-NuGet-Packages")
    .WithCriteria(!isPullRequest)
    .IsDependentOn("Build")
    .Does(() =>
{
    DotNetCorePack(solutionFile, new DotNetCorePackSettings
    {
        Configuration = configuration,
        OutputDirectory = artifactDirectory
    });
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore-NuGet-Packages")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Create-NuGet-Packages");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);