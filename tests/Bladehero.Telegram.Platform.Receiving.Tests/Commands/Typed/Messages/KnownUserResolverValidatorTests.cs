using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.Messages;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Commands.Typed.Messages;

// Abstract commands, so the assembly scan never sees them.
public sealed class KnownUserResolverValidatorTests
{
    private static readonly IReadOnlyDictionary<Type, string[]> Requirements = new Dictionary<Type, string[]>
    {
        [typeof(Guest)] = ["CheckInCommand", "RoomButton"],
    };

    [Fact]
    public void Validate_WhenEveryResolverIsRegistered_ShouldSucceed()
    {
        // Arrange
        var sut = Validator(services => services.AddScoped<ITelegramUserResolver<Guest>, GuestResolver>());

        // Act
        var result = sut.Validate(name: null, new KnownUserResolvers());

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenAResolverIsMissing_ShouldFailNamingTheUserTypeAndItsCommands()
    {
        // Arrange
        var sut = Validator(_ => { });

        // Act
        var result = sut.Validate(name: null, new KnownUserResolvers());

        // Assert
        using (new AssertionScope())
        {
            result.Failed.Should().BeTrue();
            result
                .Failures.Should()
                .Equal(
                    "Commands for known Guest users need an ITelegramUserResolver<Guest>, which isn't registered: "
                        + "CheckInCommand, RoomButton. Register one, e.g. "
                        + "services.AddScoped<ITelegramUserResolver<Guest>, …>()."
                );
        }
    }

    [Fact]
    public void Validate_WithAnOpenGenericResolver_ShouldSucceed()
    {
        // Arrange
        var sut = Validator(services => services.AddScoped(typeof(ITelegramUserResolver<>), typeof(AnyoneResolver<>)));

        // Act
        var result = sut.Validate(name: null, new KnownUserResolvers());

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithOnlyAKeyedResolver_ShouldFail()
    {
        // Arrange
        var sut = Validator(services => services.AddKeyedScoped<ITelegramUserResolver<Guest>, GuestResolver>("hotel"));

        // Act
        var result = sut.Validate(name: null, new KnownUserResolvers());

        // Assert
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithoutIServiceProviderIsService_ShouldSucceed()
    {
        // Arrange
        var sut = new KnownUserResolverValidator(Requirements, services: null);

        // Act
        var result = sut.Validate(name: null, new KnownUserResolvers());

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void RequirementsOf_ShouldGroupTheKnownUserCommandsByUserType()
    {
        // Act
        var requirements = KnownUserResolverValidator.RequirementsOf([
            typeof(RoomButton),
            typeof(PlainCommand),
            typeof(CheckInCommand),
            typeof(ShiftCommand),
        ]);

        // Assert
        requirements
            .Should()
            .BeEquivalentTo(
                new Dictionary<Type, string[]>
                {
                    [typeof(Guest)] = ["CheckInCommand", "RoomButton"],
                    [typeof(Clerk)] = ["ShiftCommand"],
                },
                options => options.WithStrictOrdering()
            );
    }

    private static KnownUserResolverValidator Validator(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new KnownUserResolverValidator(
            Requirements,
            services.BuildServiceProvider().GetService<IServiceProviderIsService>()
        );
    }

    private sealed record Guest(string Name);

    private sealed record Clerk(string Name);

    private sealed class GuestResolver : ITelegramUserResolver<Guest>
    {
        public Task<Guest?> ResolveAsync(long chatId, long userId, CancellationToken token) =>
            Task.FromResult<Guest?>(null);
    }

    private sealed class AnyoneResolver<TUser> : ITelegramUserResolver<TUser>
        where TUser : class
    {
        public Task<TUser?> ResolveAsync(long chatId, long userId, CancellationToken token) =>
            Task.FromResult<TUser?>(null);
    }

    private abstract class CheckInCommand : KnownUserCommand<Guest>;

    private abstract class RoomButton : KnownUserCallbackQueryCommand<Guest, int>;

    private abstract class ShiftCommand : KnownUserCommand<Clerk>;

    private abstract class PlainCommand : MessageCommand
    {
        protected override Task<bool> CanHandleAsync(TypedCommandRequest<Message> request, CancellationToken token) =>
            Task.FromResult(false);
    }
}
