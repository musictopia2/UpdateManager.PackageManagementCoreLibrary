namespace UpdateManager.PackageManagementCoreLibrary;
public class PackageDiscoveryService(IPackagesContext context, IPackageDiscoveryHandler handler)
{
    public async Task AddPackageAsync(NuGetPackageModel package)
    {
        string programPath = bb1.Configuration!.PackagePostBuildFeedProcessorProgram;
        //even if i add to the list as i go along, should not be bad.
        CsProjEditor editor = new(package.CsProjPath);
        editor.RemovePostBuildCommand();
        editor.AddPostBuildCommand(programPath, true);
        editor.AddFeedType(package.FeedType);
        editor.SaveChanges();
        await context.AddPackageAsync(package);
    }
    public async Task<BasicList<NuGetPackageModel>> DiscoverMissingPackagesAsync()
    {
        BasicList<NuGetPackageModel> output = [];
        BasicList<NuGetPackageModel> existingPackages = await context.GetPackagesAsync();
        var existingPackageNames = new HashSet<string>(existingPackages.Select(p => p.PackageName));
        BasicList<string> folders = await handler.GetPackageDirectoriesAsync();
        string netVersion = bb1.Configuration!.NetVersion;
        string prefixName = bb1.Configuration!.PackagePrefixFromConfig;
        foreach (var folder in folders)
        {
            if (ff1.DirectoryExists(folder) == false)
            {
                continue; //does not exist. continue
            }
            BasicList<string> toCheck = await ff1.DirectoryListAsync(folder, SearchOption.AllDirectories);
            toCheck.RemoveAllAndObtain(d =>
            {
                if (d.Contains("Archived", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return handler.CanIncludeProject(d) == false;
            });
            foreach (var dir in toCheck)
            {
                var projectFiles = await ff1.GetSeveralSpecificFilesAsync(dir, "csproj");
                foreach (var projectFile in projectFiles)
                {
                    if (Path.GetFileName(projectFile).Contains(".backup", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip this file
                    }
                    string packageName = ff1.FileName(projectFile);

                    // **Skip the extraction if the package already exists**
                    if (existingPackageNames.Contains(packageName))
                    {
                        continue; // Skip this package
                    }

                    if (handler.CanIncludeProject(projectFile) == false)
                    {
                        continue;
                    }
                    NuGetPackageModel model = ExtractPackageInfo(projectFile, packageName, netVersion, prefixName);
                    output.Add(model);
                }
            }
        }
        return output;
    }
    private NuGetPackageModel ExtractPackageInfo(string projectFile, string packageName, string netVersion, string prefixName)
    {
        CsProjEditor editor = new(projectFile);
        NuGetPackageModel model = new();
        //you can customize any other stuff but some things are forced.
        model.PackageName = packageName;
        handler.CustomizePackageModel(model);
        model.PackageName = packageName;
        model.CsProjPath = projectFile;
        model.FeedType = handler.GetFeedType(projectFile);
        model.NugetPackagePath = GetNuGetPackagePath(projectFile);
        model.Framework = editor.GetTargetFramework();
        if (model.Framework == EnumTargetFramework.NetStandard || model.FeedType == EnumFeedType.Local)
        {
            model.Version = "1.0.0"; //when you do a build, will already increment by 1.
        }
        else
        {
            model.Version = $"{netVersion}.0.0"; //when you do a first build, then will increment by 1.
        }
        if (model.FeedType == EnumFeedType.Public)
        {
            if (handler.NeedsPrefix(model, editor))
            {
                model.PrefixForPackageName = prefixName; //must be forced to this.
            }
        }
        return model;
    }
    private static string GetNuGetPackagePath(string projectFile)
    {
        string directoryPath = Path.GetDirectoryName(projectFile)!;
        return Path.Combine(directoryPath, "bin", "Release");
    }
}