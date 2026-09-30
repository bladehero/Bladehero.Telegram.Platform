using System.Text.Json.Nodes;

namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A reply keyboard as a user's app shows it under the message field.</summary>
public sealed class TestReplyKeyboard
{
    private readonly JsonObject[][] _buttons;

    internal TestReplyKeyboard(JsonObject markup, bool hidden)
    {
        // A button is an object, or a bare string that is its text.
        _buttons =
        [
            .. markup["keyboard"]!
                .AsArray()
                .OfType<JsonArray>()
                .Select(row =>
                    row.Select(button =>
                            button as JsonObject ?? new JsonObject { ["text"] = button!.GetValue<string>() }
                        )
                        .ToArray()
                ),
        ];

        Rows =
        [
            .. _buttons.Select(row =>
                (IReadOnlyList<string>)[.. row.Select(button => button["text"]!.GetValue<string>())]
            ),
        ];
        IsOneTime = markup["one_time_keyboard"]?.GetValue<bool>() is true;
        IsHidden = hidden;
        IsPersistent = markup["is_persistent"]?.GetValue<bool>() is true;
        Placeholder = markup["input_field_placeholder"]?.GetValue<string>();
    }

    /// <summary>The button labels, row by row.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>Whether the app hides it after a press.</summary>
    public bool IsOneTime { get; }

    /// <summary>Whether the app hid it after a press; its buttons can still be pressed.</summary>
    public bool IsHidden { get; }

    /// <summary>Whether the app keeps it shown when the user switches to the regular keyboard.</summary>
    public bool IsPersistent { get; }

    /// <summary>The message field's placeholder while it shows, if any.</summary>
    public string? Placeholder { get; }

    /// <summary>The buttons row by row, e.g. <c>[A] [B] / [C]</c>.</summary>
    public override string ToString() =>
        string.Join(" / ", Rows.Select(row => string.Join(" ", row.Select(label => $"[{label}]"))));

    internal JsonObject? Button(string label) =>
        _buttons.SelectMany(row => row).FirstOrDefault(button => button["text"]!.GetValue<string>() == label);
}
