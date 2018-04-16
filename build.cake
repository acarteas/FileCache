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
var isPullRequest = EnvironmentVariable("BUILD_REASON") == "PullRequest";

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
    MSBuild(solutionFile, settings =>
        settings
            .WithTarget("Restore")
            .SetConfiguration(configuration)
            .UseToolVersion(MSBuildToolVersion.VS2017)
            );
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
	// Use MSBuild for the Sln, but deploy
	MSBuild(solutionFile, settings =>
		settings
            .WithTarget("Build")
            .SetConfiguration(configuration)
            .UseToolVersion(MSBuildToolVersion.VS2017)
            );
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
    var settings = new MSBuildSettings()
        .WithTarget("Pack")
        .SetConfiguration(configuration)
        .UseToolVersion(MSBuildToolVersion.VS2017)
        .WithProperty("PackageOutputPath",artifactDirectory);

    // Pack the Sln (unit tests has the <IsPackable> to false)
    MSBuild(solutionFile, settings);
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