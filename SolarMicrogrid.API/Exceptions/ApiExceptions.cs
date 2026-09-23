/*
 * ApiExceptions.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines domain exceptions translated by the shared API exception
 *           middleware into stable HTTP status codes.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Exceptions
{

    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message)
        {
            // Preserve the domain-safe conflict message for the shared error response.
        }
    }

    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message)
        {
            // Preserve the authentication failure message for the shared error response.
        }
    }

    public class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message)
        {
            // Preserve the authorization failure message for the shared error response.
        }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message)
        {
            // Preserve the missing-resource message for the shared error response.
        }
    }
}
