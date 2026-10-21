namespace UpdateManager.PackageManagementCoreLibrary;
public class NuGetPublicPackageUploadManager(IPackagesContext packagesContext,
    IUploadedPackagesStorage uploadContext,
    INugetUploader uploader
    )
{
    public async Task UploadRequiredPackagesAsync(string requiredBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredBy);
        string feedUrl = bb1.Configuration!.StagingPackagePath;
        var allPackages = await packagesContext.GetPackagesAsync();
        allPackages.KeepConditionalItems(x =>
            string.Equals(
                x.RequiredBy,
                requiredBy,
                StringComparison.OrdinalIgnoreCase));
        BasicList<UploadedPackageModel> list = await GetUploadedPackagesAsync(feedUrl, allPackages, cancellationToken);
        await UploadPackagesAsync(list, cancellationToken);
    }
    public async Task<bool> NeedsToWaitAsync(string requiredBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredBy);
        var uploadedPackages =
            await uploadContext.GetAllUploadedPackagesAsync();
        var allPackages =
            await packagesContext.GetPackagesAsync();
        var requiredPackageNames = allPackages
            .Where(x => string.Equals(
                x.RequiredBy,
                requiredBy,
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.PackageID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        uploadedPackages.KeepConditionalItems(x =>
            requiredPackageNames.Contains(x.PackageId));
        if (uploadedPackages.Any(x => x.Uploaded == false))
        {
            throw new CustomBasicException(
                $"There are packages required by {requiredBy} that have not been uploaded yet");
        }
        return uploadedPackages.Count > 0;
    }
    public async Task<bool> WaitForRequiredPackagesAsync(
        string requiredBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredBy);
        WaitHelper.Status = $"packages required by {requiredBy.ToLowerInvariant()}";
        string feedUrl = bb1.Configuration!.StagingPackagePath;
        var allPackages = await packagesContext.GetPackagesAsync();
        var requiredPackageNames = allPackages
            .Where(x => string.Equals(
                x.RequiredBy,
                requiredBy,
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.PackageID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await WaitHelper.WaitUntilAsync(async ct =>
        {
            var uploadedPackages =
                await uploadContext.GetAllUploadedPackagesAsync();
            uploadedPackages.KeepConditionalItems(x =>
                requiredPackageNames.Contains(x.PackageId));
            if (uploadedPackages.Any(x => x.Uploaded == false))
            {
                throw new CustomBasicException(
                $"There are packages required by {requiredBy} that have not been uploaded yet");
            }
            return await AllSuccessfulAsync(uploadedPackages, feedUrl);

        }, cancellationToken: cancellationToken);
    }
    private async Task<bool> AllSuccessfulAsync(BasicList<UploadedPackageModel> uploads, string feedUrl)
    {
        bool failed = false;
        Console.WriteLine($"Checking packages to see if they are now available on {DateTime.Now}");
        await uploads.ForEachAsync(async item =>
        {
            if (failed)
            {
                return;
            }
            string packageId = item.PackageId;
            bool rets = await NuGetPackageChecker.IsPublicPackageAvailableAsync(packageId, item.Version);
            if (rets)
            {
                Console.WriteLine($"Required Package {item.PackageId} is finally on nuget.  Can now delete");
                await uploadContext.DeleteUploadedPackageAsync(item.PackageId);
                await LocalNuGetFeedManager.DeletePackageFolderAsync(feedUrl, item.PackageId);
            }
            else
            {
                Console.WriteLine($"Required {item.PackageId} is still not on nuget.  Checking again in a minute");
                failed = true;
            }
        });
        return failed == false;
    }
    // The method that does all the work of checking, uploading, and tracking
    public async Task UploadPackagesAsync(CancellationToken cancellationToken = default)
    {
        string feedUrl = bb1.Configuration!.StagingPackagePath;
        var allPackages = await packagesContext.GetPackagesAsync();
        BasicList<UploadedPackageModel> list = await GetUploadedPackagesAsync(feedUrl, allPackages, cancellationToken);
        list = list.ToBasicList(); //try to make a copy here too.
        await UploadPackagesAsync(list, cancellationToken);
        await CheckPackagesAsync(list, feedUrl);
    }
    public async Task<bool> HasRequiredItemsToProcessAsync(
        string requiredBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredBy);
        string feedUrl = bb1.Configuration!.StagingPackagePath;
        var stagingPackages =
            await LocalNuGetFeedManager.GetAllPackagesAsync(
                feedUrl, cancellationToken);
        var trackedPackages =
            await uploadContext.GetAllUploadedPackagesAsync();
        var allPackages = await packagesContext.GetPackagesAsync();
        var requiredPackageNames = allPackages
            .Where(x => string.Equals(
                x.RequiredBy,
                requiredBy,
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.PackageID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasStaged = stagingPackages.Any(x =>
            requiredPackageNames.Contains(x));
        bool hasTracked = trackedPackages.Any(x =>
            requiredPackageNames.Contains(x.PackageId));
        return hasStaged || hasTracked;
    }
    public async Task<bool> HasItemsToProcessAsync(
        CancellationToken cancellationToken = default)
    {
        string feedUrl = bb1.Configuration!.StagingPackagePath;

        var stagingPackages =
            await LocalNuGetFeedManager.GetAllPackagesAsync(
                feedUrl, cancellationToken);

        var trackedPackages =
            await uploadContext.GetAllUploadedPackagesAsync();

        return stagingPackages.Any() || trackedPackages.Count > 0;
    }
    private async Task UploadPackagesAsync(BasicList<UploadedPackageModel> packages, CancellationToken cancellationToken)
    {
        await packages.ForConditionalItemsAsync(x => x.Uploaded == false, async item =>
        {
            bool rets;
            rets = await uploader.UploadNugetPackageAsync(item.NugetFilePath, item.PackageId, item.Version, cancellationToken);
            if (rets)
            {
                item.Uploaded = true;
                await uploadContext.UpdateUploadedPackageAsync(item); //update this one since it was not uploaded
                Console.WriteLine("Your package was pushed");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Failed to upload {item.PackageId} version {item.Version}. " +
                    "Check GitHub Actions for failure details.");
            }
        });
    }
    private async Task CheckPackagesAsync(BasicList<UploadedPackageModel> packages, string feedUrl)
    {
        await packages.ForConditionalItemsAsync(x => x.Uploaded, async item =>
        {
            Console.WriteLine($"Checking {item.PackageId} to see if its on public nuget");
            bool rets;
            rets = await NuGetPackageChecker.IsPublicPackageAvailableAsync(item.PackageId, item.Version);
            if (rets)
            {
                Console.WriteLine($"Package {item.PackageId} is finally on nuget.  Can now delete");
                await uploadContext.DeleteUploadedPackageAsync(item.PackageId);
                await LocalNuGetFeedManager.DeletePackageFolderAsync(feedUrl, item.PackageId);
            }
        });
    }
    private async Task<BasicList<UploadedPackageModel>> GetUploadedPackagesAsync(string feedUrl, BasicList<NuGetPackageModel> packages, CancellationToken cancellationToken)
    {
        var stagingPackages = await LocalNuGetFeedManager.GetAllPackagesAsync(feedUrl, cancellationToken);
        //var allPackages = await packagesContext.GetPackagesAsync();
        var uploadedPackages = await uploadContext.GetAllUploadedPackagesAsync();
        BasicList<UploadedPackageModel> output = [];
        //the moment of truth has to be the staging packages.
        foreach (var name in stagingPackages)
        {
            //this means needs to add package.
            var ourPackage = packages.SingleOrDefault(x => x.PackageID.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            var uploadedPackage = uploadedPackages.SingleOrDefault(x => x.PackageId.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            //i am guessing if you are now temporarily ignoring it, still okay to process because it was the past.
            //same thing for development.


            if (uploadedPackage is null && ourPackage is not null)
            {
                string packageId = ourPackage.PackageID;
                uploadedPackage = new()
                {
                    PackageId = packageId,
                    Version = ourPackage.Version,
                    NugetFilePath = LocalNuGetFeedManager.GetNugetFile(feedUrl, packageId, ourPackage.Version)
                };
                output.Add(uploadedPackage);
            }
            else if (ourPackage is not null && uploadedPackage is not null)
            {
                if (uploadedPackage.Version != ourPackage.Version)
                {
                    //this means needs to use the new version regardless of status
                    uploadedPackage.Version = ourPackage.Version;
                    uploadedPackage.NugetFilePath = LocalNuGetFeedManager.GetNugetFile(feedUrl, uploadedPackage.PackageId, ourPackage.Version);
                    uploadedPackage.Uploaded = false; //we have new version now.
                }
                output.Add(uploadedPackage);
            }
        }
        // Keep the selected packages separate for uploading.
        BasicList<UploadedPackageModel> updatedTracking = output.ToBasicList();

        var selectedPackageIds = packages
            .Select(x => x.PackageID)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var uploadedPackage in uploadedPackages)
        {
            if (!output.Any(x =>
                string.Equals(
                    x.PackageId,
                    uploadedPackage.PackageId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                updatedTracking.Add(uploadedPackage);
            }
        }

        // Save everything, including packages belonging to other workflows.
        await uploadContext.SaveUpdatedUploadedListAsync(updatedTracking);

        // Only upload the packages selected for this workflow.
        return output;
    }
}