/*
 * ExceptionMiddleware.cs
 * -----------------------------------------------------------------------------
 * Purpose : Central place that turns exceptions thrown anywhere in the request
 *           pipeline (mainly from the service layer) into HTTP responses, the
 *           same role as Spring's GlobalExceptionHandler / @ControllerAdvice.
 *           Because of this, controllers do not need try/catch blocks.
 * Mapping : ConflictException     -> 409 Conflict
 *           UnauthorizedException -> 401 Unauthorized
 *           ForbiddenException     -> 403 Forbidden
 *           NotFoundException      -> 404 Not Found
 *           ArgumentException      -> 400 Bad Request
 *           anything else          -> 500 Internal Server Error
 * Wiring  : Add app.UseMiddleware<ExceptionMiddleware>(); in Program.cs, before
 *           app.UseAuthentication(), so it can catch exceptions from every
 *           later stage of the pipeline.
 * -----------------------------------------------------------------------------
 */

using System.Net;
using System.Text.Json;
using SolarMicrogrid.API.Exceptions;

namespace SolarMicrogrid.API.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            HttpStatusCode statusCode = ex switch
            {
                ConflictException => HttpStatusCode.Conflict,           // 409
                UnauthorizedException => HttpStatusCode.Unauthorized,   // 401
                ForbiddenException => HttpStatusCode.Forbidden,         // 403
                NotFoundException => HttpStatusCode.NotFound,           // 404
                ArgumentException => HttpStatusCode.BadRequest,         // 400
                _ => HttpStatusCode.InternalServerError                 // 500
            };

            
            if (statusCode == HttpStatusCode.InternalServerError)
            {
                _logger.LogError(ex, "Unhandled exception.");
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var payload = new
            {
                status = (int)statusCode,
                message = statusCode == HttpStatusCode.InternalServerError
                    ? "An unexpected error occurred."
                    : ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
