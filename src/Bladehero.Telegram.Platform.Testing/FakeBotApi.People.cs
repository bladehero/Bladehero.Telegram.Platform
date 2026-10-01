using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bladehero.Telegram.Platform.Testing;

// A test user's details: a last name, a username and a language, checked as Telegram gives them.
public sealed partial class FakeBotApi
{
    private static readonly string[] ReservedUsernameStarts =
    [
        "admin",
        "telegram",
        "support",
        "security",
        "settings",
        "contacts",
        "service",
        "telegraph",
    ];

    private static void ThrowIfNotDetails(string? lastName, string? username, string? languageCode)
    {
        if (lastName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        }

        if (
            username is not null
            && (
                !UsernameShape().IsMatch(username)
                || username.Contains("__", StringComparison.Ordinal)
                || ReservedUsernameStarts.Any(word => username.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            )
        )
        {
            throw new ArgumentException(
                $"\"{username}\" isn't a username Telegram gives: 5-32 of a-z, A-Z, 0-9 and _, starting with a letter, "
                    + "without a trailing or double _, and not starting with a reserved word such as admin or telegram.",
                nameof(username)
            );
        }

        if (languageCode is not null && !LanguageCodeShape().IsMatch(languageCode))
        {
            throw new ArgumentException(
                $"\"{languageCode}\" isn't a language code such as en or pt-br.",
                nameof(languageCode)
            );
        }
    }

    // Under _gate. A detail given for the first time is kept, and the private chat shows it; a different one throws.
    private void AddDetails(JsonObject person, string? lastName, string? username, string? languageCode)
    {
        var firstName = person["first_name"]!.GetValue<string>();
        ThrowIfOtherThan(person, "last_name", "last name", lastName);
        ThrowIfOtherThan(person, "username", "username", username);
        ThrowIfOtherThan(person, "language_code", "language code", languageCode);

        if (
            username is not null
            && _people.Values.FirstOrDefault(other =>
                other != person
                && string.Equals(other["username"]?.GetValue<string>(), username, StringComparison.OrdinalIgnoreCase)
            )
                is { } owner
        )
        {
            throw new InvalidOperationException(
                $"The username \"{username}\" belongs to {owner["first_name"]} already."
            );
        }

        foreach (
            var (field, value) in new[]
            {
                ("last_name", lastName),
                ("username", username),
                ("language_code", languageCode),
            }
        )
        {
            if (value is not null)
            {
                person[field] = value;
            }
        }

        if (_chats.TryGetValue(person["id"]!.GetValue<long>(), out var chat))
        {
            chat.Describe(PrivateChatOf(person));
        }

        void ThrowIfOtherThan(JsonObject user, string field, string what, string? value)
        {
            if (value is not null && user[field]?.GetValue<string>() is { } old && old != value)
            {
                throw new InvalidOperationException(
                    $"{firstName} was first opened with {what} \"{old}\", not \"{value}\"; a name is one Telegram "
                        + "user throughout the test."
                );
            }
        }
    }

    private static JsonObject PrivateChatOf(JsonObject person)
    {
        var chat = new JsonObject
        {
            ["id"] = person["id"]!.DeepClone(),
            ["type"] = "private",
            ["first_name"] = person["first_name"]!.DeepClone(),
        };

        foreach (var field in new[] { "last_name", "username" })
        {
            if (person[field] is { } value)
            {
                chat[field] = value.DeepClone();
            }
        }

        return chat;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{3,30}[A-Za-z0-9]$")]
    private static partial Regex UsernameShape();

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex LanguageCodeShape();
}
