namespace Coworkee.Application.Messaging;

public static class MiddlewareOrder
{
    public const int Logging = 100;
    public const int Authorization = 200;
    public const int Features = 250;
    public const int Validation = 300;
    public const int Caching = 350;
    public const int UnitOfWork = 400;
}
