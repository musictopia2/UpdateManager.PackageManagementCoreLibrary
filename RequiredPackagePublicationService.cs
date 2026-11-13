namespace UpdateManager.PackageManagementCoreLibrary;
public class RequiredPackagePublicationService(
    NuGetPublicPackageUploadManager publication)
    : IRequiredPackagePublicationService
{
    public async Task EnsurePublishedAsync(
        BasicList<NuGetPackageModel> packages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        await publication.UploadRequiredPackagesAsync(
            packages,
            cancellationToken);
        bool available =
            await publication.WaitForRequiredPackagesAsync(
                packages,
                cancellationToken);
        if (available == false)
        {
            throw new InvalidOperationException(
                "Required package versions are not yet available on NuGet.");
        }
    }
}