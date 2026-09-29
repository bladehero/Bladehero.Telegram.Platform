using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

// Structs without [Button], given a prefix here, so the assembly scan never sees them.
public sealed class ButtonCodecTests
{
    private const string Holds = "a button holds strings, integers, bool, Guid, enums and DateOnly, or nullable ones.";

    [Theory]
    [InlineData(typeof(WithDateTime), "At is a DateTime")]
    [InlineData(typeof(WithDouble), "Ratio is a Double")]
    [InlineData(typeof(WithArray), "Items is a Int32[]")]
    [InlineData(typeof(WithStruct), "Inner is a Inner")]
    [InlineData(typeof(WithNullableDateTime), "At is a Nullable<DateTime>")]
    public void TryCreate_WithAnUnsupportedField_ShouldNameTheTypeAndTheField(Type type, string field)
    {
        // Act
        var created = ButtonCodec.TryCreate(type, "codec", out _, out var problem);

        // Assert
        using (new AssertionScope())
        {
            created.Should().BeFalse();
            problem.Should().Be($"{type.Name} can't be button data: {field}; {Holds}");
        }
    }

    [Fact]
    public void TryCreate_WithAFlagsEnum_ShouldFail()
    {
        // Act
        ButtonCodec.TryCreate(typeof(WithFlags), "codec", out _, out var problem);

        // Assert
        problem
            .Should()
            .Be("WithFlags can't be button data: Extras is the [Flags] enum Extras, whose combinations have no name.");
    }

    [Fact]
    public void TryCreate_WithEnumNamesDifferingOnlyInCase_ShouldFail()
    {
        // Act
        ButtonCodec.TryCreate(typeof(WithCasedEnum), "codec", out _, out var problem);

        // Assert
        problem
            .Should()
            .Be(
                "WithCasedEnum can't be button data: Size's enum Cased has names that differ only in case (Big, BIG), "
                    + "and buttons write names in lower case."
            );
    }

    [Fact]
    public void TryCreate_WithAGenericType_ShouldFail()
    {
        // Act
        ButtonCodec.TryCreate(typeof(Wrapper<long>), "codec", out _, out var problem);

        // Assert
        problem.Should().Be("Wrapper<Int64> can't be button data: it is generic.");
    }

    [Fact]
    public void TryCreate_WithMoreThanOneMatchingConstructor_ShouldFail()
    {
        // Act
        ButtonCodec.TryCreate(typeof(TwoWays), "codec", out _, out var problem);

        // Assert
        problem
            .Should()
            .Be(
                "TwoWays can't be button data: more than one public constructor has parameters matching its "
                    + "properties; keep one."
            );
    }

    [Fact]
    public void TryCreate_WithoutAConstructorMatchingItsProperties_ShouldFail()
    {
        // Act
        ButtonCodec.TryCreate(typeof(Mismatched), "codec", out _, out var problem);

        // Assert
        problem
            .Should()
            .Be(
                "Mismatched can't be button data: no public constructor has parameters that each match a property of "
                    + "the same name and type."
            );
    }

    [Fact]
    public void TryCreate_WithoutAPublicConstructorWithParameters_ShouldHaveNoFields()
    {
        // Act
        var created = ButtonCodec.TryCreate(typeof(Bare), "codec", out var codec, out _);

        // Assert
        using (new AssertionScope())
        {
            created.Should().BeTrue();
            codec!.Encode(new Bare()).Should().Be("codec");
        }
    }

    [Flags]
    private enum Extras
    {
        None = 0,
        Milk = 1,
        Sugar = 2,
    }

    private enum Cased
    {
        Big,
        BIG,
    }

    private readonly record struct WithDateTime(DateTime At);

    private readonly record struct WithDouble(double Ratio);

    private readonly record struct WithArray(int[] Items);

    private readonly record struct Inner(int Value);

    private readonly record struct WithStruct(Inner Inner);

    private readonly record struct WithNullableDateTime(DateTime? At);

    private readonly record struct WithFlags(Extras Extras);

    private readonly record struct WithCasedEnum(Cased Size);

    private readonly record struct Wrapper<T>(T Value);

    private readonly struct TwoWays
    {
        public TwoWays(int Cups) => this.Cups = Cups;

        public TwoWays(int Cups, string Name)
        {
            this.Cups = Cups;
            this.Name = Name;
        }

        public int Cups { get; }

        public string? Name { get; }
    }

    private readonly struct Mismatched
    {
        public Mismatched(int count) => Total = count;

        public int Total { get; }
    }

    private readonly struct Bare
    {
        public int Ignored { get; init; }
    }
}
