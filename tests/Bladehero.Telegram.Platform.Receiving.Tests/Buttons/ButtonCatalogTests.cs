using Bladehero.Telegram.Platform.Receiving.Buttons;
using Bladehero.Telegram.Platform.Receiving.Commands;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed;
using Bladehero.Telegram.Platform.Receiving.Commands.Typed.CallbackQueries;
using Bladehero.Telegram.Platform.Receiving.Conversations;
using FluentAssertions;
using FluentAssertions.Execution;
using Telegram.Bot.Types;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

// The failing setups use structs without [Button] and abstract commands, so the assembly scan never sees them.
public sealed class ButtonCatalogTests
{
    private const string Failure = "The typed buttons can't be set up:\n- ";

    [Fact]
    public void Create_WithTwoButtonsSharingAPrefix_ShouldFailNamingBoth()
    {
        // Act
        var act = () => ButtonCatalog.Create([(typeof(Tea), "drink"), (typeof(Coffee), "drink")], []);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Be(Failure + "Coffee and Tea both use the prefix \"drink\"; give each button its own.");
    }

    [Fact]
    public void Create_WithTwoRegularCommandsForOneButton_ShouldFailNamingBoth()
    {
        // Act
        var act = () => ButtonCatalog.Create([(typeof(Tea), "tea")], [Regular<PourTea>(), Regular<BrewTea>()]);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Be(Failure + "Tea buttons are handled by BrewTea and PourTea; give each button type one command.");
    }

    [Theory]
    [InlineData("pick")]
    [InlineData(null)]
    public void Create_WithTwoStepsThatCanMatchOneState_ShouldFail(string? secondStep)
    {
        // Act
        var act = () =>
            ButtonCatalog.Create(
                [(typeof(Tea), "tea")],
                [Step<PourTea>("order", "pick"), Step<BrewTea>("order", secondStep)]
            );

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Be(
                Failure
                    + "Tea buttons are handled by BrewTea and PourTea, which can both run at the same step of "
                    + "\"order\"; give each step one command for it."
            );
    }

    [Fact]
    public void Create_WhenACommandHandlesANonButtonTypeWithoutParse_ShouldFail()
    {
        // Act
        var act = () => ButtonCatalog.Create([], [Regular<PourTea>(), Regular<PickPair>()]);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Be(
                Failure
                    + "PickPair handles ValueTuple<Int64, Int32>, which has no [Button] attribute; mark "
                    + "ValueTuple<Int64, Int32> [Button(\"prefix\")] or override Parse.\n- "
                    + "PourTea handles Tea, which has no [Button] attribute; mark Tea [Button(\"prefix\")] or override "
                    + "Parse."
            );
    }

    [Fact]
    public void Create_WithSeveralProblems_ShouldListThemAll()
    {
        // Act
        var act = () =>
            ButtonCatalog.Create(
                [(typeof(Tea), "drink"), (typeof(Coffee), "drink"), (typeof(Broken), "broken")],
                [Regular<PourTea>(), Regular<BrewTea>()]
            );

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Be(
                Failure
                    + "Broken can't be button data: At is a DateTime; a button holds strings, integers, bool, Guid, "
                    + "enums and DateOnly, or nullable ones.\n- "
                    + "Coffee and Tea both use the prefix \"drink\"; give each button its own.\n- "
                    + "Tea buttons are handled by BrewTea and PourTea; give each button type one command."
            );
    }

    [Fact]
    public void Create_WithStepsAtDifferentStepsOrFlows_ShouldPass()
    {
        // Act
        var act = () =>
            ButtonCatalog.Create(
                [(typeof(Tea), "tea")],
                [Step<PourTea>("order", "pick"), Step<BrewTea>("order", "confirm"), Step<SteepTea>("refill", null)]
            );

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_WithAStepAndARegularCommandForOneButton_ShouldPass()
    {
        // Act
        var act = () =>
            ButtonCatalog.Create([(typeof(Tea), "tea")], [Regular<PourTea>(), Step<BrewTea>("order", null)]);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Create_WhenACommandOverridesParse_ShouldAcceptANonButtonType()
    {
        // Act
        var act = () => ButtonCatalog.Create([], [Regular<ParsePair>()]);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Scan_ShouldFindButtonsDeclaredInTheAssemblyAndThoseTheCommandsHandle()
    {
        // Act
        var catalog = ButtonCatalog.Scan([typeof(Declared), typeof(Tea)], [Regular<PourHandled>()]);

        // Assert
        using (new AssertionScope())
        {
            catalog.TryFind("cat-declared:1", out var declared).Should().BeTrue();
            declared!.Type.Should().Be<Declared>();
            catalog.TryFind("cat-handled:1", out var handled).Should().BeTrue();
            handled!.Type.Should().Be<Handled>();
        }
    }

    [Theory]
    [InlineData("x:1", true)]
    [InlineData("x", true)]
    [InlineData("xy:1", false)]
    [InlineData("again", false)]
    public void TryFind_ShouldMatchOnlyAWholeRegisteredFirstSegment(string data, bool expected)
    {
        // Arrange
        var catalog = ButtonCatalog.Create([(typeof(Tea), "x")], []);

        // Act
        var found = catalog.TryFind(data, out _);

        // Assert
        found.Should().Be(expected);
    }

    private static CatalogedCommand Regular<TCommand>() => new(typeof(TCommand), CommandPriority.Default, null);

    private static CatalogedCommand Step<TCommand>(string flow, string? step) =>
        new(typeof(TCommand), CommandPriority.Default, new ConversationStepAttribute(flow, step));

    private readonly record struct Tea(int Cups);

    private readonly record struct Coffee(int Cups);

    private readonly record struct Broken(DateTime At);

    [Button("cat-declared")]
    private readonly record struct Declared(int Id);

    [Button("cat-handled")]
    private readonly record struct Handled(int Id);

    private abstract class PourTea : CallbackQueryCommand<Tea>;

    private abstract class BrewTea : CallbackQueryCommand<Tea>;

    private abstract class SteepTea : CallbackQueryCommand<Tea>;

    private abstract class PourHandled : CallbackQueryCommand<Handled>;

    private abstract class PickPair : CallbackQueryCommand<(long OwnerId, int Cups)>;

    private abstract class ParsePair : CallbackQueryCommand<(long OwnerId, int Cups)>
    {
        protected override (long OwnerId, int Cups)? Parse(string data) => null;

        protected override Task HandleAsync(TypedCommandRequest<CallbackQuery> request, CancellationToken token) =>
            Task.CompletedTask;
    }
}
