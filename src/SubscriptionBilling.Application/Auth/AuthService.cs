using Microsoft.Extensions.Logging;
using SubscriptionBilling.Application.Abstractions;
using SubscriptionBilling.Application.Common;
using SubscriptionBilling.Domain.Users;

namespace SubscriptionBilling.Application.Auth;

public sealed partial class AuthService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    ITokenService tokens,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    public async Task<Result<UserDto>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PasswordPolicy.IsAcceptable(request.Password))
        {
            return Failure.Validation("weak_password", $"The password must have between {PasswordPolicy.MinLength} and {PasswordPolicy.MaxLength} characters.");
        }

        var email = User.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return EmailTaken();
        }

        var user = User.Register(request.Email, request.FullName, hasher.Hash(request.Password), UserRole.User, time.GetUtcNow().UtcDateTime);
        users.Add(user);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return EmailTaken();
        }

        LogUserRegistered(user.Id);
        return UserDto.From(user);
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), cancellationToken);
        var passwordMatches = hasher.Verify(user?.PasswordHash, request.Password);
        if (user is null || !passwordMatches)
        {
            LogLoginRejected();
            return Failure.Unauthorized("invalid_credentials", "The email or password is incorrect.");
        }

        var token = tokens.Issue(user, time.GetUtcNow().UtcDateTime);
        LogLoginSucceeded(user.Id);
        return new AuthResponse(token.Value, "Bearer", token.ExpiresAtUtc, UserDto.From(user));
    }

    public async Task<Result<UserDto>> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is null ? Failure.NotFound("user_not_found", "The user does not exist.") : UserDto.From(user);
    }

    private static Failure EmailTaken() => Failure.Conflict("email_taken", "An account with this email already exists.");

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "User registered {UserId}")]
    private partial void LogUserRegistered(Guid userId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Login succeeded {UserId}")]
    private partial void LogLoginSucceeded(Guid userId);

    // No identifier on purpose: logging the attempted email would store personal data for accounts that may not exist.
    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Login rejected")]
    private partial void LogLoginRejected();
}
