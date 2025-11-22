namespace UpdateManager.PackageManagementCoreLibrary;
public class NuGetPublicPackageUploadManager(IPackagesContext packagesContext,
    IUploadedPackagesStorage uploadContext,
    INugetUploader uploader
    )
{
    // The method that does all the work of checking, uploading, and tracking
    public async Task UploadPackagesAsync(CancellationToken cancellationToken = default)
    {
        string feedUrl = bb1.Configuration!.StagingPackagePath;
        BasicList<UploadedPackageModel> list = await GetUploadedPackagesAsync(feedUrl, cancellationToken);
        list = list.ToBasicList(); //try to make a copy here too.
        await UploadPackagesAsync(list, cancellationToken);
        await CheckPackagesAsync(list, feedUrl);
    }
    public async Task<bool> HasItemsToProcessAsync()
    {
        var list = await uploadContext.GetAllUploadedPackagesAsync();
        return list.Count > 0;
    }
    private async Task UploadPackagesAsync(BasicList<UploadedPackageModel> packages, CancellationToken cancellationToken)
    {
        await packages.ForConditionalItemsAsync(x => x.Uploaded == false, async item =>
        {
            bool rets;
            rets = await uploader.UploadNugetPackageAsync(item.NugetFilePath, cancellationToken);
            if (rets)
            {
                item.Uploaded = true;
                await uploadContext.UpdateUploadedPackageAsync(item); //update this one since it was not uploaded
                Console.WriteLine("Your package was pushed");
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
    private async Task<BasicList<UploadedPackageModel>> GetUploadedPackagesAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var stagingPackages = await LocalNuGetFeedManager.GetAllPackagesAsync(feedUrl, cancellationToken);
        var allPackages = await packagesContext.GetPackagesAsync();
        var uploadedPackages = await uploadContext.GetAllUploadedPackagesAsync();
        BasicList<UploadedPackageModel> output = [];
        //the moment of truth has to be the staging packages.
        foreach (var name in stagingPackages)
        {
            //this means needs to add package.
            var ourPackage = allPackages.SingleOrDefault(x => x.PackageID.Equals(name, StringComparison.CurrentCultureIgnoreCase));
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
        await uploadContext.SaveUpdatedUploadedListAsync(output); //i think.
        return output;
    }
}