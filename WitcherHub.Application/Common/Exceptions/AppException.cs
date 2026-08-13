using System.Net;

namespace WitcherHub.Application.Common.Exceptions
{
    /// <summary>
    /// Base application exception with HTTP semantics (useful for UI + APIs).
    /// </summary>
    public abstract class AppException : Exception
    {
        protected AppException(string message, Exception? inner = null)
            : base(message, inner) { }

        public virtual int StatusCode { get; } = (int)HttpStatusCode.InternalServerError;

        /// <summary>
        /// Short title shown to user (optional).
        /// </summary>
        public virtual string Title { get; } = "Error";
    }

    public sealed class NotFoundAppException : AppException
    {
        public NotFoundAppException(string message = "Not found.", Exception? inner = null)
            : base(message, inner) { }

        public override int StatusCode => (int)HttpStatusCode.NotFound;
        public override string Title => "Not Found";
    }

    public sealed class ConflictAppException : AppException
    {
        public ConflictAppException(string message = "Conflict.", Exception? inner = null)
            : base(message, inner) { }

        public override int StatusCode => (int)HttpStatusCode.Conflict;
        public override string Title => "Conflict";
    }

    /// <summary>
    /// The supplied credentials did not match an account.
    ///
    /// Distinct from every other failure so a sign-in page can tell "wrong
    /// password" apart from "the database is unreachable" — previously both
    /// produced the same "check email/password" message, which sent people to
    /// retype a password that was never the problem.
    /// </summary>
    public sealed class AuthenticationFailedAppException : AppException
    {
        public AuthenticationFailedAppException(string message = "Invalid credentials.", Exception? inner = null)
            : base(message, inner) { }

        public override int StatusCode => (int)HttpStatusCode.Unauthorized;
        public override string Title => "Sign-in failed";
    }

    public sealed class ForbiddenAppException : AppException
    {
        public ForbiddenAppException(string message = "Forbidden.", Exception? inner = null)
            : base(message, inner) { }

        public override int StatusCode => (int)HttpStatusCode.Forbidden;
        public override string Title => "Forbidden";
    }

    public sealed class BadRequestAppException : AppException
    {
        public BadRequestAppException(string message = "Bad request.", Exception? inner = null)
            : base(message, inner) { }

        public override int StatusCode => (int)HttpStatusCode.BadRequest;
        public override string Title => "Bad Request";
    }

    /// <summary>
    /// Validation exception that carries field errors (perfect for Razor Pages + APIs).
    /// </summary>
    public sealed class ValidationAppException : AppException
    {
        public ValidationAppException(
            IDictionary<string, string[]> errors,
            string message = "Validation failed.",
            Exception? inner = null)
            : base(message, inner)
        {
            Errors = errors ?? new Dictionary<string, string[]>();
        }

        public override int StatusCode => (int)HttpStatusCode.BadRequest;
        public override string Title => "Validation Error";

        public IDictionary<string, string[]> Errors { get; }
    }
}
