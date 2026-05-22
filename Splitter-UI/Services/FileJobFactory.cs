using Microsoft.Extensions.DependencyInjection;

public sealed class FileJobFactory : IFileJobFactory
{
    private readonly IServiceProvider _services;

    public FileJobFactory(IServiceProvider services)
    {
        _services = services;
    }

    public JobViewModel Create(SingleJob job)
    {
        // Resolve a fresh VM + fresh services
        return ActivatorUtilities.CreateInstance<JobViewModel>(_services, job);
    }
}
