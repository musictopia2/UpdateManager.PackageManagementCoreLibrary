namespace UpdateManager.PackageManagementCoreLibrary;
public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection RegisterPostBuildServices()
        {
            services.AddSingleton<IPackagesContext, FilePackagesContext>()
                .AddSingleton<INugetPacker, NugetPacker>()
                .AddSingleton<PrivatePackageDeploymentProcessor>()
                ; //for now, just one.
            return services;
        }
        public IServiceCollection RegisterPackageDiscoveryServices()
        {
            services.AddTransient<IPackagesContext, FilePackagesContext>()
                .AddTransient<PackageDiscoveryService>();
            return services;
        }
        public IServiceCollection RegisterPublicPackageUploadServices()
        {
            services.AddSingleton<IPackagesContext, FilePackagesContext>()
                .AddSingleton<IUploadedPackagesStorage, FileUploadedPackagesStorage>()
                .AddSingleton<INugetUploader, PublicNugetUploader>()
                .AddSingleton<NuGetPublicPackageUploadManager>();
            return services;
        }
    }
    
}