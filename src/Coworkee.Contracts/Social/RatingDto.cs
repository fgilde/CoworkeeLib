namespace Coworkee.Contracts.Social;

public sealed record RatingDto(double Average, int Count, int? Mine, bool CanRate);
