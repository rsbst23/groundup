using GroundUp.Auth.Services.Invitations;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Sample.Dev;

/// <summary>
/// The sample app's Development-only tools (GU-90): the dev mailbox for invitation emails and the
/// accept-URL seed. Not part of the framework.
/// </summary>
public static class SampleDevToolsServiceCollectionExtensions
{
    /// <summary>
    /// In Development, keeps invitation emails in a <see cref="DevMailbox"/> (shown by
    /// <c>GET /api/dev/invitation-emails</c>) instead of the framework's logging sender, and seeds the
    /// accept URL. Outside Development it registers nothing. Call after <c>AddGroundUpAuth</c>.
    /// </summary>
    public static IServiceCollection AddSampleDevTools(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment())
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DevMailbox>();
        // AddGroundUpAuth registers the default sender with TryAdd, so replace it here
        services.Replace(ServiceDescriptor.Scoped<IInvitationEmailSender, DevMailboxInvitationEmailSender>());
        services.AddScoped<IDataSeeder, DevSettingsSeeder>();
        return services;
    }
}
