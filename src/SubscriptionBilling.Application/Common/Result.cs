namespace SubscriptionBilling.Application.Common;

public enum FailureKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <summary>A business outcome that is not a success. <see cref="Code"/> is stable and safe to expose to clients.</summary>
public sealed record Failure(FailureKind Kind, string Code, string Message)
{
    public static Failure Validation(string code, string message) => new(FailureKind.Validation, code, message);

    public static Failure NotFound(string code, string message) => new(FailureKind.NotFound, code, message);

    public static Failure Conflict(string code, string message) => new(FailureKind.Conflict, code, message);

    public static Failure Unauthorized(string code, string message) => new(FailureKind.Unauthorized, code, message);

    public static Failure Forbidden(string code, string message) => new(FailureKind.Forbidden, code, message);
}

/// <summary>Expected failures are values, not exceptions: callers must look at the outcome before using the value.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Failure? error)
    {
        _value = value;
        Failure = error;
    }

    public Failure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    internal static Result<T> Success(T value) => new(value, null);

    internal static Result<T> Failed(Failure failure) => new(default, failure);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Failure failure) => Failed(failure);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Failure, TOut> onFailure) => IsSuccess ? onSuccess(_value!) : onFailure(Failure!);
}

/// <summary>Marker for operations that succeed without a value.</summary>
public readonly record struct Unit;
