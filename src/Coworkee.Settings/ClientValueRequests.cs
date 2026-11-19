using System.Text;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings;

// open, all of them: every signed-in user reads the organisation's global values and reads and writes his own; the handler checks
// SettingsPermissions.ManageGlobalClientValues for a global write. Not for the assistant: the values are the client's state.

/// <summary>Every client value the caller sees, ordered by key: his own where he has one, else the global value.</summary>
[AiTool(Exclude = true)]
public sealed record GetClientValues : IQuery<Result<IReadOnlyList<ClientValueDto>>>;

/// <summary>The keys of <see cref="GetClientValues"/>.</summary>
[AiTool(Exclude = true)]
public sealed record GetClientValueKeys : IQuery<Result<IReadOnlyList<string>>>;

/// <summary>One client value: the caller's own, else the global value, else no value (<see cref="ClientValueDto.Value"/> null).</summary>
[AiTool(Exclude = true)]
public sealed record GetClientValue(string Key) : IQuery<Result<ClientValueDto>>;

/// <summary>Saves the caller's own value or, with <see cref="ClientValueScope.Global"/>, the organisation's (ManageGlobalClientValues).</summary>
[AiTool(Exclude = true)]
public sealed record SetClientValue(string Key, string Value, ClientValueScope Scope = ClientValueScope.User) : ICommand<Result>;

/// <summary>Deletes the caller's own value or the organisation's global one (ManageGlobalClientValues); nothing to delete is no error.</summary>
[AiTool(Exclude = true)]
public sealed record DeleteClientValue(string Key, ClientValueScope Scope = ClientValueScope.User) : ICommand<Result>;

internal static class ClientValueKey
{
    // any text a user may type into a template name, but no control characters (line breaks, NUL) and not only blanks
    public static IRuleBuilderOptions<T, string> ValidClientValueKey<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .MaximumLength(ClientValue.KeyLength)
        .Must(key => key is null || (!string.IsNullOrWhiteSpace(key) && !key.Any(char.IsControl)))
        .WithMessage("A key holds printable characters only.");
}

internal sealed class GetClientValueValidator : AbstractValidator<GetClientValue>
{
    public GetClientValueValidator() => RuleFor(q => q.Key).ValidClientValueKey();
}

internal sealed class SetClientValueValidator : AbstractValidator<SetClientValue>
{
    public SetClientValueValidator(IOptions<ClientValueOptions> options)
    {
        var max = options.Value.MaxValueBytes;
        RuleFor(c => c.Key).ValidClientValueKey();
        RuleFor(c => c.Scope).IsInEnum();
        RuleFor(c => c.Value).NotNull()
            .Must(value => value is null || Encoding.UTF8.GetByteCount(value) <= max)
            .WithMessage($"A value holds at most {max} bytes.");
    }
}

internal sealed class DeleteClientValueValidator : AbstractValidator<DeleteClientValue>
{
    public DeleteClientValueValidator()
    {
        RuleFor(c => c.Key).ValidClientValueKey();
        RuleFor(c => c.Scope).IsInEnum();
    }
}
