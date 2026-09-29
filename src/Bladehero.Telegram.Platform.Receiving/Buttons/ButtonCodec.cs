using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace Bladehero.Telegram.Platform.Receiving.Buttons;

// Writes and reads the callback data of one button type: its prefix, then ':' and a segment for each field. Built by
// reflection once per type. A segment decodes only in the one form encoding writes, so equal buttons always carry equal
// data. No static constructor: a failure there would stay a TypeInitializationException for the rest of the process.
internal sealed class ButtonCodec
{
    private const string Holds = "a button holds strings, integers, bool, Guid, enums and DateOnly, or nullable ones.";

    private static readonly ConcurrentDictionary<Type, (ButtonCodec? Codec, string? Problem)> Attributed = new();
    private static readonly ConcurrentDictionary<Type, EnumSegment> Enums = new();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private static readonly Dictionary<Type, Segment> Segments = new()
    {
        [typeof(sbyte)] = new IntegerSegment<sbyte>(),
        [typeof(byte)] = new IntegerSegment<byte>(),
        [typeof(short)] = new IntegerSegment<short>(),
        [typeof(ushort)] = new IntegerSegment<ushort>(),
        [typeof(int)] = new IntegerSegment<int>(),
        [typeof(uint)] = new IntegerSegment<uint>(),
        [typeof(long)] = new IntegerSegment<long>(),
        [typeof(ulong)] = new IntegerSegment<ulong>(),
        [typeof(bool)] = new BoolSegment(),
        [typeof(Guid)] = new GuidSegment(),
        [typeof(DateOnly)] = new DateOnlySegment(),
        [typeof(string)] = new StringSegment(),
    };

    private readonly ConstructorInfo? _constructor;
    private readonly Field[] _fields;
    private readonly int _required;

    private ButtonCodec(Type type, string prefix, ConstructorInfo? constructor, Field[] fields)
    {
        Type = type;
        Prefix = prefix;
        _constructor = constructor;
        _fields = fields;
        _required = fields
            .Select((field, index) => field.Parameter.HasDefaultValue ? 0 : index + 1)
            .DefaultIfEmpty()
            .Max();
    }

    public Type Type { get; }

    public string Prefix { get; }

    // Explicit prefixes let tests build codecs for structs without [Button].
    internal static bool TryCreate(
        Type type,
        string prefix,
        [NotNullWhen(true)] out ButtonCodec? codec,
        [NotNullWhen(false)] out string? problem
    )
    {
        if (ShapeOf(type, out var constructor, out var fields) is { } reason)
        {
            codec = null;
            problem = $"{NameOf(type)} can't be button data: {reason}";
            return false;
        }

        codec = new ButtonCodec(type, prefix, constructor, fields);
        problem = null;
        return true;
    }

    // The codec of a [Button] type.
    internal static ButtonCodec Of(Type type)
    {
        var (codec, problem) = Attributed.GetOrAdd(
            type,
            static type =>
                type.GetCustomAttribute<ButtonAttribute>() is not { } button
                    ? (null, $"{NameOf(type)} isn't button data: mark it [Button(\"prefix\")].")
                : TryCreate(type, button.Prefix, out var codec, out var problem) ? (codec, null)
                : (null, problem)
        );

        return codec ?? throw new InvalidOperationException(problem);
    }

    // Type.Name, and ValueTuple<Int64, Int32> for a generic type.
    internal static string NameOf(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return !type.IsGenericType || tick < 0
            ? type.Name
            : $"{type.Name[..tick]}<{string.Join(", ", type.GetGenericArguments().Select(NameOf))}>";
    }

    public string Encode(object button)
    {
        var data = new StringBuilder(Prefix);
        foreach (var field in _fields)
        {
            data.Append(':').Append(Write(field, field.Property.GetValue(button)));
        }

        var encoded = data.ToString();
        var bytes = Encoding.UTF8.GetByteCount(encoded);
        if (bytes > ButtonData.MaxBytes)
        {
            throw new ArgumentException(
                $"The {NameOf(Type)} button's data \"{encoded}\" is {bytes} bytes; Telegram takes at most "
                    + $"{ButtonData.MaxBytes}.",
                nameof(button)
            );
        }

        return encoded;
    }

    // Never throws: data that doesn't decode, or that the constructor refuses, is simply not this button.
    public bool TryDecode(string data, out object? button)
    {
        button = null;

        var segments = data.Split(':');
        var count = segments.Length - 1;
        if (segments[0] != Prefix || count < _required || count > _fields.Length)
        {
            return false;
        }

        var arguments = new object?[_fields.Length];
        for (var index = 0; index < _fields.Length; index++)
        {
            var field = _fields[index];
            if (index >= count)
            {
                arguments[index] = DefaultOf(field.Parameter);
            }
            else if (!TryRead(field, segments[index + 1], out arguments[index]))
            {
                return false;
            }
        }

        try
        {
            button = _constructor is null ? Activator.CreateInstance(Type) : _constructor.Invoke(arguments);
            return button is not null;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
    }

    private string Write(Field field, object? value)
    {
        if (value is null)
        {
            return field.IsNullable
                ? ""
                : throw new ArgumentException(
                    $"{NameOf(Type)}.{field.Name} is null, and button data can't carry null strings.",
                    "button"
                );
        }

        if (value is string text && !IsValidUnicode(text))
        {
            throw new ArgumentException(
                $"{NameOf(Type)}.{field.Name} isn't valid Unicode (it has a lone surrogate), so Telegram would "
                    + "change it.",
                "button"
            );
        }

        return field.Segment.Write(value)
            ?? throw new ArgumentException(
                $"{NameOf(Type)}.{field.Name} is {value}, which isn't a named {NameOf(value.GetType())} value.",
                "button"
            );
    }

    // Only the canonical form: the value must encode back to exactly the segment it came from.
    private static bool TryRead(Field field, string segment, out object? value)
    {
        value = null;
        if (field.IsNullable && segment.Length == 0)
        {
            return true;
        }

        return field.Segment.TryRead(segment, out value) && field.Segment.Write(value) == segment;
    }

    private static object? DefaultOf(ParameterInfo parameter)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return parameter.DefaultValue switch
        {
            null => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null,
            var value when type.IsEnum && value.GetType() != type => Enum.ToObject(type, value),
            var value => value,
        };
    }

    private static bool IsValidUnicode(string text)
    {
        try
        {
            StrictUtf8.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    // Why the type can't be button data, or null with its constructor (none for a button without fields) and fields.
    private static string? ShapeOf(Type type, out ConstructorInfo? constructor, out Field[] fields)
    {
        constructor = null;
        fields = [];

        if (type.IsGenericType)
        {
            return "it is generic.";
        }

        var candidates = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.GetParameters().Length > 0)
            .ToArray();

        if (candidates.Length > 0)
        {
            var matching = candidates.Where(x => x.GetParameters().All(p => PropertyOf(type, p) is not null)).ToArray();
            switch (matching)
            {
                case []:
                    return "no public constructor has parameters that each match a property of the same name and type.";
                case [_, _, ..]:
                    return "more than one public constructor has parameters matching its properties; keep one.";
            }

            constructor = matching[0];
        }

        var built = new List<Field>();
        foreach (var parameter in constructor?.GetParameters() ?? [])
        {
            var name = parameter.Name!;
            var underlying = Nullable.GetUnderlyingType(parameter.ParameterType);
            var valueType = underlying ?? parameter.ParameterType;

            if (valueType.IsEnum)
            {
                if (valueType.IsDefined(typeof(FlagsAttribute), inherit: false))
                {
                    return $"{name} is the [Flags] enum {NameOf(valueType)}, whose combinations have no name.";
                }

                if (
                    Enum.GetNames(valueType).GroupBy(x => x.ToLowerInvariant()).FirstOrDefault(x => x.Count() > 1) is
                    { } clash
                )
                {
                    var names = string.Join(", ", clash.Take(2));
                    return $"{name}'s enum {NameOf(valueType)} has names that differ only in case ({names}), and "
                        + "buttons write names in lower case.";
                }
            }

            if (SegmentOf(valueType) is not { } segment)
            {
                return $"{name} is a {NameOf(parameter.ParameterType)}; {Holds}";
            }

            built.Add(
                new Field(name, PropertyOf(type, parameter)!, parameter, segment, IsNullable: underlying is not null)
            );
        }

        // A value set any other way would be lost: the data carries only the constructor's fields.
        if (SettableOutsideTheConstructor(type, built) is { } lost)
        {
            return $"{lost.Name} isn't a constructor parameter, so its value would be lost; make it one.";
        }

        fields = [.. built];
        return null;
    }

    // The first public property with a setter or init accessor that isn't a field, or else the first public field.
    private static MemberInfo? SettableOutsideTheConstructor(Type type, List<Field> fields)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.SetMethod?.IsPublic is true
                && property.GetIndexParameters().Length == 0
                && fields.All(field => field.Name != property.Name)
            )
            .OrderBy(property => property.MetadataToken);

        var publicFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(field => field.MetadataToken);

        return properties.Cast<MemberInfo>().Concat(publicFields).FirstOrDefault();
    }

    private static Segment? SegmentOf(Type type) =>
        type.IsEnum ? Enums.GetOrAdd(type, static type => new EnumSegment(type)) : Segments.GetValueOrDefault(type);

    // The public readable property with the parameter's name and type.
    private static PropertyInfo? PropertyOf(Type type, ParameterInfo parameter) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property =>
                property.Name == parameter.Name
                && property.PropertyType == parameter.ParameterType
                && property.GetMethod?.IsPublic is true
                && property.GetIndexParameters().Length == 0
            );

    private sealed record Field(
        string Name,
        PropertyInfo Property,
        ParameterInfo Parameter,
        Segment Segment,
        bool IsNullable
    );

    private abstract class Segment
    {
        // Null only for an enum value without a name.
        public abstract string? Write(object value);

        public abstract bool TryRead(string segment, [NotNullWhen(true)] out object? value);
    }

    private sealed class IntegerSegment<T> : Segment
        where T : IBinaryInteger<T>
    {
        public override string Write(object value) => ((T)value).ToString("D", CultureInfo.InvariantCulture);

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value)
        {
            var parsed = T.TryParse(
                segment,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var number
            );
            value = parsed ? number : null;
            return parsed;
        }
    }

    private sealed class BoolSegment : Segment
    {
        public override string Write(object value) => (bool)value ? "1" : "0";

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value)
        {
            value = segment switch
            {
                "1" => true,
                "0" => false,
                _ => null,
            };
            return value is not null;
        }
    }

    private sealed class GuidSegment : Segment
    {
        public override string Write(object value) => ((Guid)value).ToString("N");

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value)
        {
            var parsed = Guid.TryParseExact(segment, "N", out var guid);
            value = parsed ? guid : null;
            return parsed;
        }
    }

    private sealed class DateOnlySegment : Segment
    {
        private const string Format = "yyyy-MM-dd";

        public override string Write(object value) => ((DateOnly)value).ToString(Format, CultureInfo.InvariantCulture);

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value)
        {
            var parsed = DateOnly.TryParseExact(
                segment,
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            );
            value = parsed ? date : null;
            return parsed;
        }
    }

    // ':' separates segments, and '@' is escaped too, which Telegram clients may treat as a mention.
    private sealed class StringSegment : Segment
    {
        public override string Write(object value) =>
            ((string)value).Replace("%", "%25").Replace(":", "%3A").Replace("@", "%40");

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value)
        {
            value = Unescape(segment);
            return true;
        }

        private static string Unescape(string segment)
        {
            if (!segment.Contains('%'))
            {
                return segment;
            }

            var text = new StringBuilder(segment.Length);
            for (var index = 0; index < segment.Length; index++)
            {
                char? escaped = segment.AsSpan(index, Math.Min(3, segment.Length - index)) switch
                {
                    "%25" => '%',
                    "%3A" => ':',
                    "%40" => '@',
                    _ => null,
                };

                text.Append(escaped ?? segment[index]);
                index += escaped is null ? 0 : 2;
            }

            return text.ToString();
        }
    }

    // Lower-case names, looked up both ways.
    private sealed class EnumSegment : Segment
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<object, string> _names = [];

        public EnumSegment(Type type)
        {
            foreach (var name in Enum.GetNames(type))
            {
                var value = Enum.Parse(type, name);
                var lower = name.ToLowerInvariant();
                _values.TryAdd(lower, value);
                _names.TryAdd(value, lower);
            }
        }

        public override string? Write(object value) => _names.GetValueOrDefault(value);

        public override bool TryRead(string segment, [NotNullWhen(true)] out object? value) =>
            _values.TryGetValue(segment, out value);
    }
}
