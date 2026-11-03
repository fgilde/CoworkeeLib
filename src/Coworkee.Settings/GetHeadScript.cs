using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

/// <summary>The head script as it applies to the caller; disabled and empty while the app has not turned the head script on.</summary>
// open: the value is public by design (anonymous clients run it), like the theme
[AiTool(Exclude = true)]
public sealed record GetHeadScript : IQuery<Result<HeadScriptDto>>;
