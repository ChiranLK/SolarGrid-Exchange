/*
 * RequireActiveAccountAttribute.cs
 * -----------------------------------------------------------------------------
 * File        : RequireActiveAccountAttribute.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : [RequireActiveAccount] re-checks the signed-in account in
 *               MongoDB on every request to a protected endpoint. A JWT stays
 *               valid until it expires, so without this a user deactivated by
 *               Backoffice could keep calling the API with an old token.
 *               Runs as a resource filter, i.e. after [Authorize] has already
 *               validated the token and role, so JWT validation is unchanged.
 * Errors      : Throws UnauthorizedException (account missing / role changed)
 *               or ForbiddenException (pending / deactivated); these reach
 *               ExceptionMiddleware and use the shared { status, message } body.
 * Usage       : Put [RequireActiveAccount] on a controller or action. Other
 *               components may reuse it on their own authenticated endpoints.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequireActiveAccountAttribute : TypeFilterAttribute
    {
        // Wires the attribute to ActiveAccountFilter, resolving its dependencies from DI.
        public RequireActiveAccountAttribute()
            : base(typeof(ActiveAccountFilter))
        {
            // Resolve the live-account resource filter through dependency injection.
        }
    }

    public sealed class ActiveAccountFilter : IAsyncResourceFilter
    {
        private readonly MongoDbContext _context;

        // Receives the shared MongoDB context through dependency injection.
        public ActiveAccountFilter(MongoDbContext context)
        {
            // Execute ActiveAccountFilter with validated inputs and the authoritative application state.
            _context = context;
        }

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            // Let anonymous requests pass untouched; [Authorize] has already challenged them where required.
            ClaimsPrincipal principal = context.HttpContext.User;
            if (principal.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            string nic = (principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
            if (nic.Length == 0)
            {
                throw new UnauthorizedException("Your session is invalid. Please sign in again.");
            }

            User? account = await _context.Users
                .Find(user => user.Nic == nic)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            // A token for a removed account, or whose role no longer matches, must be re-issued.
            if (account is null ||
                !string.Equals(account.Role.ToString(), principal.FindFirstValue(ClaimTypes.Role), StringComparison.Ordinal))
            {
                throw new UnauthorizedException("Your session is no longer valid. Please sign in again.");
            }

            if (account.Status == UserStatus.PendingActivation)
            {
                throw new ForbiddenException("This account is awaiting activation.");
            }

            if (account.Status == UserStatus.Deactivated)
            {
                throw new ForbiddenException("This account is deactivated. Please contact Backoffice.");
            }

            await next();
        }
    }
}
