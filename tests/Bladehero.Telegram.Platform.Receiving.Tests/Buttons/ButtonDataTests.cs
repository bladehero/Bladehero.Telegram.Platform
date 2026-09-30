using System.Reflection;
using Bladehero.Telegram.Platform.Receiving.Buttons;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Bladehero.Telegram.Platform.Receiving.Tests.Buttons;

public sealed class ButtonDataTests
{
    private static readonly Guid SomeGuid = Guid.Parse("0199C3A1-B2C3-4D5E-8F90-A1B2C3D4E5F6");

    public static TheoryData<object, string> CanonicalForms =>
        new()
        {
            { new BoolButton(true), "data-bool:1" },
            { new BoolButton(false), "data-bool:0" },
            { new GuidButton(SomeGuid), "data-guid:0199c3a1b2c34d5e8f90a1b2c3d4e5f6" },
            { new SizeButton(Size.Large), "data-size:large" },
            { new DateButton(new DateOnly(2026, 9, 29)), "data-date:2026-09-29" },
            { new LongButton(-1001234567890), "data-long:-1001234567890" },
            { new MaybeButton(null), "data-maybe:" },
            { new TextButton("a:b%c@d"), "data-text:a%3Ab%25c%40d" },
        };

    public static TheoryData<object> EveryType =>
        new()
        {
            new SByteButton(sbyte.MinValue),
            new SByteButton(sbyte.MaxValue),
            new ByteButton(byte.MinValue),
            new ByteButton(byte.MaxValue),
            new ShortButton(short.MinValue),
            new ShortButton(short.MaxValue),
            new UShortButton(ushort.MinValue),
            new UShortButton(ushort.MaxValue),
            new IntButton(int.MinValue),
            new IntButton(int.MaxValue),
            new UIntButton(uint.MinValue),
            new UIntButton(uint.MaxValue),
            new LongButton(long.MinValue),
            new LongButton(long.MaxValue),
            new ULongButton(ulong.MinValue),
            new ULongButton(ulong.MaxValue),
            new BoolButton(true),
            new BoolButton(false),
            new GuidButton(SomeGuid),
            new SizeButton(Size.Small),
            new DateButton(new DateOnly(2026, 9, 29)),
            new TextButton(""),
            new TextButton("a:b%c@d %3A %25"),
            new TextButton("Привет"),
            new TextButton("☕🎉"),
            new MaybeButton(null),
            new MaybeButton(5),
            new MaybeSizeButton(null),
            new MaybeSizeButton(Size.Large),
        };

    [Fact]
    public void Encode_ShouldWriteThePrefixAndTheFieldsInConstructorOrder()
    {
        // Act
        var data = ButtonData.Encode(new Order(7000000001, 2, "Nick"));

        // Assert
        data.Should().Be("data-order:7000000001:2:Nick");
    }

    [Fact]
    public void Encode_AButtonWithoutFields_ShouldWriteOnlyThePrefix()
    {
        // Act
        var data = ButtonData.Encode(new Close());

        // Assert
        data.Should().Be("data-close");
    }

    [Theory]
    [MemberData(nameof(CanonicalForms))]
    public void Encode_ShouldWriteTheCanonicalForms(object button, string expected)
    {
        // Act
        var data = Encode(button);

        // Assert
        data.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(EveryType))]
    public void EncodeAndTryDecode_ShouldRoundTripEverySupportedType(object button)
    {
        // Act
        var decoded = TryDecode(button.GetType(), Encode(button), out var roundTripped);

        // Assert
        using (new AssertionScope())
        {
            decoded.Should().BeTrue();
            roundTripped.Should().Be(button);
        }
    }

    [Fact]
    public void Encode_WhenAStringIsNull_ShouldThrowNamingTheField()
    {
        // Act
        var act = () => ButtonData.Encode(new TextButton(null!));

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("button")
            .WithMessage("TextButton.Value is null, and button data can't carry null strings.*");
    }

    [Fact]
    public void Encode_WhenAnEnumValueHasNoName_ShouldThrow()
    {
        // Act
        var act = () => ButtonData.Encode(new SizeButton((Size)7));

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("button")
            .WithMessage("SizeButton.Value is 7, which isn't a named Size value.*");
    }

    [Fact]
    public void Encode_WhenAStringHasALoneSurrogate_ShouldThrow()
    {
        // Act
        var act = () => ButtonData.Encode(new TextButton("tea \ud83c"));

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("button")
            .WithMessage(
                "TextButton.Value isn't valid Unicode (it has a lone surrogate), so Telegram would change it.*"
            );
    }

    // "data-text:" is 10 bytes; the value fills the other 54, one byte or two at a time.
    [Theory]
    [InlineData("a", 54)]
    [InlineData("я", 27)]
    public void Encode_AtExactly64Bytes_ShouldSucceed(string letter, int count)
    {
        // Arrange
        var value = string.Concat(Enumerable.Repeat(letter, count));

        // Act
        var data = ButtonData.Encode(new TextButton(value));

        // Assert
        data.Should().Be($"data-text:{value}");
    }

    // 65 bytes each; the emoji is 4 bytes but only 2 characters.
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa😀")]
    public void Encode_Over64Bytes_ShouldThrowWithTheByteCount(string value)
    {
        // Act
        var act = () => ButtonData.Encode(new TextButton(value));

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("button")
            .WithMessage(
                $"The TextButton button's data \"data-text:{value}\" is 65 bytes; Telegram takes at most 64.*"
            );
    }

    [Fact]
    public void Encode_WhenTheTypeHasNoButtonDataAttribute_ShouldThrow()
    {
        // Act
        var act = () => ButtonData.Encode(new Plain(1));

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Plain isn't button data: mark it [ButtonData(\"prefix\")].");
    }

    [Theory]
    [InlineData(typeof(IntButton), "data-int:+5")]
    [InlineData(typeof(IntButton), "data-int:007")]
    [InlineData(typeof(IntButton), "data-int:-0")]
    [InlineData(typeof(IntButton), "data-int: 5")]
    [InlineData(typeof(BoolButton), "data-bool:True")]
    [InlineData(typeof(BoolButton), "data-bool:2")]
    [InlineData(typeof(GuidButton), "data-guid:0199C3A1B2C34D5E8F90A1B2C3D4E5F6")]
    [InlineData(typeof(GuidButton), "data-guid:0199c3a1-b2c3-4d5e-8f90-a1b2c3d4e5f6")]
    [InlineData(typeof(SizeButton), "data-size:Large")]
    [InlineData(typeof(SizeButton), "data-size:1")]
    [InlineData(typeof(SizeButton), "data-size:small,large")]
    [InlineData(typeof(DateButton), "data-date:2026-9-29")]
    [InlineData(typeof(TextButton), "data-text:%3a")]
    [InlineData(typeof(TextButton), "data-text:%zz")]
    [InlineData(typeof(TextButton), "data-text:@")]
    public void TryDecode_WhenTheDataIsNotCanonical_ShouldReturnFalse(Type type, string data)
    {
        // Act
        var decoded = TryDecode(type, data, out _);

        // Assert
        decoded.Should().BeFalse();
    }

    [Theory]
    [InlineData("data-intx:5")]
    [InlineData("data-in:5")]
    [InlineData("DATA-INT:5")]
    public void TryDecode_WhenThePrefixDiffers_ShouldReturnFalse(string data)
    {
        // Act
        var decoded = ButtonData.TryDecode<IntButton>(data, out _);

        // Assert
        decoded.Should().BeFalse();
    }

    [Theory]
    [InlineData("data-order:7000000001:2")]
    [InlineData("data-order:7000000001:2:Nick:x")]
    [InlineData("data-order:7000000001:2:Nick:")]
    public void TryDecode_WhenTheSegmentCountDiffers_ShouldReturnFalse(string data)
    {
        // Act
        var decoded = ButtonData.TryDecode<Order>(data, out _);

        // Assert
        decoded.Should().BeFalse();
    }

    [Fact]
    public void TryDecode_WhenTrailingFieldsWithDefaultsAreMissing_ShouldUseTheDefaults()
    {
        // Act
        var pageOnly = ButtonData.TryDecode<PageButton>("data-page:3", out var three);
        var withSize = ButtonData.TryDecode<PageButton>("data-page:4:small", out var four);

        // Assert
        using (new AssertionScope())
        {
            pageOnly.Should().BeTrue();
            three.Should().Be(new PageButton(3, Size.Large, default));
            withSize.Should().BeTrue();
            four.Should().Be(new PageButton(4, Size.Small, default));
        }
    }

    [Fact]
    public void TryDecode_WhenTheConstructorThrows_ShouldReturnFalse()
    {
        // Act
        var decoded = ButtonData.TryDecode<Positive>("data-positive:-1", out var button);

        // Assert
        using (new AssertionScope())
        {
            decoded.Should().BeFalse();
            button.Should().Be(default(Positive));
        }
    }

    [Fact]
    public void TryDecode_WhenTheDataIsNull_ShouldReturnFalse()
    {
        // Act
        var decoded = ButtonData.TryDecode<IntButton>(null, out var button);

        // Assert
        using (new AssertionScope())
        {
            decoded.Should().BeFalse();
            button.Should().Be(default(IntButton));
        }
    }

    [Theory]
    [InlineData("data-close", true)]
    [InlineData("data-close:", false)]
    [InlineData("data-close:1", false)]
    [InlineData("data-closed", false)]
    public void TryDecode_AButtonWithoutFields_ShouldMatchOnlyItsPrefix(string data, bool expected)
    {
        // Act
        var decoded = ButtonData.TryDecode<Close>(data, out _);

        // Assert
        decoded.Should().Be(expected);
    }

    [Fact]
    public void Button_ShouldCarryTheTextAndTheEncodedData()
    {
        // Act
        var button = ButtonData.Button("Two cups", new Order(7000000001, 2, "Nick"));

        // Assert
        using (new AssertionScope())
        {
            button.Text.Should().Be("Two cups");
            button.CallbackData.Should().Be("data-order:7000000001:2:Nick");
        }
    }

    // ButtonData is generic over the button type, so theory rows of mixed types go through reflection.
    private static string Encode(object button) =>
        (string)Generic(nameof(ButtonData.Encode), button.GetType()).Invoke(null, [button])!;

    private static bool TryDecode(Type type, string data, out object? button)
    {
        var arguments = new object?[] { data, null };
        var decoded = (bool)Generic(nameof(ButtonData.TryDecode), type).Invoke(null, arguments)!;
        button = arguments[1];
        return decoded;
    }

    private static MethodInfo Generic(string name, Type type) =>
        typeof(ButtonData).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(type);

    private enum Size
    {
        Small,
        Large,
    }

    [ButtonData("data-order")]
    private readonly record struct Order(long OwnerId, int Cups, string Name);

    [ButtonData("data-close")]
    private readonly record struct Close;

    [ButtonData("data-sbyte")]
    private readonly record struct SByteButton(sbyte Value);

    [ButtonData("data-byte")]
    private readonly record struct ByteButton(byte Value);

    [ButtonData("data-short")]
    private readonly record struct ShortButton(short Value);

    [ButtonData("data-ushort")]
    private readonly record struct UShortButton(ushort Value);

    [ButtonData("data-int")]
    private readonly record struct IntButton(int Value);

    [ButtonData("data-uint")]
    private readonly record struct UIntButton(uint Value);

    [ButtonData("data-long")]
    private readonly record struct LongButton(long Value);

    [ButtonData("data-ulong")]
    private readonly record struct ULongButton(ulong Value);

    [ButtonData("data-bool")]
    private readonly record struct BoolButton(bool Value);

    [ButtonData("data-guid")]
    private readonly record struct GuidButton(Guid Value);

    [ButtonData("data-size")]
    private readonly record struct SizeButton(Size Value);

    [ButtonData("data-date")]
    private readonly record struct DateButton(DateOnly Value);

    [ButtonData("data-text")]
    private readonly record struct TextButton(string Value);

    [ButtonData("data-maybe")]
    private readonly record struct MaybeButton(int? Value);

    [ButtonData("data-maybe-size")]
    private readonly record struct MaybeSizeButton(Size? Value);

    [ButtonData("data-page")]
    private readonly record struct PageButton(int Page, Size Size = Size.Large, DateOnly Day = default);

    [ButtonData("data-positive")]
    private readonly record struct Positive(int Value)
    {
        public int Value { get; } = Value > 0 ? Value : throw new ArgumentOutOfRangeException(nameof(Value));
    }

    private readonly record struct Plain(int Value);
}
