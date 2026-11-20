using System.ComponentModel;
using Coworkee.Application.Authorization;
using Coworkee.Application.Events;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Notifications;
using Coworkee.Core.Results;
using FluentValidation;

namespace Coworkee.Notifications;

[RequiresPermission(NotificationPermissions.Send)]
[RequiresPermission(IdentityPermissions.Users.View)]
[Description("Sends a notification (inbox, toast, mail) to users chosen by a filter of the Users set, added or left out by hand.")]
public sealed record SendNotification(SendNotificationRequest Request) : ICommand<Result>;

internal sealed class SendNotificationValidator : AbstractValidator<SendNotification>
{
    public SendNotificationValidator()
    {
        RuleFor(c => c.Request).NotNull();
        When(c => c.Request is not null, () =>
        {
            RuleFor(c => c.Request.Recipients).NotNull().SetValidator(new UserSelectionValidator());
            RuleFor(c => c.Request.Title).NotEmpty().MaximumLength(300);
            RuleFor(c => c.Request.Body).MaximumLength(4000);
            RuleFor(c => c.Request.Type).MaximumLength(100);
            RuleFor(c => c.Request.Link).MaximumLength(2000).Must(link => BeSafe(link!)).When(c => !string.IsNullOrWhiteSpace(c.Request.Link)).WithMessage("The link must be a path of the app or an http(s) address.");
            RuleFor(c => c.Request.Arguments).Must(a => a is null || (a.Count <= 10 && a.All(x => x is { Length: <= 500 }))).WithMessage("At most 10 arguments of 500 characters.");
            RuleFor(c => c.Request).Must(r => r.Stored || r.Toast || r.Mail != NotificationMail.Never).WithMessage("Choose where the notification shows.");
        });
    }

    // no javascript: or data: links in inboxes, toasts and mails
    private static bool BeSafe(string link) =>
        (link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal))
        || (Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
}

internal sealed class SendNotificationHandler(INotifier notifier, IUserSelectionResolver selections) : IHandler<SendNotification, Result>
{
    public async Task<Result> HandleAsync(SendNotification command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // a filter the sender may not run answers 400, not 500 in the middle of the send
        // ponytail: the selection resolves here, for the rows and for the toast (three user queries); resolve once if senders pick huge selections often
        var resolved = await selections.ResolveAsync(request.Recipients, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!;
        }

        await notifier.SendAsync(new NotificationRequest(EventTarget.Users(request.Recipients), request.Title.Trim(), string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim())
        {
            Type = string.IsNullOrWhiteSpace(request.Type) ? NotificationRequest.DefaultType : request.Type.Trim(),
            Delivery = (request.Stored ? Delivery.Stored : 0) | (request.Toast ? Delivery.Toast : 0),
            Severity = request.Severity,
            Mail = request.Mail switch
            {
                NotificationMail.Never => MailDelivery.Never,
                NotificationMail.Always => MailDelivery.Always,
                NotificationMail.WhenOffline => MailDelivery.WhenOffline,
                _ => MailDelivery.ByPreference,
            },
            Arguments = request.Arguments is { Count: > 0 } arguments ? arguments : null,
            Link = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim(),
        }, cancellationToken);
        return Result.Success();
    }
}
