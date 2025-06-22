namespace UpdateManager.PackageManagementCoreLibrary;
public static class ServiceExtensions
{
    public static IServiceCollection RegisterPostBuildServices(this IServiceCollection services)
    {
        services.AddSingleton<IPackagesContext, FilePackagesContext>()
            .AddSingleton<INugetPacker, NugetPacker>()
            .AddSingleton<PrivatePackageDeploymentProcessor>()
            ; //for now, just one.
        return services;
    }
    public static IServiceCollection RegisterPackageDiscoveryServices(this IServiceCollection services)
    {
        services.AddTransient<IPackagesContext, FilePackagesContext>()
            .AddTransient<PackageDiscoveryService>();
        return services;
    }
    public static IServiceCollection RegisterPublicPackageUploadServices(this IServiceCollection services)
    {
        services.AddSingleton<IPackagesContext, FilePackagesContext>()
            .AddSingleton<IUploadedPackagesStorage, FileUploadedPackagesStorage>()
            .AddSingleton<INugetUploader, PublicNugetUploader>()
            .AddSingleton<NuGetPublicPackageUploadManager>();
        return services;
    }
}