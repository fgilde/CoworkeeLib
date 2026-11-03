using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

/// <summary>Sets the head script of the caller's organisation, or of the installation from the system organisation.</summary>
// never an AI tool: a script written from a prompt would run in every user's browser
[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.HeadScript)]
public sealed record SetHeadScript(bool Enabled, string? Value) : ICommand<Result>;
