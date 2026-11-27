using FluentAssertions;
using GroundUp.Auth.Services.Invitations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Invitations;

/// <summary>
/// GU-68 Requirement 6.5: the placeholder sender logs the link (a credential) only in Development.
/// </summary>
public sealed class LoggingInvitationEmailSenderTests
{
    private const string Link = "https://app.test/invite/secret-token-value";

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public async Task SendAsync_LogsTheLinkOnlyInDevelopment(string environmentName, bool linkLogged)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var logger = new ListLogger();
        var invitationId = Guid.NewGuid();

        await new LoggingInvitationEmailSender(environment, logger)
            .SendAsync(new InvitationEmail("ana@example.com", "Acme", "Editor", Link, DateTime.UtcNow.AddDays(7), invitationId));

        var message = logger.Messages.Should().ContainSingle().Subject;
        message.Should().Contain(invitationId.ToString());
        message.Contains("secret-token-value").Should().Be(linkLogged);
    }

    private sealed class ListLogger : ILogger<LoggingInvitationEmailSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
