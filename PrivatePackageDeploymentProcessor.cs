namespace UpdateManager.PackageManagementCoreLibrary;
public class PrivatePackageDeploymentProcessor(IPackagesContext context, INugetPacker packer, IPackageDiscoveryHandler handler)
{
    // The main post-build method, specifically for private feed deployment
    public async Task ProcessPostBuildToPrivateFeedAsync(PostBuildArguments arguments)
    {
        try
        {
            var configuration = bb1.Configuration ?? throw new CustomBasicException("Configuration is not initialized.");
            string netVersion = configuration.NetVersion;
            string prefixName = bb1.Configuration!.PackagePrefixFromConfig;
            BasicList<NuGetPackageModel> packages = await context.GetPackagesAsync();
            NuGetPackageModel? package = packages.SingleOrDefault(x => x.PackageName == arguments.ProjectName);
            bool rets;
            if (package is not null)
            {
                if (package.IsExcluded)
                {
                    return; //this means can return because you are ignoring.
                }
                
                string directory = package.RepositoryDirectory;
                rets = await GitBranchManager.IsOnDefaultBranchAsync(directory);
                if (rets == false)
                {
                    Console.WriteLine("You are not on default branch.  Therefore, will not update the packages");
                    return;
                }
                await UpdatePackageVersionAsync(package);
            }
            else
            {
                package = new NuGetPackageModel
                {
                    PackageName = arguments.ProjectName,
                };
                handler.CustomizePackageModel(package);
                package.PackageName = arguments.ProjectName;
                package.CsProjPath = Path.Combine(arguments.ProjectDirectory, arguments.ProjectFile);
                string directory = package.RepositoryDirectory;
                rets = await GitBranchManager.IsOnDefaultBranchAsync(directory);
                if (rets == false)
                {
                    Console.WriteLine("You are not on default branch.  Therefore, will not create or update the packages.");
                    return;
                }
                package.NugetPackagePath = Path.Combine(arguments.ProjectDirectory, "bin", "Release");
                CsProjEditor editor = new(package.CsProjPath);
                EnumFeedType? feedType = editor.GetFeedType() ?? throw new CustomBasicException("No feed type found in the csproj file");
                package.FeedType = feedType.Value;
                // Determine the target framework (NetStandard or NetRuntime)
                EnumTargetFramework targetFramework = GetTargetFrameworkFromOutputDirectory(arguments.OutputDirectory);
                package.Framework = targetFramework;
                if (package.FeedType == EnumFeedType.Public && targetFramework == EnumTargetFramework.NetRuntime)
                {
                    package.Version = $"{netVersion}.0.1";
                }
                else
                {
                    package.Version = "1.0.1";
                }
                if (package.FeedType == EnumFeedType.Public)
                {
                    if (handler.NeedsPrefix(package, editor))
                    {
                        package.PrefixForPackageName = prefixName;
                    }
                }
                await context.AddPackageAsync(package);
            }
            await CreateAndUploadNuGetPackageAsync(package);
            if (package.Development == false)
            {
                string developmentFeed = configuration.DevelopmentPackagePath;
                await LocalNuGetFeedManager.DeletePackageFolderAsync(developmentFeed, package.PackageName); //if its not there, just ignore.
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during post-build processing to private feed: {ex.Message}");
            Environment.Exit(1); //so will error out.
        }
    }
    private async Task CreateAndUploadNuGetPackageAsync(NuGetPackageModel package)
    {
        bool created = await packer.CreateNugetPackageAsync(package, true);
        if (!created)
        {
            throw new CustomBasicException("Failed to create nuget package.");
        }
        if (!Directory.Exists(package.NugetPackagePath))
        {
            throw new CustomBasicException($"NuGet package path does not exist: {package.NugetPackagePath}");
        }
        var files = ff1.FileList(package.NugetPackagePath);
        files.RemoveAllOnly(x => !x.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
        if (files.Count != 1)
        {
            throw new CustomBasicException($"Error: Expected 1 .nupkg file, but found {files.Count}.");
        }
        string nugetFile = ff1.FullFile(files.Single());
        bool uploaded = await LocalNuGetFeedUploader.UploadPrivateNugetPackageAsync(GetFeedToUse(package), package.NugetPackagePath, nugetFile);
        if (!uploaded)
        {
            throw new CustomBasicException("Failed to publish nuget package to private feed");
        }
    }
    private async Task UpdatePackageVersionAsync(NuGetPackageModel package)
    {
        string version = package.Version.IncrementMinorVersion();
        await context.UpdatePackageVersionAsync(package.PackageName, version);
    }
    private static string GetFeedToUse(NuGetPackageModel package)
    {
        string stagingPath = bb1.Configuration!.StagingPackagePath;
        string developmentPath = bb1.Configuration!.DevelopmentPackagePath;
        string localPath = bb1.Configuration!.PrivatePackagePath;
        if (package.Development)
        {
            return developmentPath;
        }
        if (package.FeedType == EnumFeedType.Local)
        {
            return localPath;
        }
        return stagingPath;
    }

    // Method to determine if the target framework is NetStandard or NetRuntime based on the output directory
    private static EnumTargetFramework GetTargetFrameworkFromOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrEmpty(outputDirectory))
        {
            throw new CustomBasicException("Output directory is null or empty.");
        }

        // Check if the last folder in the path contains "netstandard"
        if (outputDirectory.Contains("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return EnumTargetFramework.NetStandard;  // Targeting .NET Standard
        }

        // Otherwise, check if the last folder is in the form of "netX.X" (e.g., net9.0, net6.0, etc.)
        var directorySegments = outputDirectory.Split(Path.DirectorySeparatorChar);
        var lastSegment = directorySegments.LastOrDefault();

        if (!string.IsNullOrEmpty(lastSegment) && lastSegment.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            // If the last segment starts with "net", it's likely a .NET runtime (e.g., net5.0, net6.0, etc.)
            return EnumTargetFramework.NetRuntime;  // Targeting .NET runtime
        }

        // Default case, if no matching patterns are found
        throw new CustomBasicException("Unable to determine target framework from the output directory.");
    }
}